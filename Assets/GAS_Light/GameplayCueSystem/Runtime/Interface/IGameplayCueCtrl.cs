using System;
using System.Collections.Generic;

namespace WS_Modules.GAS.GameplayCue
{
    /// <summary>提供 ASC 对 GameplayCue 的只读查询和生命周期操作。</summary>
    public interface IGameplayCueCtrl
    {
        /// <summary>获取当前仍在运行的持续 Cue。</summary>
        IReadOnlyList<GameplayCueRuntime> ActiveCues { get; }
        /// <summary>移除属于当前 Controller 的 Cue 句柄。</summary>
        bool TryRemove(GameplayCueRuntime runtime);
        /// <summary>清理当前 Controller 的全部 Cue。</summary>
        void Clear();
    }
}
