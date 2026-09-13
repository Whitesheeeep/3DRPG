using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RPG.Game.UI.Views.Common;
using RPG.Game.UI.WeaponDevelopment;

namespace RPG.Game.UI.Views.WeaponDevelopment
{
    /// <summary>武器培养窗口的 UGUI 表现层，只转发用户意图和渲染展示数据。</summary>
    public sealed class WeaponDevelopmentView : MonoBehaviour
    {
        #region 依赖字段

        /// <summary>武器培养页面静态控件的序列化绑定。</summary>
        [Serializable]
        private sealed class PageBindings
        {
            [SerializeField] private GameObject root;
            [SerializeField] private TMP_Text titleText;
            [SerializeField] private TMP_Text subtitleText;
            [SerializeField] private TMP_Text summaryText;
            [SerializeField] private TMP_Text linesText;
            [SerializeField] private TMP_Text statusText;
            [SerializeField] private Button actionButton;
            [SerializeField] private TMP_Text actionLabelText;
            [SerializeField] private Button selectMaterialButton;
            [SerializeField] private Button autoAddButton;
            [SerializeField] private TMP_Text selectMaterialLabelText;
            [SerializeField] private TMP_Text autoAddLabelText;

            /// <summary>获取页面根节点。</summary>
            public GameObject Root => root;

            /// <summary>获取成长页的选择素材按钮。</summary>
            public Button SelectMaterialButton => selectMaterialButton;

            /// <summary>获取成长页的自动添加按钮。</summary>
            public Button AutoAddButton => autoAddButton;

            /// <summary>校验页面的静态控件绑定。</summary>
            /// <param name="pageName">页面名称。</param>
            public void Validate(string pageName)
            {
                if (root == null || titleText == null || subtitleText == null || summaryText == null ||
                    linesText == null || statusText == null ||
                    actionButton == null || actionLabelText == null)
                    throw new InvalidOperationException($"[WeaponDevelopmentView] {pageName} 页面存在未绑定控件。");
            }

            /// <summary>将页面展示数据写入该页面的静态控件。</summary>
            /// <param name="pageData">页面展示数据。</param>
            public void Bind(WeaponDevelopmentViewData pageData)
            {
                titleText.text = pageData?.Title ?? string.Empty;
                subtitleText.text = pageData?.Subtitle ?? string.Empty;
                summaryText.text = pageData?.SummaryText ?? string.Empty;
                linesText.text = pageData == null ? string.Empty : string.Join("\n", pageData.Lines);
                statusText.text = pageData?.StatusText ?? string.Empty;
                actionButton.interactable = pageData != null && pageData.ActionInteractable;
                actionLabelText.text = pageData?.ActionLabel ?? string.Empty;
                bool showEnhancementControls = pageData?.GrowthPreview?.Mode == WeaponGrowthMode.Enhancement;
                if (selectMaterialButton != null)
                {
                    // 突破、满级和配置错误状态不显示升级素材操作，避免把不可用按钮误解为可执行动作。
                    selectMaterialButton.gameObject.SetActive(showEnhancementControls);
                    selectMaterialButton.interactable = showEnhancementControls &&
                                                         (pageData?.GrowthPreview?.SelectMaterialInteractable ?? false);
                }
                if (autoAddButton != null)
                {
                    autoAddButton.gameObject.SetActive(showEnhancementControls);
                    autoAddButton.interactable = showEnhancementControls &&
                                                  (pageData?.GrowthPreview?.AutoAddInteractable ?? false);
                }
                if (selectMaterialLabelText != null)
                    selectMaterialLabelText.text = pageData?.GrowthPreview?.SelectMaterialLabel ?? string.Empty;
                if (autoAddLabelText != null)
                    autoAddLabelText.text = pageData?.GrowthPreview?.AutoAddLabel ?? string.Empty;
            }

            /// <summary>校验成长页额外的素材选择控件。</summary>
            /// <exception cref="InvalidOperationException">成长页按钮或文案未绑定时抛出。</exception>
            public void ValidateGrowthControls()
            {
                if (selectMaterialButton == null || autoAddButton == null ||
                    selectMaterialLabelText == null || autoAddLabelText == null)
                    throw new InvalidOperationException("[WeaponDevelopmentView] 成长页素材控件存在未绑定项。");
            }
        }

        // 页面节点和页面内部控件必须由正式 Prefab 显式绑定，运行时不再创建临时对象。
        [Header("页面绑定")]
        [SerializeField] private PageBindings growthPage = new PageBindings();
        [SerializeField] private PageBindings refinementPage = new PageBindings();
        [SerializeField] private GameObject selectionPanel;

        // 共用顶部和武器展示区域依赖字段。
        [Header("通用文本")]
        [SerializeField] private TMP_Text weaponNameText;
        [SerializeField] private TMP_Text weaponTypeText;
        [SerializeField] private TMP_Text moraText;
        [SerializeField] private RawImage weaponPreviewRawImage;

        // 页签与用户操作依赖字段。
        [Header("页签与操作")]
        [SerializeField] private Button growthButton;
        [SerializeField] private TMP_Text growthButtonLabelText;
        [SerializeField] private Button refinementButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button refinementAddMaterialButton;

        #endregion

        #region 事件

        /// <summary>用户切换页面时触发。</summary>
        public event Action<WeaponDevelopmentPage> PageRequested;

