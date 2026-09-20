using System;
using System.Collections.Generic;
using RPG.ItemSystem;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules;
using WS_Modules.GAS.AttributeSystem;

namespace RPG.Character
{
    /// <summary>角色身份配置组。</summary>
    [Serializable]
    public sealed class CharacterIdentityConfig
    {
        /// <summary>角色显示名称。</summary>
        [SerializeField, LabelText("角色名称")] private string characterName;
        /// <summary>角色稀有度。</summary>
        [SerializeField, LabelText("稀有度")] private CharacterRarity rarity = CharacterRarity.Five;
        /// <summary>读取名称。</summary>
        public string CharacterName => characterName;
        /// <summary>读取稀有度。</summary>
        public CharacterRarity Rarity => rarity;
        /// <summary>从旧的平铺字段迁移身份值。</summary>
        internal void CopyFrom(string name, CharacterRarity value) { characterName = name; rarity = value; }
    }

    /// <summary>角色 Prefab 和头像资源配置组。</summary>
    [Serializable]
    public sealed class CharacterPresentationConfig
    {
        /// <summary>角色世界 Prefab 地址。</summary>
        [SerializeField, WSAddressableKey("CharacterPrefabs"), LabelText("角色 Prefab 地址")] private string prefabAddress;
        /// <summary>侧面头像图集地址。</summary>
        [SerializeField, WSAddressableKey("UISpriteAtlas"), LabelText("侧面头像图集 Address")] private string sideIconAddress = CharacterAssetAddresses.SideIconsAtlas;
        /// <summary>侧面头像 Sprite 名称。</summary>
        [SerializeField, LabelText("侧面头像 Sprite 名称")] private string sideIconSpriteName;
        /// <summary>角色头像图集地址。</summary>
        [SerializeField, WSAddressableKey("UISpriteAtlas"), LabelText("角色头像图集 Address")] private string avatarAddress = CharacterAssetAddresses.AvatarAtlas;
        /// <summary>角色头像 Sprite 名称。</summary>
        [SerializeField, LabelText("角色头像 Sprite 名称")] private string avatarSpriteName;
        /// <summary>读取 Prefab 地址。</summary>
        public string PrefabAddress => prefabAddress;
        /// <summary>读取侧面头像地址。</summary>
        public string SideIconAddress => sideIconAddress;
        /// <summary>读取侧面头像名称。</summary>
        public string SideIconSpriteName => sideIconSpriteName;
        /// <summary>读取头像地址。</summary>
        public string AvatarAddress => avatarAddress;
        /// <summary>读取头像名称。</summary>
        public string AvatarSpriteName => avatarSpriteName;
        /// <summary>从旧的平铺字段迁移资源值。</summary>
        internal void CopyFrom(string prefab, string sideAddress, string sideSprite, string avatar, string avatarSprite)
        {
            prefabAddress = prefab;
            sideIconAddress = sideAddress;
            sideIconSpriteName = sideSprite;
            avatarAddress = avatar;
            avatarSpriteName = avatarSprite;
        }
    }

    /// <summary>角色武器装备规则配置组。</summary>
    [Serializable]
    public sealed class CharacterEquipmentRuleConfig
    {
        /// <summary>允许的武器类型。</summary>
        [SerializeField, EnumToggleButtons, LabelText("允许装备的武器类型")] private WeaponTypeFlags allowedWeaponTypes = WeaponTypeFlags.Sword;
        /// <summary>默认武器类型。</summary>
        [SerializeField, LabelText("默认武器类型")] private WeaponType defaultWeaponType = WeaponType.Sword;
        /// <summary>读取允许武器类型。</summary>
        public WeaponTypeFlags AllowedWeaponTypes => allowedWeaponTypes;
        /// <summary>读取默认武器类型。</summary>
        public WeaponType DefaultWeaponType => defaultWeaponType;
        /// <summary>从旧的平铺字段迁移装备规则。</summary>
        internal void CopyFrom(WeaponTypeFlags allowed, WeaponType defaultType) { allowedWeaponTypes = allowed; defaultWeaponType = defaultType; }
    }

    /// <summary>角色等级、突破和初始属性配置组。</summary>
    [Serializable]
    public sealed class CharacterProgressionConfig
    {
        /// <summary>最大等级。</summary>
        [SerializeField, MinValue(1), LabelText("最大等级")] private int maxLevel = 90;
        /// <summary>最大突破阶数。</summary>
        [SerializeField, MinValue(0), LabelText("最大突破阶数")] private int maxAscensionRank = 6;
        /// <summary>成长 Profile。</summary>
        [SerializeField, Required, LabelText("成长配置")] private CharacterGrowthProfile growthProfile;
        /// <summary>突破阶段。</summary>
        [SerializeField, LabelText("突破阶段与消耗")] private List<CharacterAscensionStage> ascensionStages = new();
        /// <summary>初始属性集。</summary>
        [SerializeField, LabelText("初始属性集")] private GameplayAttributeSet[] initialAttributeSets = Array.Empty<GameplayAttributeSet>();
        /// <summary>读取最大等级。</summary>
        public int MaxLevel => maxLevel;
        /// <summary>读取最大突破阶数。</summary>
        public int MaxAscensionRank => maxAscensionRank;
        /// <summary>读取成长 Profile。</summary>
        public CharacterGrowthProfile GrowthProfile => growthProfile;
        /// <summary>读取突破阶段。</summary>
        public IReadOnlyList<CharacterAscensionStage> AscensionStages => ascensionStages;
        /// <summary>读取初始属性集。</summary>
        public IReadOnlyList<GameplayAttributeSet> InitialAttributeSets => initialAttributeSets;
        /// <summary>从旧的平铺字段迁移成长值。</summary>
        internal void CopyFrom(int level, int rank, CharacterGrowthProfile profile, List<CharacterAscensionStage> stages,
            GameplayAttributeSet[] sets)
        {
            maxLevel = level;
            maxAscensionRank = rank;
            growthProfile = profile;
            ascensionStages = stages ?? new List<CharacterAscensionStage>();
            initialAttributeSets = sets ?? Array.Empty<GameplayAttributeSet>();
        }
    }

    /// <summary>角色移动参数配置组。</summary>
    [Serializable]
    public sealed class CharacterLocomotionConfig
    {
        /// <summary>重力。</summary>
        [SerializeField, MinValue(0f), LabelText("重力")] private float gravity = 9.81f;
        /// <summary>Locomotion 状态过渡。</summary>
        [SerializeField, Required, LabelText("Locomotion 状态过渡")] private PlayerFSMTransition locomotionTransition;
        /// <summary>读取重力。</summary>
        public float Gravity => gravity;
        /// <summary>读取状态过渡。</summary>
        public PlayerFSMTransition LocomotionTransition => locomotionTransition;
        /// <summary>从旧的平铺字段迁移移动配置。</summary>
        internal void CopyFrom(float value, PlayerFSMTransition transition) { gravity = value; locomotionTransition = transition; }
    }
}
