using System;
using System.Collections.Generic;
using DG.Tweening;
using RPG.Game.UI.Bag;
using RPG.Game.UI.Views.Bag;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.Common
{
    /// <summary>武器培养窗口内可复用的候选物品选择面板。</summary>
    public sealed class ItemSelectionPanelView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private BagGridView gridView;
        [SerializeField] private Button returnButton;
        [SerializeField] private TMP_Dropdown sortDropdown;
        [SerializeField] private Button sortDirectionButton;
        [SerializeField, MinValue(0f)] private float slideDuration = 0.22f;
        [SerializeField] private Ease slideEase = Ease.OutCubic;
        [SerializeField] private Ease slideOutEase = Ease.InCubic;
        private Action<BagEntryKey> boundEntryCallback;
        private RectTransform panelRectTransform;
        private Vector2 shownAnchoredPosition;
        private Tween activeSlideTween;
        private bool hasShownPosition;
        private bool hasSortControlConfiguration;
        private bool sortControlVisible;
        private BagSortMode sortControlMode;
        private BagSortDirection sortControlDirection;
        private string primarySortLabel = string.Empty;
        private string configuredPrimarySortLabel;

        #endregion

        #region 事件

        /// <summary>候选条目被点击时触发。</summary>
        public event Action<BagEntryKey> EntryClicked;
        /// <summary>数量模式候选条目请求调整数量时触发。</summary>
        public event Action<BagItemQuantityIntent> QuantityChangeRequested;
        /// <summary>用户请求退出选择面板时触发。</summary>
        public event Action ReturnRequested;
        /// <summary>用户选择新的排序字段时触发。</summary>
        public event Action<BagSortMode> SortModeChanged;
        /// <summary>用户请求切换当前列表的升降序时触发。</summary>
        public event Action SortDirectionRequested;

        #endregion

        #region 生命周期与绑定

        /// <summary>绑定返回、排序控件和网格点击转发。</summary>
        private void Awake()
        {
            panelRectTransform = transform as RectTransform;
            if (panelRectTransform == null)
                throw new InvalidOperationException("[ItemSelectionPanelView] 选择面板根节点必须是 RectTransform。");
            shownAnchoredPosition = panelRectTransform.anchoredPosition;
            hasShownPosition = true;
            returnButton?.onClick.AddListener(HandleReturnClicked);
            sortDropdown?.onValueChanged.AddListener(HandleSortModeChanged);
            sortDirectionButton?.onClick.AddListener(HandleSortDirectionClicked);
            // 排序配置由所属窗口控制器提供；视图只恢复选项、下拉值和控件显隐。
            ApplySortControlState();
        }

        /// <summary>销毁时对称移除返回和排序控件监听。</summary>
        private void OnDestroy()
        {
            KillSlideTweenAndReset(false);
            returnButton?.onClick.RemoveListener(HandleReturnClicked);
            sortDropdown?.onValueChanged.RemoveListener(HandleSortModeChanged);
            sortDirectionButton?.onClick.RemoveListener(HandleSortDirectionClicked);
        }

        /// <summary>从左侧屏外滑入选择面板；动画期间暂时禁止候选网格交互。</summary>
        public void ShowAnimated()
        {
            if (panelRectTransform == null) panelRectTransform = transform as RectTransform;
            if (panelRectTransform == null) throw new InvalidOperationException("[ItemSelectionPanelView] 缺少面板 RectTransform。");
            KillSlideTweenAndReset(false);
            gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            if (!hasShownPosition)
            {
                shownAnchoredPosition = panelRectTransform.anchoredPosition;
                hasShownPosition = true;
            }

            float panelWidth = panelRectTransform.rect.width;
            panelRectTransform.anchoredPosition = shownAnchoredPosition + Vector2.left * panelWidth;
            gridView?.SetInteractable(false);
            Debug.Log($"[ItemSelectionPanelView] 开始从左侧滑入，width={panelWidth:0.##}。", this);
            activeSlideTween = panelRectTransform
                .DOAnchorPos(shownAnchoredPosition, slideDuration)
                .SetEase(slideEase)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    if (activeSlideTween == null || !gameObject.activeSelf) return;
                    activeSlideTween = null;
                    gridView?.SetInteractable(true);
                    Debug.Log("[ItemSelectionPanelView] 选择面板滑入完成。", this);
                });
        }

        /// <summary>立即终止进入动画、关闭面板并恢复 Prefab 配置位置。</summary>
        public void HideImmediateAndReset()
        {
            KillSlideTweenAndReset(true);
        }

        /// <summary>从当前位置向左侧滑出选择面板，并在完成后隐藏和复位。</summary>
        public void HideAnimated()
        {
            if (panelRectTransform == null) panelRectTransform = transform as RectTransform;
            if (panelRectTransform == null)
                throw new InvalidOperationException("[ItemSelectionPanelView] 缺少面板 RectTransform。");

            if (!hasShownPosition)
            {
                shownAnchoredPosition = panelRectTransform.anchoredPosition;
                hasShownPosition = true;
            }

            KillActiveSlideTween();
            gridView?.SetInteractable(false);
            if (!gameObject.activeSelf)
            {
                panelRectTransform.anchoredPosition = shownAnchoredPosition;
                return;
            }

            Canvas.ForceUpdateCanvases();
            float panelWidth = panelRectTransform.rect.width;
            Vector2 hiddenAnchoredPosition = shownAnchoredPosition + Vector2.left * panelWidth;
            float remainingDistance = Vector2.Distance(panelRectTransform.anchoredPosition, hiddenAnchoredPosition);
            if (remainingDistance <= 0.01f || panelWidth <= 0.01f)
            {
                CompleteAnimatedHide();
                return;
            }

            float normalizedDistance = Mathf.Clamp01(remainingDistance / panelWidth);
            float duration = Mathf.Max(0.01f, slideDuration * normalizedDistance);
            Debug.Log($"[ItemSelectionPanelView] 开始向左侧滑出，width={panelWidth:0.##}，duration={duration:0.###}。", this);
            activeSlideTween = panelRectTransform
                .DOAnchorPos(hiddenAnchoredPosition, duration)
                .SetEase(slideOutEase)
                .SetUpdate(true)
                .OnComplete(CompleteAnimatedHide);
        }

        /// <summary>立即终止当前滑动 Tween 并恢复面板的稳定位置。</summary>
        /// <param name="hide">是否同时隐藏面板。</param>
        private void KillSlideTweenAndReset(bool hide)
        {
            bool hadTween = KillActiveSlideTween();
            gridView?.SetInteractable(false);
            if (panelRectTransform != null && hasShownPosition)
                panelRectTransform.anchoredPosition = shownAnchoredPosition;
            if (hide) gameObject.SetActive(false);
            if (hadTween) Debug.Log("[ItemSelectionPanelView] 选择面板动画被立即终止并复位。", this);
        }

        /// <summary>终止当前滑动 Tween，不改变面板位置。</summary>
        /// <returns>本次是否存在正在运行的 Tween。</returns>
        private bool KillActiveSlideTween()
        {
            Tween tween = activeSlideTween;
            activeSlideTween = null;
            if (tween == null) return false;
            tween.Kill(false);
            return true;
        }

        /// <summary>完成滑出动画，隐藏面板并恢复下一次打开所需的初始位置。</summary>
        private void CompleteAnimatedHide()
        {
            activeSlideTween = null;
            if (panelRectTransform != null && hasShownPosition)
                panelRectTransform.anchoredPosition = shownAnchoredPosition;
            gameObject.SetActive(false);
            Debug.Log("[ItemSelectionPanelView] 选择面板滑出完成并复位。", this);
        }

        /// <summary>禁用节点时清理 Tween，避免过期完成回调恢复交互。</summary>
        private void OnDisable()
        {
            if (activeSlideTween != null || (panelRectTransform != null && hasShownPosition && panelRectTransform.anchoredPosition != shownAnchoredPosition))
                KillSlideTweenAndReset(false);
        }

        /// <summary>校验候选网格、返回按钮、排序下拉框与方向按钮。</summary>
        public void ValidateConfiguration()
        {
            if (gridView == null || returnButton == null || sortDropdown == null || sortDirectionButton == null)
                throw new InvalidOperationException("[ItemSelectionPanelView] 选择面板存在未绑定控件。");
        }

        /// <summary>绑定候选列表。</summary>
        /// <param name="entries">候选条目。</param>
        /// <param name="selectedEntryKeys">当前已选中的候选条目标识。</param>
        /// <param name="onEntryClicked">条目点击回调。</param>
        public void Bind(IReadOnlyList<BagItemViewData> entries,
            IReadOnlyCollection<BagEntryKey> selectedEntryKeys, Action<BagEntryKey> onEntryClicked)
        {
            boundEntryCallback = onEntryClicked;
            // 先写入多选稳定键，再创建当前可见池对象，避免列表重绑时短暂套用上一批选择。
            gridView?.SetSelectedEntries(selectedEntryKeys ?? Array.Empty<BagEntryKey>());
            gridView?.Bind(entries ?? Array.Empty<BagItemViewData>(), HandleEntryClicked);
        }

        /// <summary>绑定支持数量调整的候选列表，并在创建可见池对象前投影选择键。</summary>
        /// <param name="entries">候选条目。</param>
        /// <param name="selectedEntryKeys">当前数量大于零的条目标识。</param>
        public void BindQuantitySelection(IReadOnlyList<BagItemViewData> entries,
            IReadOnlyCollection<BagEntryKey> selectedEntryKeys)
        {
            boundEntryCallback = null;
            gridView?.SetSelectedEntries(selectedEntryKeys ?? Array.Empty<BagEntryKey>());
            gridView?.BindQuantitySelection(entries ?? Array.Empty<BagItemViewData>(),
                intent => QuantityChangeRequested?.Invoke(intent));
        }

        /// <summary>设置面板网格是否可交互。</summary>
        /// <param name="interactable">是否可交互。</param>
        public void SetInteractable(bool interactable) => gridView?.SetInteractable(interactable);

        /// <summary>配置排序字段、排序方向和分类主数值文案，并控制两个排序控件的显隐。</summary>
        /// <param name="visible">是否显示并启用排序按钮。</param>
        /// <param name="sortMode">当前排序字段。</param>
        /// <param name="sortDirection">当前排序方向。</param>
        /// <param name="primaryLabel">主数值选项文案，例如“等级”或“数量”。</param>
        public void SetSortControl(bool visible, BagSortMode sortMode,
            BagSortDirection sortDirection, string primaryLabel)
        {
            hasSortControlConfiguration = true;
            sortControlVisible = visible;
            sortControlMode = sortMode;
            sortControlDirection = sortDirection;
            primarySortLabel = string.IsNullOrWhiteSpace(primaryLabel) ? "主数值" : primaryLabel;
            ApplySortControlState();
        }

        /// <summary>将最近一次排序配置同步到控件，兼容面板首次激活才触发 Awake 的时序。</summary>
        private void ApplySortControlState()
        {
            if (!hasSortControlConfiguration) return;
            if (sortDropdown == null || sortDirectionButton == null) return;

            if (!string.Equals(configuredPrimarySortLabel, primarySortLabel, StringComparison.Ordinal))
            {
                // 分类主值文案不同，但下拉字段顺序保持与 BagWindow 一致，控制器按同一枚举排序。
                sortDropdown.ClearOptions();
                sortDropdown.AddOptions(new List<string> { "品质", primarySortLabel, "获得顺序" });
                configuredPrimarySortLabel = primarySortLabel;
            }

            sortDropdown.SetValueWithoutNotify(Mathf.Clamp((int)sortControlMode, 0, 2));
            sortDropdown.gameObject.SetActive(sortControlVisible);
            sortDropdown.interactable = sortControlVisible;
            // 复用背包的排序方向图标，并通过半周旋转明确区分升序和降序状态。
            sortDirectionButton.transform.localRotation = Quaternion.Euler(0f, 0f,
                sortControlDirection == BagSortDirection.Descending ? 180f : 0f);
            sortDirectionButton.gameObject.SetActive(sortControlVisible);
            sortDirectionButton.interactable = sortControlVisible;
        }

        /// <summary>发送返回面板请求。</summary>
        private void HandleReturnClicked() => ReturnRequested?.Invoke();

        /// <summary>转发下拉框字段选择，由所属 Controller 更新排序状态。</summary>
        /// <param name="value">下拉选项序号。</param>
        private void HandleSortModeChanged(int value)
        {
            if (!sortControlVisible) return;
            SortModeChanged?.Invoke((BagSortMode)Mathf.Clamp(value, 0, 2));
        }

        /// <summary>转发升降序切换请求，由所属 Controller 更新排序状态。</summary>
        private void HandleSortDirectionClicked()
        {
            if (sortControlVisible) SortDirectionRequested?.Invoke();
        }

        /// <summary>把网格条目点击转发为面板级用户意图。</summary>
        /// <param name="entryKey">被点击的候选条目标识。</param>
        private void HandleEntryClicked(BagEntryKey entryKey)
        {
            EntryClicked?.Invoke(entryKey);
            boundEntryCallback?.Invoke(entryKey);
        }

        #endregion
    }
}
