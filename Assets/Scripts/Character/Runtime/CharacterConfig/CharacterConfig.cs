using System;
using System.Collections.Generic;
using RPG.ItemSystem;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules;
using WS_Modules.GAS.AttributeSystem;

#if UNITY_EDITOR
using System.Globalization;
using UnityEditor;
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
        [SerializeField, LabelText("移动配置")]
        private CharacterLocomotionConfig locomotion = new();

#if UNITY_EDITOR
        [SerializeField, HideInInspector]
        private Sprite editorSideIcon;
        [SerializeField, HideInInspector]
        private Sprite editorAvatar;
        [SerializeField, HideInInspector]
        private Sprite editorFullBodyPortrait;
#endif
        #endregion

        #region 属性
        /// <summary>获取稳定角色标识。</summary>
        public CharacterId CharacterId => characterId;
        /// <summary>获取用于编辑器和界面展示的角色名称。</summary>
        public string Name => identity.CharacterName;
        /// <summary>获取角色稀有度。</summary>
        public CharacterRarity Rarity => identity.Rarity;
        /// <summary>获取 Addressables 角色 Prefab 地址。</summary>
        public string PrefabAddress => presentation.PrefabAddress;
        /// <summary>获取侧面头像 SpriteAtlas 的 Addressable Address。</summary>
        public string SideIconAddress => presentation.SideIconAddress;
        /// <summary>获取侧面头像图集内的 Sprite 名称。</summary>
        public string SideIconSpriteName => presentation.SideIconSpriteName;
        /// <summary>获取角色头像 SpriteAtlas 的 Addressable Address。</summary>
        public string AvatarAddress => presentation.AvatarAddress;
        /// <summary>获取角色头像图集内的 Sprite 名称。</summary>
        public string AvatarSpriteName => presentation.AvatarSpriteName;
        /// <summary>获取角色全身立绘 SpriteAtlas 的 Addressable Address。</summary>
        public string FullBodyPortraitAddress => presentation.FullBodyPortraitAddress;
        /// <summary>获取全身立绘图集内的 Sprite 名称；未配置时角色详情仍可正常打开。</summary>
        public string FullBodyPortraitSpriteName => presentation.FullBodyPortraitSpriteName;
        /// <summary>获取角色允许装备的武器类型位掩码。</summary>
        public WeaponTypeFlags AllowedWeaponTypes => equipmentRules.AllowedWeaponTypes;
        /// <summary>获取新角色生成默认武器时使用的武器类型。</summary>
        public WeaponType DefaultWeaponType => equipmentRules.DefaultWeaponType;
        /// <summary>获取角色最大等级。</summary>
        public int MaxLevel => progression.MaxLevel;
        /// <summary>获取角色最大突破阶数。</summary>
        public int MaxAscensionRank => progression.MaxAscensionRank;
        /// <summary>获取角色等级、货币和 Attribute 成长配置。</summary>
        public CharacterGrowthProfile GrowthProfile => progression.GrowthProfile;
        /// <summary>获取角色突破阶段配置。</summary>
        public IReadOnlyList<CharacterAscensionStage> AscensionStages => progression.AscensionStages;
        /// <summary>获取角色初始属性集，顺序保持作者配置。</summary>
        public IReadOnlyList<GameplayAttributeSet> InitialAttributeSets => progression.InitialAttributeSets;
        /// <summary>获取角色资源规则，资源必须通过显式配置声明。</summary>
        public IReadOnlyList<CharacterResourceRule> ResourceRules => progression.ResourceRules;
        /// <summary>获取角色普通攻击连段和技能槽位配置。</summary>
        public CharacterCombatConfig CombatConfig => combatConfig;
        /// <summary>获取角色 Locomotion 重力。</summary>
        public float Gravity => locomotion.Gravity;
        /// <summary>获取角色 Locomotion 状态过渡配置。</summary>
        public PlayerFSMTransition LocomotionTransition => locomotion.LocomotionTransition;

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
        /// <summary>获取 Editor 预览用角色全身立绘。</summary>
        public Sprite EditorFullBodyPortrait => editorFullBodyPortrait;
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
            ValidateResourceRules();
            if (GrowthProfile.NeedsRebake(InitialAttributeSets))
                throw new InvalidOperationException($"CharacterConfig '{name}' 的成长烘焙结果已过期，请重新 Bake。");
        }

        /// <summary>校验所有 Resource 都有唯一规则，并检查容量关系的属性类型。</summary>
        private void ValidateResourceRules()
        {
            if (ResourceRules == null)
                throw new InvalidOperationException($"CharacterConfig '{name}' 的 ResourceRules 为空引用。");
            var definitionByAttributeIdMap = new Dictionary<int, GameplayAttributeDefinition>();
            for (int setIndex = 0; setIndex < InitialAttributeSets.Count; setIndex++)
            {
                IReadOnlyList<GameplayAttributeDefinition> definitions = InitialAttributeSets[setIndex].Definitions;
                for (int definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
                {
                    GameplayAttributeDefinition definition = definitions[definitionIndex];
                    if (!definitionByAttributeIdMap.TryAdd(definition.Attribute.Id, definition))
                        throw new InvalidOperationException($"CharacterConfig '{name}' 重复配置 AttributeId {definition.Attribute.Id}。");
                }
            }

            var resourceRuleByAttributeIdMap = new Dictionary<int, CharacterResourceRule>();
            for (int index = 0; index < ResourceRules.Count; index++)
            {
                CharacterResourceRule rule = ResourceRules[index];
                if (rule == null || !rule.ResourceAttribute.IsValid ||
                    !resourceRuleByAttributeIdMap.TryAdd(rule.ResourceAttribute.Id, rule))
                    throw new InvalidOperationException($"CharacterConfig '{name}' 的 ResourceRules 包含空项、无效 Attribute 或重复资源。");
                if (!Enum.IsDefined(typeof(CharacterResourceInitialValueMode), rule.InitialValueMode))
                    throw new InvalidOperationException($"CharacterConfig '{name}' 的资源 {rule.ResourceAttribute} 初始值模式无效。");
                if (!definitionByAttributeIdMap.TryGetValue(rule.ResourceAttribute.Id, out GameplayAttributeDefinition resourceDefinition) ||
                    resourceDefinition.Type != GameplayAttributeType.Resource)
                    throw new InvalidOperationException($"CharacterConfig '{name}' 的资源规则未指向 Resource Attribute：{rule.ResourceAttribute}。");
                if (rule.InitialValueMode == CharacterResourceInitialValueMode.FullCapacity &&
                    !rule.CapacityAttribute.IsValid)
                    throw new InvalidOperationException($"CharacterConfig '{name}' 的资源 {rule.ResourceAttribute} 使用 FullCapacity 却没有 Capacity Attribute。");
                if (rule.CapacityAttribute.IsValid &&
                    (!definitionByAttributeIdMap.TryGetValue(rule.CapacityAttribute.Id, out GameplayAttributeDefinition capacityDefinition) ||
                     capacityDefinition.Type != GameplayAttributeType.Stat))
                    throw new InvalidOperationException($"CharacterConfig '{name}' 的容量 Attribute 无效或不是 Stat：{rule.CapacityAttribute}。");
            }

            foreach (KeyValuePair<int, GameplayAttributeDefinition> pair in definitionByAttributeIdMap)
                if (pair.Value.Type == GameplayAttributeType.Resource && !resourceRuleByAttributeIdMap.ContainsKey(pair.Key))
                    throw new InvalidOperationException($"CharacterConfig '{name}' 的 Resource Attribute 未配置 ResourceRule：{pair.Value.Attribute}。");
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