        /// <summary>用户请求关闭窗口时触发。</summary>
        public event Action CloseRequested;

        /// <summary>用户请求打开精炼材料选择面板时触发。</summary>
        public event Action AddMaterialRequested;

        /// <summary>用户请求打开升级素材选择面板时触发。</summary>
        public event Action EnhancementMaterialRequested;

        /// <summary>用户请求自动添加升级素材时触发。</summary>
        public event Action AutoAddRequested;

        #endregion

        #region 生命周期

        /// <summary>绑定页签、关闭、升级素材和精炼材料按钮。</summary>
        private void Awake()
        {
            growthButton?.onClick.AddListener(HandleGrowthClicked);
            refinementButton?.onClick.AddListener(HandleRefinementClicked);
            closeButton?.onClick.AddListener(HandleCloseClicked);
            growthPage?.SelectMaterialButton?.onClick.AddListener(HandleEnhancementMaterialClicked);
            growthPage?.AutoAddButton?.onClick.AddListener(HandleAutoAddClicked);
            refinementAddMaterialButton?.onClick.AddListener(HandleRefinementMaterialClicked);
            if (weaponPreviewRawImage != null) weaponPreviewRawImage.texture = null;
        }

        /// <summary>移除本 View 注册的按钮监听，保留 Inspector 中可能存在的其它监听。</summary>
        private void OnDestroy()
        {
            growthButton?.onClick.RemoveListener(HandleGrowthClicked);
            refinementButton?.onClick.RemoveListener(HandleRefinementClicked);
            closeButton?.onClick.RemoveListener(HandleCloseClicked);
            growthPage?.SelectMaterialButton?.onClick.RemoveListener(HandleEnhancementMaterialClicked);
            growthPage?.AutoAddButton?.onClick.RemoveListener(HandleAutoAddClicked);
            refinementAddMaterialButton?.onClick.RemoveListener(HandleRefinementMaterialClicked);
        }

        #endregion

        #region 校验

        /// <summary>校验正式 Prefab 的突破、精炼页面和共用控件绑定。</summary>
        public void ValidateConfiguration()
        {
            growthPage?.Validate("成长");
            growthPage?.ValidateGrowthControls();
            refinementPage?.Validate("精炼");
            if (growthPage == null || refinementPage == null || selectionPanel == null ||
                weaponNameText == null || weaponTypeText == null || moraText == null || weaponPreviewRawImage == null ||
                growthButton == null || growthButtonLabelText == null || refinementButton == null || closeButton == null ||
                refinementAddMaterialButton == null)
                throw new InvalidOperationException("[WeaponDevelopmentView] 正式 Prefab 存在未绑定的培养窗口控件。");
        }

        #endregion

        #region 展示

        /// <summary>绑定当前武器公共标题和升级、突破或精炼页面数据。</summary>
        /// <param name="weaponName">武器名称。</param>
        /// <param name="weaponType">武器类型。</param>
        /// <param name="mora">当前货币显示。</param>
        /// <param name="data">页面展示数据。</param>
        public void Bind(string weaponName, string weaponType, string mora, WeaponDevelopmentViewData data)
        {
            weaponNameText.text = weaponName ?? string.Empty;
            weaponTypeText.text = weaponType ?? string.Empty;
            moraText.text = mora ?? string.Empty;
            growthButtonLabelText.text = data?.GrowthPreview?.TabLabel ?? "升级";
            switch (data?.Page ?? WeaponDevelopmentPage.Growth)
            {
                case WeaponDevelopmentPage.Growth:
                    growthPage.Bind(data);
                    break;
                case WeaponDevelopmentPage.Refinement:
                    refinementPage.Bind(data);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(data), data?.Page, "未知的武器培养页面。");
            }

            SetPage(data?.Page ?? WeaponDevelopmentPage.Growth);
        }

        /// <summary>切换成长或精炼页面节点。</summary>
        /// <param name="page">要显示的页面。</param>
        public void SetPage(WeaponDevelopmentPage page)
        {
            growthPage.Root.SetActive(page == WeaponDevelopmentPage.Growth);
            refinementPage.Root.SetActive(page == WeaponDevelopmentPage.Refinement);
        }

        /// <summary>设置培养材料选择面板的显示状态。</summary>
        /// <param name="visible">是否显示。</param>
        public void SetSelectionPanelVisible(bool visible)
        {
            selectionPanel.SetActive(visible);
        }

        #endregion

        #region 用户意图

        /// <summary>转发成长页签点击。</summary>
        private void HandleGrowthClicked() => PageRequested?.Invoke(WeaponDevelopmentPage.Growth);

        /// <summary>转发精炼页签点击。</summary>
        private void HandleRefinementClicked() => PageRequested?.Invoke(WeaponDevelopmentPage.Refinement);

        /// <summary>转发关闭窗口意图。</summary>
        private void HandleCloseClicked() => CloseRequested?.Invoke();

        /// <summary>转发打开升级素材面板意图。</summary>
        private void HandleEnhancementMaterialClicked() => EnhancementMaterialRequested?.Invoke();

        /// <summary>转发自动添加升级素材意图。</summary>
        private void HandleAutoAddClicked() => AutoAddRequested?.Invoke();

        /// <summary>转发打开精炼材料面板意图。</summary>
        private void HandleRefinementMaterialClicked() => AddMaterialRequested?.Invoke();

        #endregion
    }
}
