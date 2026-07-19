using UnityEngine;
using WS_Modules.Pooling;

namespace WS_Modules.Utilities
{
    internal static class TimerClassPoolPrewarmRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            ClassPoolPrewarmRegistry.Register(new ClassPoolPrewarmRegistryEntry(
                ClassPoolPrewarmId.WS_Modules_Utilities_Timer_57FED158,
                typeof(Timer),
                "WS_Modules.Utilities.Timer",
                (module, count, capacity) => module.Prewarm<Timer>(count, capacity)));
        }
    }
}
