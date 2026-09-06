#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.EditorExtensions;

namespace RPG.Character.Editor
{
    /// <summary>角色配置编辑器右侧摘要卡、分区详情和头像提交 View。</summary>
    internal sealed class CharacterConfigDetailsView : IDisposable
    {
        #region 依赖字段

        private const string SummaryUxmlPath = "Assets/Scripts/Character/Editor/Style/CharacterConfigSummary.uxml";
        private const string DetailsUxmlPath = "Assets/Scripts/Character/Editor/Style/CharacterConfigDetails.uxml";
        private readonly VisualElement summaryHost;
        private readonly ScrollView detailsScrollView;
        private readonly VisualElement emptyState;
        private readonly VisualTreeAsset summaryTemplate;
        private readonly VisualTreeAsset detailsTemplate;
        private VisualElement serializedObjectTracker;
        private CharacterConfig selectedConfig;
        private SerializedObject serializedObject;
        private ObjectField sideIconField;
        private ObjectField avatarField;
        private Image sideIconPreviewImage;
        private Label sideIconPreviewFallback;
        private Image avatarPreviewImage;
        private Label avatarPreviewFallback;
        private bool suppressCallbacks;
        private bool disposed;

        #endregion

        #region 属性

        /// <summary>获取当前详情绑定的角色配置。</summary>
        internal CharacterConfig SelectedConfig => selectedConfig;

        #endregion

        #region 事件

        /// <summary>侧面头像选择变化事件。</summary>
        internal event Action<CharacterConfig, Sprite> SideIconChanged;
        /// <summary>角色头像选择变化事件。</summary>
        internal event Action<CharacterConfig, Sprite> AvatarChanged;
        /// <summary>角色配置序列化字段变化事件。</summary>
        internal event Action<CharacterConfig, string> PropertiesChanged;

        #endregion

        #region 生命周期

