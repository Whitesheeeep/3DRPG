using System;
using System.Collections.Generic;
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
        [SerializeField, LabelText("角色名称")]
        private string characterName;
        [SerializeField, LabelText("稀有度")]
        private CharacterRarity rarity = CharacterRarity.Five;
        [SerializeField, WSAddressableKey("CharacterPrefabs"), LabelText("角色 Prefab 地址")]
        private string prefabAddress;
        [SerializeField, WSAddressableKey("UISpriteAtlas"), LabelText("侧面头像图集 Address")]
        private string sideIconAddress = CharacterAssetAddresses.SideIconsAtlas;
        [SerializeField, LabelText("侧面头像 Sprite 名称")]
        private string sideIconSpriteName;
        [SerializeField, WSAddressableKey("UISpriteAtlas"), LabelText("角色头像图集 Address")]
        private string avatarAddress = CharacterAssetAddresses.AvatarAtlas;
        [SerializeField, LabelText("角色头像 Sprite 名称")]
        private string avatarSpriteName;
        [SerializeField, MinValue(1), LabelText("最大等级")]
        private int maxLevel = 90;
        [SerializeField, MinValue(0), LabelText("最大突破阶数")]
        private int maxAscensionRank = 6;
        [SerializeField, Required, LabelText("成长配置")]
        private CharacterGrowthProfile growthProfile;
        [SerializeField, LabelText("突破阶段与消耗")]
        private List<CharacterAscensionStage> ascensionStages = new();
        [SerializeField, LabelText("初始属性集")]
        private GameplayAttributeSet[] initialAttributeSets = Array.Empty<GameplayAttributeSet>();
        [SerializeField, LabelText("战斗配置")]
        private CharacterCombatConfig combatConfig = new();
        [SerializeField, MinValue(0f), LabelText("重力")]
        private float gravity = 9.81f;
        [SerializeField, Required, LabelText("Locomotion 状态过渡")]
        private PlayerFSMTransition locomotionTransition;

#if UNITY_EDITOR
        [SerializeField, HideInInspector]
        private Sprite editorSideIcon;
        [SerializeField, HideInInspector]
        private Sprite editorAvatar;
