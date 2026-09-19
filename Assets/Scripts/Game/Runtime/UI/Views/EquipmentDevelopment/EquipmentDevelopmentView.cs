using System;
using RPG.Game.UI.Views.Common;
using RPG.Game.UI.WeaponDevelopment;
using TMPro;
using UnityEngine;

namespace RPG.Game.UI.Views.WeaponDevelopment
{
    /// <summary>统一装备培养窗口的顶层表现层，只编排页面 View 并转发用户意图。</summary>
    public sealed class EquipmentDevelopmentView : MonoBehaviour
    {
        #region 依赖字段

        [Header("页面 View")]
        [SerializeField] private WeaponDevelopmentTabView growthTabView;
        [SerializeField] private WeaponDevelopmentTabView refinementTabView;
        [SerializeField] private EquipmentEnhancementPageView enhancementPageView;
        [SerializeField] private WeaponAscensionPageView ascensionPageView;
        [SerializeField] private WeaponRefinementPageView refinementPageView;

        [Header("窗口公共控件")]
        [SerializeField] private TMP_Text weaponNameText;
        [SerializeField] private TMP_Text weaponTypeText;
        [SerializeField] private TMP_Text moraText;
        [SerializeField] private WeaponPreviewViewportView weaponPreviewViewportView;
        [SerializeField] private UnityEngine.UI.Image artifactPreviewImage;
        [SerializeField] private UnityEngine.UI.Button closeButton;

        private EquipmentGrowthMode currentGrowthMode = EquipmentGrowthMode.ConfigurationUnavailable;
        private bool artifactMode;

        #endregion

        #region 事件

        /// <summary>用户切换成长或精炼页面时触发。</summary>
        public event Action<EquipmentDevelopmentPage> PageRequested;
        /// <summary>用户请求关闭窗口时触发。</summary>
        public event Action CloseRequested;
        /// <summary>用户请求打开精炼材料面板时触发。</summary>
        public event Action AddMaterialRequested;
        /// <summary>用户请求打开升级材料面板时触发。</summary>
        public event Action EnhancementMaterialRequested;
        /// <summary>用户请求自动添加升级素材时触发。</summary>
        public event Action AutoAddRequested;
        /// <summary>用户请求执行升级。</summary>
        public event Action EnhancementRequested;
        /// <summary>用户请求执行突破。</summary>
        public event Action AscensionRequested;
        /// <summary>用户请求执行精炼。</summary>
        public event Action RefinementRequested;

        #endregion

        #region 生命周期

        /// <summary>注册 Tab、关闭和子页面按钮监听。</summary>
        private void Awake()
        {
            growthTabView.Clicked += HandleGrowthClicked;
            refinementTabView.Clicked += HandleRefinementClicked;
            enhancementPageView.SelectMaterialRequested += HandleEnhancementMaterialClicked;
            enhancementPageView.AutoAddRequested += HandleAutoAddClicked;
            enhancementPageView.ActionRequested += HandleEnhancementRequested;
            ascensionPageView.ActionRequested += HandleAscensionRequested;
            refinementPageView.AddMaterialRequested += HandleRefinementMaterialClicked;
            refinementPageView.ActionRequested += HandleRefinementRequested;
            closeButton.onClick.AddListener(HandleCloseClicked);
            weaponPreviewViewportView.Clear();
            artifactPreviewImage.sprite = null;
            artifactPreviewImage.enabled = false;
            artifactPreviewImage.preserveAspect = true;
            artifactPreviewImage.raycastTarget = false;
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
                enhancementPageView.ActionRequested -= HandleEnhancementRequested;
            }
            if (ascensionPageView != null) ascensionPageView.ActionRequested -= HandleAscensionRequested;
            if (refinementPageView != null)
            {
                refinementPageView.AddMaterialRequested -= HandleRefinementMaterialClicked;
                refinementPageView.ActionRequested -= HandleRefinementRequested;
            }
            if (closeButton != null) closeButton.onClick.RemoveListener(HandleCloseClicked);
        }

        #endregion

        #region 校验

        /// <summary>校验顶层公共控件、两个 Tab 和分层页面 View 的显式绑定。</summary>
        public void ValidateConfiguration()
        {
            if (growthTabView == null || refinementTabView == null || enhancementPageView == null ||
                ascensionPageView == null || refinementPageView == null ||
                weaponNameText == null || weaponTypeText == null || moraText == null ||
                weaponPreviewViewportView == null || artifactPreviewImage == null || closeButton == null)
                throw new InvalidOperationException("[EquipmentDevelopmentView] 顶层培养窗口存在未绑定控件。");

            growthTabView.ValidateConfiguration();
            refinementTabView.ValidateConfiguration();
            enhancementPageView.ValidateConfiguration();
            ascensionPageView.ValidateConfiguration();
            refinementPageView.ValidateConfiguration();
            weaponPreviewViewportView.ValidateConfiguration();
        }

        /// <summary>
        /// 获取武器三维预览输出 View，供窗口 Controller 创建窗口级预览运行时。
        /// </summary>
        public WeaponPreviewViewportView WeaponPreviewViewport => weaponPreviewViewportView;

