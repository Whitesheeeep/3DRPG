#if UNITY_EDITOR
using System;
using WS_Modules.GAS.GameplayEffect;

namespace WS_Modules.GAS.Editor
{
    /// <summary>定义 GAS 主窗口中可嵌入的 Gameplay Effect 页面能力。</summary>
    public interface IGameplayEffectWindow : IDisposable
    {
        /// <summary>获取当前编辑的 GE 资产。</summary>
        GameplayEffectData CurrentEffect { get; }

        /// <summary>切换当前 GE 并按需恢复 Modifier 选择。</summary>
        /// <param name="effect">目标 GE；null 表示清空选择。</param>
        /// <param name="restoreSelection">是否使用 SessionState 恢复 Modifier 索引。</param>
        void SetEffect(GameplayEffectData effect, bool restoreSelection);
    }
}
#endif
