#if UNITY_EDITOR
using System;
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 指定攻击检测在 Scene View 中当前允许编辑的几何类别。
    /// </summary>
    internal enum AttackDetectionHandleMode
    {
        None,
        Position,
        Rotation,
        Shape
    }

    /// <summary>
    /// 保存单刃武器在上一采样帧与当前帧的世界空间端点。
    /// </summary>
    internal readonly struct WeaponTraceSweepSegment
    {
        internal Vector3 PreviousRoot { get; }
        internal Vector3 PreviousTip { get; }
        internal Vector3 CurrentRoot { get; }
        internal Vector3 CurrentTip { get; }

        // 创建只读扫掠线段快照，避免 Scene GUI 持有预览 Transform 引用。
        internal WeaponTraceSweepSegment(Vector3 previousRoot, Vector3 previousTip,
            Vector3 currentRoot, Vector3 currentTip)
        {
            PreviousRoot = previousRoot;
            PreviousTip = previousTip;
            CurrentRoot = currentRoot;
            CurrentTip = currentTip;
        }
    }

    /// <summary>
    /// 提供一次攻击检测 Scene 绘制所需的角色空间、线框与表面颜色、曲面精度及可选单刃轨迹快照。
    /// </summary>
    internal readonly struct AttackDetectionSceneDrawContext
    {
        internal Transform ActorRoot { get; }
        internal Color Color { get; }
        internal Color FillColor { get; }
        internal int SurfaceSegments { get; }
        internal WeaponTraceSweepSegment? WeaponSegment { get; }

        // 创建单个 Clip 的只读绘制上下文，线框与半透明表面共享同一帧姿态。
        internal AttackDetectionSceneDrawContext(Transform actorRoot, Color color,
            Color fillColor, int surfaceSegments, WeaponTraceSweepSegment? weaponSegment)
        {
            ActorRoot = actorRoot;
            Color = color;
            FillColor = fillColor;
            SurfaceSegments = surfaceSegments;
            WeaponSegment = weaponSegment;
        }
    }

    /// <summary>
    /// 定义一种 AttackDetectionData 的 Scene 绘制和可选 Handle 编辑策略。
    /// </summary>
    internal interface IAttackDetectionSceneDrawer
    {
        Type DataType { get; }
        bool SupportsHandles { get; }
        bool RequiresWeaponTraceMarkers { get; }

        /// <summary>
        /// 绘制当前帧的只读检测形状。
        /// </summary>
        /// <param name="context">当前 Clip 的场景绘制上下文。</param>
        /// <param name="data">具体检测配置。</param>
        void Draw(in AttackDetectionSceneDrawContext context, AttackDetectionDataBase data);

        /// <summary>
        /// 绘制当前 Unity 工具对应的可编辑 Handle；发生变化时返回独立数据快照，否则返回原值。
        /// </summary>
        /// <param name="context">当前 Clip 的场景绘制上下文。</param>
        /// <param name="data">当前权威数据或本地草稿。</param>
        /// <param name="mode">由 Unity W/E/R 工具映射得到的单一编辑类别。</param>
        /// <returns>本次 Scene GUI 后的检测数据。</returns>
        AttackDetectionDataBase DrawHandles(in AttackDetectionSceneDrawContext context,
            AttackDetectionDataBase data, AttackDetectionHandleMode mode);
    }
}
#endif