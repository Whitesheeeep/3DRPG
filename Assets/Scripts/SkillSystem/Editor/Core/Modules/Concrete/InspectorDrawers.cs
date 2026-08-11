#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using RPG.Markers;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.MVVM;
using WS_Modules.UIToolkitExtensions.Editor;

namespace RPG.SkillSystem.Editor
{
    #region Shared helpers
    /// <summary>
    /// 提供具体 Inspector Drawer 共用的控件创建与样式辅助方法。
    /// </summary>
    internal abstract class InspectorDrawer
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
        protected static void AddItemActions(VisualElement container, EditorViewModel viewModel)
        {
            VisualElement row = AddActionRow(container);
            row.Add(new Button(viewModel.DuplicateSelectedItem) { text = "复制" });
            row.Add(new Button(viewModel.RemoveSelectedItem) { text = "删除" });
        }
    }
    #endregion

    #region Concrete drawers
    /// <summary>
    /// 绘制所有具体轨道共用的名称、静音、锁定和排序操作。
    /// </summary>
    internal sealed class TrackInspectorDrawer : InspectorDrawer, IInspectorDrawer
    {

        /// <summary>
        /// 绘制并提交轨道公共字段。
        /// </summary>
        public void Draw(VisualElement container, object data, EditorViewModel viewModel,
            InspectorFieldCommitController fieldCommitController)
        {
            if (data is not TrackConfigBase track) return;
            AddTitle(container, track.DisplayName);
            TextField name = AddField(container, new TextField("名称")
            {
                value = track.DisplayName,
                isDelayed = true
            });
            Toggle muted = AddField(container, new Toggle("静音") { value = track.Muted });
            Toggle locked = AddField(container, new Toggle("锁定") { value = track.EditorLocked });
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
    /// 绘制动作阶段区间、阶段类型与外部打断设置。
    /// </summary>
    internal sealed class ActionPhaseInspectorDrawer : InspectorDrawer, IInspectorDrawer
    {
        /// <summary>
        /// 绘制动作阶段完整配置，并将一次完成输入提交为单条 Document 事务。
        /// </summary>
        /// <param name="container">Unity 原生 Inspector 中的字段容器。</param>
        /// <param name="data">当前选中的实际 Item 配置。</param>
        /// <param name="viewModel">负责提交语义编辑请求的窗口 ViewModel。</param>
        public void Draw(VisualElement container, object data, EditorViewModel viewModel,
            InspectorFieldCommitController fieldCommitController)
        {
            if (data is not ActionPhaseSkillClipConfig clip) return;
            AddTitle(container, GetPhaseDisplayName(clip.Phase));
            IntegerField start = AddField(container, new IntegerField("起始帧")
            {
                value = clip.StartFrame,
                isDelayed = true
            });
            IntegerField duration = AddField(container, new IntegerField("持续帧")
            {
                value = clip.DurationFrames,
                isDelayed = true
            });
            EnumField phase = AddField(container, new EnumField("动作阶段", clip.Phase));
            Toggle canBeInterrupted = AddField(container,
                new Toggle("可被外部打断") { value = clip.CanBeInterrupted });

            // 每次离散修改或延迟输入完成时提交完整请求，避免 Inspector 逐字重建。
            void Submit() => viewModel.EditItem(viewModel.SelectedTrack, clip,
                new ActionPhaseEditRequest(start.value, duration.value,
                    (ActionPhaseType)phase.value, canBeInterrupted.value));

            start.RegisterValueChangedCallback(_ => Submit());
            duration.RegisterValueChangedCallback(_ => Submit());
            phase.RegisterValueChangedCallback(_ => Submit());
            canBeInterrupted.RegisterValueChangedCallback(_ => Submit());
            AddItemActions(container, viewModel);
        }

        // 将运行时枚举映射为时间轴和 Inspector 共用的中文阶段名称。
        private static string GetPhaseDisplayName(ActionPhaseType phase) => phase switch
        {
            ActionPhaseType.None => "未指定",
            ActionPhaseType.Startup => "前摇",
            ActionPhaseType.Active => "生效",
            ActionPhaseType.Recovery => "后摇",
            _ => phase.ToString()
        };
    }
    /// <summary>
    /// 绘制动画片段配置并提交类型化编辑请求。
    /// </summary>
    internal sealed class AnimationInspectorDrawer : InspectorDrawer, IInspectorDrawer
    {

        /// <summary>
        /// 绘制动画资源、帧区间、源偏移和速度字段。
        /// </summary>
        public void Draw(VisualElement container, object data, EditorViewModel viewModel,
            InspectorFieldCommitController fieldCommitController)
        {
            if (data is not AnimationSkillClipConfig clip) return;
            AddTitle(container, clip.AnimationClip != null ? clip.AnimationClip.name : "Animation Clip");
            ObjectField animation = AddField(container, new ObjectField("AnimationClip")
            {
                objectType = typeof(AnimationClip), allowSceneObjects = false, value = clip.AnimationClip
            });
            IntegerField start = AddField(container, new IntegerField("起始帧") { value = clip.StartFrame });
            VisualElement durationRow = AddActionRow(container);
            IntegerField duration = new("持续帧") { value = clip.DurationFrames };
            duration.AddToClassList("inspector-field");
            durationRow.Add(duration);
            Button matchDuration = new(() => viewModel.MatchAnimationDuration(clip))
            {
                text = "匹配动画长度",
                tooltip = "按 AnimationClip 原始时长和当前技能 FPS 恢复持续帧。"
            };
            matchDuration.SetEnabled(viewModel.CanMatchAnimationDuration(clip));
            durationRow.Add(matchDuration);
            IntegerField sourceStart = AddField(container, new IntegerField("源动画偏移") { value = clip.SourceStartFrame });
            FloatField speed = AddField(container, new FloatField("播放速度") { value = clip.PlaybackSpeed });
            FloatField fadeDuration = AddField(container, new FloatField("淡入时长") { value = clip.FadeDuration });

            void Submit() => viewModel.EditItem(viewModel.SelectedTrack, clip, new AnimationEditRequest(
                animation.value as AnimationClip, start.value, duration.value, sourceStart.value,
                speed.value, fadeDuration.value));

            // 避免一改就提交，导致动画片段在拖动滑块时频繁刷新。
            start.isDelayed = true;
            duration.isDelayed = true;
            sourceStart.isDelayed = true;
            speed.isDelayed = true;
            fadeDuration.isDelayed = true;

            animation.RegisterValueChangedCallback(_ => Submit());
            start.RegisterValueChangedCallback(_ => Submit());
            duration.RegisterValueChangedCallback(_ => Submit());
            sourceStart.RegisterValueChangedCallback(_ => Submit());
            speed.RegisterValueChangedCallback(_ => Submit());
            fadeDuration.RegisterValueChangedCallback(_ => Submit());
            AddItemActions(container, viewModel);
        }
    }


    /// <summary>
    /// 绘制攻击检测 Clip 公共帧字段，并通过具体数据 Drawer 注册表绘制形状参数。
    /// </summary>
    internal sealed class AttackDetectionInspectorDrawer : InspectorDrawer, IInspectorDrawer
    {
        #region 字段与创建

        private readonly Dictionary<Type, IAttackDetectionDataDrawer> drawers = new();

        /// <summary>
        /// 创建内置攻击检测数据 Drawer 注册表。
        /// </summary>
        public AttackDetectionInspectorDrawer()
        {
            Register(new BoxAttackDetectionDataDrawer());
            Register(new SphereAttackDetectionDataDrawer());
            Register(new CapsuleAttackDetectionDataDrawer());
            Register(new SectorAttackDetectionDataDrawer());
            Register(new WeaponTraceAttackDetectionDataDrawer());
        }

        #endregion

        #region 绘制

        /// <summary>
        /// 绘制公共区间、采样间隔、类型和当前具体检测参数。
        /// </summary>
        public void Draw(VisualElement container, object data, EditorViewModel viewModel,
            InspectorFieldCommitController fieldCommitController)
        {
            if (data is not AttackDetectionSkillClipConfig clip) return;
            AddTitle(container, $"{clip.DetectionType} Detection");
            IntegerField start = AddField(container,
                new IntegerField("起始帧") { value = clip.StartFrame });
            IntegerField duration = AddField(container,
                new IntegerField("持续帧") { value = clip.DurationFrames });
            IntegerField interval = AddField(container,
                new IntegerField("采样间隔帧") { value = clip.SampleIntervalFrames });
            EnumField type = AddField(container,
                new EnumField("检测类型", clip.DetectionType));

            // 每次正式提交都携带完整独立快照，Document 继续负责事务与区间校验。
            void Submit(AttackDetectionDataBase detectionData)
            {
                viewModel.ClearAttackDetectionInspectorDraft(clip);
                viewModel.EditItem(viewModel.SelectedTrack, clip,
                    new AttackDetectionEditRequest(start.value, duration.value, interval.value, detectionData));
            }

            // 连续输入仅替换 Scene View 草稿，不写入 Config。
            void Preview(AttackDetectionDataBase detectionData) =>
                viewModel.PreviewAttackDetectionInspectorDraft(clip, detectionData);

            fieldCommitController.Bind(start, null, () => Submit(clip.DetectionData));
            fieldCommitController.Bind(duration, null, () => Submit(clip.DetectionData));
            fieldCommitController.Bind(interval, null, () => Submit(clip.DetectionData));
            type.RegisterValueChangedCallback(evt =>
                Submit(AttackDetectionDataBase.Create((AttackDetectionType)evt.newValue)));

            if (clip.DetectionData == null)
            {
                container.Add(new Label("当前检测类型为 None，不包含具体参数。"));
            }
            else if (drawers.TryGetValue(clip.DetectionData.GetType(), out IAttackDetectionDataDrawer drawer))
            {
                drawer.Draw(container, clip.DetectionData, Preview, Submit, fieldCommitController);
            }
            else
            {
                container.Add(new Label($"未注册检测数据 Drawer：{clip.DetectionData.GetType().FullName}"));
            }

            AddItemActions(container, viewModel);
        }

        #endregion

        #region 注册校验

        // 使用精确具体类型注册 Drawer，避免主 Inspector 对检测 Type 写 switch。
        private void Register(IAttackDetectionDataDrawer drawer)
        {
            if (drawer == null) throw new ArgumentNullException(nameof(drawer));
            if (!drawers.TryAdd(drawer.DataType, drawer))
                throw new InvalidOperationException($"攻击检测 Drawer 已注册：{drawer.DataType.FullName}");
        }

        #endregion
    }

    /// <summary>
    /// 绘制 Box 攻击检测的局部变换和尺寸。
    /// </summary>
    internal sealed class BoxAttackDetectionDataDrawer : InspectorDrawer, IAttackDetectionDataDrawer
    {
        public Type DataType => typeof(BoxAttackDetectionData);

        /// <summary>
        /// 绘制 Box 参数，并区分实时预览快照与最终提交快照。
        /// </summary>
        public void Draw(VisualElement container, AttackDetectionDataBase data,
            Action<AttackDetectionDataBase> preview, Action<AttackDetectionDataBase> submit,
            InspectorFieldCommitController fieldCommitController)
        {
            if (data is not BoxAttackDetectionData box) return;
            Vector3Field position = AddField(container,
                new Vector3Field("局部位置") { value = box.LocalPosition });
            Vector3Field rotation = AddField(container,
                new Vector3Field("局部旋转") { value = box.LocalEulerAngles });
            Vector3Field size = AddField(container,
                new Vector3Field("尺寸") { value = box.Size });

            AttackDetectionDataBase Snapshot() => new BoxAttackDetectionData(
                position.value, rotation.value, ClampPositive(size.value));

            fieldCommitController.Bind(position, () => preview(Snapshot()), () => submit(Snapshot()));
            fieldCommitController.Bind(rotation, () => preview(Snapshot()), () => submit(Snapshot()));
            fieldCommitController.Bind(size, () => preview(Snapshot()), () => submit(Snapshot()));
        }

        // 尺寸各轴保持正数，避免产生不可见或翻转的检测体。
        private static Vector3 ClampPositive(Vector3 value) => new(
            Mathf.Max(0.001f, value.x), Mathf.Max(0.001f, value.y), Mathf.Max(0.001f, value.z));
    }

    /// <summary>
    /// 绘制 Sphere 攻击检测的局部位置和半径。
    /// </summary>
    internal sealed class SphereAttackDetectionDataDrawer : InspectorDrawer, IAttackDetectionDataDrawer
    {
        public Type DataType => typeof(SphereAttackDetectionData);

        /// <summary>
        /// 绘制 Sphere 参数，并区分实时预览快照与最终提交快照。
        /// </summary>
        public void Draw(VisualElement container, AttackDetectionDataBase data,
            Action<AttackDetectionDataBase> preview, Action<AttackDetectionDataBase> submit,
            InspectorFieldCommitController fieldCommitController)
        {
            if (data is not SphereAttackDetectionData sphere) return;
            Vector3Field position = AddField(container,
                new Vector3Field("局部位置") { value = sphere.LocalPosition });
            FloatField radius = AddField(container,
                new FloatField("半径") { value = sphere.Radius });

            AttackDetectionDataBase Snapshot() => new SphereAttackDetectionData(
                position.value, Mathf.Max(0.001f, radius.value));

            fieldCommitController.Bind(position, () => preview(Snapshot()), () => submit(Snapshot()));
            fieldCommitController.Bind(radius, () => preview(Snapshot()), () => submit(Snapshot()));
        }
    }

    /// <summary>
    /// 绘制 Capsule 攻击检测的局部变换、尺寸和轴向。
    /// </summary>
    internal sealed class CapsuleAttackDetectionDataDrawer : InspectorDrawer, IAttackDetectionDataDrawer
    {
        public Type DataType => typeof(CapsuleAttackDetectionData);

        /// <summary>
        /// 绘制 Capsule 参数，并区分实时预览快照与最终提交快照。
        /// </summary>
        public void Draw(VisualElement container, AttackDetectionDataBase data,
            Action<AttackDetectionDataBase> preview, Action<AttackDetectionDataBase> submit,
            InspectorFieldCommitController fieldCommitController)
        {
            if (data is not CapsuleAttackDetectionData capsule) return;
            Vector3Field position = AddField(container,
                new Vector3Field("局部位置") { value = capsule.LocalPosition });
            Vector3Field rotation = AddField(container,
                new Vector3Field("局部旋转") { value = capsule.LocalEulerAngles });
            FloatField radius = AddField(container,
                new FloatField("半径") { value = capsule.Radius });
            FloatField height = AddField(container,
                new FloatField("高度") { value = capsule.Height });
            EnumField axis = AddField(container, new EnumField("轴向", capsule.Axis));

            AttackDetectionDataBase Snapshot() => new CapsuleAttackDetectionData(
                position.value, rotation.value, Mathf.Max(0.001f, radius.value),
                Mathf.Max(0.001f, height.value), (CapsuleAxis)axis.value);

            fieldCommitController.Bind(position, () => preview(Snapshot()), () => submit(Snapshot()));
            fieldCommitController.Bind(rotation, () => preview(Snapshot()), () => submit(Snapshot()));
            fieldCommitController.Bind(radius, () => preview(Snapshot()), () => submit(Snapshot()));
            fieldCommitController.Bind(height, () => preview(Snapshot()), () => submit(Snapshot()));
            axis.RegisterValueChangedCallback(_ => submit(Snapshot()));
        }
    }

    /// <summary>
    /// 绘制 Sector 攻击检测的局部变换、半径、角度和高度。
    /// </summary>
    internal sealed class SectorAttackDetectionDataDrawer : InspectorDrawer, IAttackDetectionDataDrawer
    {
        public Type DataType => typeof(SectorAttackDetectionData);

        /// <summary>
        /// 绘制 Sector 参数，并区分实时预览快照与最终提交快照。
        /// </summary>
        public void Draw(VisualElement container, AttackDetectionDataBase data,
            Action<AttackDetectionDataBase> preview, Action<AttackDetectionDataBase> submit,
            InspectorFieldCommitController fieldCommitController)
        {
            if (data is not SectorAttackDetectionData sector) return;
            Vector3Field position = AddField(container,
                new Vector3Field("局部位置") { value = sector.LocalPosition });
            Vector3Field rotation = AddField(container,
                new Vector3Field("局部旋转") { value = sector.LocalEulerAngles });
            FloatField inner = AddField(container,
                new FloatField("内半径") { value = sector.InnerRadius });
            FloatField outer = AddField(container,
                new FloatField("外半径") { value = sector.OuterRadius });
            FloatField angle = AddField(container,
                new FloatField("角度") { value = sector.Angle });
            FloatField height = AddField(container,
                new FloatField("高度") { value = sector.Height });

            AttackDetectionDataBase Snapshot()
            {
                float innerRadius = Mathf.Max(0f, inner.value);
                return new SectorAttackDetectionData(position.value, rotation.value,
                    innerRadius, Mathf.Max(Mathf.Max(innerRadius, 0.001f), outer.value),
                    Mathf.Clamp(angle.value, 0.01f, 360f), Mathf.Max(0.001f, height.value));
            }

            fieldCommitController.Bind(position, () => preview(Snapshot()), () => submit(Snapshot()));
            fieldCommitController.Bind(rotation, () => preview(Snapshot()), () => submit(Snapshot()));
            fieldCommitController.Bind(inner, () => preview(Snapshot()), () => submit(Snapshot()));
            fieldCommitController.Bind(outer, () => preview(Snapshot()), () => submit(Snapshot()));
            fieldCommitController.Bind(angle, () => preview(Snapshot()), () => submit(Snapshot()));
            fieldCommitController.Bind(height, () => preview(Snapshot()), () => submit(Snapshot()));
        }
    }

    /// <summary>
    /// 绘制 WeaponTrace 检测沿刀根到刀尖插值的采样点数量。
    /// </summary>
    internal sealed class WeaponTraceAttackDetectionDataDrawer : InspectorDrawer, IAttackDetectionDataDrawer
    {
        public Type DataType => typeof(WeaponTraceAttackDetectionData);

        /// <summary>
        /// 绘制采样点数量，并区分实时预览快照与最终提交快照。
        /// </summary>
        public void Draw(VisualElement container, AttackDetectionDataBase data,
            Action<AttackDetectionDataBase> preview, Action<AttackDetectionDataBase> submit,
            InspectorFieldCommitController fieldCommitController)
        {
            if (data is not WeaponTraceAttackDetectionData trace) return;
            IntegerField count = AddField(container,
                new IntegerField("采样点数量") { value = trace.SamplePointCount });

            AttackDetectionDataBase Snapshot() =>
                new WeaponTraceAttackDetectionData(Mathf.Clamp(count.value, 2, 16));

            fieldCommitController.Bind(count, () => preview(Snapshot()), () => submit(Snapshot()));
        }
    }
    /// <summary>
    /// 绘制特效片段配置并提交类型化编辑请求。
    /// </summary>
    internal sealed class VfxInspectorDrawer : InspectorDrawer, IInspectorDrawer
    {

        /// <summary>
        /// 绘制特效资源、语义挂点、帧区间、局部变换和独立播放倍率字段。
        /// </summary>
        public void Draw(VisualElement container, object data, EditorViewModel viewModel,
            InspectorFieldCommitController fieldCommitController)
        {
            if (data is not VfxSkillClipConfig clip) return;
            AddTitle(container, clip.Prefab != null ? clip.Prefab.name : "VFX Clip");
            ObjectField prefab = AddField(container, new ObjectField("Prefab")
            {
                objectType = typeof(GameObject), allowSceneObjects = false, value = clip.Prefab
            });
            ObjectField marker = AddField(container, new ObjectField("挂点")
            {
                objectType = typeof(MarkerKey), allowSceneObjects = false, value = clip.MarkerKey
            });
            IntegerField start = AddField(container, new IntegerField("起始帧") { value = clip.StartFrame });
            IntegerField duration = AddField(container, new IntegerField("持续帧") { value = clip.DurationFrames });
            Vector3Field position = AddField(container, new Vector3Field("局部位置") { value = clip.LocalPosition });
            Vector3Field rotation = AddField(container, new Vector3Field("局部旋转") { value = clip.LocalEulerAngles });
            Vector3Field scale = AddField(container, new Vector3Field("局部缩放") { value = clip.LocalScale });
            FloatField playbackSpeed = AddField(container, new FloatField("播放速度") { value = clip.PlaybackSpeed });
            EnumField follow = AddField(container, new EnumField("跟随模式", clip.FollowMode));
            EnumField stop = AddField(container, new EnumField("结束模式", clip.StopMode));
            stop.tooltip = "ReturnToPoolAtEnd：到达结束帧时立即回收或销毁特效。\n"
                           + "StopEmissionAtEnd：到达结束帧时停止发射，已生成粒子继续播放至自然消失。\n"
                           + "KeepAlive：到达结束帧后不停止发射或回收，特效继续运行。";

            void Submit() => viewModel.EditItem(viewModel.SelectedTrack, clip, new VfxEditRequest(prefab.value as GameObject,
                marker.value as MarkerKey, start.value, duration.value, position.value, rotation.value, scale.value,
                playbackSpeed.value, (VfxFollowMode)follow.value, (VfxStopMode)stop.value));

            start.isDelayed = true;
            duration.isDelayed = true;
            position.SetIsDelayed(true);
            rotation.SetIsDelayed(true);
            scale.SetIsDelayed(true);

            playbackSpeed.isDelayed = true;
            prefab.RegisterValueChangedCallback(_ => Submit());
            marker.RegisterValueChangedCallback(_ => Submit());
            start.RegisterValueChangedCallback(_ => Submit());
            duration.RegisterValueChangedCallback(_ => Submit());
            position.RegisterValueChangedCallback(_ => Submit());
            rotation.RegisterValueChangedCallback(_ => Submit());
            scale.RegisterValueChangedCallback(_ => Submit());
            follow.RegisterValueChangedCallback(_ => Submit());
            playbackSpeed.RegisterValueChangedCallback(_ => Submit());
            stop.RegisterValueChangedCallback(_ => Submit());

            bool sceneEditing = viewModel.IsVfxSceneEditing(clip);
            VisualElement editRow = AddActionRow(container);
            Button beginEdit = sceneEditing
                ? new Button(() => viewModel.SelectVfxSceneEditProxy(clip)) { text = "选择编辑代理" }
                : new Button(() => viewModel.BeginVfxSceneEdit(clip)) { text = "在场景中编辑" };
            Button applyEdit = new(() => viewModel.ApplyVfxSceneEdit(clip)) { text = "应用预览 Transform" };
            Button cancelEdit = new(viewModel.CancelVfxSceneEdit) { text = "取消场景编辑" };
            applyEdit.SetEnabled(sceneEditing);
            cancelEdit.SetEnabled(sceneEditing);
            editRow.Add(beginEdit);
            editRow.Add(applyEdit);
            editRow.Add(cancelEdit);
            AddItemActions(container, viewModel);
        }
    }

    /// <summary>
    /// 绘制音频片段配置并提交类型化编辑请求。
    /// </summary>
    internal sealed class AudioInspectorDrawer : InspectorDrawer, IInspectorDrawer
    {

        /// <summary>
        /// 绘制音频素材、半开帧区间、音量和 Pitch 字段。
        /// </summary>
        public void Draw(VisualElement container, object data, EditorViewModel viewModel,
            InspectorFieldCommitController fieldCommitController)
        {
            if (data is not AudioSkillClipConfig clip) return;
            AddTitle(container, clip.AudioClip != null ? clip.AudioClip.name : "Audio Clip");
            ObjectField audio = AddField(container, new ObjectField("AudioClip")
            {
                objectType = typeof(AudioClip), allowSceneObjects = false, value = clip.AudioClip
            });
            IntegerField start = AddField(container, new IntegerField("起始帧") { value = clip.StartFrame });
            VisualElement durationRow = AddActionRow(container);
            IntegerField duration = new("持续帧") { value = clip.DurationFrames };
            duration.AddToClassList("inspector-field");
            durationRow.Add(duration);
            Button matchDuration = new(() => viewModel.MatchAudioDuration(clip))
            {
                text = "匹配音频长度",
                tooltip = "按 AudioClip 原始时长、当前技能 FPS 和 Pitch 恢复持续帧。"
            };
            matchDuration.SetEnabled(viewModel.CanMatchAudioDuration(clip));
            durationRow.Add(matchDuration);
            Slider volume = AddField(container, new Slider("音量", 0f, 1f) { value = clip.Volume });
            FloatField pitch = AddField(container, new FloatField("Pitch") { value = clip.Pitch });

            void Submit() => viewModel.EditItem(viewModel.SelectedTrack, clip, new AudioEditRequest(
                audio.value as AudioClip, start.value, duration.value, volume.value, pitch.value));

            bool volumeChanged = false;

            start.isDelayed = true;
            duration.isDelayed = true;
            pitch.isDelayed = true;

            audio.RegisterValueChangedCallback(_ => Submit());
            start.RegisterValueChangedCallback(_ => Submit());
            duration.RegisterValueChangedCallback(_ => Submit());
            volume.RegisterValueChangedCallback(_ => volumeChanged = true);
            volume.RegisterCallback<PointerUpEvent>(_ =>
            {
                if (!volumeChanged) return;
                volumeChanged  = false;
                Submit();
            });
            volume.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (!volumeChanged) return;
                volumeChanged  = false;
                Submit();
            });
            pitch.RegisterValueChangedCallback(_ => Submit());
            AddItemActions(container, viewModel);
        }
    }

    /// <summary>
    /// 绘制事件标记配置并提交类型化编辑请求。
    /// </summary>
    internal sealed class EventInspectorDrawer : InspectorDrawer, IInspectorDrawer
    {

        /// <summary>
        /// 绘制事件公共字段，以及当前 ValueType 对应的唯一活动参数字段。
        /// </summary>
        /// <param name="container">承载事件字段的原生 Inspector 容器。</param>
        /// <param name="data">当前选中的事件 Marker 配置。</param>
        /// <param name="viewModel">接收完整语义编辑请求的 ViewModel。</param>
        /// <param name="fieldCommitController">当前 Inspector 的字段提交控制器。</param>
        public void Draw(VisualElement container, object data, EditorViewModel viewModel,
            InspectorFieldCommitController fieldCommitController)
        {
            if (data is not SkillEventMarkerConfig marker) return;
            AddTitle(container, marker.DisplayName);
            IntegerField frame = AddField(container, new IntegerField("触发帧")
            {
                value = marker.Frame,
                isDelayed = true
            });
            TextField eventKey = AddField(container, new TextField("事件 Key")
            {
                value = marker.EventKey,
                isDelayed = true
            });
            TextField displayName = AddField(container, new TextField("显示名称")
            {
                value = marker.DisplayName,
                isDelayed = true
            });
            SkillEventValueType selectedValueType = marker.ValueType;
            EnumField valueType = AddField(container, new EnumField("参数类型", selectedValueType));

            // Inspector 只显示活动字段，但提交时始终携带完整联合体，保证切换类型不会丢失旧值。
            int intValue = marker.IntValue;
            string stringValue = marker.StringValue;
            long longValue = marker.LongValue;
            bool boolValue = marker.BoolValue;
            double doubleValue = marker.DoubleValue;
            float floatValue = marker.FloatValue;
            UnityEngine.Object objectValue = marker.ObjectValue;
            Action submit = () => viewModel.EditItem(viewModel.SelectedTrack, marker, new EventEditRequest(
                frame.value, eventKey.value, displayName.value, selectedValueType, intValue, stringValue,
                longValue, boolValue, doubleValue, floatValue, objectValue));

            frame.RegisterValueChangedCallback(_ => submit());
            eventKey.RegisterValueChangedCallback(_ => submit());
            displayName.RegisterValueChangedCallback(_ => submit());
            valueType.RegisterValueChangedCallback(evt =>
            {
                selectedValueType = (SkillEventValueType)evt.newValue;
                submit();
            });

            switch (selectedValueType)
            {
                case SkillEventValueType.Int:
                {
                    IntegerField field = AddField(container, new IntegerField("整数值")
                    {
                        value = intValue,
                        isDelayed = true
                    });
                    field.RegisterValueChangedCallback(evt =>
                    {
                        intValue = evt.newValue;
                        submit();
                    });
                    break;
                }
                case SkillEventValueType.String:
                {
                    TextField field = AddField(container, new TextField("字符串值")
                    {
                        value = stringValue,
                        multiline = true,
                        isDelayed = true
                    });
                    field.RegisterValueChangedCallback(evt =>
                    {
                        stringValue = evt.newValue;
                        submit();
                    });
                    break;
                }
                case SkillEventValueType.Long:
                {
                    LongField field = AddField(container, new LongField("长整数值")
                    {
                        value = longValue,
                        isDelayed = true
                    });
                    field.RegisterValueChangedCallback(evt =>
                    {
                        longValue = evt.newValue;
                        submit();
                    });
                    break;
                }
                case SkillEventValueType.Bool:
                {
                    Toggle field = AddField(container, new Toggle("布尔值") { value = boolValue });
                    field.RegisterValueChangedCallback(evt =>
                    {
                        boolValue = evt.newValue;
                        submit();
                    });
                    break;
                }
                case SkillEventValueType.Double:
                {
                    DoubleField field = AddField(container, new DoubleField("双精度值")
                    {
                        value = doubleValue,
                        isDelayed = true
                    });
                    field.RegisterValueChangedCallback(evt =>
                    {
                        doubleValue = evt.newValue;
                        submit();
                    });
                    break;
                }
                case SkillEventValueType.Float:
                {
                    FloatField field = AddField(container, new FloatField("单精度值")
                    {
                        value = floatValue,
                        isDelayed = true
                    });
                    field.RegisterValueChangedCallback(evt =>
                    {
                        floatValue = evt.newValue;
                        submit();
                    });
                    break;
                }
                case SkillEventValueType.Object:
                {
                    ObjectField field = AddField(container, new ObjectField("对象值")
                    {
                        objectType = typeof(UnityEngine.Object),
                        allowSceneObjects = false,
                        value = objectValue
                    });
                    field.RegisterValueChangedCallback(evt =>
                    {
                        objectValue = evt.newValue;
                        submit();
                    });
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(selectedValueType), selectedValueType,
                        "未知的技能事件参数类型。");
            }

            AddItemActions(container, viewModel);
        }
    }
    #endregion
}
#endif
