using System;
using RPG.Game.UI.Bag;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WS_Modules.Pooling;

namespace RPG.Game.UI.Views.Bag
{
    /// <summary>背包网格中一格物品的池化表现 View。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖 ItemBK 根 Button/品质背景、ItemIcon、固定 InfoBG、OwnerBK/OwnerIcon、StarLevel/StarLevelImage 和可选状态节点。")]
    public sealed class BagItemView : PoolObjectIdentity
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
        [SerializeField] private Sprite[] rarityBackgrounds = new Sprite[5];
        [SerializeField] private Sprite[] selectedRarityBackgrounds = new Sprite[5];

        private Action<BagEntryKey> selected;
        private BagItemViewData boundData;
        private bool isSelected;

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
            rootButton.onClick.AddListener(HandleClicked);
        }

        /// <summary>回收前清除图标、文本、状态和选择回调，避免虚拟化复用串数据。</summary>
        protected override void OnDespawn()
        {
            boundData = null;
            selected = null;
            isSelected = false;
            if (itemIcon != null) itemIcon.sprite = null;
            if (ownerIcon != null) ownerIcon.sprite = null;
            if (countOrLevelText != null) countOrLevelText.text = string.Empty;
            ownerRoot?.SetActive(false);
            isNewIcon?.SetActive(false);
            lockIcon?.SetActive(false);
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
            if (itemIcon != null) itemIcon.sprite = data.Icon;
            if (ownerIcon != null) ownerIcon.sprite = data.OwnerIcon;
            if (countOrLevelText != null) countOrLevelText.text = data.LevelText;
            ownerRoot?.SetActive(data.IsEquipped && data.OwnerIcon != null);
            isNewIcon?.SetActive(data.IsNew);
            lockIcon?.SetActive(data.IsLocked);
            ApplyBackground(data.Rarity);
        }

        /// <summary>刷新当前条目的选中背景。</summary>
        /// <param name="selectedState">是否选中。</param>
        public void SetSelected(bool selectedState)
        {
            isSelected = selectedState;
            ApplyBackground(boundData?.Rarity ?? 0);
        }

        /// <summary>
        /// 设置当前物品格是否响应点击；窗口隐藏时由网格统一关闭交互。
        /// </summary>
        /// <param name="interactable">是否允许响应点击。</param>
        public void SetInteractable(bool interactable)
        {
            rootButton.interactable = interactable;
        }

        /// <summary>响应根 Button 点击并上报稳定条目标识。</summary>
        private void HandleClicked()
        {
            if (boundData != null) selected?.Invoke(boundData.EntryKey);
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
