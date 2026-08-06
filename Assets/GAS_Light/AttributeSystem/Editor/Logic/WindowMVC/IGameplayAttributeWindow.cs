#if UNITY_EDITOR
using System;
using WS_Modules.GAS.AttributeSystem;

namespace WS_Modules.GAS.Editor
{
    /// <summary>标识 Attribute 模块内部的子页面。</summary>
    public enum GameplayAttributeEditorPage
    {
        /// <summary>全局 Attribute Spec 作者与 Bake 页面。</summary>
        Specs,

        /// <summary>GameplayAttributeSet Definition 编辑页面。</summary>
        Sets
    }

    /// <summary>定义嵌入式 Attribute 页面选择和资源生命周期能力。</summary>
    public interface IGameplayAttributeWindow : IDisposable
    {
        /// <summary>获取当前 Registry。</summary>
        GameplayAttributeRegistry CurrentRegistry { get; }

        /// <summary>获取当前 AttributeSet。</summary>
        GameplayAttributeSet CurrentSet { get; }

        /// <summary>切换 Attribute 子页面。</summary>
        /// <param name="page">目标子页面。</param>
        void SelectPage(GameplayAttributeEditorPage page);

        /// <summary>选择 Registry，并决定是否恢复已有选择。</summary>
        /// <param name="registry">目标 Registry。</param>
        /// <param name="restoreSelection">是否恢复 Session 选择。</param>
        void SetRegistry(GameplayAttributeRegistry registry, bool restoreSelection);

        /// <summary>选择 AttributeSet，并决定是否恢复已有选择。</summary>
        /// <param name="set">目标 Set。</param>
        /// <param name="restoreSelection">是否恢复 Session 选择。</param>
        void SetAttributeSet(GameplayAttributeSet set, bool restoreSelection);
    }
}
#endif
