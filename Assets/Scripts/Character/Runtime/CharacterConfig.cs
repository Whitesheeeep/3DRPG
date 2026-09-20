using System;
using System.Collections.Generic;
using RPG.ItemSystem;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules;
using WS_Modules.GAS.AttributeSystem;

#if UNITY_EDITOR
using System.Globalization;
using WS_Modules.Baking;
#endif

namespace RPG.Character
{
    /// <summary>保存角色运行时所需的身份、Prefab 地址、属性、战斗与移动配置。</summary>
    [CreateAssetMenu(fileName = "CharacterConfig", menuName = "RPG/Character/Character Config")]
#if UNITY_EDITOR
    public sealed class CharacterConfig : ScriptableObject, IBakedResultDataSource
#else
    public sealed class CharacterConfig : ScriptableObject
#endif
    {
        #region 配置字段
        [SerializeField, ReadOnly, LabelText("角色标识")]
        private CharacterId characterId;
        [SerializeField, HideInInspector]
        private string characterName;
        [SerializeField, HideInInspector]
        private CharacterRarity rarity = CharacterRarity.Five;
        [SerializeField, HideInInspector]
        private string prefabAddress;
        [SerializeField, HideInInspector]
        private string sideIconAddress = CharacterAssetAddresses.SideIconsAtlas;
        [SerializeField, HideInInspector]
        private string sideIconSpriteName;
        [SerializeField, HideInInspector]
        private string avatarAddress = CharacterAssetAddresses.AvatarAtlas;
        [SerializeField, HideInInspector]
        private string avatarSpriteName;
        [SerializeField, HideInInspector]
        private WeaponTypeFlags allowedWeaponTypes = WeaponTypeFlags.Sword;
        [SerializeField, HideInInspector]
        private WeaponType defaultWeaponType = WeaponType.Sword;
        [SerializeField, HideInInspector]
        private int maxLevel = 90;
        [SerializeField, HideInInspector]
        private int maxAscensionRank = 6;
        [SerializeField, HideInInspector]
        private CharacterGrowthProfile growthProfile;
        [SerializeField, HideInInspector]
        private List<CharacterAscensionStage> ascensionStages = new();
        [SerializeField, HideInInspector]
        private GameplayAttributeSet[] initialAttributeSets = Array.Empty<GameplayAttributeSet>();
        [SerializeField, LabelText("身份配置")]
        private CharacterIdentityConfig identity = new();
        [SerializeField, LabelText("表现配置")]
        private CharacterPresentationConfig presentation = new();
        [SerializeField, LabelText("装备规则")]
        private CharacterEquipmentRuleConfig equipmentRules = new();
        [SerializeField, LabelText("成长配置")]
        private CharacterProgressionConfig progression = new();
        [SerializeField, LabelText("战斗配置")]
        private CharacterCombatConfig combatConfig = new();
        [SerializeField, MinValue(0f), LabelText("重力")]
        private float gravity = 9.81f;
        [SerializeField, Required, LabelText("Locomotion 状态过渡")]
        private PlayerFSMTransition locomotionTransition;
        [SerializeField, LabelText("移动配置")]
        private CharacterLocomotionConfig locomotion = new();
        [SerializeField, HideInInspector] private bool legacyFieldsMigrated;

#if UNITY_EDITOR
        [SerializeField, HideInInspector]
        private Sprite editorSideIcon;
        [SerializeField, HideInInspector]
        private Sprite editorAvatar;
#endif
        #endregion

        #region 兼容迁移

        /// <summary>对象启用时把旧版平铺字段迁移到新的职责分组。</summary>
        private void OnEnable() => MigrateLegacyFields();

        /// <summary>编辑器序列化值变化后保持旧资产与分组字段一致。</summary>
        private void OnValidate() => MigrateLegacyFields();

        /// <summary>只在检测到旧版字段时执行一次内存和序列化分组迁移。</summary>
        private void MigrateLegacyFields()
        {
            if (legacyFieldsMigrated) return;
            bool hasLegacyValue = !string.IsNullOrWhiteSpace(characterName) ||
                                  !string.IsNullOrWhiteSpace(prefabAddress) || growthProfile != null ||
                                  initialAttributeSets != null && initialAttributeSets.Length > 0;
            if (!hasLegacyValue) return;
            if (identity == null) identity = new CharacterIdentityConfig();
            if (presentation == null) presentation = new CharacterPresentationConfig();
            if (equipmentRules == null) equipmentRules = new CharacterEquipmentRuleConfig();
            if (progression == null) progression = new CharacterProgressionConfig();
            if (locomotion == null) locomotion = new CharacterLocomotionConfig();
            identity.CopyFrom(characterName, rarity);
            presentation.CopyFrom(prefabAddress, sideIconAddress, sideIconSpriteName, avatarAddress, avatarSpriteName);
            equipmentRules.CopyFrom(allowedWeaponTypes, defaultWeaponType);
            progression.CopyFrom(maxLevel, maxAscensionRank, growthProfile, ascensionStages, initialAttributeSets);
            locomotion.CopyFrom(gravity, locomotionTransition);
            legacyFieldsMigrated = true;
        }

        #endregion

        #region 属性
        /// <summary>获取稳定角色标识。</summary>
        public CharacterId CharacterId => characterId;
        /// <summary>获取用于编辑器和界面展示的角色名称。</summary>
        public string Name => identity != null && !string.IsNullOrWhiteSpace(identity.CharacterName) ? identity.CharacterName : characterName;
        /// <summary>获取角色稀有度。</summary>
        public CharacterRarity Rarity => identity != null ? identity.Rarity : rarity;
        /// <summary>获取 Addressables 角色 Prefab 地址。</summary>
        public string PrefabAddress => presentation != null && !string.IsNullOrWhiteSpace(presentation.PrefabAddress) ? presentation.PrefabAddress : prefabAddress;
        /// <summary>获取侧面头像 SpriteAtlas 的 Addressable Address。</summary>
        public string SideIconAddress => presentation != null && !string.IsNullOrWhiteSpace(presentation.SideIconAddress) ? presentation.SideIconAddress : sideIconAddress;
        /// <summary>获取侧面头像图集内的 Sprite 名称。</summary>
        public string SideIconSpriteName => presentation != null && !string.IsNullOrWhiteSpace(presentation.SideIconSpriteName) ? presentation.SideIconSpriteName : sideIconSpriteName;
        /// <summary>获取角色头像 SpriteAtlas 的 Addressable Address。</summary>
        public string AvatarAddress => presentation != null && !string.IsNullOrWhiteSpace(presentation.AvatarAddress) ? presentation.AvatarAddress : avatarAddress;
        /// <summary>获取角色头像图集内的 Sprite 名称。</summary>
        public string AvatarSpriteName => presentation != null && !string.IsNullOrWhiteSpace(presentation.AvatarSpriteName) ? presentation.AvatarSpriteName : avatarSpriteName;
        /// <summary>获取角色允许装备的武器类型位掩码。</summary>
        public WeaponTypeFlags AllowedWeaponTypes => equipmentRules != null ? equipmentRules.AllowedWeaponTypes : allowedWeaponTypes;
        /// <summary>获取新角色生成默认武器时使用的武器类型。</summary>
        public WeaponType DefaultWeaponType => equipmentRules != null ? equipmentRules.DefaultWeaponType : defaultWeaponType;
        /// <summary>获取角色最大等级。</summary>
        public int MaxLevel => progression != null ? progression.MaxLevel : maxLevel;
        /// <summary>获取角色最大突破阶数。</summary>
        public int MaxAscensionRank => progression != null ? progression.MaxAscensionRank : maxAscensionRank;
        /// <summary>获取角色等级、货币和 Attribute 成长配置。</summary>
        public CharacterGrowthProfile GrowthProfile => progression != null && progression.GrowthProfile != null ? progression.GrowthProfile : growthProfile;
        /// <summary>获取角色突破阶段配置。</summary>
        public IReadOnlyList<CharacterAscensionStage> AscensionStages => progression != null && progression.AscensionStages != null ? progression.AscensionStages : ascensionStages;
        /// <summary>获取角色初始属性集，顺序保持作者配置。</summary>
        public IReadOnlyList<GameplayAttributeSet> InitialAttributeSets => progression != null && progression.InitialAttributeSets != null ? progression.InitialAttributeSets : initialAttributeSets;
        /// <summary>获取角色普通攻击连段和技能槽位配置。</summary>
        public CharacterCombatConfig CombatConfig => combatConfig;
        /// <summary>获取角色 Locomotion 重力。</summary>
        public float Gravity => locomotion != null ? locomotion.Gravity : gravity;
        /// <summary>获取角色 Locomotion 状态过渡配置。</summary>
        public PlayerFSMTransition LocomotionTransition => locomotion != null && locomotion.LocomotionTransition != null ? locomotion.LocomotionTransition : locomotionTransition;

        /// <summary>获取身份配置组。</summary>
        public CharacterIdentityConfig Identity => identity;
        /// <summary>获取表现配置组。</summary>
        public CharacterPresentationConfig Presentation => presentation;
        /// <summary>获取装备规则配置组。</summary>
        public CharacterEquipmentRuleConfig EquipmentRules => equipmentRules;
        /// <summary>获取成长配置组。</summary>
        public CharacterProgressionConfig Progression => progression;
        /// <summary>获取战斗配置组。</summary>
        public CharacterCombatConfig Combat => combatConfig;
        /// <summary>获取移动配置组。</summary>
        public CharacterLocomotionConfig Locomotion => locomotion;

        /// <summary>判断当前角色是否允许装备指定类型的武器。</summary>
        /// <param name="weaponType">待检查的武器类型。</param>
        /// <returns>角色允许该类型时返回 true。</returns>
        public bool AllowsWeaponType(WeaponType weaponType) => AllowedWeaponTypes.Includes(weaponType);

#if UNITY_EDITOR
        /// <summary>获取 Editor 预览用侧面头像。</summary>
        public Sprite EditorSideIcon => editorSideIcon;
        /// <summary>获取 Editor 预览用角色头像。</summary>
        public Sprite EditorAvatar => editorAvatar;
#endif
        #endregion

        #region 校验
        /// <summary>校验角色配置是否可被 CharacterManager 加载。</summary>
        /// <exception cref="InvalidOperationException">配置缺少必需字段时抛出。</exception>
        public void Validate()
        {
            if (!characterId.IsValid)
                throw new InvalidOperationException($"CharacterConfig '{name}' 的 CharacterId 无效。");
            if (string.IsNullOrWhiteSpace(Name))
                throw new InvalidOperationException($"CharacterConfig '{name}' 的角色名称不能为空。");
            if (!Enum.IsDefined(typeof(CharacterRarity), Rarity))
                throw new InvalidOperationException($"CharacterConfig '{name}' 的 Rarity 无效。");
            if (string.IsNullOrWhiteSpace(PrefabAddress))
                throw new InvalidOperationException($"CharacterConfig '{name}' 未配置 PrefabAddress。");
            if (string.IsNullOrWhiteSpace(SideIconAddress))
                throw new InvalidOperationException($"CharacterConfig '{name}' 未配置 SideIconAddress。");
            if (string.IsNullOrWhiteSpace(SideIconSpriteName))
                throw new InvalidOperationException($"CharacterConfig '{name}' 未配置 SideIconSpriteName。");
            if (string.IsNullOrWhiteSpace(AvatarAddress))
                throw new InvalidOperationException($"CharacterConfig '{name}' 未配置 AvatarAddress。");
            if (string.IsNullOrWhiteSpace(AvatarSpriteName))
                throw new InvalidOperationException($"CharacterConfig '{name}' 未配置 AvatarSpriteName。");
            if (AllowedWeaponTypes == WeaponTypeFlags.None ||
                (AllowedWeaponTypes & ~WeaponTypeFlags.All) != WeaponTypeFlags.None)
                throw new InvalidOperationException($"CharacterConfig '{name}' 的 AllowedWeaponTypes 无效。");
            if (!Enum.IsDefined(typeof(WeaponType), DefaultWeaponType))
                throw new InvalidOperationException($"CharacterConfig '{name}' 的 DefaultWeaponType 无效。");
            if (!AllowedWeaponTypes.Includes(DefaultWeaponType))
                throw new InvalidOperationException($"CharacterConfig '{name}' 的默认武器类型未包含在允许装备的武器类型中。");
            if (MaxLevel < 1)
                throw new InvalidOperationException($"CharacterConfig '{name}' 的最大等级必须大于零。");
            if (MaxAscensionRank < 0)
                throw new InvalidOperationException($"CharacterConfig '{name}' 的最大突破阶数不能为负数。");
            if (GrowthProfile == null)
                throw new InvalidOperationException($"CharacterConfig '{name}' 未配置 CharacterGrowthProfile。");
            if (GrowthProfile.MaxLevel != MaxLevel)
                throw new InvalidOperationException($"CharacterConfig '{name}' 与成长配置最大等级不一致。");
            if (float.IsNaN(Gravity) || float.IsInfinity(Gravity) || Gravity < 0f)
                throw new InvalidOperationException($"CharacterConfig '{name}' 的 Gravity 必须是非负有限值。");
            if (LocomotionTransition == null)
                throw new InvalidOperationException($"CharacterConfig '{name}' 未配置 LocomotionTransition。");
            if (combatConfig == null)
                throw new InvalidOperationException($"CharacterConfig '{name}' 未配置 CharacterCombatConfig。");
            try
            {
                LocomotionTransition.Validate();
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    $"CharacterConfig '{name}' 的 LocomotionTransition 配置无效：{exception.Message}",
                    exception);
            }
            ValidateList(InitialAttributeSets, "InitialAttributeSets");
            combatConfig.Validate(name);
            ValidateAscensionStages();
            GrowthProfile.Validate(InitialAttributeSets);
        }

