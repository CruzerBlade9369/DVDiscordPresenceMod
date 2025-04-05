using DV.Logic.Job;
using DV.ThingTypes.TransitionHelpers;
using DV.ThingTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using DV.Localization;
using DV.Utils;
using System.Collections;
using PassengerJobs.Generation;

namespace DVDiscordPresenceMod
{
    public class RPHandler : MonoBehaviour
    {
        public const string CLIENT_ID = "1344358363584139415"; // ID to your discord developer app
        public const string DETAILS_IDLE = "No active jobs";
        public const string STATE_IDLE = "On foot";
        // public const string STATE_NO_CARGO = "No Cargo";
        public static readonly DateTime EPOCH = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public const string LARGE_ICON = "dvicon";
        public const float ACTIVITY_UPDATE_TIME = 1;
        public const float MAX_TIMER_DIFFERENCE = 10;
        public const long SLACK_TIME = 60;
        // public const float RPC_UPDATE_TIME = 15; // RPC dll should handle rate limiting just fine.

        // Scene Info
        private static int currentSceneIndex;
        public const string DETAILS_MAINMENU = "In Main Menu";
        public const string DETAILS_BENCH = "Benchmarking";
        public const string DETAILS_LOC = "Testing Localization";
        public const string DETAILS_VRCAL = "Calibrating VR";
        public const string DETAILS_LOADING = "Loading...";

        // Status Info
        private static Job currentJob; // Highest-paying active job.
        private static Trainset lastTrain;
        private static int lastCarsCount;
        private static int oldDerailedAmount;
        private static float lastLength;
        private static float lastWeight;
        private static float totalNumJobs;
        private static float totalNumDest;

        // Presence Info
        private static string activityState;
        private static string activityDetails;
        private static long activityStart;
        private static long activityEnd;
        private static string smallImageKey;
        private static string smallImageText;
        private static float activityTimer;
        // private static float rpcTimer; // RPC dll should handle rate limiting just fine.
        private static bool updateActivity;

        private static bool forceAllUpdate;
        private static bool isLoadingSave;

        public static void Initialize()
        {
            activityState = "";
            activityDetails = DETAILS_MAINMENU;
            activityStart = UnixTime();
            activityEnd = 0;
            currentJob = null;
            updateActivity = true;
            forceAllUpdate = true;

            // Train Status Trackers
            lastCarsCount = -1;
            oldDerailedAmount = -1;
            lastLength = -1;
            lastWeight = -1;

            // Rate Timers
            activityTimer = ACTIVITY_UPDATE_TIME;
            // rpcTimer = RPC_UPDATE_TIME;

            DiscordRpc.EventHandlers handlers = new DiscordRpc.EventHandlers();
            handlers.readyCallback = Main.ReadyCallback;
            handlers.disconnectedCallback += Main.DisconnectedCallback;
            handlers.errorCallback += Main.ErrorCallback;

            DiscordRpc.Initialize(CLIENT_ID, ref handlers, true, null);

            SceneManager.activeSceneChanged += OnSceneChanged;
            WorldStreamingInit.LoadingStatusChanged += StartLoading;
            WorldStreamingInit.LoadingFinished += LoadingFinished;
        }

        /*-----------------------------------------------------------------------------------------------------------------------*/

        #region RPC HANDLER

        public static bool StartRPC(bool active)
        {
            if (!active)
            {
                DiscordRpc.RichPresence presence = new DiscordRpc.RichPresence
                {
                    details = "",
                    state = "",
                    startTimestamp = 0,
                    endTimestamp = 0,
                    largeImageKey = LARGE_ICON,
                    largeImageText = "",
                    smallImageKey = "",
                    smallImageText = ""
                };

                DiscordRpc.UpdatePresence(ref presence);
            }
            else
                updateActivity = true;

            return true;
        }

