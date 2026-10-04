using System;
using System.Collections.Generic;
using Animancer;
using RPG.Character;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.GameplayAbilitySystem;

#if UNITY_EDITOR
using System.Globalization;
using WS_Modules.Baking;
#endif

namespace RPG.NPC
{
    /// <summary>保存 NPC 的初始 GAS 属性、等级成长、资源初始化、可授予技能与 Idle/Move 动画。</summary>
    [CreateAssetMenu(menuName = "RPG/NPC/NPC Config", fileName = "NPCConfig")]
#if UNITY_EDITOR
    public sealed class NPCConfig : ScriptableObject, IBakedResultDataSource
#else
    public sealed class NPCConfig : ScriptableObject
#endif
    {
        #region 配置字段

        [SerializeField, Required, AssetsOnly, LabelText("初始属性集")]
        private GameplayAttributeSet[] initialAttributeSets = Array.Empty<GameplayAttributeSet>();
        [SerializeField, Required, AssetsOnly, LabelText("等级成长配置")]
        private NPCGrowthProfile growthProfile;
        [SerializeField, LabelText("资源初始化规则")]
        private List<CharacterResourceRule> resourceRules = new();
        [SerializeField, Required, AssetsOnly, LabelText("可用技能")]
        private GameplayAbilityData[] grantedAbilities = Array.Empty<GameplayAbilityData>();
        [SerializeField, Required, AssetsOnly, LabelText("Idle 动画")]
        private TransitionAsset idleTransition;
        [SerializeField, Required, AssetsOnly, LabelText("Move 动画")]
        private TransitionAsset moveTransition;
        [SerializeField, Required, AssetsOnly, LabelText("Move 混合参数")]
        private StringAsset moveParameterX;

        #endregion

        #region 配置查询

        /// <summary>获取按作者顺序应用的初始属性集。</summary>
        public IReadOnlyList<GameplayAttributeSet> InitialAttributeSets =>
            initialAttributeSets ?? Array.Empty<GameplayAttributeSet>();

        /// <summary>获取 NPC 专用的逐级 Attribute BaseValue 烘焙配置。</summary>
        public NPCGrowthProfile GrowthProfile => growthProfile;

        /// <summary>获取 NPC Resource CurrentValue 的生成时初始化规则。</summary>
        public IReadOnlyList<CharacterResourceRule> ResourceRules =>
            (IReadOnlyList<CharacterResourceRule>)resourceRules ?? Array.Empty<CharacterResourceRule>();

        /// <summary>获取初始化时授予 NPC 的技能列表。</summary>
        public IReadOnlyList<GameplayAbilityData> GrantedAbilities =>
            grantedAbilities ?? Array.Empty<GameplayAbilityData>();

        /// <summary>获取 NPC Idle 状态进入时播放的 Animancer Transition。</summary>
        public TransitionAsset IdleTransition => idleTransition;

        /// <summary>获取 NPC Move 状态进入时播放的 Animancer Transition。</summary>
        public TransitionAsset MoveTransition => moveTransition;

        /// <summary>获取 Move Mixer 中用于选择步行采样的参数。</summary>
        public StringAsset MoveParameterX => moveParameterX;

        #endregion

        #region 配置校验

        // 配置整体校验
        /// <summary>在 NPC 初始化前校验属性、技能与状态表现资源是否完整。</summary>
        /// <exception cref="InvalidOperationException">配置缺少必需资源或包含空条目。</exception>
        public void Validate()
        {
            if (initialAttributeSets == null || initialAttributeSets.Length == 0)
                throw new InvalidOperationException($"NPCConfig '{name}' 必须配置至少一个初始 AttributeSet。");
            for (int index = 0; index < initialAttributeSets.Length; index++)
            {
                if (initialAttributeSets[index] == null)
                    throw new InvalidOperationException($"NPCConfig '{name}' 的 AttributeSet[{index}] 未配置。");
            }

            if (growthProfile == null)
                throw new InvalidOperationException($"NPCConfig '{name}' 未配置 NPCGrowthProfile。");

            if (grantedAbilities == null || grantedAbilities.Length == 0)
                throw new InvalidOperationException($"NPCConfig '{name}' 必须配置至少一个可用 Ability。");
            for (int index = 0; index < grantedAbilities.Length; index++)
            {
                if (grantedAbilities[index] == null)
                    throw new InvalidOperationException($"NPCConfig '{name}' 的 Ability[{index}] 未配置。");
                for (int previousIndex = 0; previousIndex < index; previousIndex++)
                {
                    if (ReferenceEquals(grantedAbilities[index], grantedAbilities[previousIndex]))
                        throw new InvalidOperationException(
                            $"NPCConfig '{name}' 重复配置 Ability '{grantedAbilities[index].name}'。");
                }
            }

            if (idleTransition == null || idleTransition.Transition == null)
                throw new InvalidOperationException($"NPCConfig '{name}' 未配置有效 Idle Transition。");
            if (moveTransition == null || moveTransition.Transition == null)
                throw new InvalidOperationException($"NPCConfig '{name}' 未配置有效 Move Transition。");
            if (moveParameterX == null)
                throw new InvalidOperationException($"NPCConfig '{name}' 未配置 Move Mixer 参数。");

            growthProfile.Validate(InitialAttributeSets);
            ValidateResourceRules();
            if (growthProfile.NeedsRebake(InitialAttributeSets))
                throw new InvalidOperationException(
                    $"NPCConfig '{name}' 的等级属性烘焙结果缺失或已过期，请重新 Bake。");
        }

        // 等级属性解析
        /// <summary>使用已验证的成长 Profile 解析指定生成等级的完整 Attribute BaseValue。</summary>
        /// <param name="level">NPC 实例生成等级。</param>
        /// <returns>与初始 AttributeSet 顺序一致的烘焙 BaseValue。</returns>
        internal IReadOnlyList<GameplayAttributeValue> ResolveBaseValues(int level) =>
            growthProfile.ResolveBaseValues(level, InitialAttributeSets);

        // Resource 规则校验
        /// <summary>校验 Resource 规则只引用唯一且类型匹配的输入属性。</summary>
        private void ValidateResourceRules()
        {
            // key：AttributeId；value：NPC 初始集合中对应的 Attribute Definition。
            var definitionByAttributeIdMap = new Dictionary<int, GameplayAttributeDefinition>();
            for (int setIndex = 0; setIndex < InitialAttributeSets.Count; setIndex++)
            {
                IReadOnlyList<GameplayAttributeDefinition> definitions = InitialAttributeSets[setIndex].Definitions;
                for (int definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
                {
                    GameplayAttributeDefinition definition = definitions[definitionIndex];
                    if (!definitionByAttributeIdMap.TryAdd(definition.Attribute.Id, definition))
                        throw new InvalidOperationException(
                            $"NPCConfig '{name}' 重复配置 AttributeId {definition.Attribute.Id}。");
                }
            }

            if (resourceRules == null)
                throw new InvalidOperationException($"NPCConfig '{name}' 的 ResourceRules 为空引用。");

            // key：Resource AttributeId；value：该资源唯一的 NPC 初始值规则。
            var resourceRuleByAttributeIdMap = new Dictionary<int, CharacterResourceRule>();
            for (int index = 0; index < resourceRules.Count; index++)
            {
                CharacterResourceRule rule = resourceRules[index];
                if (rule == null || !rule.ResourceAttribute.IsValid ||
                    !resourceRuleByAttributeIdMap.TryAdd(rule.ResourceAttribute.Id, rule))
                    throw new InvalidOperationException(
                        $"NPCConfig '{name}' 的 ResourceRules[{index}] 为空、无效或重复配置。");
                if (!Enum.IsDefined(typeof(CharacterResourceInitialValueMode), rule.InitialValueMode))
                    throw new InvalidOperationException(
                        $"NPCConfig '{name}' 的资源 {rule.ResourceAttribute} 初始值模式无效。");
                if (!definitionByAttributeIdMap.TryGetValue(
                        rule.ResourceAttribute.Id,
                        out GameplayAttributeDefinition resourceDefinition) ||
                    resourceDefinition.Type != GameplayAttributeType.Resource)
                    throw new InvalidOperationException(
                        $"NPCConfig '{name}' 的资源规则未指向初始 Resource Attribute：{rule.ResourceAttribute}。");
                if (rule.InitialValueMode == CharacterResourceInitialValueMode.FullCapacity &&
                    !rule.CapacityAttribute.IsValid)
                    throw new InvalidOperationException(
                        $"NPCConfig '{name}' 的资源 {rule.ResourceAttribute} 使用 FullCapacity 却没有容量 Attribute。");
                if (rule.CapacityAttribute.IsValid &&
                    (!definitionByAttributeIdMap.TryGetValue(
                         rule.CapacityAttribute.Id,
                         out GameplayAttributeDefinition capacityDefinition) ||
                     capacityDefinition.Type != GameplayAttributeType.Stat))
                    throw new InvalidOperationException(
                        $"NPCConfig '{name}' 的容量 Attribute 无效或不是 Stat：{rule.CapacityAttribute}。");
            }
        }

        #endregion

#if UNITY_EDITOR
        #region 烘焙结果数据源

        /// <summary>获取 NPC 等级成长结果窗口标题。</summary>
        public string BakedResultTitle => $"{name} - NPC 等级属性烘焙结果";

        /// <summary>获取执行 Bake 时会被修改的成长 Profile 资产。</summary>
        public IReadOnlyList<UnityEngine.Object> BakeTargets => growthProfile == null
            ? Array.Empty<UnityEngine.Object>()
            : new UnityEngine.Object[] { growthProfile };

        /// <summary>委托 NPCGrowthProfile 烘焙完整逐级属性结果。</summary>
        public void Bake()
        {
            if (growthProfile == null)
                throw new InvalidOperationException($"NPCConfig '{name}' 缺少 NPCGrowthProfile，无法 Bake。");
            Debug.Log($"[NPCConfig] 开始烘焙 '{name}' 的等级属性，sets={InitialAttributeSets.Count}。", this);
            growthProfile.Bake(InitialAttributeSets);
        }

        /// <summary>将最新保存的逐级 BaseValue 转换为“等级 × Attribute”结果表。</summary>
        /// <returns>包含全部等级与 Attribute 列的扁平结果表。</returns>
        public BakedResultTableData CreateBakedResultTableData()
        {
            if (growthProfile == null)
                throw new InvalidOperationException($"NPCConfig '{name}' 缺少 NPCGrowthProfile，无法创建烘焙结果表。");

            IReadOnlyList<BakedCharacterAttributeProgression> progressions =
                growthProfile.BakedAttributeProgressions;
            var headers = new List<string> { "等级" };
            for (int attributeIndex = 0; attributeIndex < progressions.Count; attributeIndex++)
            {
                GameplayAttribute attribute = progressions[attributeIndex].Attribute;
                string title = string.IsNullOrWhiteSpace(attribute.DisplayName)
                    ? attribute.Name
                    : attribute.DisplayName;
                headers.Add(string.IsNullOrWhiteSpace(title) ? attribute.ToString() : title);
            }

            // 旧快照的行数必须由已保存数据决定，不能因当前 MaxLevel 修改而伪造缺失等级。
            int rowCount = 0;
            for (int attributeIndex = 0; attributeIndex < progressions.Count; attributeIndex++)
            {
                BakedCharacterAttributeProgression progression = progressions[attributeIndex];
                if (progression?.BaseValues != null)
                    rowCount = Math.Max(rowCount, progression.BaseValues.Count);
            }

            var rows = new List<BakedResultRowData>(rowCount);
            for (int levelIndex = 0; levelIndex < rowCount; levelIndex++)
            {
                var cells = new List<string>(headers.Count)
                {
                    (levelIndex + 1).ToString("N0", CultureInfo.InvariantCulture)
                };
                for (int attributeIndex = 0; attributeIndex < progressions.Count; attributeIndex++)
                {
                    IReadOnlyList<float> values = progressions[attributeIndex].BaseValues;
                    cells.Add(values.Count > levelIndex
                        ? values[levelIndex].ToString("0.###", CultureInfo.InvariantCulture)
                        : "—");
                }
                rows.Add(new BakedResultRowData(cells));
            }

            string statusText;
            if (rows.Count == 0)
            {
                statusText = "尚未生成烘焙结果。";
            }
            else if (growthProfile.NeedsRebake(InitialAttributeSets))
            {
                statusText = $"等级属性烘焙结果已过期，请重新烘焙；当前显示上次保存的数据（{rows.Count} 个等级、{progressions.Count} 个 Attribute）。";
            }
            else
            {
                statusText = $"等级属性烘焙有效：{rows.Count} 个等级、{progressions.Count} 个 Attribute。";
            }

            return new BakedResultTableData(BakedResultTitle, headers, rows, statusText);
        }

        #endregion
#endif
    }
}
