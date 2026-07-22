#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.MVVM;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 绑定技能配置、预览设置、播放控制和 Canvas 缩放输入的顶部工具栏。
    /// </summary>
    internal sealed class ToolbarView : IView<EditorViewModel>
    {
        #region 控件与依赖

        private readonly VisualElement root;
        private readonly CanvasModel canvasModel;
        private EditorViewModel viewModel;
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

        #region 绑定生命周期

        /// <summary>
        /// 创建并初始化 ToolbarView。
        /// </summary>
        public ToolbarView(VisualElement root, CanvasModel canvasModel)
        {
            this.root = root ?? throw new System.ArgumentNullException(nameof(root));
            this.canvasModel = canvasModel ?? throw new System.ArgumentNullException(nameof(canvasModel));
        }

        /// <summary>
        /// 绑定 ViewModel、注册事件并执行首次界面刷新。
        /// </summary>
        public void Bind(EditorViewModel model)
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
            canvasModel.ZoomChanged -= RefreshZoom;
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

        #region 控件初始化

        // 从主 UXML 查询并缓存工具栏控件。
        private void QueryElements()
        {
            configField = root.Q<ObjectField>("ConfigField");
            previewSceneField = root.Q<ObjectField>("PreviewSceneField");
            previewActorField = root.Q<ObjectField>("PreviewActorField");
            frameRateField = root.Q<IntegerField>("FrameRateField");
            durationField = root.Q<IntegerField>("DurationField");
            currentFrameField = root.Q<IntegerField>("CurrentFrameField");
            zoomSlider = root.Q<Slider>("ZoomSlider");
            zoomSlider.lowValue = canvasModel.MinimumPixelsPerFrame;
            zoomSlider.highValue = canvasModel.MaximumPixelsPerFrame;
            createConfigButton = root.Q<Button>("CreateConfigButton");
            trimButton = root.Q<Button>("TrimButton");
            openPreviewSceneButton = root.Q<Button>("OpenPreviewSceneButton");
            playPauseButton = root.Q<Button>("PlayPauseButton");
            stopButton = root.Q<Button>("StopButton");
            previousFrameButton = root.Q<Button>("PreviousFrameButton");
            nextFrameButton = root.Q<Button>("NextFrameButton");
        }

        // 配置 ObjectField 接受的 Unity 对象类型。
        private void ConfigureFields()
        {
            configField.objectType = typeof(SkillConfig);
            configField.allowSceneObjects = false;
            previewSceneField.objectType = typeof(SceneAsset);
            previewSceneField.allowSceneObjects = false;
            previewActorField.objectType = typeof(GameObject);
            previewActorField.allowSceneObjects = true;
        }

        // 注册工具栏控件及 ViewModel 的状态变更回调。
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
            canvasModel.ZoomChanged += RefreshZoom;
        }

        #endregion

        #region 状态刷新

        // 首次绑定时依次刷新工具栏的全部显示状态。
        private void RefreshAll()
        {
            RefreshConfig();
            RefreshPlayhead();
            RefreshPlayback();
            RefreshSettings();
            RefreshZoom();
        }

        // 刷新当前配置、帧率和总帧数字段。
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

        // 刷新贯穿时间轴的播放头位置。
        private void RefreshPlayhead() => currentFrameField.SetValueWithoutNotify(viewModel.CurrentFrame);

        // 刷新播放按钮的播放或暂停文字。
        private void RefreshPlayback() => playPauseButton.text = viewModel.IsPlaying ? "暂停" : "播放";

        // 刷新固定预览场景和演示角色字段。
        private void RefreshSettings()
        {
            previewSceneField.SetValueWithoutNotify(viewModel.PreviewScene);
            previewActorField.SetValueWithoutNotify(viewModel.PreviewActor);
        }
        // 缩放变化后同步 Slider，避免状态刷新反向触发输入回调。
        private void RefreshZoom() => zoomSlider.SetValueWithoutNotify(canvasModel.PixelsPerFrame);

        #endregion

        #region UI 事件处理

        // 处理新建技能配置按钮点击事件。
        private void OnCreateConfigClicked()
        {
            string path = EditorUtility.SaveFilePanelInProject("新建技能配置", "SkillConfig", "asset", "选择保存位置");
            if (string.IsNullOrEmpty(path)) return;
            viewModel.CreateConfig(path);
        }

        // 根据当前播放状态提交播放或暂停意图。
        private void OnPlayPauseClicked()
        {
            if (viewModel.IsPlaying) viewModel.Pause();
            else viewModel.Play();
        }

        // 把 Config ObjectField 变化转换为打开配置意图。
        private void OnConfigChanged(ChangeEvent<UnityEngine.Object> evt) => viewModel.OpenConfig(evt.newValue as SkillConfig);

        // 把预览场景字段变化写入固定编辑器设置。
        private void OnPreviewSceneChanged(ChangeEvent<UnityEngine.Object> evt) => viewModel.SetPreviewScene(evt.newValue as SceneAsset);

        // 把演示角色字段变化写入固定编辑器设置。
        private void OnPreviewActorChanged(ChangeEvent<UnityEngine.Object> evt) => viewModel.SetPreviewActor(evt.newValue as GameObject);

        // 把帧率输入转换为保持实际时间的重采样请求。
        private void OnFrameRateChanged(ChangeEvent<int> evt) => viewModel.ChangeFrameRate(evt.newValue);

        // 把总帧输入转换为技能时间范围修改请求。
        private void OnDurationChanged(ChangeEvent<int> evt) => viewModel.SetDurationFrames(evt.newValue);

        // 把当前帧输入转换为播放头定位请求。
        private void OnCurrentFrameChanged(ChangeEvent<int> evt) => viewModel.SetCurrentFrame(evt.newValue);

        // 把 Slider 输入写入 Canvas 表现模型的每帧像素宽度。
        private void OnZoomChanged(ChangeEvent<float> evt) => canvasModel.SetZoom(evt.newValue);

        #endregion
    }
}
#endif
