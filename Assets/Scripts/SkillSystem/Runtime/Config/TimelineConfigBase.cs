using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 声明一种可由技能时间轴自动发现的具体轨道类型及其菜单顺序。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class TimelineTrackAttribute : Attribute
    {
        /// <summary>
        /// 右键添加轨道菜单中使用的稳定显示路径。
        /// </summary>
        public string MenuPath { get; }

        /// <summary>
        /// 类型重排及菜单展示使用的升序权重。
        /// </summary>
        public int Order { get; }

        /// <summary>
        /// 创建轨道类型元数据。
        /// </summary>
        /// <param name="menuPath">右键菜单路径。</param>
        /// <param name="order">类型排序权重。</param>
        public TimelineTrackAttribute(string menuPath, int order)
        {
            MenuPath = menuPath;
            Order = order;
        }
    }

    /// <summary>
    /// 作为 SkillConfig 子资产保存一条轨道的公共运行时状态与 Editor 显示状态。
    /// </summary>
    public abstract class TrackConfigBase : ScriptableObject
    {
        #region 序列化字段

        [SerializeField, ReadOnly, LabelText("轨道 ID")] private string id = string.Empty;
        [SerializeField, LabelText("静音")] private bool muted;

#if UNITY_EDITOR
        [SerializeField, LabelText("轨道名称")] private string displayName = "新轨道";
        [SerializeField, LabelText("锁定")] private bool editorLocked;
        [SerializeField, LabelText("编辑颜色")] private Color editorColor = Color.white;
#endif

        #endregion

        #region 只读数据

        public string Id => id;
        public bool Muted => muted;

#if UNITY_EDITOR
        public string DisplayName => displayName;
        public bool EditorLocked => editorLocked;
        public Color EditorColor => editorColor;
#endif

        /// <summary>
        /// 返回该轨道当前持有的实际内容配置，不创建时间轴显示副本。
        /// </summary>
        public abstract IReadOnlyList<TimelineItemConfigBase> Items { get; }

        #endregion
    }

    /// <summary>
    /// 提供所有时间轴内容共享的稳定标识与半开帧区间只读契约。
    /// </summary>
    [Serializable]
    public abstract class TimelineItemConfigBase
    {
        /// <summary>
        /// 内容自身的稳定 GUID。
        /// </summary>
        public abstract string Id { get; }

        /// <summary>
        /// 内容半开区间的起始帧。
        /// </summary>
        public abstract int StartFrame { get; }

        /// <summary>
        /// 内容半开区间的持续帧数；Marker 固定返回 1。
        /// </summary>
        public abstract int DurationFrames { get; }

        /// <summary>
        /// 内容半开区间的排他结束帧。
        /// </summary>
        public int EndFrame => StartFrame + DurationFrames;
    }
}