        public static bool StopRPC()
        {
            SceneManager.activeSceneChanged -= OnSceneChanged;
            WorldStreamingInit.LoadingStatusChanged += StartLoading;
            WorldStreamingInit.LoadingFinished -= LoadingFinished;
            DiscordRpc.Shutdown();
            return true;
        }

        public static void UpdateRPC(float delta)
        {
            activityTimer += delta;
            // rpcTimer += delta;

            if (updateActivity)
            {
                DiscordRpc.RichPresence presence = new DiscordRpc.RichPresence
                {
                    details = activityDetails,
                    state = activityState,
                    startTimestamp = activityStart,
                    endTimestamp = activityEnd > UnixTime() ? activityEnd : 0,
                    largeImageKey = LARGE_ICON,
                    largeImageText = "",
                    smallImageKey = smallImageKey,
                    smallImageText = smallImageText
                };

                DiscordRpc.UpdatePresence(ref presence);
                // mod.Logger.Log("Requested to set presence.");

                updateActivity = false;
            }
            else if (activityTimer > ACTIVITY_UPDATE_TIME)
            {
                activityTimer %= ACTIVITY_UPDATE_TIME;

                if (isLoadingSave)
                {
                    bool loadingChanged = LoadingState();
                    updateActivity = loadingChanged;
                }
                else if (IsInGame())
                {
                    bool jobChanged = UpdateJobStatus();
                    bool trainChanged = UpdateTrainStatus();
                    updateActivity = jobChanged || trainChanged;
                }
                else
                {
                    bool sceneChanged = UpdateSceneStatus();
                    updateActivity = sceneChanged;
                }

                forceAllUpdate = false;
            }

            // old method
            /*if (!updateActivity)
            {
                if (activityTimer > ACTIVITY_UPDATE_TIME)
                {
                    activityTimer %= ACTIVITY_UPDATE_TIME;
                    bool jobChanged = UpdateJobStatus();
                    bool trainChanged = UpdateTrainStatus();
                    updateActivity = jobChanged || trainChanged;
                }
                else
                    return;
            } // Don't update activity and Discord presence on the same frame.
            else if (rpcTimer > RPC_UPDATE_TIME)
            {
                rpcTimer %= RPC_UPDATE_TIME;

                DiscordRpc.RichPresence presence = new DiscordRpc.RichPresence
                {
                    details = activityDetails,
                    state = activityState,
                    startTimestamp = activityStart,
                    // endTimestamp = activityEnd > activityStart ? activityEnd : 0,
                    largeImageKey = LARGE_ICON,
                    largeImageText = "",
                    smallImageKey = smallImageKey,
                    smallImageText = smallImageText
                };
                if (activityEnd > activityStart)
                    presence.endTimestamp = activityEnd;

                DiscordRpc.UpdatePresence(ref presence);
                // mod.Logger.Log("Requested to set presence.");

                updateActivity = false;
            }*/
        }

        #endregion

        /*-----------------------------------------------------------------------------------------------------------------------*/

        #region GAME DATA GATHERER

