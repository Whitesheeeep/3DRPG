#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.MVVM;

namespace RPG.SkillSystem.Editor
{
    #region Drawer contract and host

    /// <summary>
    /// 定义根据具体 ViewData 类型绘制时间轴 Inspector 的能力。
    /// </summary>
    internal interface ISkillTimelineInspectorDrawer
    {
        bool CanDraw(IViewData viewData);
        void Draw(VisualElement container, IViewData viewData, SkillTimelineEditorViewModel viewModel);
    }

    /// <summary>
    /// 管理 Inspector Drawer 的选择、绑定与重绘生命周期。
    /// </summary>
    internal sealed class SkillTimelineInspectorView
    {
        private readonly VisualElement root;
        private readonly List<ISkillTimelineInspectorDrawer> drawers = new()
        {
            new GroupInspectorDrawer(),
            new TrackInspectorDrawer(),
            new AnimationClipInspectorDrawer(),
            new VfxClipInspectorDrawer(),
            new EventMarkerInspectorDrawer()
        };
        private SkillTimelineEditorViewModel viewModel;
        private VisualElement container;

        /// <summary>
        /// 创建 Inspector 主视图。
        /// </summary>
        public SkillTimelineInspectorView(VisualElement root) => this.root = root;

        /// <summary>
        /// 绑定 ViewModel 并执行首次 Inspector 刷新。
        /// </summary>
        public void Bind(SkillTimelineEditorViewModel model)
        {
            viewModel = model;
            container = root.Q<VisualElement>("InspectorContainer");
            viewModel.InspectorChanged += RefreshInspector;
            RefreshInspector();
        }

        /// <summary>
        /// 解除事件绑定并清理动态 Inspector 内容。
        /// </summary>
        public void Unbind()
        {
            if (viewModel != null) viewModel.InspectorChanged -= RefreshInspector;
            container?.Clear();
            viewModel = null;
        }

        // 根据当前具体 ViewData 类型选择 Drawer，避免使用 Kind 枚举分发。
        private void RefreshInspector()
        {
            container.Clear();
            IViewData selected = viewModel.SelectedViewData;
            if (selected == null)
            {
                Label empty = new("选择 Group、Track、Clip 或 Marker 后在这里编辑属性。");
                empty.AddToClassList("empty-inspector");
                container.Add(empty);
                return;
            }

            ISkillTimelineInspectorDrawer drawer = drawers.Find(x => x.CanDraw(selected));
            if (drawer == null)
            {
                container.Add(new Label("当前选择没有可用 Inspector。"));
                return;
            }
            drawer.Draw(container, selected, viewModel);
        }
    }

    #endregion

    #region Shared helpers

    /// <summary>
    /// 提供具体 Inspector Drawer 共用的控件创建与样式辅助方法。
    /// </summary>
    internal abstract class SkillTimelineInspectorDrawerBase
    {
        // 添加当前选择对象的 Inspector 标题。
        protected static void AddTitle(VisualElement container, string title)
        {
            Label label = new(title);
            label.AddToClassList("inspector-title");
            container.Add(label);
        }

        // 添加具有统一 USS class 的编辑字段。
        protected static T AddField<T>(VisualElement container, T field) where T : VisualElement
        {
            field.AddToClassList("inspector-field");
            container.Add(field);
            return field;
        }

        // 添加用于放置复制、删除和排序按钮的操作行。
        protected static VisualElement AddActionRow(VisualElement container)
        {
            VisualElement row = new();
            row.AddToClassList("inspector-button-row");
            container.Add(row);
            return row;
        }

        // 添加所有 Clip 与 Marker 共用的复制和删除操作。
        protected static void AddItemActions(VisualElement container, SkillTimelineEditorViewModel viewModel)
        {
            VisualElement row = AddActionRow(container);
            row.Add(new Button(viewModel.DuplicateSelectedItem) { text = "复制" });
            row.Add(new Button(viewModel.RemoveSelectedItem) { text = "删除" });
        }
    }

    #endregion

    #region Concrete drawers

    /// <summary>
    /// 绘制所有具体轨道分组的只读信息与新增轨道操作。
    /// </summary>
    internal sealed class GroupInspectorDrawer : SkillTimelineInspectorDrawerBase, ISkillTimelineInspectorDrawer
    {
        /// <summary>
        /// 判断 ViewData 是否为轨道分组。
        /// </summary>
        public bool CanDraw(IViewData viewData) => viewData is SkillTimelineGroupViewData;

