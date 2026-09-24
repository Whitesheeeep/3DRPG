using System;
using System.Collections.Generic;
using RPG.Game.UI.Views.Character;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

namespace WS_Modules.UIModule
{
    /// <summary>CharacterWindow 的序列化依赖容器，不在运行时扫描或创建 UI 节点。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖 CharacterWindow 根节点下显式绑定的 View、页面和按钮。")]
    public sealed class CharacterWindowDataComponent : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private bool isFullWindow = true;
        [SerializeField] private bool doAnimation = true;
        [SerializeField, Required] private CharacterWindowView view;
        [SerializeField, Required] private CharacterRosterStripView rosterStrip;
        [SerializeField, Required] private Button closeButton;
        [SerializeField, Required] private Button previousCharacterButton;
        [SerializeField, Required] private Button nextCharacterButton;
        [SerializeField] private Button attributeButton;
        [SerializeField] private Button weaponButton;
        [SerializeField] private Button artifactButton;
        [SerializeField] private Image starfieldImage;
        [SerializeField] private Sprite[] partyMarkSprites = Array.Empty<Sprite>();

        #endregion

        #region 动态图集配置

        [SerializeField, WSAddressableKey("UISpriteAtlas")]
        private List<string> dynamicAtlasAddresses = new()
        {
            "Characters",
            "CharacterSideIcons",
            "WeaponIcons",
            "Artifacts_00",
            "Artifacts_01",
            "Artifacts_02",
            RPG.Character.CharacterAssetAddresses.FullBodyPortraitsAtlas
        };
        [SerializeField, MinValue(0f)] private float atlasReleaseDelaySeconds = 30f;

        #endregion

        #region 属性

        /// <summary>获取窗口是否为全屏业务窗口。</summary>
        public bool IsFullWindow => isFullWindow;
        /// <summary>获取是否播放窗口动画。</summary>
        public bool DoAnimation => doAnimation;
        /// <summary>获取顶层 View。</summary>
        public CharacterWindowView View => view;
        /// <summary>获取头像条 View。</summary>
        public CharacterRosterStripView RosterStrip => rosterStrip;
        /// <summary>获取关闭按钮。</summary>
        public Button CloseButton => closeButton;
        /// <summary>获取动态图集地址。</summary>
        public IReadOnlyList<string> DynamicAtlasAddresses => dynamicAtlasAddresses;
        /// <summary>获取动态图集延迟释放秒数。</summary>
        public float AtlasReleaseDelaySeconds => atlasReleaseDelaySeconds;
        /// <summary>获取背景图。</summary>
        public Image StarfieldImage => starfieldImage;
        /// <summary>获取队伍槽位标记图，数组下标对应队伍槽位。</summary>
        public IReadOnlyList<Sprite> PartyMarkSprites => partyMarkSprites;

        #endregion

        #region 配置校验

        /// <summary>校验所有 CharacterWindow 序列化依赖。</summary>
        public void ValidateConfiguration()
        {
            if (view == null) throw new InvalidOperationException("[CharacterWindowDataComponent] 未绑定 CharacterWindowView。");
            if (rosterStrip == null) throw new InvalidOperationException("[CharacterWindowDataComponent] 未绑定 CharacterRosterStripView。");
            if (closeButton == null || previousCharacterButton == null || nextCharacterButton == null)
                throw new InvalidOperationException("[CharacterWindowDataComponent] 未绑定窗口或角色切换按钮。");
            if (dynamicAtlasAddresses == null) throw new InvalidOperationException("[CharacterWindowDataComponent] 动态图集地址为空。");
            if (partyMarkSprites == null || partyMarkSprites.Length != RPG.Character.CharacterParty.SlotCount)
                throw new InvalidOperationException("[CharacterWindowDataComponent] 必须绑定四个队伍槽位标记图。");
        }

        #endregion
    }
}
