using System;
using RPG.Game.UI.Bag;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WS_Modules.Pooling;

namespace RPG.Game.UI.Views.Bag
{
    /// <summary>背包网格中一格物品的池化表现 View。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖 ItemBK 根 Button/品质背景、ItemIcon、固定 InfoBG、OwnerBK/OwnerIcon、StarLevel/StarLevelImage、Selected Image 和可选状态节点。")]
    public sealed class BagItemView : PoolObjectIdentity, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        #region 依赖字段

        [SerializeField] private Button rootButton;
        [SerializeField] private Image itemIcon;
        [SerializeField] private Image rarityBackground;
        [SerializeField] private Image ownerIcon;
        [SerializeField] private Image starLevelImage;
        [SerializeField] private TMP_Text countOrLevelText;
        [SerializeField] private GameObject ownerRoot;
        [SerializeField] private GameObject isNewIcon;
        [SerializeField] private GameObject lockIcon;
        [SerializeField] private Image selectedIcon;
        [SerializeField] private Sprite[] rarityBackgrounds = new Sprite[5];
        [SerializeField] private Sprite[] selectedRarityBackgrounds = new Sprite[5];

        private Action<BagEntryKey> selected;
        private Action<BagItemQuantityIntent> quantityIntent;
        private BagItemViewData boundData;
        private bool isSelected;
        private bool quantitySelectionMode;
        // 指针按下后，Button 的 Click 事件会在 PointerUp 时触发；为了避免数量模式下重复 +1，需要在 PointerDown 阶段先发出一次 +1 并标记 Click 待处理。
        private bool pointerClickPending;
        private bool pointerHeld;
        private PointerEventData.InputButton heldButton;
        private float nextRepeatTime;
        private int repeatStep = 1;

        private const float HoldDelaySeconds = 0.45f;
        private const float RepeatIntervalSeconds = 0.18f;

        #endregion

        #region 属性

        /// <summary>获取当前池化 View 绑定的稳定条目标识。</summary>
        public BagEntryKey EntryKey => boundData == null ? default : boundData.EntryKey;

        #endregion

        #region 池生命周期

        /// <summary>校验 Prefab 绑定并注册根按钮回调。</summary>
        protected override void Awake()
        {
            base.Awake();
            if (rootButton == null) throw new InvalidOperationException("[BagItemView] 未绑定根 Button。 ");
            if (itemIcon == null) throw new InvalidOperationException("[BagItemView] 未绑定 ItemIcon。 ");
            if (rarityBackground == null) throw new InvalidOperationException("[BagItemView] 未绑定根品质背景。 ");
            if (ownerRoot == null) throw new InvalidOperationException("[BagItemView] 未绑定 OwnerBK。 ");
            if (ownerIcon == null) throw new InvalidOperationException("[BagItemView] 未绑定 OwnerIcon。 ");
            if (starLevelImage == null) throw new InvalidOperationException("[BagItemView] 未绑定 StarLevelImage。 ");
            if (countOrLevelText == null) throw new InvalidOperationException("[BagItemView] 未绑定 CountOrLevelText。 ");
            if (selectedIcon == null) throw new InvalidOperationException("[BagItemView] 未绑定 Selected Image。 ");
            rootButton.onClick.AddListener(HandleClicked);
        }

        /// <summary>回收前清除图标、文本、状态和选择回调，避免虚拟化复用串数据。</summary>
        protected override void OnDespawn()
        {
            boundData = null;
            selected = null;
            quantityIntent = null;
            quantitySelectionMode = false;
            ResetPointerState();
            isSelected = false;
            if (itemIcon != null) itemIcon.sprite = null;
            if (ownerIcon != null) ownerIcon.sprite = null;
            if (countOrLevelText != null) countOrLevelText.text = string.Empty;
            ownerRoot?.SetActive(false);
            isNewIcon?.SetActive(false);
            lockIcon?.SetActive(false);
            selectedIcon?.gameObject.SetActive(false);
            ApplyBackground(0);
        }

        #endregion

        #region 绑定

        /// <summary>绑定一条列表快照和选择回调。</summary>
        /// <param name="data">列表快照。</param>
        /// <param name="onSelected">点击后的选择回调。</param>
        public void Bind(BagItemViewData data, Action<BagEntryKey> onSelected)
        {
            boundData = data ?? throw new ArgumentNullException(nameof(data));
            selected = onSelected;
            quantityIntent = null;
            quantitySelectionMode = false;
            ResetPointerState();
            ApplyDataVisuals(data);
        }

        /// <summary>绑定需要左右键数量调整的物品条目。</summary>
        /// <param name="data">列表快照。</param>
        /// <param name="onQuantityChanged">数量调整意图回调。</param>
        public void BindQuantitySelection(BagItemViewData data, Action<BagItemQuantityIntent> onQuantityChanged)
        {
            boundData = data ?? throw new ArgumentNullException(nameof(data));
            selected = null;
            quantityIntent = onQuantityChanged;
            quantitySelectionMode = true;
            ResetPointerState();
            ApplyDataVisuals(data);
        }

        /// <summary>
        /// 原地刷新数量模式条目数据，保留当前指针按下和长按重复状态。
        /// </summary>
        /// <param name="data">同一稳定键的新列表快照。</param>
        /// <param name="onQuantityChanged">数量调整意图回调。</param>
        public void RefreshQuantitySelection(BagItemViewData data,
            Action<BagItemQuantityIntent> onQuantityChanged)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (!quantitySelectionMode || boundData == null || boundData.EntryKey != data.EntryKey)
            {
                BindQuantitySelection(data, onQuantityChanged);
                return;
            }

