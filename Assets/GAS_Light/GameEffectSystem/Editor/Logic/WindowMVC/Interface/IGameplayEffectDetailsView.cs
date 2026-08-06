#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using WS_Modules.GAS.GameplayEffect;

namespace WS_Modules.GAS.Editor
{
    /// <summary>定义 GE Editor 右侧详情、Modifier 编辑与校验区域的内部 View 边界。</summary>
    internal interface IGameplayEffectDetailsView : IDisposable
    {
        #region 用户意图事件

        /// <summary>原生绑定提交任意 GE 序列化字段时触发。</summary>
        event Action EffectSerializedChanged;
        /// <summary>Modifier 选择索引变化时触发。</summary>
        event Action<int> ModifierSelectionChanged;
        /// <summary>请求创建指定派生类型的 Modifier。</summary>
        event Action<Type> AddModifierRequested;
        /// <summary>请求删除当前 Modifier。</summary>
        event Action RemoveModifierRequested;
        /// <summary>请求移动 Modifier 索引。</summary>
        event Action<GameplayEffectModifierMoveRequest> MoveModifierRequested;
        /// <summary>请求重新校验当前 GE。</summary>
        event Action ValidateRequested;

        #endregion

        #region 状态与渲染

        /// <summary>设置 AttributeId 作者名称解析上下文。</summary>
        /// <param name="registry">当前明确的 Registry；null 表示无法解析名称。</param>
        /// <param name="unavailableReason">Registry 不明确时显示的原因。</param>
        void SetAttributeRegistry(
            GameplayAttributeRegistry registry,
            string unavailableReason);
        /// <summary>建立或解除当前 GE 的右侧原生序列化绑定。</summary>
        /// <param name="effect">当前 GE；null 表示清空详情。</param>
        void BindEffect(GameplayEffectData effect);
        /// <summary>绑定选定 Modifier 的 managed-reference 详情。</summary>
        /// <param name="selectedModifierIndex">目标索引；负数表示清空详情。</param>
        void BindModifier(int selectedModifierIndex);
        /// <summary>渲染 Modifier Model 引用与可创建类型。</summary>
        /// <param name="modifiers">当前 GE 的 Modifier。</param>
        /// <param name="selectedIndex">当前选择索引。</param>
        /// <param name="availableTypes">可创建的 Modifier 派生类型。</param>
        void RenderModifiers(
            IReadOnlyList<GameplayEffectModifier> modifiers,
            int selectedIndex,
            IReadOnlyList<Type> availableTypes);
        /// <summary>刷新 Duration、Period 和 Stacking 策略字段显隐。</summary>
        /// <param name="effect">当前 GE；null 时隐藏详情。</param>
        void RefreshPolicyVisibility(GameplayEffectData effect);
        /// <summary>渲染当前 GE 校验结果。</summary>
        /// <param name="issues">当前错误、警告和提示。</param>
        void RenderValidation(IReadOnlyList<GameplayEffectValidationIssue> issues);

        #endregion
    }
}
#endif