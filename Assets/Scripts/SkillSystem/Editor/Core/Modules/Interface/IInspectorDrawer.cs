#if UNITY_EDITOR
using System;
using UnityEngine.UIElements;
using WS_Modules.MVVM;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 定义根据具体 ViewData 类型绘制时间轴 Inspector 的能力。
    /// </summary>
    internal interface IInspectorDrawer
    {
        /// <summary>
        /// 判断 Drawer 是否支持给定显示投影。
        /// </summary>
        bool CanDraw(IViewData viewData);
        /// <summary>
        /// 把显示投影绘制到容器，并把字段意图转发给 ViewModel。
        /// </summary>
        void Draw(VisualElement container, IViewData viewData, EditorViewModel viewModel);
    }

    /// <summary>
    /// 定义一种攻击检测具体数据的 Inspector 绘制与完整快照提交能力。
    /// </summary>
    internal interface IAttackDetectionDataDrawer
    {
        Type DataType { get; }

        /// <summary>
        /// 绘制具体检测参数，并在字段变化时提交独立的完整配置快照。
        /// </summary>
        void Draw(VisualElement container, AttackDetectionDataBase data,
            Action<AttackDetectionDataBase> submit);
    }
}
#endif
