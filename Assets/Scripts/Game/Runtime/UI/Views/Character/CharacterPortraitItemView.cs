using System;
using RPG.Game.UI.Character;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.Character
{
    /// <summary>顶部角色头像条中的一个可复用头像项。</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterPortraitItemView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private Button button;
        [SerializeField] private Image sideIcon;
        [SerializeField] private Image qualityFrame;
        [SerializeField] private GameObject selectedRing;
        [SerializeField] private GameObject selectedUnderline;
        [SerializeField] private Image activeCharacterMarkImage;
        [SerializeField] private TMP_Text fallbackNameText;
        private UnityAction boundClick;

        #endregion

        #region 事件

        /// <summary>头像被点击时发送稳定角色标识。</summary>
        public event Action<RPG.Character.CharacterId> Clicked;

        #endregion

        #region 生命周期

        /// <summary>校验头像项的序列化依赖。</summary>
        private void Awake()
        {
            if (button == null) throw new InvalidOperationException("[CharacterPortraitItemView] 未绑定 Button。");
            if (sideIcon == null) throw new InvalidOperationException("[CharacterPortraitItemView] 未绑定 SideIcon。");
        }

        /// <summary>组件销毁时移除当前绑定的点击回调。</summary>
        private void OnDestroy()
        {
            if (button != null && boundClick != null) button.onClick.RemoveListener(boundClick);
        }

        #endregion

        #region 绑定

        /// <summary>完整覆盖头像项状态，避免池化或复用后残留旧角色信息。</summary>
        /// <param name="data">头像条目数据。</param>
        public void Bind(CharacterRosterEntryViewData data)
        {
            if (data == null)
            {
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);
            sideIcon.sprite = data.SideIcon;
            sideIcon.enabled = data.SideIcon != null;
            if (fallbackNameText != null)
            {
                fallbackNameText.text = data.Instance.Config.Name;
                fallbackNameText.gameObject.SetActive(data.SideIcon == null);
            }
            if (qualityFrame != null) qualityFrame.color = GetRarityColor(data.Rarity);
            if (selectedRing != null) selectedRing.SetActive(data.Selected);
            if (selectedUnderline != null) selectedUnderline.SetActive(data.Selected);
            if (activeCharacterMarkImage != null)
            {
                activeCharacterMarkImage.sprite = data.PartyMarkSprite;
                activeCharacterMarkImage.gameObject.SetActive(data.IsInActiveParty && data.PartyMarkSprite != null);
            }

            if (boundClick != null) button.onClick.RemoveListener(boundClick);
            boundClick = () => Clicked?.Invoke(data.Instance.CharacterId);
            button.onClick.AddListener(boundClick);
        }

        /// <summary>隐藏未使用的头像项。</summary>
        public void Clear()
        {
            if (boundClick != null && button != null) button.onClick.RemoveListener(boundClick);
            boundClick = null;
            if (sideIcon != null) sideIcon.sprite = null;
            if (qualityFrame != null) qualityFrame.color = Color.clear;
            if (selectedRing != null) selectedRing.SetActive(false);
            if (selectedUnderline != null) selectedUnderline.SetActive(false);
            if (activeCharacterMarkImage != null)
            {
                activeCharacterMarkImage.sprite = null;
                activeCharacterMarkImage.gameObject.SetActive(false);
            }
            if (fallbackNameText != null)
            {
                fallbackNameText.text = string.Empty;
                fallbackNameText.gameObject.SetActive(false);
            }
            gameObject.SetActive(false);
        }

        #endregion

        #region 内部辅助

        /// <summary>按角色稀有度返回基础边框颜色。</summary>
        /// <param name="rarity">一至五星稀有度。</param>
        /// <returns>稀有度颜色。</returns>
        private static Color GetRarityColor(int rarity)
        {
            switch (rarity)
            {
                case 5: return new Color(1f, 0.69f, 0.30f, 0.18f);
                case 4: return new Color(0.72f, 0.43f, 0.97f, 0.18f);
                case 3: return new Color(0.33f, 0.72f, 1f, 0.18f);
                case 2: return new Color(0.40f, 0.83f, 0.53f, 0.18f);
                default: return new Color(0.85f, 0.89f, 1f, 0.14f);
            }
        }

        #endregion
    }
}