        private static bool UpdateTrainStatus()
        {
            TrainCar car = PlayerManager.Car;
            Trainset train = car?.trainset;
            TrainCar loco = null; // Current train has loco?
            bool changed = train != lastTrain;

            // If no current train, use last train if job is active.
            if (train == null)
            {
                // If no active job, switch to idle.
                if (currentJob == null)
                {
                    lastTrain = null;
                    lastCarsCount = -1;
                    lastLength = -1;
                    lastWeight = -1;
                    activityState = STATE_IDLE;
                    smallImageKey = "";
                    smallImageText = "";
                    return changed;
                }
                // Otherwise keep using last train.
            }
            // Switch current train if player entered locomotive.
            else if (train == PlayerManager.LastLoco?.trainset)
            {
                lastTrain = train;
                loco = PlayerManager.LastLoco;
            }
            // Switch current train if player entered caboose or tender.
            else if (car != null && (CarTypes.IsCaboose(car.carLivery) || CarTypes.IsTender(car.carLivery)))
            {
                lastTrain = train;
                loco = car;
            }
            // No changes if otherwise, and we need to check the last train itself.
            if (lastTrain == null)
                return false;

            // Update status based on lastTrain.
            int cabooseAmount = 0; // How many cabeese
            int derailedAmount = 0; // Number of derailed cars
            float length = 0; // Length of whole consist
            float weight = 0; // Weight of whole consist

            List<string> cargos = new List<string>(); // list of cargos on train

            foreach (TrainCar c in lastTrain.cars)
            {
                length += c.logicCar.length;

                if (c.derailed)
                {
                    derailedAmount++;
                }

                if (CarTypes.IsAnyLocomotiveOrTender(c.carLivery))
                {
                    if (loco == null || CarTypes.IsCaboose(loco.carLivery))
                        loco = c;
                }
                else if (CarTypes.IsCaboose(c.carLivery))
                {
                    if (loco == null)
                        loco = c;
                    cabooseAmount++;
                }
                else if (c.logicCar.CurrentCargoTypeInCar == CargoType.None && !CargoExclusions.IsCarExcluded(c.carLivery.v1))
                {
                    cargos.Add(GetEmptyCarName(c));
                    weight += c.massController.TotalMass;
                }

                // Individual cargo check, since locos can load cargo now
                if (c.logicCar.CurrentCargoTypeInCar != CargoType.None)
                {
                    cargos.Add(Translation(c.logicCar.CurrentCargoTypeInCar.ToV2().localizationKeyShort));
                    weight += c.massController.TotalMass;
                }
            }

            changed = changed || lastCarsCount != lastTrain.cars.Count || lastLength != length || lastWeight != weight || oldDerailedAmount != derailedAmount;
            lastCarsCount = lastTrain.cars.Count;
            lastLength = length;
            lastWeight = weight;
            oldDerailedAmount = derailedAmount;

            if (forceAllUpdate)
            {
                changed = forceAllUpdate;
            }

            if (changed)
            {
                if (loco != null)
                {
                    smallImageText = GetLocoOrFromTenderName(loco);

                    // These aren't icons from the game; they're from rich presence app
                    // https://discord.com/developers/applications
                    switch (loco.carType)
                    {
                        case TrainCarType.LocoShunter:
                            smallImageKey = "locode2";
                            break;

                        case TrainCarType.LocoDiesel:
                            smallImageKey = "locode6";
                            break;

                        case TrainCarType.LocoDH4:
                            smallImageKey = "locodh4";
                            break;

                        case TrainCarType.LocoDM3:
                            smallImageKey = "locodm3";
                            break;

                        case TrainCarType.LocoS060:
                            smallImageKey = "locos060";
                            break;

                        case TrainCarType.LocoSteamHeavy:
                        case TrainCarType.Tender:
                            smallImageKey = "locos282";
                            break;

                        case TrainCarType.LocoMicroshunter:
                            smallImageKey = "locobe2";
                            break;

                        case TrainCarType.LocoDM1U:
                            smallImageKey = "locodm1u";
                            break;

                        case TrainCarType.HandCar:
                            smallImageKey = "handcar";
                            break;

                        case TrainCarType.CabooseRed:
                            smallImageKey = "caboose";
                            break;

                        default:
                            smallImageKey = "";
                            smallImageText = "";
                            break;
                    }
                }

                string isDerailed = derailedAmount > 0 ? $" | {derailedAmount} {(derailedAmount > 1 ? "cars" : "car")} derailed" : "";

                if (cargos.Any())
                {
                    cargos = cargos.Distinct().ToList();
                    string cabooseString = cabooseAmount > 0 ? $", and {cabooseAmount} {(cabooseAmount > 1 ? Main.settings.caboosePlural : "caboose")}" : "";
                    string cargoNames = cargos.Count > 1 ? $"{cargos[0]}, etc" : string.Join(", ", cargos);

                    // manage activityState length to be less than 128 bytes
                    string activityText = $"Hauling {(int)Math.Round(weight / 1000)}t of {cargoNames}{cabooseString}{isDerailed}";
                    StringBuilder activitySb = new StringBuilder(activityText, 128);

                    byte[] buffer = Encoding.ASCII.GetBytes(activitySb.ToString());

                    // might work
                    if (buffer.Length > 128)
                    {
                        activitySb.Length = 128 / buffer.Length;
                    }

                    activityState = activitySb.ToString();

                }
                else
                {
                    activityState = $"No cargo{isDerailed}";
                }
            }

            return changed;
        }

