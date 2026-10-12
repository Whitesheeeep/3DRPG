using System;
using System.Collections.Generic;
using Animancer;
using RPG.Character;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.Generated;
using WS_Modules.GAS.GameplayAbilitySystem;

#if UNITY_EDITOR
using System.Globalization;
using WS_Modules.Baking;
#endif

namespace RPG.NPC
{
    /// <summary>保存 NPC 的初始 GAS 属性、等级成长、可授予技能与 Alive/Dead 动画表现。</summary>
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
        [SerializeField, AssetsOnly, LabelText("其他可用技能")]
        private GameplayAbilityData[] grantedAbilities = Array.Empty<GameplayAbilityData>();
        [SerializeField, Required, AssetsOnly, LabelText("Idle 动画")]
        private TransitionAsset idleTransition;
        [SerializeField, Required, AssetsOnly, LabelText("Move 动画")]
        private TransitionAsset moveTransition;
        [SerializeField, Required, AssetsOnly, LabelText("死亡动画")]
        private TransitionAsset deathTransition;
        [SerializeField, Required, AssetsOnly, LabelText("技能激活阻断规则")]
        private GameplayAbilityActivationRules activationRules;
        [SerializeField, Required, AssetsOnly, LabelText("Move 混合参数")]
        private StringAsset moveParameterX;
        [SerializeField, AssetsOnly, LabelText("Move Y 混合参数")]
        private StringAsset moveParameterY;

        // Behavior Designer 与 NavMesh 追击参数只在启用 AI 的 NPC 配置中生效。
        [SerializeField, LabelText("启用行为树 AI")]
        private bool useBehaviorTree;
        [SerializeField, AssetsOnly, LabelText("三段普通攻击（有序）")]
        private GameplayAbilityData[] normalAttackAbilities = Array.Empty<GameplayAbilityData>();
        [SerializeField, AssetsOnly, LabelText("瞬移 Ability")]
        private GameplayAbilityData teleportAbility;
        [SerializeField, MinValue(0.01f), LabelText("移动速度（仅 Walk）")]
        private float moveSpeed = 2.5f;
        [SerializeField, MinValue(0.01f), LabelText("瞬移后前方距离")]
        private float teleportForwardOffset = 2f;
        [SerializeField, MinValue(0.01f), LabelText("瞬移落点搜索半径")]
        private float teleportLandingRadius = 0.5f;
        [SerializeField, MinValue(0.01f), LabelText("路径更新间隔")]
        private float navigationRefreshInterval = 0.2f;
        [SerializeField, MinValue(0f), LabelText("普通攻击缓存时间")]
        private float normalAttackComboRetention = 3f;
        [SerializeField, LabelText("攻击视线阻挡 Layer")]
        private LayerMask attackObstructionMask = ~0;

        // 可选死亡表现出口只由正常死亡动画完成回调生成。
        [SerializeField, AssetsOnly, LabelText("死亡完成时生成")]
        private GameObject deathCompletionPrefab;

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

        /// <summary>获取 NPC 进入 Dead 时播放的非循环死亡动画。</summary>
        public TransitionAsset DeathTransition => deathTransition;

        /// <summary>获取 ASC 初始化使用的全局技能激活阻断规则。</summary>
        public GameplayAbilityActivationRules ActivationRules => activationRules;

        /// <summary>获取 Move Mixer 中用于选择步行采样的参数。</summary>
        public StringAsset MoveParameterX => moveParameterX;

        /// <summary>获取 Move Mixer 中用于选择左右步行采样的参数。</summary>
        public StringAsset MoveParameterY => moveParameterY;

        /// <summary>判断该 NPC 是否在初始化完成后启动外部行为树。</summary>
        public bool UseBehaviorTree => useBehaviorTree;

        /// <summary>获取按连段顺序执行的三段普通攻击数据。</summary>
        public IReadOnlyList<GameplayAbilityData> NormalAttackAbilities =>
            normalAttackAbilities ?? Array.Empty<GameplayAbilityData>();

        /// <summary>获取远距离追击时尝试激活的独立瞬移技能。</summary>
        public GameplayAbilityData TeleportAbility => teleportAbility;

        /// <summary>获取 Boss Walk 的 NavMeshAgent 速度。</summary>
        public float MoveSpeed => moveSpeed;

        /// <summary>获取理想瞬移落点相对玩家前向的距离。</summary>
        public float TeleportForwardOffset => teleportForwardOffset;

        /// <summary>获取瞬移落点在 NavMesh 上允许搜索的最大半径。</summary>
        public float TeleportLandingRadius => teleportLandingRadius;

        /// <summary>获取导航重算间隔。</summary>
        public float NavigationRefreshInterval => navigationRefreshInterval;

        /// <summary>获取连段下一段缓存持续时间。</summary>
        public float NormalAttackComboRetention => normalAttackComboRetention;

