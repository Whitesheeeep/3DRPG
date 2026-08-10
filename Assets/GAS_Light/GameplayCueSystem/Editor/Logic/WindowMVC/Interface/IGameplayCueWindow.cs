#if UNITY_EDITOR
using System;
using WS_Modules.GAS.GameplayCue;

namespace WS_Modules.GAS.Editor
{
    /// <summary>定义可嵌入 GAS 主窗口的 Gameplay Cue 编辑页面。</summary>
    public interface IGameplayCueWindow : IDisposable
    {
        /// <summary>获取当前编辑的 Cue Database。</summary>
        GameplayCueDatabase CurrentDatabase { get; }

        /// <summary>获取当前选中的 CueData。</summary>
        GameplayCueData CurrentCue { get; }

        /// <summary>切换当前数据库并按需恢复选择状态。</summary>
        /// <param name="database">要编辑的数据库。</param>
        /// <param name="restoreSelection">是否从 SessionState 恢复选择。</param>
        void SetDatabase(GameplayCueDatabase database, bool restoreSelection);

        /// <summary>在当前数据库中选择指定 Cue。</summary>
        /// <param name="cue">要选中的 CueData。</param>
        /// <param name="restoreSelection">是否允许恢复数据库级选择。</param>
        void SetCue(GameplayCueData cue, bool restoreSelection);
    }
}
#endif
