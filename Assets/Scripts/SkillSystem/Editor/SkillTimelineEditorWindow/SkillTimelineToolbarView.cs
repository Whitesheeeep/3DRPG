#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    internal sealed class SkillTimelineToolbarView
    {
        #region Controls and dependencies

        private readonly VisualElement root;
        private readonly SkillTimelineViewportController viewport;
        private SkillTimelineEditorViewModel viewModel;
        private ObjectField configField;
        private ObjectField previewSceneField;
        private ObjectField previewActorField;
        private IntegerField frameRateField;
        private IntegerField durationField;
        private IntegerField currentFrameField;
        private Slider zoomSlider;
        private Button createConfigButton;
        private Button trimButton;
        private Button openPreviewSceneButton;
        private Button playPauseButton;
        private Button stopButton;
        private Button previousFrameButton;
        private Button nextFrameButton;

        #endregion

        #region Lifecycle and binding

        /// <summary>
        /// 创建并初始化 SkillTimelineToolbarView。
        /// </summary>
        public SkillTimelineToolbarView(VisualElement root, SkillTimelineViewportController viewport)
        {
            this.root = root;
            this.viewport = viewport;
        }

        /// <summary>
        /// 绑定 ViewModel、注册事件并执行首次界面刷新。
        /// </summary>
        public void Bind(SkillTimelineEditorViewModel model)
        {
            viewModel = model;
            QueryElements();
            ConfigureFields();
            RegisterEvents();
            RefreshAll();
        }

        /// <summary>
        /// 解除事件绑定并清空持有的界面引用。
        /// </summary>
        public void Unbind()
        {
            if (viewModel == null) return;
            viewModel.TimelineChanged -= RefreshConfig;
            viewModel.PlayheadChanged -= RefreshPlayhead;
            viewModel.PlaybackChanged -= RefreshPlayback;
            viewModel.SettingsChanged -= RefreshSettings;
            viewport.ViewportChanged -= RefreshViewport;
            createConfigButton.clicked -= OnCreateConfigClicked;
            configField.UnregisterValueChangedCallback(OnConfigChanged);
            previewSceneField.UnregisterValueChangedCallback(OnPreviewSceneChanged);
            previewActorField.UnregisterValueChangedCallback(OnPreviewActorChanged);
            frameRateField.UnregisterValueChangedCallback(OnFrameRateChanged);
            durationField.UnregisterValueChangedCallback(OnDurationChanged);
            currentFrameField.UnregisterValueChangedCallback(OnCurrentFrameChanged);
            zoomSlider.UnregisterValueChangedCallback(OnZoomChanged);
            trimButton.clicked -= viewModel.TrimToContent;
            openPreviewSceneButton.clicked -= viewModel.OpenPreviewScene;
            playPauseButton.clicked -= OnPlayPauseClicked;
            stopButton.clicked -= viewModel.Stop;
            previousFrameButton.clicked -= viewModel.StepPreviousFrame;
            nextFrameButton.clicked -= viewModel.StepNextFrame;
            viewModel = null;
        }

        #endregion

        #region Control setup

        /// <summary>
        /// 从主 UXML 查询并缓存工具栏控件。
        /// </summary>
        private void QueryElements()
        {
            configField = root.Q<ObjectField>("ConfigField");
            previewSceneField = root.Q<ObjectField>("PreviewSceneField");
            previewActorField = root.Q<ObjectField>("PreviewActorField");
            frameRateField = root.Q<IntegerField>("FrameRateField");
            durationField = root.Q<IntegerField>("DurationField");
            currentFrameField = root.Q<IntegerField>("CurrentFrameField");
            zoomSlider = root.Q<Slider>("ZoomSlider");
            zoomSlider.lowValue = viewport.MinimumPixelsPerFrame;
            zoomSlider.highValue = viewport.MaximumPixelsPerFrame;
            createConfigButton = root.Q<Button>("CreateConfigButton");
            trimButton = root.Q<Button>("TrimButton");
            openPreviewSceneButton = root.Q<Button>("OpenPreviewSceneButton");
            playPauseButton = root.Q<Button>("PlayPauseButton");
            stopButton = root.Q<Button>("StopButton");
            previousFrameButton = root.Q<Button>("PreviousFrameButton");
            nextFrameButton = root.Q<Button>("NextFrameButton");
        }

        /// <summary>
        /// 配置 ObjectField 接受的 Unity 对象类型。
        /// </summary>
        private void ConfigureFields()
        {
            configField.objectType = typeof(SkillConfig);
            configField.allowSceneObjects = false;
            previewSceneField.objectType = typeof(SceneAsset);
            previewSceneField.allowSceneObjects = false;
            previewActorField.objectType = typeof(GameObject);
            previewActorField.allowSceneObjects = true;
        }

        /// <summary>
        /// 注册工具栏控件及 ViewModel 的状态变更回调。
        /// </summary>
        private void RegisterEvents()
        {
            configField.RegisterValueChangedCallback(OnConfigChanged);
            previewSceneField.RegisterValueChangedCallback(OnPreviewSceneChanged);
            previewActorField.RegisterValueChangedCallback(OnPreviewActorChanged);
            frameRateField.RegisterValueChangedCallback(OnFrameRateChanged);
            durationField.RegisterValueChangedCallback(OnDurationChanged);
            currentFrameField.RegisterValueChangedCallback(OnCurrentFrameChanged);
            zoomSlider.RegisterValueChangedCallback(OnZoomChanged);
            createConfigButton.clicked += OnCreateConfigClicked;
            trimButton.clicked += viewModel.TrimToContent;
            openPreviewSceneButton.clicked += viewModel.OpenPreviewScene;
            playPauseButton.clicked += OnPlayPauseClicked;
            stopButton.clicked += viewModel.Stop;
            previousFrameButton.clicked += viewModel.StepPreviousFrame;
            nextFrameButton.clicked += viewModel.StepNextFrame;
            viewModel.TimelineChanged += RefreshConfig;
            viewModel.PlayheadChanged += RefreshPlayhead;
            viewModel.PlaybackChanged += RefreshPlayback;
            viewModel.SettingsChanged += RefreshSettings;
            viewport.ViewportChanged += RefreshViewport;
        }

        #endregion

        #region State refresh

        /// <summary>
        /// 刷新工具栏全部状态。
        /// </summary>
        private void RefreshAll()
        {
            RefreshConfig();
            RefreshPlayhead();
            RefreshPlayback();
            RefreshSettings();
            RefreshViewport();
        }

        /// <summary>
        /// 刷新当前配置、帧率和总帧数字段。
        /// </summary>
        private void RefreshConfig()
        {
            SkillConfig config = viewModel.CurrentConfig;
            configField.SetValueWithoutNotify(config);
            frameRateField.SetValueWithoutNotify(config != null ? config.FrameRate : 30);
            durationField.SetValueWithoutNotify(config != null ? config.DurationFrames : 1);
            bool enabled = config != null;
            frameRateField.SetEnabled(enabled);
            durationField.SetEnabled(enabled);
            trimButton.SetEnabled(enabled);
            playPauseButton.SetEnabled(enabled);
            stopButton.SetEnabled(enabled);
            previousFrameButton.SetEnabled(enabled);
            nextFrameButton.SetEnabled(enabled);
        }

        /// <summary>
        /// 刷新贯穿时间轴的播放头位置。
        /// </summary>
        private void RefreshPlayhead() => currentFrameField.SetValueWithoutNotify(viewModel.CurrentFrame);
        /// <summary>
        /// 刷新播放按钮和当前帧字段。
        /// </summary>
        private void RefreshPlayback() => playPauseButton.text = viewModel.IsPlaying ? "暂停" : "播放";
        /// <summary>
        /// 刷新固定预览场景和演示角色字段。
        /// </summary>
        private void RefreshSettings()
        {
            previewSceneField.SetValueWithoutNotify(viewModel.PreviewScene);
            previewActorField.SetValueWithoutNotify(viewModel.PreviewActor);
        }
        /// <summary>
        /// 在缩放或滚动后同步标尺、轨道内容和播放头。
        /// </summary>
        private void RefreshViewport() => zoomSlider.SetValueWithoutNotify(viewport.PixelsPerFrame);

        #endregion

        #region UI event handlers

        /// <summary>
        /// 处理新建技能配置按钮点击事件。
        /// </summary>
        private void OnCreateConfigClicked()
        {
            string path = EditorUtility.SaveFilePanelInProject("新建技能配置", "SkillConfig", "asset", "选择保存位置");
            if (string.IsNullOrEmpty(path)) return;
            viewModel.CreateConfig(path);
        }

        /// <summary>
        /// 处理播放或暂停按钮点击事件。
        /// </summary>
        private void OnPlayPauseClicked()
        {
            if (viewModel.IsPlaying) viewModel.Pause();
            else viewModel.Play();
        }

        /// <summary>
        /// 处理当前技能配置切换完成事件。
        /// </summary>
        private void OnConfigChanged(ChangeEvent<UnityEngine.Object> evt) => viewModel.OpenConfig(evt.newValue as SkillConfig);
        /// <summary>
        /// 处理固定预览场景字段变更。
        /// </summary>
        private void OnPreviewSceneChanged(ChangeEvent<UnityEngine.Object> evt) => viewModel.SetPreviewScene(evt.newValue as SceneAsset);
        /// <summary>
        /// 处理固定演示角色字段变更。
        /// </summary>
        private void OnPreviewActorChanged(ChangeEvent<UnityEngine.Object> evt) => viewModel.SetPreviewActor(evt.newValue as GameObject);
        /// <summary>
        /// 处理帧率输入字段变更事件。
        /// </summary>
        private void OnFrameRateChanged(ChangeEvent<int> evt) => viewModel.ChangeFrameRate(evt.newValue);
        /// <summary>
        /// 处理总帧数输入字段变更事件。
        /// </summary>
        private void OnDurationChanged(ChangeEvent<int> evt) => viewModel.SetDurationFrames(evt.newValue);
        /// <summary>
        /// 处理当前帧输入字段变更事件。
        /// </summary>
        private void OnCurrentFrameChanged(ChangeEvent<int> evt) => viewModel.SetCurrentFrame(evt.newValue);
        /// <summary>
        /// 处理工具栏缩放值变更。
        /// </summary>
        private void OnZoomChanged(ChangeEvent<float> evt) => viewport.SetZoom(evt.newValue);

        #endregion
    }
}
#endif
