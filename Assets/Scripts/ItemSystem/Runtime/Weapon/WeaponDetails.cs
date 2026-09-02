using System.Collections.Generic;
using RPG.Character;
using WS_Modules.GAS.GameplayEffect;

namespace RPG.ItemSystem
{
    /// <summary>武器详情中单个 GE 的有序属性贡献。</summary>
    public readonly struct WeaponEffectContribution
    {
        /// <summary>创建武器效果贡献。</summary>
        /// <param name="effectIndex">效果列表索引。</param>
        /// <param name="modifierIndex">Modifier 索引。</param>
        /// <param name="result">静态 Modifier 查询结果。</param>
        public WeaponEffectContribution(int effectIndex, int modifierIndex, GameplayEffectStaticModifierResult result)
        {
            EffectIndex = effectIndex;
            ModifierIndex = modifierIndex;
            Result = result;
        }

        /// <summary>效果列表索引。</summary>
        public int EffectIndex { get; }
        /// <summary>GE 内 Modifier 索引。</summary>
        public int ModifierIndex { get; }
        /// <summary>静态查询结果。</summary>
        public GameplayEffectStaticModifierResult Result { get; }
    }

    /// <summary>一个武器 GE 的静态查询状态和有序贡献集合。</summary>
    public sealed class WeaponEffectEvaluation
    {
        /// <summary>创建 GE 静态查询结果。</summary>
        /// <param name="effectIndex">效果列表索引。</param>
        /// <param name="status">静态查询状态。</param>
        /// <param name="contributions">已计算的贡献列表。</param>
        public WeaponEffectEvaluation(int effectIndex, GameplayEffectStaticEvaluationStatus status,
            IReadOnlyList<WeaponEffectContribution> contributions)
        {
            EffectIndex = effectIndex;
            Status = status;
            Contributions = contributions;
        }

        /// <summary>效果列表索引。</summary>
        public int EffectIndex { get; }
        /// <summary>静态查询状态。</summary>
        public GameplayEffectStaticEvaluationStatus Status { get; }
        /// <summary>已成功计算的有序贡献；动态项不会伪造数值。</summary>
        public IReadOnlyList<WeaponEffectContribution> Contributions { get; }
    }

    /// <summary>面向背包和详情页面的只读武器数据快照。</summary>
    public sealed class WeaponDetails
    {
        /// <summary>创建武器详情快照。</summary>
        /// <param name="definition">武器静态定义。</param>
        /// <param name="instance">武器运行时实例。</param>
        /// <param name="levelEffects">等级效果查询结果。</param>
        /// <param name="refinementEffects">精炼效果查询结果。</param>
        public WeaponDetails(WeaponDefinition definition, WeaponInstance instance,
            IReadOnlyList<WeaponEffectEvaluation> levelEffects,
            IReadOnlyList<WeaponEffectEvaluation> refinementEffects)
        {
            Definition = definition;
            Instance = instance;
            LevelEffects = levelEffects;
            RefinementEffects = refinementEffects;
        }

        /// <summary>武器静态定义。</summary>
        public WeaponDefinition Definition { get; }
        /// <summary>武器运行时实例。</summary>
        public WeaponInstance Instance { get; }
        /// <summary>武器实例标识。</summary>
        public EquipmentInstanceId InstanceId => Instance.InstanceId;
        /// <summary>武器 Definition 标识。</summary>
        public ItemId DefinitionId => Definition.ItemId;
        /// <summary>武器显示名称。</summary>
        public string DisplayName => Definition.DisplayName;
        /// <summary>武器描述。</summary>
        public string Description => Definition.Description;
        /// <summary>武器类型。</summary>
        public WeaponType WeaponType => Definition.WeaponType;
        /// <summary>武器稀有度。</summary>
        public ItemRarity Rarity => Definition.Rarity;
        /// <summary>武器图标图集地址。</summary>
        public string IconAddress => Definition.IconAddress;
        /// <summary>武器图标在图集中的 Sprite 名称。</summary>
        public string IconSpriteName => Definition.IconSpriteName;
        /// <summary>当前武器等级。</summary>
        public int Level => Instance.Level;
        /// <summary>当前等级内经验。</summary>
        public int CurrentExperience => Instance.CurrentExperience;
        /// <summary>当前突破阶数。</summary>
        public int AscensionRank => Instance.AscensionRank;
        /// <summary>当前精炼阶数。</summary>
        public int RefinementRank => Instance.RefinementRank;
        /// <summary>当前锁定状态。</summary>
        public bool IsLocked => Instance.IsLocked;
        /// <summary>当前新获得状态。</summary>
        public bool IsNew => Instance.IsNew;
        /// <summary>武器等级效果的有序贡献。</summary>
        public IReadOnlyList<WeaponEffectEvaluation> LevelEffects { get; }
        /// <summary>武器精炼效果的有序贡献。</summary>
        public IReadOnlyList<WeaponEffectEvaluation> RefinementEffects { get; }
        /// <summary>装备者角色标识。</summary>
        public CharacterId EquippedCharacterId => Instance.EquippedCharacterId;
        /// <summary>判断武器是否已装备。</summary>
        public bool IsEquipped => Instance.IsEquipped;
    }
}
