using System;
using RPG.Game.UI.Views.Common;
using RPG.Game.UI.WeaponDevelopment;
using TMPro;
using UnityEngine;

namespace RPG.Game.UI.Views.WeaponDevelopment
{
    /// <summary>武器培养窗口的顶层表现层，只编排页面 View 并转发用户意图。</summary>
    public sealed class WeaponDevelopmentView : MonoBehaviour
    {
        #region 依赖字段

        [Header("页面 View")]
        [SerializeField] private WeaponDevelopmentTabView growthTabView;
        [SerializeField] private WeaponDevelopmentTabView refinementTabView;
        [SerializeField] private WeaponEnhancementPageView enhancementPageView;
        [SerializeField] private WeaponAscensionPageView ascensionPageView;
        [SerializeField] private WeaponRefinementPageView refinementPageView;
        [SerializeField] private ItemSelectionPanelView selectionPanel;

        [Header("窗口公共控件")]
        [SerializeField] private TMP_Text weaponNameText;
        [SerializeField] private TMP_Text weaponTypeText;
        [SerializeField] private TMP_Text moraText;
        [SerializeField] private UnityEngine.UI.RawImage weaponPreviewRawImage;
        [SerializeField] private UnityEngine.UI.Button closeButton;

        private WeaponGrowthMode currentGrowthMode = WeaponGrowthMode.ConfigurationUnavailable;

        #endregion

        #region 事件

        /// <summary>用户切换成长或精炼页面时触发。</summary>
        public event Action<WeaponDevelopmentPage> PageRequested;
        /// <summary>用户请求关闭窗口时触发。</summary>
        public event Action CloseRequested;
        /// <summary>用户请求打开精炼材料面板时触发。</summary>
        public event Action AddMaterialRequested;
        /// <summary>用户请求打开升级材料面板时触发。</summary>
        public event Action EnhancementMaterialRequested;
        /// <summary>用户请求自动添加升级素材时触发。</summary>
        public event Action AutoAddRequested;

        #endregion

        #region 生命周期

        /// <summary>注册 Tab、关闭和子页面按钮监听。</summary>
        private void Awake()
        {
            growthTabView.Clicked += HandleGrowthClicked;
            refinementTabView.Clicked += HandleRefinementClicked;
            enhancementPageView.SelectMaterialRequested += HandleEnhancementMaterialClicked;
            enhancementPageView.AutoAddRequested += HandleAutoAddClicked;
            refinementPageView.AddMaterialRequested += HandleRefinementMaterialClicked;
            closeButton.onClick.AddListener(HandleCloseClicked);
            weaponPreviewRawImage.texture = null;
        }

        /// <summary>移除顶层 View 注册的监听。</summary>
        private void OnDestroy()
        {
            if (growthTabView != null) growthTabView.Clicked -= HandleGrowthClicked;
            if (refinementTabView != null) refinementTabView.Clicked -= HandleRefinementClicked;
            if (enhancementPageView != null)
            {
                enhancementPageView.SelectMaterialRequested -= HandleEnhancementMaterialClicked;
                enhancementPageView.AutoAddRequested -= HandleAutoAddClicked;
            }
            if (refinementPageView != null) refinementPageView.AddMaterialRequested -= HandleRefinementMaterialClicked;
            if (closeButton != null) closeButton.onClick.RemoveListener(HandleCloseClicked);
        }

        #endregion

        #region 校验

        /// <summary>校验顶层公共控件、两个 Tab 和分层页面 View 的显式绑定。</summary>
        public void ValidateConfiguration()
        {
            if (growthTabView == null || refinementTabView == null || enhancementPageView == null ||
                ascensionPageView == null || refinementPageView == null || selectionPanel == null ||
                weaponNameText == null || weaponTypeText == null || moraText == null ||
                weaponPreviewRawImage == null || closeButton == null)
                throw new InvalidOperationException("[WeaponDevelopmentView] 顶层培养窗口存在未绑定控件。");

            growthTabView.ValidateConfiguration();
            refinementTabView.ValidateConfiguration();
            enhancementPageView.ValidateConfiguration();
            ascensionPageView.ValidateConfiguration();
            refinementPageView.ValidateConfiguration();
            selectionPanel.ValidateConfiguration();
        }

        #endregion

        #region 展示

        /// <summary>绑定公共标题并把页面数据路由到对应子 View。</summary>
        /// <param name="weaponName">武器名称。</param>
        /// <param name="weaponType">武器类型。</param>
        /// <param name="mora">当前摩拉余额。</param>
        /// <param name="data">顶层页面数据。</param>
        public void Bind(string weaponName, string weaponType, string mora, WeaponDevelopmentViewData data)
        {
            weaponNameText.text = weaponName ?? string.Empty;
            weaponTypeText.text = weaponType ?? string.Empty;
            moraText.text = mora ?? string.Empty;
            if (data == null) throw new ArgumentNullException(nameof(data));

            currentGrowthMode = data.GrowthMode;
            // 成长入口文案始终来自目标武器的成长状态；精炼页不能覆盖左侧成长入口。
            growthTabView.SetLabel(data.GrowthTabLabel);
            refinementTabView.SetLabel("精炼");
            switch (data.Page)
            {
                case WeaponDevelopmentPage.Growth:
                    if (data.GrowthMode == WeaponGrowthMode.Enhancement)
                    {
                        enhancementPageView.Bind(data.Enhancement);
                        enhancementPageView.gameObject.SetActive(true);
                        ascensionPageView.gameObject.SetActive(false);
                    }
                    else
                    {
                        ascensionPageView.Bind(data.Ascension);
                        enhancementPageView.gameObject.SetActive(false);
                        ascensionPageView.gameObject.SetActive(true);
                    }
                    refinementPageView.gameObject.SetActive(false);
                    break;
                case WeaponDevelopmentPage.Refinement:
                    refinementPageView.Bind(data.Refinement);
                    enhancementPageView.gameObject.SetActive(false);
                    ascensionPageView.gameObject.SetActive(false);
                    refinementPageView.gameObject.SetActive(true);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(data.Page), data.Page, "未知的武器培养页面。");
            }

            SetPage(data.Page);
        }

        /// <summary>同步 Tab 选中态和页面根节点激活状态。</summary>
        /// <param name="page">目标一级页面。</param>
        public void SetPage(WeaponDevelopmentPage page)
        {
            bool growth = page == WeaponDevelopmentPage.Growth;
            bool refinement = page == WeaponDevelopmentPage.Refinement;
            growthTabView.SetSelected(growth);
            refinementTabView.SetSelected(refinement);
            if (growth)
            {
                enhancementPageView.gameObject.SetActive(currentGrowthMode == WeaponGrowthMode.Enhancement);
                ascensionPageView.gameObject.SetActive(currentGrowthMode != WeaponGrowthMode.Enhancement);
                refinementPageView.gameObject.SetActive(false);
            }
            else if (refinement)
            {
                enhancementPageView.gameObject.SetActive(false);
                ascensionPageView.gameObject.SetActive(false);
                refinementPageView.gameObject.SetActive(true);
            }
        }

        /// <summary>设置覆盖式材料选择面板的显示状态。</summary>
        /// <param name="visible">是否显示。</param>
        public void SetSelectionPanelVisible(bool visible) => selectionPanel.gameObject.SetActive(visible);

        #endregion

        #region 用户意图

        /// <summary>转发成长 Tab 点击。</summary>
        private void HandleGrowthClicked() => PageRequested?.Invoke(WeaponDevelopmentPage.Growth);
        /// <summary>转发精炼 Tab 点击。</summary>
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