        private static bool UpdateJobStatus()
        {
            bool changed = false;

            List<Job> activeNonShuntJobs = JobsManager.Instance.currentJobs
                .Where(j => j.jobType != JobType.ShuntingLoad && j.jobType != JobType.ShuntingUnload)
                .ToList();
            List<Job> activeShuntJobs = JobsManager.Instance.currentJobs
                .Where(j => j.jobType == JobType.ShuntingLoad || j.jobType == JobType.ShuntingUnload).ToList();

            int numJobs = activeNonShuntJobs.Count;
            int numUniqueJobs = activeNonShuntJobs
                .Select(j => j.jobType)
                .Distinct()
                .Count();
            int numShuntJobs = activeShuntJobs.Count;
            int numDest = activeNonShuntJobs
                .Select(j => j.chainData.chainDestinationYardId)
                .Distinct()
                .Count();
            int numShuntLocations = activeShuntJobs
                .Select(j => j.jobType == JobType.ShuntingLoad ? j.chainData.chainOriginYardId : j.chainData.chainDestinationYardId)
                .Distinct()
                .Count();
            bool anyIsShunting = activeShuntJobs.Count >= 1;

            changed = numJobs + numShuntJobs != totalNumJobs || numDest + numShuntLocations != totalNumDest;
            totalNumJobs = numJobs + numShuntJobs;
            totalNumDest = numDest + numShuntLocations;

            if (forceAllUpdate)
            {
                changed = forceAllUpdate;
            }

            if (changed)
            {
                if (totalNumJobs <= 0)
                {
                    activityDetails = DETAILS_IDLE;
                }
                else
                {
                    Job firstJobInList;

                    if (anyIsShunting)
                    {
                        firstJobInList = activeShuntJobs.FirstOrDefault();
                    }
                    else
                    {
                        firstJobInList = activeNonShuntJobs.FirstOrDefault();
                    }

                    string jobTypeString;
                    string preposition;
                    string location;
                    StationInfo srcStation = ExtractStationInfoWithYardID(firstJobInList.chainData.chainOriginYardId);
                    StationInfo stationInfo = ExtractStationInfoWithYardID(firstJobInList.chainData.chainDestinationYardId);

                    if (anyIsShunting)
                    {
                        jobTypeString = "Shunting";
                        preposition = "in";

                        if (firstJobInList.jobType == JobType.ShuntingLoad)
                            stationInfo = srcStation;
                    }
                    else
                    {
                        if (numUniqueJobs > 1)
                        {
                            jobTypeString = "Multiple jobs";
                            preposition = "to";
                        }
                        else
                        {
                            switch (firstJobInList.jobType)
                            {
                                case JobType.Transport:
                                    jobTypeString = "Freight Haul";
                                    preposition = "to";
                                    break;
                                case JobType.EmptyHaul:
                                    jobTypeString = "Logistical Haul";
                                    preposition = "to";
                                    break;
                                // Cruzer's notes: this part needs at least passenger jobs b99 dev build or newer
                                // in project references
                                case PassJobType.Express:
                                    jobTypeString = "Express Service";
                                    preposition = "to";
                                    break;
                                case PassJobType.Local:
                                    jobTypeString = "Regional Service";
                                    preposition = "to";
                                    break;
                                default:
                                    stationInfo = srcStation;
                                    jobTypeString = "Unknown Job";
                                    preposition = "from";
                                    break;
                            }
                        }
                    }

                    if (stationInfo != null)
                    {
                        if ((anyIsShunting && numShuntLocations > 1) || (!anyIsShunting && numDest > 1))
                        {
                            location = "various " + (anyIsShunting ? "locations" : "destinations");
                        }
                        else
                        {
                            location = Translation(stationInfo.LocalizationKey);
                        }

                        activityDetails = string.Format("{0} {1} {2}", jobTypeString, preposition, location);
                    }
                    else
                    {
                        activityDetails = jobTypeString;
                    }
                }
            }

            return changed;
        }

