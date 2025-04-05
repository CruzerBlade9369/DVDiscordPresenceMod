using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace DVDiscordPresenceMod
{
    public class CoroHandler : MonoBehaviour
    {
        private static CoroHandler instance;

        public static CoroHandler Instance
        {
            get
            {
                if (instance == null)
                {
                    GameObject obj = new GameObject("RichPresenceCoroHandler");
                    instance = obj.AddComponent<CoroHandler>();
                    DontDestroyOnLoad(obj);
                }
                return instance;
            }
        }

        public void RunCoroutine(IEnumerator coroutine)
        {
            StartCoroutine(coroutine);
        }
    }
}
