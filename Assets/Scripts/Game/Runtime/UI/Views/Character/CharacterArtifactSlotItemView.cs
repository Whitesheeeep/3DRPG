using System;
using RPG.Game.UI.Character;
using RPG.ItemSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.Character
{
    /// <summary>角色页面中的单个固定圣遗物槽位。</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterArtifactSlotItemView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private Button button;
        [SerializeField] private Image artifactIcon;
        [SerializeField] private Image qualityFrame;
        [SerializeField] private TMP_Text slotNameText;
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private GameObject emptyRoot;
        [SerializeField] private GameObject selectedRoot;
        private ArtifactSlot boundSlot;

        #endregion

        #region 事件

        /// <summary>槽位被点击时发送部位。</summary>
        public event Action<ArtifactSlot> Clicked;

        #endregion

        #region 生命周期

        /// <summary>校验并注册槽位按钮。</summary>
        private void Awake()
        {
            if (button == null || artifactIcon == null || qualityFrame == null || slotNameText == null ||
                levelText == null || emptyRoot == null || selectedRoot == null)
                throw new InvalidOperationException("[CharacterArtifactSlotItemView] 圣遗物槽位绑定不完整。");
            button.onClick.AddListener(HandleClicked);
        }

        /// <summary>注销槽位按钮。</summary>
        private void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(HandleClicked);
        }

        #endregion

        #region 绑定

        /// <summary>完整覆盖圣遗物槽位状态。</summary>
        /// <param name="data">槽位数据。</param>
        public void Bind(CharacterArtifactSlotItemViewData data)
        {
            if (data == null)
            {
                gameObject.SetActive(false);
                return;
            }
            gameObject.SetActive(true);
            boundSlot = data.Slot;
            artifactIcon.sprite = data.Icon;
            artifactIcon.enabled = data.Icon != null;
            artifactIcon.gameObject.SetActive(data.Icon != null);
            slotNameText.text = data.SlotName;
            levelText.text = data.LevelText;
            emptyRoot.SetActive(!data.HasArtifact);
            levelText.gameObject.SetActive(data.HasArtifact);
            selectedRoot.SetActive(data.Selected);
            qualityFrame.color = GetRarityColor(data.HasArtifact ? data.Rarity : 0);
        }

        /// <summary>隐藏槽位。</summary>
        public void Clear()
        {
            if (artifactIcon != null) artifactIcon.sprite = null;
            if (artifactIcon != null) artifactIcon.gameObject.SetActive(false);
            if (selectedRoot != null) selectedRoot.SetActive(false);
            gameObject.SetActive(false);
        }

        #endregion

        #region 内部事件

        /// <summary>转发当前部位选择意图。</summary>
        private void HandleClicked()
        {
            Clicked?.Invoke(boundSlot);
        }

        /// <summary>按圣遗物稀有度返回槽位边框颜色。</summary>
        /// <param name="rarity">稀有度。</param>
        /// <returns>颜色。</returns>
        private static Color GetRarityColor(int rarity)
        {
            switch (rarity)
            {
                case 5: return new Color(1f, 0.69f, 0.30f, 0.32f);
                case 4: return new Color(0.72f, 0.43f, 0.97f, 0.32f);
                case 3: return new Color(0.33f, 0.72f, 1f, 0.32f);
                case 2: return new Color(0.40f, 0.83f, 0.53f, 0.32f);
                default: return new Color(0.34f, 0.44f, 0.61f, 0.22f);
            }
        }

        #endregion
    }
}
