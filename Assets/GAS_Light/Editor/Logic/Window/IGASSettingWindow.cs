#if UNITY_EDITOR
using System;

namespace WS_Modules.GAS.Editor
{
    /// <summary>标识 GAS 主窗口中可显示的编辑模块。</summary>
    public enum GASEditorModule
    {
        /// <summary>Gameplay Tag 作者与烘焙页面。</summary>
        GameplayTags,
        /// <summary>Gameplay Attribute Spec 与 AttributeSet 编辑页面。</summary>
        GameplayAttributes,
        /// <summary>Gameplay Effect 作者页面。</summary>
        GameplayEffects,
        /// <summary>Gameplay Ability 作者页面。</summary>
        GameplayAbilities,
        /// <summary>Gameplay Cue 编辑页面。</summary>
        GameplayCues
    }

    /// <summary>定义 GAS 选项卡宿主的模块选择能力，不暴露 UI Toolkit 控件。</summary>
    public interface IGASSettingWindow
    {
        /// <summary>获取当前显示的 GAS 编辑模块。</summary>
        GASEditorModule ActiveModule { get; }

        /// <summary>切换当前选项卡，并在唯一内容宿主中显示对应模块。</summary>
        /// <param name="module">需要显示的模块。</param>
        /// <exception cref="ArgumentOutOfRangeException">模块值未在当前版本中定义。</exception>
        void SelectModule(GASEditorModule module);
    }
}
#endif