        /// <summary>
        /// 绘制分组标题、轨道数量和新增轨道按钮。
        /// </summary>
        public void Draw(VisualElement container, IViewData viewData, SkillTimelineEditorViewModel viewModel)
        {
            if (viewData is not SkillTimelineGroupViewData group) return;
            AddTitle(container, group.DisplayName);
            container.Add(new Label($"轨道数量：{group.Tracks.Count}"));
            AddActionRow(container).Add(new Button(() => viewModel.AddTrack(group)) { text = "添加轨道" });
        }
    }

    /// <summary>
    /// 绘制所有具体轨道共用的名称、静音、锁定和排序操作。
    /// </summary>
    internal sealed class TrackInspectorDrawer : SkillTimelineInspectorDrawerBase, ISkillTimelineInspectorDrawer
    {
        /// <summary>
        /// 判断 ViewData 是否为具体轨道。
        /// </summary>
        public bool CanDraw(IViewData viewData) => viewData is SkillTimelineTrackViewData;

        /// <summary>
        /// 绘制并提交轨道公共字段。
        /// </summary>
        public void Draw(VisualElement container, IViewData viewData, SkillTimelineEditorViewModel viewModel)
        {
            if (viewData is not SkillTimelineTrackViewData track) return;
            AddTitle(container, track.DisplayName);
            TextField name = AddField(container, new TextField("名称") { value = track.DisplayName });
            Toggle muted = AddField(container, new Toggle("静音") { value = track.Muted });
            Toggle locked = AddField(container, new Toggle("锁定") { value = track.Locked });
            void Submit() => viewModel.EditSelectedTrack(name.value, muted.value, locked.value);
            name.RegisterValueChangedCallback(_ => Submit());
            muted.RegisterValueChangedCallback(_ => Submit());
            locked.RegisterValueChangedCallback(_ => Submit());
            VisualElement row = AddActionRow(container);
            row.Add(new Button(() => viewModel.MoveSelectedTrack(-1)) { text = "上移" });
            row.Add(new Button(() => viewModel.MoveSelectedTrack(1)) { text = "下移" });
            row.Add(new Button(viewModel.RemoveSelectedTrack) { text = "删除" });
        }
    }

    /// <summary>
    /// 绘制动画片段配置并提交类型化编辑请求。
    /// </summary>
    internal sealed class AnimationClipInspectorDrawer : SkillTimelineInspectorDrawerBase, ISkillTimelineInspectorDrawer
    {
        /// <summary>
        /// 判断 ViewData 是否为动画片段。
        /// </summary>
        public bool CanDraw(IViewData viewData) => viewData is AnimationClipTimelineItemViewData;

        /// <summary>
        /// 绘制动画资源、帧区间、源偏移和速度字段。
        /// </summary>
        public void Draw(VisualElement container, IViewData viewData, SkillTimelineEditorViewModel viewModel)
        {
            if (viewData is not AnimationClipTimelineItemViewData item) return;
            AnimationSkillClipConfig clip = item.Config;
            AddTitle(container, item.DisplayName);
            ObjectField animation = AddField(container, new ObjectField("AnimationClip")
            {
                objectType = typeof(AnimationClip), allowSceneObjects = false, value = clip.AnimationClip
            });
            IntegerField start = AddField(container, new IntegerField("起始帧") { value = clip.StartFrame });
            IntegerField duration = AddField(container, new IntegerField("持续帧") { value = clip.DurationFrames });
            IntegerField sourceStart = AddField(container, new IntegerField("源动画偏移") { value = clip.SourceStartFrame });
            FloatField speed = AddField(container, new FloatField("播放速度") { value = clip.PlaybackSpeed });
            void Submit() => viewModel.EditSelectedAnimationClip(new AnimationClipEditRequest(
                animation.value as AnimationClip, start.value, duration.value, sourceStart.value, speed.value));
            animation.RegisterValueChangedCallback(_ => Submit());
            start.RegisterValueChangedCallback(_ => Submit());
            duration.RegisterValueChangedCallback(_ => Submit());
            sourceStart.RegisterValueChangedCallback(_ => Submit());
            speed.RegisterValueChangedCallback(_ => Submit());
            AddItemActions(container, viewModel);
        }
    }

