using DV.Logic.Job;
using System.Collections.Generic;
using UnityModManagerNet;

namespace DVDiscordPresenceMod
{
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        public readonly string version = Main.mod?.Info.Version;

        [Draw("Enable logging")]
        public bool isLoggingEnabled =
#if DEBUG
            true;
#else
            false;
#endif

        [Draw("Force rich presence data to always return in English")]
        public bool forceEnglish = true;

        [Draw("Caboose plural word")]
        public string caboosePlural = "cabooses";

        public override void Save(UnityModManager.ModEntry entry)
        {
            Save(this, entry);
        }

        public void OnChange() { }
    }
}