        // old method
        /*private static bool UpdateJobStatus()
        {
            bool changed = false;

            int curActiveJobs = JobsManager.Instance.currentJobs.Count;

            if (numActiveJobs != curActiveJobs)
            {
                Job highest = null;

                foreach (Job j in JobsManager.Instance.currentJobs)
                    if (highest == null || highest.GetBasePaymentForTheJob() < j.GetBasePaymentForTheJob())
                        highest = j;

                changed = currentJob != highest;
                currentJob = highest;
                // TODO: Determine if this actually happens.
                if (currentJob == null && numActiveJobs > 0)
                    numActiveJobs = -1; // Flag for checking this again.
                else
                    numActiveJobs = curActiveJobs;
            }

            if (currentJob != null)
            {
                long curTime = UnixTime();
                long actualActivityStart = curTime - (long)currentJob.GetTimeOnJob();
                activityEnd = actualActivityStart + (long)currentJob.TimeLimit + SLACK_TIME;
                bool timesUp = activityEnd < curTime;
                changed = changed || bonusOver != timesUp || Math.Abs(actualActivityStart - activityStart) > MAX_TIMER_DIFFERENCE;
                activityStart = actualActivityStart;
                bonusOver = timesUp;
            }

            if (forceAllUpdate)
            {
                changed = forceAllUpdate;
            }

            if (changed)
            {
                if (currentJob == null)
                {
                    activityDetails = DETAILS_IDLE;
                    activityStart = UnixTime();
                    activityEnd = 0;
                    bonusOver = false;
                }
                else
                {
                    StationInfo srcStation = ExtractStationInfoWithYardID(currentJob.chainData.chainOriginYardId);
                    StationInfo stationInfo = ExtractStationInfoWithYardID(currentJob.chainData.chainDestinationYardId);
                    string jobTypeString;
                    string preposition;
                    switch (currentJob.jobType)
                    {
                        case JobType.ShuntingLoad:
                            stationInfo = srcStation;
                            jobTypeString = "Loading Cars";
                            preposition = "in";
                            break;
                        case JobType.ShuntingUnload:
                            jobTypeString = "Unloading Cars";
                            preposition = "in";
                            break;
                        case JobType.Transport:
                            jobTypeString = "Freight Haul";
                            preposition = "to";
                            break;
                        case JobType.EmptyHaul:
                            jobTypeString = "Logistical Haul";
                            preposition = "to";
                            // Power Move Jobs Integration
                            // Cruzer's notes: Just gonna comment this out since this aint a feature or a mod anyway
                            EmptyHaulJobData jobData = JobDataExtractor.ExtractEmptyHaulJobData(currentJob);
                            if (jobData.transportingCars.All(c => CarTypes.IsAnyLocomotiveOrTender(TrainCar.logicCarToTrainCar[c].carType)))
                                jobTypeString = "Power Move";
                            break;
                        default:
                            stationInfo = srcStation;
                            jobTypeString = "Unknown Job";
                            preposition = "from";
                            break;
                    }

                    if (stationInfo != null)
                        activityDetails = string.Format("{0} {1} {2}", jobTypeString, preposition, Translation(stationInfo.LocalizationKey));
                    else
                        activityDetails = jobTypeString;
                    activityStart = UnixTime() - (long)currentJob.GetTimeOnJob();
                    activityEnd = activityStart + (long)currentJob.TimeLimit + SLACK_TIME;
                }
            }

            return changed;
        }*/