    /// <summary>
    /// 绘制特效片段配置并提交类型化编辑请求。
    /// </summary>
    internal sealed class VfxClipInspectorDrawer : SkillTimelineInspectorDrawerBase, ISkillTimelineInspectorDrawer
    {
        /// <summary>
        /// 判断 ViewData 是否为特效片段。
        /// </summary>
        public bool CanDraw(IViewData viewData) => viewData is VfxClipTimelineItemViewData;

        /// <summary>
        /// 绘制特效资源、帧区间、绑定和局部变换字段。
        /// </summary>
        public void Draw(VisualElement container, IViewData viewData, SkillTimelineEditorViewModel viewModel)
        {
            if (viewData is not VfxClipTimelineItemViewData item) return;
            VfxSkillClipConfig clip = item.Config;
            AddTitle(container, item.DisplayName);
            ObjectField prefab = AddField(container, new ObjectField("Prefab")
            {
                objectType = typeof(GameObject), allowSceneObjects = false, value = clip.Prefab
            });
            IntegerField start = AddField(container, new IntegerField("起始帧") { value = clip.StartFrame });
            IntegerField duration = AddField(container, new IntegerField("持续帧") { value = clip.DurationFrames });
            TextField binding = AddField(container, new TextField("挂点路径") { value = clip.BindingPath });
            Vector3Field position = AddField(container, new Vector3Field("局部位置") { value = clip.LocalPosition });
            Vector3Field rotation = AddField(container, new Vector3Field("局部旋转") { value = clip.LocalEulerAngles });
            Vector3Field scale = AddField(container, new Vector3Field("局部缩放") { value = clip.LocalScale });
            EnumField follow = AddField(container, new EnumField("跟随模式", clip.FollowMode));
            EnumField stop = AddField(container, new EnumField("结束模式", clip.StopMode));
            void Submit() => viewModel.EditSelectedVfxClip(new VfxClipEditRequest(prefab.value as GameObject,
                start.value, duration.value, binding.value, position.value, rotation.value, scale.value,
                (VfxFollowMode)follow.value, (VfxStopMode)stop.value));
            prefab.RegisterValueChangedCallback(_ => Submit());
            start.RegisterValueChangedCallback(_ => Submit());
            duration.RegisterValueChangedCallback(_ => Submit());
            binding.RegisterValueChangedCallback(_ => Submit());
            position.RegisterValueChangedCallback(_ => Submit());
            rotation.RegisterValueChangedCallback(_ => Submit());
            scale.RegisterValueChangedCallback(_ => Submit());
            follow.RegisterValueChangedCallback(_ => Submit());
            stop.RegisterValueChangedCallback(_ => Submit());
            AddItemActions(container, viewModel);
        }
    }

    /// <summary>
    /// 绘制事件标记配置并提交类型化编辑请求。
    /// </summary>
    internal sealed class EventMarkerInspectorDrawer : SkillTimelineInspectorDrawerBase, ISkillTimelineInspectorDrawer
    {
        /// <summary>
        /// 判断 ViewData 是否为事件标记。
        /// </summary>
        public bool CanDraw(IViewData viewData) => viewData is EventMarkerTimelineItemViewData;

        /// <summary>
        /// 绘制事件帧、类型、名称和参数文本字段。
        /// </summary>
        public void Draw(VisualElement container, IViewData viewData, SkillTimelineEditorViewModel viewModel)
        {
            if (viewData is not EventMarkerTimelineItemViewData item) return;
            SkillEventMarkerConfig marker = item.Config;
            AddTitle(container, item.DisplayName);
            IntegerField frame = AddField(container, new IntegerField("触发帧") { value = marker.Frame });
            TextField eventType = AddField(container, new TextField("事件类型名") { value = marker.EventTypeName });
            TextField displayName = AddField(container, new TextField("显示名称") { value = marker.DisplayName });
            TextField parameters = AddField(container, new TextField("参数文本")
            {
                value = marker.ParameterText, multiline = true
            });
            void Submit() => viewModel.EditSelectedEventMarker(new EventMarkerEditRequest(
                frame.value, eventType.value, displayName.value, parameters.value));
            frame.RegisterValueChangedCallback(_ => Submit());
            eventType.RegisterValueChangedCallback(_ => Submit());
            displayName.RegisterValueChangedCallback(_ => Submit());
            parameters.RegisterValueChangedCallback(_ => Submit());
            AddItemActions(container, viewModel);
        }
    }

    #endregion
}
#endif