        /// <summary>获取攻击时视线检测使用的环境阻挡 LayerMask。</summary>
        public LayerMask AttackObstructionMask => attackObstructionMask;

        /// <summary>获取死亡动画完成后生成的可选返回入口 Prefab。</summary>
        public GameObject DeathCompletionPrefab => deathCompletionPrefab;

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

            if (grantedAbilities == null)
                throw new InvalidOperationException($"NPCConfig '{name}' 的 GrantedAbilities 为空引用。");
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
            if (deathTransition == null || deathTransition.Transition == null)
                throw new InvalidOperationException($"NPCConfig '{name}' 未配置有效 Death Transition。");
            if (activationRules == null)
                throw new InvalidOperationException($"NPCConfig '{name}' 未配置 GameplayAbilityActivationRules。");
            if (moveParameterX == null)
                throw new InvalidOperationException($"NPCConfig '{name}' 未配置 Move Mixer 参数。");

            growthProfile.Validate(InitialAttributeSets);
            ValidateResourceRules();
            ValidateBehaviorTreeConfiguration();
            if (growthProfile.NeedsRebake(InitialAttributeSets))
                throw new InvalidOperationException(
                    $"NPCConfig '{name}' 的等级属性烘焙结果缺失或已过期，请重新 Bake。");
        }

        /// <summary>校验启用 AI 的 Boss 必须具备三段普攻、瞬移与可用移动参数。</summary>
        private void ValidateBehaviorTreeConfiguration()
        {
            if (!useBehaviorTree)
            {
                if (grantedAbilities.Length == 0)
                    throw new InvalidOperationException($"NPCConfig '{name}' 必须至少授予一个 Ability。");
                return;
            }

            if (normalAttackAbilities == null || normalAttackAbilities.Length != 3)
                throw new InvalidOperationException(
                    $"NPCConfig '{name}' 的普通攻击必须按顺序配置 Attack1–3，当前数量={normalAttackAbilities?.Length ?? 0}。");
            for (int index = 0; index < normalAttackAbilities.Length; index++)
            {
                GameplayAbilityData ability = normalAttackAbilities[index];
                if (ability == null)
                    throw new InvalidOperationException($"NPCConfig '{name}' 的普通攻击[{index}] 未配置。");
                for (int priorIndex = 0; priorIndex < index; priorIndex++)
                    if (ReferenceEquals(ability, normalAttackAbilities[priorIndex]))
                        throw new InvalidOperationException($"NPCConfig '{name}' 重复配置普通攻击 '{ability.name}'。");
                bool isNormalAttack = false;
                for (int tagIndex = 0; tagIndex < ability.AbilityTags.Count; tagIndex++)
                    if (ability.AbilityTags[tagIndex].MatchesTag(GameplayTags.Tag_Skill_NormalAttack))
                    {
                        isNormalAttack = true;
                        break;
                    }
                if (!isNormalAttack)
                    throw new InvalidOperationException(
                        $"NPCConfig '{name}' 的 '{ability.name}' 缺少 Skill.NormalAttack 分类 Tag。");
            }

            if (teleportAbility == null)
                throw new InvalidOperationException($"NPCConfig '{name}' 启用了行为树但没有配置独立瞬移 Ability。");
            bool isTeleportAbility = false;
            for (int tagIndex = 0; tagIndex < teleportAbility.AbilityTags.Count; tagIndex++)
                if (teleportAbility.AbilityTags[tagIndex] == GameplayTags.Tag_Skill_Active_BossTeleport)
                {
                    isTeleportAbility = true;
                    break;
                }
            if (!isTeleportAbility)
                throw new InvalidOperationException(
                    $"NPCConfig '{name}' 的瞬移 Ability '{teleportAbility.name}' 缺少 Skill.BossTeleport Tag。");
            if (moveParameterY == null)
                throw new InvalidOperationException($"NPCConfig '{name}' 启用了二维移动但未配置 MoveParameterY。");
            if (!IsFinitePositive(moveSpeed) || !IsFinitePositive(teleportForwardOffset) ||
                !IsFinitePositive(teleportLandingRadius) || !IsFinitePositive(navigationRefreshInterval) ||
                !IsFiniteNonNegative(normalAttackComboRetention))
                throw new InvalidOperationException($"NPCConfig '{name}' 含有非法行为树或导航参数。");
        }

        /// <summary>判断配置数值是否为有限正数。</summary>
        /// <param name="value">待校验的距离或速度。</param>
        /// <returns>有限且大于零时返回 true。</returns>
        private static bool IsFinitePositive(float value) => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>判断配置数值是否为有限非负数。</summary>
        /// <param name="value">待校验的时间间隔。</param>
        /// <returns>有限且不小于零时返回 true。</returns>
        private static bool IsFiniteNonNegative(float value) => value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);

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
