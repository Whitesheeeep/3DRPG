using System.Collections.Generic;
using UnityEditor;

namespace WS_Modules
{
    internal sealed class EventSystemInfo
    {
        public List<int> registerLine = new List<int>();
        public List<int> triggerLine = new List<int>();
        public List<MonoScript> registerScripts = new List<MonoScript>();
        public List<MonoScript> triggerScripts = new List<MonoScript>();
    }
}

