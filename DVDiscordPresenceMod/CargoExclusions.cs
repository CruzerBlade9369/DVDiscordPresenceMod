using DV.ThingTypes;
using System.Collections.Generic;

namespace DVDiscordPresenceMod
{
    internal class CargoExclusions
    {
        public static readonly HashSet<TrainCarType> excludedCars = new HashSet<TrainCarType>
        {
            TrainCarType.LocoDE6Slug
        };

        public static bool IsCarExcluded(TrainCarType carType)
        {
            if (excludedCars.Contains(carType)) return true;
            return false;
        }
    }
}