            // 页面刷新只替换展示快照和回调；不重置指针状态，长按可以跨越一次数据刷新继续工作。
            boundData = data;
            quantityIntent = onQuantityChanged;
            ApplyDataVisuals(data);
        }

        /// <summary>刷新当前条目的选中图标与品质背景。</summary>
        /// <param name="selectedState">是否选中。</param>
        public void SetSelected(bool selectedState)
        {
            isSelected = selectedState;
            selectedIcon.gameObject.SetActive(selectedState);
            ApplyBackground(boundData?.Rarity ?? 0);
        }

        /// <summary>
        /// 设置当前物品格是否响应点击；窗口隐藏时由网格统一关闭交互。
        /// </summary>
        /// <param name="interactable">是否允许响应点击。</param>
        public void SetInteractable(bool interactable)
        {
            rootButton.interactable = interactable;
            if (!interactable) ResetPointerState();
        }

        /// <summary>响应根 Button 点击并按当前绑定模式上报选择或数量意图。</summary>
        private void HandleClicked()
        {
            if (boundData == null) return;
            if (quantitySelectionMode)
            {
                // 鼠标左键已在 PointerDown 阶段发出一次 +1；Button 的 Click 只保留给键盘和手柄 Submit。
                if (pointerClickPending)
                {
                    pointerClickPending = false;
                    return;
                }

                quantityIntent?.Invoke(new BagItemQuantityIntent(
                    boundData.EntryKey, BagItemQuantityDirection.Increase, 1));
                return;
            }

            selected?.Invoke(boundData.EntryKey);
        }

        /// <summary>接收左右键按下并开始数量调整或长按重复。</summary>
        /// <param name="eventData">Unity 指针事件。</param>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (!quantitySelectionMode || quantityIntent == null || boundData == null ||
                eventData == null || !rootButton.interactable ||
                (eventData.button != PointerEventData.InputButton.Left &&
                 eventData.button != PointerEventData.InputButton.Right)) return;

            pointerClickPending = eventData.button == PointerEventData.InputButton.Left;
            pointerHeld = true;
            heldButton = eventData.button;
            repeatStep = 1;
            nextRepeatTime = Time.unscaledTime + HoldDelaySeconds;
            EmitQuantityIntent(1);
        }

        /// <summary>停止当前条目的数量长按重复。</summary>
        /// <param name="eventData">Unity 指针事件。</param>
        public void OnPointerUp(PointerEventData eventData)
        {
            pointerHeld = false;
        }

        /// <summary>指针离开条目时停止长按，避免拖出后继续修改数量。</summary>
        /// <param name="eventData">Unity 指针事件。</param>
        public void OnPointerExit(PointerEventData eventData)
        {
            pointerHeld = false;
            pointerClickPending = false;
        }

        /// <summary>驱动激活条目的非缩放时间长按重复，不在空闲条目上产生额外工作。</summary>
        private void Update()
        {
            if (!pointerHeld || !quantitySelectionMode || quantityIntent == null || boundData == null) return;
            float now = Time.unscaledTime;
            if (now < nextRepeatTime) return;

            // 每次重复先将步长翻倍；控制器仍会按拥有量和等级上限进行最终截断。
            repeatStep = repeatStep > (int.MaxValue >> 1) ? int.MaxValue : repeatStep << 1;
            EmitQuantityIntent(repeatStep);
            nextRepeatTime = now + RepeatIntervalSeconds;
        }

        /// <summary>按当前按键方向发出一次数量调整意图。</summary>
        /// <param name="step">本次调整步长。</param>
        private void EmitQuantityIntent(int step)
        {
            quantityIntent?.Invoke(new BagItemQuantityIntent(
                boundData.EntryKey,
                heldButton == PointerEventData.InputButton.Right
                    ? BagItemQuantityDirection.Decrease
                    : BagItemQuantityDirection.Increase,
                step));
        }

        /// <summary>清理指针按下、长按和下一次 Button Click 的临时状态。</summary>
        private void ResetPointerState()
        {
            pointerHeld = false;
            pointerClickPending = false;
            heldButton = PointerEventData.InputButton.Left;
            nextRepeatTime = 0f;
            repeatStep = 1;
        }

        /// <summary>将条目快照写入图标、文本、状态节点和品质背景。</summary>
        /// <param name="data">待显示的条目快照。</param>
        private void ApplyDataVisuals(BagItemViewData data)
        {
            if (itemIcon != null) itemIcon.sprite = data.Icon;
            if (ownerIcon != null) ownerIcon.sprite = data.OwnerIcon;
            if (countOrLevelText != null) countOrLevelText.text = data.LevelText;
            ownerRoot?.SetActive(data.IsEquipped && data.OwnerIcon != null);
            isNewIcon?.SetActive(data.IsNew);
            lockIcon?.SetActive(data.IsLocked);
            ApplyBackground(data.Rarity);
        }

        /// <summary>按品质和选中状态选择根品质背景及星级宽度，固定 InfoBG 不参与动态刷新。</summary>
        private void ApplyBackground(int rarity)
        {
            if (rarityBackground != null)
            {
                if (rarity <= 0)
                {
                    // 回收池对象时清掉旧品质，避免下一次绑定前短暂显示上一件物品的颜色。
                    rarityBackground.sprite = null;
                }
                else
                {
                    int index = Mathf.Clamp(rarity - 1, 0, 4);
                    Sprite[] sources = isSelected ? selectedRarityBackgrounds : rarityBackgrounds;
                    rarityBackground.sprite = sources != null && sources.Length > index ? sources[index] : null;
                }
            }

            if (starLevelImage != null)
            {
                RectTransform rectTransform = starLevelImage.rectTransform;
                rectTransform.sizeDelta = new Vector2(Mathf.Max(0, rarity) * 8f, rectTransform.sizeDelta.y);
                starLevelImage.enabled = rarity > 0;
            }
        }

        #endregion
    }
}