        /// <summary>校验突破阶段顺序、等级上限和阶段消耗。</summary>
        /// <exception cref="InvalidOperationException">突破阶段不满足配置契约时抛出。</exception>
        private void ValidateAscensionStages()
        {
            if (AscensionStages == null)
                throw new InvalidOperationException($"CharacterConfig '{name}' 的突破阶段列表为空引用。");
            if (AscensionStages.Count != MaxAscensionRank)
                throw new InvalidOperationException($"CharacterConfig '{name}' 的突破阶段数量必须等于最大突破阶数：{AscensionStages.Count}/{MaxAscensionRank}。");

            int previousRequiredLevel = 0;
            int previousMaxLevel = 0;
            for (int index = 0; index < AscensionStages.Count; index++)
            {
                CharacterAscensionStage stage = AscensionStages[index];
                if (stage == null) throw new InvalidOperationException($"CharacterConfig '{name}' 的突破阶段第 {index} 项为空。");
                if (stage.RequiredLevel <= previousRequiredLevel || stage.RequiredLevel >= stage.MaxLevelAfter || stage.MaxLevelAfter > MaxLevel)
                    throw new InvalidOperationException($"CharacterConfig '{name}' 的突破阶段 {index + 1} 等级范围无效。");
                if (index > 0 && stage.RequiredLevel != previousMaxLevel)
                    throw new InvalidOperationException($"CharacterConfig '{name}' 的突破阶段 {index + 1} 未与上一阶段连续。");
                if (stage.Cost == null)
                    throw new InvalidOperationException($"CharacterConfig '{name}' 的突破阶段 {index + 1} 缺少消耗配置。");
                stage.Cost.Validate();
                previousRequiredLevel = stage.RequiredLevel;
                previousMaxLevel = stage.MaxLevelAfter;
            }
            if (AscensionStages.Count > 0 && AscensionStages[AscensionStages.Count - 1].MaxLevelAfter != MaxLevel)
                throw new InvalidOperationException($"CharacterConfig '{name}' 的最后突破阶段必须达到最大等级 {MaxLevel}。");
        }

