using UnityModManagerNet;
using UnityEngine;

namespace DVDiscordPresenceMod
{
    public static class Main
    {
        public static bool enabled;
        public static UnityModManager.ModEntry mod;

        public static Settings settings { get; private set; }

        private static bool Load(UnityModManager.ModEntry modEntry)
        {
            try
            {
                settings = Settings.Load<Settings>(modEntry);
            }
            catch
            {
                Debug.LogWarning("Unabled to load mod settings. Using defaults instead.");
                settings = new Settings();
            }
            mod = modEntry;

            modEntry.OnToggle = OnToggle;
            modEntry.OnUnload = OnUnload;
            modEntry.OnUpdate = OnUpdate;

            modEntry.OnGUI = OnGui;
            modEntry.OnSaveGUI = OnSaveGui;

            RPHandler.Initialize();

            return true;
        }

        static void OnGui(UnityModManager.ModEntry modEntry)
        {
            settings.Draw(modEntry);
        }

        static void OnSaveGui(UnityModManager.ModEntry modEntry)
        {
            settings.Save(modEntry);
        }

        public static void DebugLog(string message)
        {
            if (settings.isLoggingEnabled)
                mod?.Logger.Log(message);
        }

        /*-----------------------------------------------------------------------------------------------------------------------*/

        public static void ReadyCallback()
        {
            mod.Logger.Log("Got ready callback.");
        }

        public static void DisconnectedCallback(int errorCode, string message)
        {
            mod.Logger.Log(string.Format("Got disconnect {0}: {1}", errorCode, message));
        }

        public static void ErrorCallback(int errorCode, string message)
        {
            mod.Logger.Log(string.Format("Got error {0}: {1}", errorCode, message));
        }

        /*-----------------------------------------------------------------------------------------------------------------------*/

        private static bool OnToggle(UnityModManager.ModEntry _, bool active)
        {
            return RPHandler.StartRPC(active);
        }

        private static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            return RPHandler.StopRPC();
        }

        private static void OnUpdate(UnityModManager.ModEntry _, float delta)
        {
            RPHandler.UpdateRPC(delta);
        }
    }
}
