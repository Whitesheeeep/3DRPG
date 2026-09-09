using System;
using System.Collections.Generic;
using RPG.Game.UI.Views.Bag;
using RPG.ItemSystem;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WS_Modules;

namespace WS_Modules.UIModule
{
    /// <summary>
    /// 背包窗口的序列化绑定容器。
    /// 该组件只保存按钮、View 和动态图集配置，不在运行时扫描或修复层级。
    /// </summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖 BagWindow 根节点下已绑定的分类按钮、虚拟网格、详情 View 和动态图集地址配置。")]
    public sealed class BagWindowDataComponent : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private bool isFullWindow = true;
        [SerializeField] private bool doAnimation = true;
        [SerializeField, Required] private Button[] categoryButtons = Array.Empty<Button>();
        [SerializeField] private Button previousCategoryButton;
        [SerializeField] private Button nextCategoryButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private TMP_Dropdown sortDropdown;
        [SerializeField] private Button sortDirectionButton;
        [SerializeField] private Button deleteButton;
        [SerializeField] private Button detailsButton;
        [SerializeField, Required] private BagGridView gridView;
        [SerializeField, Required] private BagDetailShellView detailShellView;
        [SerializeField, Required] private WeaponBagDetailView weaponDetailView;

        #endregion

        #region 动态图集配置

        [SerializeField, WSAddressableKey("UISpriteAtlas")]
        private List<string> dynamicAtlasAddresses = new() { "WeaponIcons", "CharacterSideIcons" };
        [SerializeField, MinValue(0f)] private float atlasReleaseDelaySeconds = 30f;

        #endregion

        #region 作者配置

        [SerializeField] private List<ItemCategory> categoryOrder = new()
        {
            ItemCategory.Material,
            ItemCategory.Weapon,
            ItemCategory.Ingredient,
            ItemCategory.Artifact,
            ItemCategory.Food
        };

        #endregion

        #region 属性

        /// <summary>获取窗口是否应作为全屏窗口参与 UI 层级切换。</summary>
        public bool IsFullWindow => isFullWindow;

        /// <summary>获取窗口是否启用 WindowBase 的默认过渡动画。</summary>
        public bool DoAnimation => doAnimation;

        /// <summary>获取分类按钮配置。</summary>
        public IReadOnlyList<Button> CategoryButtons => categoryButtons;

        /// <summary>获取前一分类按钮。</summary>
        public Button PreviousCategoryButton => previousCategoryButton;

        /// <summary>获取后一分类按钮。</summary>
        public Button NextCategoryButton => nextCategoryButton;

        /// <summary>获取关闭按钮。</summary>
        public Button CloseButton => closeButton;

        /// <summary>获取排序下拉框。</summary>
        public TMP_Dropdown SortDropdown => sortDropdown;

        /// <summary>获取升降序按钮。</summary>
        public Button SortDirectionButton => sortDirectionButton;

        /// <summary>获取删除请求按钮。</summary>
        public Button DeleteButton => deleteButton;

        /// <summary>获取详情请求按钮。</summary>
        public Button DetailsButton => detailsButton;

        /// <summary>获取虚拟网格 View。</summary>
        public BagGridView GridView => gridView;

        /// <summary>获取共用详情外壳。</summary>
        public BagDetailShellView DetailShellView => detailShellView;

        /// <summary>获取武器详情内容 View。</summary>
        public WeaponBagDetailView WeaponDetailView => weaponDetailView;

        /// <summary>获取动态图集地址配置。</summary>
        public IReadOnlyList<string> DynamicAtlasAddresses => dynamicAtlasAddresses;

        /// <summary>获取隐藏后的动态图集延迟释放秒数。</summary>
        public float AtlasReleaseDelaySeconds => atlasReleaseDelaySeconds;

        /// <summary>获取分类按钮采用的稳定顺序。</summary>
        public IReadOnlyList<ItemCategory> CategoryOrder => categoryOrder;

        #endregion

        #region 配置校验

        /// <summary>
        /// 校验 Prefab 已完成的序列化绑定；该方法不查找、不创建也不修复任何对象。
        /// </summary>
        public void ValidateConfiguration()
        {
            if (categoryButtons == null || categoryButtons.Length == 0)
                throw new InvalidOperationException("[BagWindowDataComponent] 未绑定分类按钮。");
            for (int index = 0; index < categoryButtons.Length; index++)
                if (categoryButtons[index] == null)
                    throw new InvalidOperationException($"[BagWindowDataComponent] 分类按钮索引 {index} 未绑定。");
            if (gridView == null) throw new InvalidOperationException("[BagWindowDataComponent] 未绑定 BagGridView。");
            if (detailShellView == null) throw new InvalidOperationException("[BagWindowDataComponent] 未绑定 BagDetailShellView。");
            if (weaponDetailView == null) throw new InvalidOperationException("[BagWindowDataComponent] 未绑定 WeaponBagDetailView。");
            if (dynamicAtlasAddresses == null) throw new InvalidOperationException("[BagWindowDataComponent] 动态图集地址列表为空。");
        }

        #endregion
    }
}