        /// <summary>校验配置列表中不存在空元素。</summary>
        /// <typeparam name="T">列表元素类型。</typeparam>
        /// <param name="items">待校验列表。</param>
        /// <param name="fieldName">字段名称。</param>
        private void ValidateList<T>(IReadOnlyList<T> items, string fieldName) where T : class
        {
            if (items == null) throw new InvalidOperationException($"CharacterConfig '{name}' 的 {fieldName} 为空。");
            for (int index = 0; index < items.Count; index++)
                if (items[index] == null)
                    throw new InvalidOperationException($"CharacterConfig '{name}' 的 {fieldName}[{index}] 为空。");
        }
        #endregion

#if UNITY_EDITOR
        #region 烘焙结果数据源

        /// <summary>获取角色成长结果窗口标题。</summary>
        public string BakedResultTitle => $"{(string.IsNullOrWhiteSpace(Name) ? name : Name)} - 角色成长烘焙结果";

        /// <summary>获取 Bake 会修改的成长 Profile。</summary>
        public IReadOnlyList<UnityEngine.Object> BakeTargets => GrowthProfile == null
            ? Array.Empty<UnityEngine.Object>()
            : new UnityEngine.Object[] { GrowthProfile };

        /// <summary>委托 GrowthProfile 生成角色成长烘焙结果。</summary>
        public void Bake()
        {
            if (GrowthProfile == null) throw new InvalidOperationException($"角色配置 '{name}' 缺少 CharacterGrowthProfile。");
            GrowthProfile.Bake(InitialAttributeSets);
        }

