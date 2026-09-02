#if UNITY_EDITOR
using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.ItemSystem.Editor
{
    /// <summary>物品配置窗口顶部工具栏的子 View。</summary>
    internal sealed class ItemConfigToolbarView : IDisposable
    {
        #region 字段

        private readonly VisualElement root;
        private readonly ObjectField databaseField;
        private bool disposed;

        #endregion

        #region 事件

        /// <summary>数据库选择事件。</summary>
        internal event Action<ItemDatabase> DatabaseChanged;
        /// <summary>新建可堆叠物品请求。</summary>
        internal event Action NewStackableRequested;
        /// <summary>新建武器请求。</summary>
        internal event Action NewWeaponRequested;
        /// <summary>新建养成道具请求。</summary>
        internal event Action NewDevelopmentItemRequested;
        /// <summary>新建圣遗物请求。</summary>
        internal event Action NewArtifactRequested;
        /// <summary>复制当前定义请求。</summary>
        internal event Action DuplicateRequested;
        /// <summary>移出数据库请求。</summary>
        internal event Action RemoveRequested;
        /// <summary>删除资产请求。</summary>
        internal event Action DeleteRequested;
        /// <summary>应用类型默认值请求。</summary>
        internal event Action ApplyDefaultsRequested;
        /// <summary>验证数据库请求。</summary>
        internal event Action ValidateRequested;
        /// <summary>定位资产请求。</summary>
        internal event Action PingRequested;

        #endregion

        #region 生命周期

        /// <summary>查询工具栏控件并注册用户意图回调。</summary>
        /// <param name="root">工具栏根节点。</param>
        internal ItemConfigToolbarView(VisualElement root)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            databaseField = Require<ObjectField>("DatabaseField");
            databaseField.objectType = typeof(ItemDatabase);
            databaseField.allowSceneObjects = false;
            databaseField.RegisterValueChangedCallback(OnDatabaseChanged);
            RegisterButton("NewStackableButton", OnNewStackableClicked);
            RegisterButton("NewWeaponButton", OnNewWeaponClicked);
            RegisterButton("NewDevelopmentItemButton", OnNewDevelopmentItemClicked);
            RegisterButton("NewArtifactButton", OnNewArtifactClicked);
            RegisterButton("DuplicateButton", OnDuplicateClicked);
            RegisterButton("RemoveButton", OnRemoveClicked);
            RegisterButton("DeleteButton", OnDeleteClicked);
            RegisterButton("ApplyDefaultsButton", OnApplyDefaultsClicked);
            RegisterButton("ValidateButton", OnValidateClicked);
            RegisterButton("PingButton", OnPingClicked);
        }

        /// <summary>解除工具栏控件回调。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            databaseField.UnregisterValueChangedCallback(OnDatabaseChanged);
            UnregisterButton("NewStackableButton", OnNewStackableClicked);
            UnregisterButton("NewWeaponButton", OnNewWeaponClicked);
            UnregisterButton("NewDevelopmentItemButton", OnNewDevelopmentItemClicked);
            UnregisterButton("NewArtifactButton", OnNewArtifactClicked);
            UnregisterButton("DuplicateButton", OnDuplicateClicked);
            UnregisterButton("RemoveButton", OnRemoveClicked);
            UnregisterButton("DeleteButton", OnDeleteClicked);
            UnregisterButton("ApplyDefaultsButton", OnApplyDefaultsClicked);
            UnregisterButton("ValidateButton", OnValidateClicked);
            UnregisterButton("PingButton", OnPingClicked);
        }

        #endregion

        #region 状态呈现

        /// <summary>设置数据库字段而不重新发送用户选择事件。</summary>
        /// <param name="database">数据库。</param>
        internal void SetDatabase(ItemDatabase database) => databaseField.SetValueWithoutNotify(database);

        #endregion

        #region 内部辅助

        /// <summary>处理数据库字段变化。</summary>
        /// <param name="change">对象变化事件。</param>
        private void OnDatabaseChanged(ChangeEvent<UnityEngine.Object> change) => DatabaseChanged?.Invoke(change.newValue as ItemDatabase);

        /// <summary>转发新建可堆叠物品请求。</summary>
        private void OnNewStackableClicked() => NewStackableRequested?.Invoke();

        /// <summary>转发新建武器请求。</summary>
        private void OnNewWeaponClicked() => NewWeaponRequested?.Invoke();

        /// <summary>转发新建养成道具请求。</summary>
        private void OnNewDevelopmentItemClicked() => NewDevelopmentItemRequested?.Invoke();

        /// <summary>转发新建圣遗物请求。</summary>
        private void OnNewArtifactClicked() => NewArtifactRequested?.Invoke();

        /// <summary>转发复制请求。</summary>
        private void OnDuplicateClicked() => DuplicateRequested?.Invoke();

        /// <summary>转发移出数据库请求。</summary>
        private void OnRemoveClicked() => RemoveRequested?.Invoke();

        /// <summary>转发删除请求。</summary>
        private void OnDeleteClicked() => DeleteRequested?.Invoke();

        /// <summary>转发应用默认值请求。</summary>
        private void OnApplyDefaultsClicked() => ApplyDefaultsRequested?.Invoke();

        /// <summary>转发验证请求。</summary>
        private void OnValidateClicked() => ValidateRequested?.Invoke();

        /// <summary>转发定位请求。</summary>
        private void OnPingClicked() => PingRequested?.Invoke();

        /// <summary>查询工具栏范围内的控件。</summary>
        /// <typeparam name="TElement">控件类型。</typeparam>
        /// <param name="name">UXML 名称。</param>
        /// <returns>找到的控件。</returns>
        private TElement Require<TElement>(string name) where TElement : VisualElement
        {
            TElement element = root.Q<TElement>(name);
            if (element == null) throw new InvalidOperationException($"Item 配置窗口工具栏缺少 UXML 控件：{name}。");
            return element;
        }

        /// <summary>注册一个按钮回调。</summary>
        /// <param name="name">按钮名称。</param>
        /// <param name="callback">点击回调。</param>
        private void RegisterButton(string name, Action callback) => Require<Button>(name).clicked += callback;

        /// <summary>解除一个按钮回调。</summary>
        /// <param name="name">按钮名称。</param>
        /// <param name="callback">点击回调。</param>
        private void UnregisterButton(string name, Action callback)
        {
            Button button = root.Q<Button>(name);
            if (button != null) button.clicked -= callback;
        }

        #endregion
    }
}
#endif
