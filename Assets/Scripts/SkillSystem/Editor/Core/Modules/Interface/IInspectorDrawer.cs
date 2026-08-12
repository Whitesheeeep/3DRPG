#if UNITY_EDITOR
using System;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 使用实际 Track 或 Item Config 绘制 Unity 原生 Inspector 内容。
    /// </summary>
    internal interface IInspectorDrawer
    {
        /// <summary>
        /// 将实际配置绘制到容器，并通过 ViewModel 提交语义修改。
        /// </summary>
        void Draw(VisualElement container, object data, EditorViewModel viewModel,
            InspectorFieldCommitController fieldCommitController);
    }

    /// <summary>
    /// 定义一种攻击检测具体数据的 Inspector 绘制与完整快照提交能力。
    /// </summary>
    internal interface IAttackDetectionDataDrawer
    {
        Type DataType { get; }

        /// <summary>
        /// 绘制具体检测参数，并在字段提交时发送独立完整快照。
        /// </summary>
        void Draw(VisualElement container, AttackDetectionDataBase data,
            Action<AttackDetectionDataBase> preview, Action<AttackDetectionDataBase> submit,
            InspectorFieldCommitController fieldCommitController);
    }
}
#endif
