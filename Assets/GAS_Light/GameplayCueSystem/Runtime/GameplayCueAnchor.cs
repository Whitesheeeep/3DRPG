namespace WS_Modules.GAS.GameplayCue
{
    /// <summary>定义 Cue 默认挂载到哪个运行时对象。</summary>
    public enum GameplayCueAnchor
    {
        /// <summary>挂载到 Source ASC 的 Transform。</summary>
        Source,
        /// <summary>挂载到 Target ASC 的 Transform。</summary>
        Target,
        /// <summary>使用世界坐标，不自动挂载到 Source 或 Target。</summary>
        World
    }
}