        #endregion

        #region 展示

        /// <summary>绑定公共标题并把页面数据路由到对应子 View。</summary>
        /// <param name="equipmentName">装备名称。</param>
        /// <param name="equipmentType">装备类型或圣遗物部位。</param>
        /// <param name="mora">当前摩拉余额。</param>
        /// <param name="artifactSprite">圣遗物图标；武器模式传入 null。</param>
        /// <param name="targetKind">目标装备类型。</param>
        /// <param name="data">顶层页面数据。</param>
        public void Bind(string equipmentName, string equipmentType, string mora, Sprite artifactSprite,
            EquipmentDevelopmentTargetKind targetKind, EquipmentDevelopmentViewData data)
        {
            weaponNameText.text = equipmentName ?? string.Empty;
            weaponTypeText.text = equipmentType ?? string.Empty;
            moraText.text = mora ?? string.Empty;
            if (data == null) throw new ArgumentNullException(nameof(data));

            artifactMode = targetKind == EquipmentDevelopmentTargetKind.Artifact;
            currentGrowthMode = data.GrowthMode;
            // WeaponPreviewArea 同时承载 RawImage 和圣遗物 Image；保持父节点激活，避免圣遗物模式把自己的图标一起隐藏。
            weaponPreviewViewportView.gameObject.SetActive(true);
            if (artifactMode)
                weaponPreviewViewportView.Clear();
            artifactPreviewImage.gameObject.SetActive(artifactMode);
            artifactPreviewImage.sprite = artifactMode ? artifactSprite : null;
            artifactPreviewImage.enabled = artifactMode && artifactSprite != null;
            refinementTabView.gameObject.SetActive(!artifactMode);
            // 成长入口文案始终来自目标装备的成长状态；精炼页不能覆盖左侧成长入口。
            growthTabView.SetLabel(data.GrowthTabLabel);
            refinementTabView.SetLabel("精炼");
            switch (data.Page)
            {
                case EquipmentDevelopmentPage.Growth:
                    if (artifactMode || data.GrowthMode == EquipmentGrowthMode.Enhancement)
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
                case EquipmentDevelopmentPage.Refinement:
                    if (artifactMode) throw new InvalidOperationException("圣遗物培养窗口不支持精炼页面。");
                    refinementPageView.Bind(data.Refinement);
                    enhancementPageView.gameObject.SetActive(false);
                    ascensionPageView.gameObject.SetActive(false);
                    refinementPageView.gameObject.SetActive(true);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(data.Page), data.Page, "未知的装备培养页面。");
            }

            SetPage(data.Page);
        }

        /// <summary>同步 Tab 选中态和页面根节点激活状态。</summary>
        /// <param name="page">目标一级页面。</param>
        public void SetPage(EquipmentDevelopmentPage page)
        {
            bool growth = page == EquipmentDevelopmentPage.Growth;
            bool refinement = !artifactMode && page == EquipmentDevelopmentPage.Refinement;
            growthTabView.SetSelected(growth);
            refinementTabView.SetSelected(refinement);
            if (growth)
            {
                enhancementPageView.gameObject.SetActive(artifactMode || currentGrowthMode == EquipmentGrowthMode.Enhancement);
                ascensionPageView.gameObject.SetActive(!artifactMode && currentGrowthMode != EquipmentGrowthMode.Enhancement);
                refinementPageView.gameObject.SetActive(false);
            }
            else if (refinement)
            {
                enhancementPageView.gameObject.SetActive(false);
                ascensionPageView.gameObject.SetActive(false);
                refinementPageView.gameObject.SetActive(true);
            }
        }

        #endregion

        #region 用户意图

        /// <summary>转发成长 Tab 点击。</summary>
        private void HandleGrowthClicked() => PageRequested?.Invoke(EquipmentDevelopmentPage.Growth);
        /// <summary>转发精炼 Tab 点击。</summary>
        private void HandleRefinementClicked()
        {
            if (!artifactMode) PageRequested?.Invoke(EquipmentDevelopmentPage.Refinement);
        }
        /// <summary>转发关闭窗口意图。</summary>
        private void HandleCloseClicked() => CloseRequested?.Invoke();
        /// <summary>转发打开升级素材面板意图。</summary>
        private void HandleEnhancementMaterialClicked() => EnhancementMaterialRequested?.Invoke();
        /// <summary>转发自动添加升级素材意图。</summary>
        private void HandleAutoAddClicked() => AutoAddRequested?.Invoke();
        /// <summary>转发打开精炼材料面板意图。</summary>
        private void HandleRefinementMaterialClicked() => AddMaterialRequested?.Invoke();
        /// <summary>转发升级动作意图。</summary>
        private void HandleEnhancementRequested() => EnhancementRequested?.Invoke();
        /// <summary>转发突破动作意图。</summary>
        private void HandleAscensionRequested() => AscensionRequested?.Invoke();
        /// <summary>转发精炼动作意图。</summary>
        private void HandleRefinementRequested() => RefinementRequested?.Invoke();

        #endregion
    }
}