        /// <summary>加载摘要和详情模板，并取得详情容器。</summary>
        /// <param name="root">窗口根节点。</param>
        public CharacterConfigDetailsView(VisualElement root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            summaryHost = Require<VisualElement>(root, "Summary");
            detailsScrollView = Require<ScrollView>(root, "DetailsScrollView");
            emptyState = Require<VisualElement>(root, "EmptyDetailsState");
            summaryTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SummaryUxmlPath);
            detailsTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DetailsUxmlPath);
            if (summaryTemplate == null) throw new InvalidOperationException($"角色配置窗口缺少摘要 UXML：{SummaryUxmlPath}。");
            if (detailsTemplate == null) throw new InvalidOperationException($"角色配置窗口缺少详情 UXML：{DetailsUxmlPath}。");
            detailsScrollView.RegisterCallback<SerializedPropertyChangeEvent>(OnSerializedPropertyChanged);
            SetVisible(false);
        }

        /// <summary>解除绑定、取消头像回调并释放当前 SerializedObject。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (sideIconField != null) sideIconField.UnregisterValueChangedCallback(OnSideIconFieldChanged);
            if (avatarField != null) avatarField.UnregisterValueChangedCallback(OnAvatarFieldChanged);
            detailsScrollView.UnregisterCallback<SerializedPropertyChangeEvent>(OnSerializedPropertyChanged);
            detailsScrollView.Unbind();
            serializedObjectTracker?.RemoveFromHierarchy();
            serializedObjectTracker = null;
            serializedObject?.Dispose();
            serializedObject = null;
            selectedConfig = null;
        }

        #endregion

        #region 绑定与刷新

        /// <summary>绑定角色配置并创建固定摘要和详情分区。</summary>
        /// <param name="config">待绑定角色配置，可为空。</param>
        internal void Bind(CharacterConfig config)
        {
            suppressCallbacks = true;
            try
            {
                detailsScrollView.Unbind();
                serializedObjectTracker?.RemoveFromHierarchy();
                serializedObjectTracker = null;
                serializedObject?.Dispose();
                serializedObject = null;
                summaryHost.Clear();
                detailsScrollView.Clear();
                selectedConfig = config;
                SetVisible(config != null);
                if (config == null) return;

                serializedObject = new SerializedObject(config);
                serializedObject.UpdateIfRequiredOrScript();
                summaryTemplate.CloneTree(summaryHost);
                detailsTemplate.CloneTree(detailsScrollView.contentContainer);
                sideIconField = Require<ObjectField>(detailsScrollView, "SideIconField");
                avatarField = Require<ObjectField>(detailsScrollView, "AvatarField");
                sideIconPreviewImage = Require<Image>(detailsScrollView, "SideIconPreviewImage");
                sideIconPreviewFallback = Require<Label>(detailsScrollView, "SideIconPreviewFallback");
                avatarPreviewImage = Require<Image>(detailsScrollView, "AvatarPreviewImage");
                avatarPreviewFallback = Require<Label>(detailsScrollView, "AvatarPreviewFallback");
                ConfigurePreviewField(sideIconField, config.EditorSideIcon, true);
                ConfigurePreviewField(avatarField, config.EditorAvatar, false);

                PropertyField characterIdField = detailsScrollView.Q<PropertyField>("CharacterIdField");
                characterIdField?.SetEnabled(false);
                TextField nameField = detailsScrollView.Q<TextField>("NameField");
                if (nameField != null) nameField.isDelayed = false;
                detailsScrollView.Bind(serializedObject);
                CreateSerializedObjectTracker();
                ConfigureCollection(detailsScrollView.Q<PropertyField>("InitialAttributeSetsField"), "暂无初始属性集");
                ConfigureCollection(detailsScrollView.Q<PropertyField>("AbilityInputBindingsField"), "暂无能力输入绑定");
                RefreshPreviewImages();
                RefreshSummary();
            }
            finally
            {
                suppressCallbacks = false;
            }
        }

        /// <summary>刷新当前 SerializedObject 和所有视觉内容，不写回资产。</summary>
        internal void Refresh()
        {
            if (selectedConfig == null || serializedObject == null) return;
            suppressCallbacks = true;
            try
            {
                serializedObject.UpdateIfRequiredOrScript();
                sideIconField?.SetValueWithoutNotify(selectedConfig.EditorSideIcon);
                avatarField?.SetValueWithoutNotify(selectedConfig.EditorAvatar);
                RefreshPreviewImages();
                RefreshSummary();
            }
            finally
            {
                suppressCallbacks = false;
            }
        }

        /// <summary>恢复指定头像字段的模型引用。</summary>
        /// <param name="sideIcon">是否恢复侧面头像。</param>
        internal void RestorePreview(bool sideIcon)
        {
            if (selectedConfig == null) return;
            if (sideIcon) sideIconField?.SetValueWithoutNotify(selectedConfig.EditorSideIcon);
            else avatarField?.SetValueWithoutNotify(selectedConfig.EditorAvatar);
            RefreshPreviewImages();
        }

        #endregion

        #region 摘要与详情

        /// <summary>刷新摘要卡的头像、名称、标识、地址和星级状态。</summary>
        private void RefreshSummary()
        {
            VisualElement card = summaryHost.Q<VisualElement>("SummaryCard");
            if (card == null || selectedConfig == null) return;
            Image image = card.Q<Image>("AvatarImage");
            Label fallback = card.Q<Label>("AvatarFallback");
            image.sprite = selectedConfig.EditorAvatar;
            image.scaleMode = ScaleMode.ScaleToFit;
            image.style.display = image.sprite == null ? DisplayStyle.None : DisplayStyle.Flex;
            fallback.style.display = image.sprite == null ? DisplayStyle.Flex : DisplayStyle.None;
            fallback.text = "♟";
            card.Q<Label>("Name").text = string.IsNullOrWhiteSpace(selectedConfig.Name) ? "未命名角色" : selectedConfig.Name;
            card.Q<Label>("Rarity").text = ConfigEditorRarityPresentation.GetRarityStars((int)selectedConfig.Rarity);
            card.Q<Label>("Id").text = $"角色标识：{selectedConfig.CharacterId}";
            card.Q<Label>("Prefab").text = $"Prefab 地址：{selectedConfig.PrefabAddress}";
            ConfigEditorRarityPresentation.EnableRarityClass(card, "character-config-summary", (int)selectedConfig.Rarity);
        }

        /// <summary>为原生集合 PropertyField 配置增删、重排和中文空状态。</summary>
        /// <param name="propertyField">目标集合字段。</param>
        /// <param name="emptyText">空集合提示。</param>
        private static void ConfigureCollection(PropertyField propertyField, string emptyText)
        {
            if (propertyField == null) return;
            propertyField.Query<ListView>().ForEach(listView =>
            {
                listView.showBoundCollectionSize = false;
                listView.showFoldoutHeader = true;
                listView.showAddRemoveFooter = true;
                listView.reorderable = true;
                listView.reorderMode = ListViewReorderMode.Simple;
                listView.Query<Label>().ForEach(label =>
                {
                    if (label.text == "List is empty") label.text = emptyText;
                });
            });
        }

        /// <summary>配置不直接绑定运行时字段的预览 ObjectField。</summary>
        /// <param name="field">UXML 中的预览字段。</param>
        /// <param name="sprite">当前 Sprite。</param>
        /// <param name="sideIcon">是否为侧面头像。</param>
        private void ConfigurePreviewField(ObjectField field, Sprite sprite, bool sideIcon)
        {
            if (field == null) return;
            field.objectType = typeof(Sprite);
            field.allowSceneObjects = false;
            field.SetValueWithoutNotify(sprite);
            // 显式选择回调，避免 Unity 旧版 C# 编译器对条件方法组推断不一致。
            if (sideIcon) field.RegisterValueChangedCallback(OnSideIconFieldChanged);
            else field.RegisterValueChangedCallback(OnAvatarFieldChanged);
        }

        /// <summary>刷新两张图片卡片中的大图和空状态。</summary>
        private void RefreshPreviewImages()
        {
            RefreshPreviewImage(sideIconPreviewImage, sideIconPreviewFallback, selectedConfig?.EditorSideIcon, "未选择侧面头像");
            RefreshPreviewImage(avatarPreviewImage, avatarPreviewFallback, selectedConfig?.EditorAvatar, "未选择角色头像");
        }

        /// <summary>设置指定预览框的 Sprite、缩放模式和空状态。</summary>
        /// <param name="image">图片控件。</param>
        /// <param name="fallback">空状态标签。</param>
        /// <param name="sprite">待展示 Sprite。</param>
        /// <param name="emptyText">Sprite 为空时的提示。</param>
        private static void RefreshPreviewImage(Image image, Label fallback, Sprite sprite, string emptyText)
        {
            if (image == null || fallback == null) return;
            image.scaleMode = ScaleMode.ScaleToFit;
            image.sprite = sprite;
            image.style.display = sprite == null ? DisplayStyle.None : DisplayStyle.Flex;
            fallback.text = emptyText;
            fallback.style.display = sprite == null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>切换摘要、详情与空状态的显示。</summary>
        /// <param name="hasSelection">是否存在选中配置。</param>
        private void SetVisible(bool hasSelection)
        {
            summaryHost.style.display = hasSelection ? DisplayStyle.Flex : DisplayStyle.None;
            detailsScrollView.style.display = hasSelection ? DisplayStyle.Flex : DisplayStyle.None;
            emptyState.style.display = hasSelection ? DisplayStyle.None : DisplayStyle.Flex;
        }

        #endregion

        #region 事件处理

        /// <summary>转发侧面头像变化。</summary>
        private void OnSideIconFieldChanged(ChangeEvent<UnityEngine.Object> eventData)
        {
            if (!suppressCallbacks) SideIconChanged?.Invoke(selectedConfig, eventData.newValue as Sprite);
        }

        /// <summary>转发角色头像变化。</summary>
        private void OnAvatarFieldChanged(ChangeEvent<UnityEngine.Object> eventData)
        {
            if (!suppressCallbacks) AvatarChanged?.Invoke(selectedConfig, eventData.newValue as Sprite);
        }

        /// <summary>接收原生绑定字段的变化并立即刷新摘要和列表投影。</summary>
        /// <param name="eventData">序列化属性变化事件。</param>
        private void OnSerializedPropertyChanged(SerializedPropertyChangeEvent eventData)
        {
            if (suppressCallbacks || selectedConfig == null || serializedObject == null) return;
            serializedObject.UpdateIfRequiredOrScript();
            RefreshSummary();
            PropertiesChanged?.Invoke(selectedConfig, eventData?.changedProperty?.propertyPath ?? string.Empty);
        }

        /// <summary>创建隐藏 SerializedObject 跟踪节点，覆盖直接绑定控件的即时变化。</summary>
        private void CreateSerializedObjectTracker()
        {
            serializedObjectTracker = new VisualElement { name = "CharacterConfigSerializedObjectTracker" };
            serializedObjectTracker.style.display = DisplayStyle.None;
            detailsScrollView.Add(serializedObjectTracker);
            serializedObjectTracker.TrackSerializedObjectValue(serializedObject, OnSerializedObjectChanged);
        }

        /// <summary>处理 SerializedObject 任意字段变化并立即通知列表投影。</summary>
        /// <param name="changedSerializedObject">发生变化的 SerializedObject。</param>
        private void OnSerializedObjectChanged(SerializedObject changedSerializedObject)
        {
            if (suppressCallbacks || changedSerializedObject == null || changedSerializedObject != serializedObject || selectedConfig == null) return;
            changedSerializedObject.UpdateIfRequiredOrScript();
            RefreshPreviewImages();
            RefreshSummary();
            PropertiesChanged?.Invoke(selectedConfig, string.Empty);
        }

        #endregion

        #region 内部辅助

        /// <summary>从窗口根节点取得必需控件。</summary>
        /// <typeparam name="T">控件类型。</typeparam>
        /// <param name="root">搜索根节点。</param>
        /// <param name="name">控件名称。</param>
        /// <returns>目标控件。</returns>
        private static T Require<T>(VisualElement root, string name) where T : VisualElement
        {
            T element = root.Q<T>(name);
            if (element == null) throw new InvalidOperationException($"角色配置窗口缺少控件：{name}。");
            return element;
        }

        #endregion
    }
}
#endif
