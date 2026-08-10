#if UNITY_EDITOR
namespace WS_Modules.GAS.GameplayCue
{
    /// <summary>用于 Odin 手动测试的 Cue 行为，记录各阶段并验证对象池回收。</summary>
    public sealed class GameplayCueProbeBehaviour : GameplayCueBehaviour
    {
        /// <summary>当前实例收到的 Execute 次数。</summary>
        public int ExecuteCount { get; private set; }
        /// <summary>当前实例收到的 Active 次数。</summary>
        public int ActiveCount { get; private set; }
        /// <summary>当前实例收到的 Remove 次数。</summary>
        public int RemoveCount { get; private set; }
        /// <summary>当前实例收到的 Recycle 次数。</summary>
        public int RecycleCount { get; private set; }

        /// <summary>记录一次性表现并主动释放实例。</summary>
        protected override void OnExecute(GameplayCueRuntime runtime)
        {
            ExecuteCount++;
            runtime.Release();
        }

        /// <summary>记录持续表现启动。</summary>
        protected override void OnActive(GameplayCueRuntime runtime) => ActiveCount++;

        /// <summary>记录持续表现移除。</summary>
        protected override void OnRemove(GameplayCueRuntime runtime) => RemoveCount++;

        /// <summary>重置测试计数，验证实例再次从对象池取出后状态干净。</summary>
        protected override void OnCueRecycle(GameplayCueRuntime runtime)
        {
            RecycleCount++;
            ExecuteCount = 0;
            ActiveCount = 0;
            RemoveCount = 0;
        }
    }
}
#endif