#endif
        #endregion

        #region 属性
        /// <summary>获取稳定角色标识。</summary>
        public CharacterId CharacterId => characterId;
        /// <summary>获取用于编辑器和界面展示的角色名称。</summary>
        public string Name => characterName;
        /// <summary>获取角色稀有度。</summary>
        public CharacterRarity Rarity => rarity;
        /// <summary>获取 Addressables 角色 Prefab 地址。</summary>
        public string PrefabAddress => prefabAddress;
        /// <summary>获取侧面头像 SpriteAtlas 的 Addressable Address。</summary>
        public string SideIconAddress => sideIconAddress;
        /// <summary>获取侧面头像图集内的 Sprite 名称。</summary>
        public string SideIconSpriteName => sideIconSpriteName;
        /// <summary>获取角色头像 SpriteAtlas 的 Addressable Address。</summary>
        public string AvatarAddress => avatarAddress;
        /// <summary>获取角色头像图集内的 Sprite 名称。</summary>
        public string AvatarSpriteName => avatarSpriteName;
        /// <summary>获取角色最大等级。</summary>
        public int MaxLevel => maxLevel;
        /// <summary>获取角色最大突破阶数。</summary>
        public int MaxAscensionRank => maxAscensionRank;
        /// <summary>获取角色等级、货币和 Attribute 成长配置。</summary>
        public CharacterGrowthProfile GrowthProfile => growthProfile;
        /// <summary>获取角色突破阶段配置。</summary>
        public IReadOnlyList<CharacterAscensionStage> AscensionStages => ascensionStages;
        /// <summary>获取角色初始属性集，顺序保持作者配置。</summary>
        public IReadOnlyList<GameplayAttributeSet> InitialAttributeSets => initialAttributeSets;
        /// <summary>获取角色普通攻击连段和技能槽位配置。</summary>
        public CharacterCombatConfig CombatConfig => combatConfig;
        /// <summary>获取角色 Locomotion 重力。</summary>
        public float Gravity => gravity;
        /// <summary>获取角色 Locomotion 状态过渡配置。</summary>
        public PlayerFSMTransition LocomotionTransition => locomotionTransition;

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
            if (string.IsNullOrWhiteSpace(characterName))
                throw new InvalidOperationException($"CharacterConfig '{name}' 的角色名称不能为空。");
            if (!Enum.IsDefined(typeof(CharacterRarity), rarity))
                throw new InvalidOperationException($"CharacterConfig '{name}' 的 Rarity 无效。");
            if (string.IsNullOrWhiteSpace(prefabAddress))
                throw new InvalidOperationException($"CharacterConfig '{name}' 未配置 PrefabAddress。");
            if (string.IsNullOrWhiteSpace(sideIconAddress))
                throw new InvalidOperationException($"CharacterConfig '{name}' 未配置 SideIconAddress。");
            if (string.IsNullOrWhiteSpace(sideIconSpriteName))
                throw new InvalidOperationException($"CharacterConfig '{name}' 未配置 SideIconSpriteName。");
            if (string.IsNullOrWhiteSpace(avatarAddress))
                throw new InvalidOperationException($"CharacterConfig '{name}' 未配置 AvatarAddress。");
            if (string.IsNullOrWhiteSpace(avatarSpriteName))
                throw new InvalidOperationException($"CharacterConfig '{name}' 未配置 AvatarSpriteName。");
            if (maxLevel < 1)
                throw new InvalidOperationException($"CharacterConfig '{name}' 的最大等级必须大于零。");
            if (maxAscensionRank < 0)
                throw new InvalidOperationException($"CharacterConfig '{name}' 的最大突破阶数不能为负数。");
            if (growthProfile == null)
                throw new InvalidOperationException($"CharacterConfig '{name}' 未配置 CharacterGrowthProfile。");
            if (growthProfile.MaxLevel != maxLevel)
                throw new InvalidOperationException($"CharacterConfig '{name}' 与成长配置最大等级不一致。");
            if (float.IsNaN(gravity) || float.IsInfinity(gravity) || gravity < 0f)
                throw new InvalidOperationException($"CharacterConfig '{name}' 的 Gravity 必须是非负有限值。");
            if (locomotionTransition == null)
                throw new InvalidOperationException($"CharacterConfig '{name}' 未配置 LocomotionTransition。");
            if (combatConfig == null)
                throw new InvalidOperationException($"CharacterConfig '{name}' 未配置 CharacterCombatConfig。");
            try
            {
                locomotionTransition.Validate();
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    $"CharacterConfig '{name}' 的 LocomotionTransition 配置无效：{exception.Message}",
                    exception);
            }
            ValidateList(initialAttributeSets, "InitialAttributeSets");
            combatConfig.Validate(name);
            ValidateAscensionStages();
            growthProfile.Validate(initialAttributeSets);
        }

        /// <summary>校验突破阶段顺序、等级上限和阶段消耗。</summary>
        /// <exception cref="InvalidOperationException">突破阶段不满足配置契约时抛出。</exception>
        private void ValidateAscensionStages()
        {
            if (ascensionStages == null)
                throw new InvalidOperationException($"CharacterConfig '{name}' 的突破阶段列表为空引用。");
            if (ascensionStages.Count > maxAscensionRank)
                throw new InvalidOperationException($"CharacterConfig '{name}' 的突破阶段数量超过最大突破阶数。");

            int previousRequiredLevel = 0;
            int previousMaxLevel = 0;
            for (int index = 0; index < ascensionStages.Count; index++)
            {
                CharacterAscensionStage stage = ascensionStages[index];
                if (stage == null) throw new InvalidOperationException($"CharacterConfig '{name}' 的突破阶段第 {index} 项为空。");
                if (stage.RequiredLevel <= previousRequiredLevel || stage.RequiredLevel >= stage.MaxLevelAfter || stage.MaxLevelAfter > maxLevel)
                    throw new InvalidOperationException($"CharacterConfig '{name}' 的突破阶段 {index + 1} 等级范围无效。");
                if (index > 0 && stage.RequiredLevel != previousMaxLevel)
                    throw new InvalidOperationException($"CharacterConfig '{name}' 的突破阶段 {index + 1} 未与上一阶段连续。");
                if (stage.Cost == null)
                    throw new InvalidOperationException($"CharacterConfig '{name}' 的突破阶段 {index + 1} 缺少消耗配置。");
                stage.Cost.Validate();
                previousRequiredLevel = stage.RequiredLevel;
                previousMaxLevel = stage.MaxLevelAfter;
            }
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
        public IReadOnlyList<UnityEngine.Object> BakeTargets => growthProfile == null
            ? Array.Empty<UnityEngine.Object>()
            : new UnityEngine.Object[] { growthProfile };

        /// <summary>委托 GrowthProfile 生成角色成长烘焙结果。</summary>
        public void Bake()
        {
            if (growthProfile == null) throw new InvalidOperationException($"角色配置 '{name}' 缺少 CharacterGrowthProfile。");
            growthProfile.Bake(initialAttributeSets);
        }

        /// <summary>将最近一次成长烘焙结果转换为扁平表格。</summary>
        /// <returns>角色等级、经验、货币、突破和 Attribute BaseValue 表格。</returns>
        public BakedResultTableData CreateBakedResultTableData()
        {
            if (growthProfile == null) throw new InvalidOperationException($"角色配置 '{name}' 缺少 CharacterGrowthProfile。");
            var headers = new List<string> { "等级", "累计经验", "下一级经验", "货币消耗", "突破状态" };
            for (int index = 0; index < growthProfile.BakedAttributeProgressions.Count; index++)
                headers.Add(growthProfile.BakedAttributeProgressions[index].Attribute.DisplayName);

            var rows = new List<BakedResultRowData>(growthProfile.BakedLevelProgressions.Count);
            for (int index = 0; index < growthProfile.BakedLevelProgressions.Count; index++)
            {
                BakedCharacterLevelProgression progression = growthProfile.BakedLevelProgressions[index];
                var cells = new List<string>
                {
                    progression.Level.ToString("N0", CultureInfo.InvariantCulture),
                    progression.CumulativeExperience.ToString("N0", CultureInfo.InvariantCulture),
                    progression.NextExperience.ToString("N0", CultureInfo.InvariantCulture),
                    progression.CurrencyCost.ToString("N0", CultureInfo.InvariantCulture),
                    IsAscensionLevel(progression.Level) ? "突破点" : "—"
                };

                for (int attributeIndex = 0; attributeIndex < growthProfile.BakedAttributeProgressions.Count; attributeIndex++)
                {
                    IReadOnlyList<float> values = growthProfile.BakedAttributeProgressions[attributeIndex].BaseValues;
                    cells.Add(values.Count > index
                        ? values[index].ToString("0.###", CultureInfo.InvariantCulture)
                        : "—");
                }

                rows.Add(new BakedResultRowData(cells));
            }

            return new BakedResultTableData(BakedResultTitle, headers, rows);
        }

        /// <summary>判断指定等级是否命中角色突破阶段。</summary>
        /// <param name="level">待检查等级。</param>
        /// <returns>命中突破点时返回 true。</returns>
        private bool IsAscensionLevel(int level)
        {
            for (int index = 0; index < ascensionStages.Count; index++)
                if (ascensionStages[index] != null && ascensionStages[index].RequiredLevel == level)
                    return true;
            return false;
        }

        #endregion
#endif
    }
}