        /// <summary>将最近一次成长烘焙结果转换为扁平表格。</summary>
        /// <returns>角色等级、经验、货币、突破和 Attribute BaseValue 表格。</returns>
        public BakedResultTableData CreateBakedResultTableData()
        {
            if (GrowthProfile == null) throw new InvalidOperationException($"角色配置 '{name}' 缺少 CharacterGrowthProfile。");
            var headers = new List<string> { "等级", "累计经验", "下一级经验", "货币消耗", "突破状态" };
            for (int index = 0; index < GrowthProfile.BakedAttributeProgressions.Count; index++)
            {
                GameplayAttribute attribute = GrowthProfile.BakedAttributeProgressions[index].Attribute;
                string header = string.IsNullOrWhiteSpace(attribute.DisplayName) ? attribute.Name : attribute.DisplayName;
                headers.Add(string.IsNullOrWhiteSpace(header) ? attribute.ToString() : header);
            }

            var rows = new List<BakedResultRowData>(GrowthProfile.BakedLevelProgressions.Count);
            for (int index = 0; index < GrowthProfile.BakedLevelProgressions.Count; index++)
            {
                BakedCharacterLevelProgression progression = GrowthProfile.BakedLevelProgressions[index];
                var cells = new List<string>
                {
                    progression.Level.ToString("N0", CultureInfo.InvariantCulture),
                    progression.CumulativeExperience.ToString("N0", CultureInfo.InvariantCulture),
                    progression.NextExperience.ToString("N0", CultureInfo.InvariantCulture),
                    progression.CurrencyCost.ToString("N0", CultureInfo.InvariantCulture),
                    IsAscensionLevel(progression.Level) ? "突破点" : "—"
                };

                for (int attributeIndex = 0; attributeIndex < GrowthProfile.BakedAttributeProgressions.Count; attributeIndex++)
                {
                    IReadOnlyList<float> values = GrowthProfile.BakedAttributeProgressions[attributeIndex].BaseValues;
                    cells.Add(values.Count > index
                        ? values[index].ToString("0.###", CultureInfo.InvariantCulture)
                        : "—");
                }

                rows.Add(new BakedResultRowData(cells));
            }

            int attributeCount = GrowthProfile.BakedAttributeProgressions.Count;
            string statusText = rows.Count == 0
                ? "尚未生成烘焙结果。"
                : $"已生成 {rows.Count} 个等级、{attributeCount} 个 Attribute，共 {headers.Count} 列。";
            return new BakedResultTableData(BakedResultTitle, headers, rows, statusText);
        }

        /// <summary>判断指定等级是否命中角色突破阶段。</summary>
        /// <param name="level">待检查等级。</param>
        /// <returns>命中突破点时返回 true。</returns>
        private bool IsAscensionLevel(int level)
        {
            for (int index = 0; index < AscensionStages.Count; index++)
                if (AscensionStages[index] != null && AscensionStages[index].RequiredLevel == level)
                    return true;
            return false;
        }

        #endregion
#endif
    }
}