        private static bool UpdateSceneStatus()
        {
            bool changed = false;

            int currentScene = SceneManager.GetActiveScene().buildIndex;
            changed = currentScene != currentSceneIndex;
            currentSceneIndex = currentScene;

            if (forceAllUpdate)
            {
                changed = forceAllUpdate;
            }

            if (changed)
            {
                activityState = "";
                smallImageKey = "";
                smallImageText = "";

                switch (currentSceneIndex)
                {
                    case (int)DVScenes.MainMenu:
                        activityDetails = DETAILS_MAINMENU;
                        break;

                    case (int)DVScenes.Benchmark:
                        activityDetails = DETAILS_BENCH;
                        break;

                    case (int)DVScenes.LocalizationTest:
                        activityDetails = DETAILS_LOC;
                        break;

                    case (int)DVScenes.VRCalibrationScene:
                        activityDetails = DETAILS_VRCAL;
                        break;

                    default:
                        activityDetails = "Not In Game";
                        break;
                }
            }

            return changed;
        }

        private static bool LoadingState()
        {
            if (forceAllUpdate)
            {
                activityDetails = DETAILS_LOADING;
                activityState = "";
            }

            return forceAllUpdate;
        }

        #endregion

        /*-----------------------------------------------------------------------------------------------------------------------*/

        #region STATIC HELPERS

        private static StationInfo ExtractStationInfoWithYardID(string yardId)
        {
            StationController stationController;
            if (SingletonBehaviour<LogicController>.Instance != null && SingletonBehaviour<LogicController>.Instance.YardIdToStationController != null && SingletonBehaviour<LogicController>.Instance.YardIdToStationController.TryGetValue(yardId, out stationController))
            {
                return stationController.stationInfo;
            }
            return null;
        }

        private static string GetEmptyCarName(TrainCar car)
        {
            string name = Translation(car.carLivery.localizationKey);
            return string.Format($"Empty {name}");
        }

        private static string GetLocoOrFromTenderName(TrainCar car)
        {
            if (CarTypes.IsTender(car.carLivery))
            {
                TrainCar s282a = car.frontCoupler.coupledTo.train;
                bool hasLoco = car.frontCoupler.IsCoupled() && CarTypes.IsMUSteamLocomotive(s282a.carType);

                if (hasLoco)
                {
                    return Translation(s282a.carLivery.localizationKey);
                }
            }

            return Translation(car.carLivery.localizationKey);
        }

        private static string Translation(string key)
        {
            if (Main.settings.forceEnglish)
            {
                return LocalizationAPI.Lo(key, "english");
            }

            return LocalizationAPI.L(key);
        }

        private static void StartLoading(string msg, bool isError, float percent)
        {
            if (isLoadingSave)
            {
                return;
            }

            isLoadingSave = true;
            forceAllUpdate = true;
        }

        private static void LoadingFinished()
        {
            isLoadingSave = false;
            forceAllUpdate = true;
        }

        private static bool IsInGame()
        {
            if (SceneManager.GetActiveScene().buildIndex != (int)DVScenes.Game)
            {
                return false;
            }

            return true;
        }

        private static void OnSceneChanged(Scene oldScene, Scene newScene)
        {
            CoroHandler.Instance.RunCoroutine(DelayedForceUpdate());
        }

        private static IEnumerator DelayedForceUpdate()
        {
            yield return new WaitForSeconds(0.5f);
            forceAllUpdate = true;
        }

        private static long UnixTime()
        {
            return (long)(DateTime.UtcNow - EPOCH).TotalSeconds;
        }

        #endregion

    }
}
