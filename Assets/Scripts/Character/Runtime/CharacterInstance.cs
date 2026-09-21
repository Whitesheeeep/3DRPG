using System;
using System.Collections.Generic;
using RPG.ItemSystem;
using WS_Modules.GAS.AttributeSystem;

namespace RPG.Character
{
    /// <summary>保存玩家已经拥有的一名角色的稳定领域状态。</summary>
    /// <remarks>Config 提供静态定义，GAS 持有战斗中的实时 Stat，实例只镜像需要跨场景和存档保留的进度、装备与 Resource 快照。</remarks>
    public sealed class CharacterInstance
    {
        #region 状态字段

        // key：Resource Attribute；value：最后一次通过 ASC 校验后的 Resource CurrentValue 快照。
        private readonly Dictionary<GameplayAttribute, float> currentResourceValueByAttributeMap = new();
        private CharacterEquipmentState equipment;

        #endregion

        #region 生命周期

        /// <summary>创建一个拥有角色的稳定领域实例。</summary>
        /// <param name="config">角色静态配置。</param><param name="level">当前等级。</param>
        /// <param name="currentExperience">当前等级内经验。</param><param name="ascensionRank">当前突破阶数。</param>
        /// <param name="acquisitionSequence">获得顺序。</param><param name="equipment">初始装备状态。</param>
        internal CharacterInstance(CharacterConfig config, int level, int currentExperience, int ascensionRank,
            long acquisitionSequence, CharacterEquipmentState equipment = null)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            if (acquisitionSequence <= 0) throw new ArgumentOutOfRangeException(nameof(acquisitionSequence));
            Level = level;
            CurrentExperience = currentExperience;
            AscensionRank = ascensionRank;
            AcquisitionSequence = acquisitionSequence;
            this.equipment = equipment ?? new CharacterEquipmentState();
        }

        #endregion

        #region 状态属性

        /// <summary>获取运行时使用的静态角色配置。</summary>
        public CharacterConfig Config { get; }
        /// <summary>获取角色稳定标识；标识始终来自 Config。</summary>
        public CharacterId CharacterId => Config.CharacterId;
        /// <summary>获取当前等级。</summary>
        public int Level { get; private set; }
        /// <summary>获取当前等级内经验。</summary>
        public int CurrentExperience { get; private set; }
        /// <summary>获取当前突破阶数。</summary>
        public int AscensionRank { get; private set; }
        /// <summary>获取角色首次获得时分配的稳定顺序。</summary>
        public long AcquisitionSequence { get; }
        /// <summary>获取角色当前完整装备状态。</summary>
        public CharacterEquipmentState Equipment => equipment;
        /// <summary>获取已装备武器实例。</summary>
        public EquipmentInstanceId EquippedWeaponInstanceId => equipment.EquippedWeaponInstanceId;

        /// <summary>按圣遗物部位读取角色已装备实例。</summary>
        /// <param name="slot">圣遗物部位。</param><param name="instanceId">找到的圣遗物实例。</param>
        /// <returns>槽位有装备时返回 true。</returns>
        public bool TryGetEquippedArtifactInstanceId(ArtifactSlot slot, out EquipmentInstanceId instanceId) =>
            equipment.TryGetArtifactInstanceId(slot, out instanceId);

        /// <summary>尝试读取角色保存的 Resource CurrentValue 快照。</summary>
        /// <param name="attribute">Resource Attribute。</param><param name="currentValue">保存的当前值。</param>
        /// <returns>存在快照时返回 true。</returns>
        public bool TryGetResourceCurrentValue(GameplayAttribute attribute, out float currentValue) =>
            currentResourceValueByAttributeMap.TryGetValue(attribute, out currentValue);

        /// <summary>获取按 AttributeId 排序的 Resource 快照副本。</summary>
        /// <returns>不暴露内部字典的资源快照列表。</returns>
        public IReadOnlyList<CharacterResourceValue> GetResourceCurrentValues()
        {
            var attributes = new List<GameplayAttribute>(currentResourceValueByAttributeMap.Keys);
            attributes.Sort((left, right) => left.Id.CompareTo(right.Id));
            var values = new List<CharacterResourceValue>(attributes.Count);
            for (int index = 0; index < attributes.Count; index++)
            {
                GameplayAttribute attribute = attributes[index];
                values.Add(new CharacterResourceValue(attribute, currentResourceValueByAttributeMap[attribute]));
            }
            return values;
        }

        #endregion

        #region 受控提交

        /// <summary>提交已由 Roster 校验的进度到同一个稳定实例。</summary>
        /// <param name="update">已经验证的目标进度。</param>
        internal void CommitProgress(CharacterProgressUpdate update)
        {
            Level = update.Level;
            CurrentExperience = update.CurrentExperience;
            AscensionRank = update.AscensionRank;
        }

        /// <summary>提交已由装备系统校验的新装备状态。</summary>
        /// <param name="nextEquipment">已经验证的完整装备状态。</param>
        internal void CommitEquipment(CharacterEquipmentState nextEquipment)
        {
            equipment = nextEquipment ?? throw new ArgumentNullException(nameof(nextEquipment));
        }

        /// <summary>写入一次已经经过 ASC Pre/Post 与 Clamp 的资源快照。</summary>
        /// <param name="attribute">Resource Attribute。</param><param name="currentValue">资源当前值。</param>
        internal void CommitResourceCurrentValue(GameplayAttribute attribute, float currentValue)
        {
            if (!attribute.IsValid) throw new ArgumentException("资源 Attribute 无效。", nameof(attribute));
            if (float.IsNaN(currentValue) || float.IsInfinity(currentValue))
                throw new ArgumentOutOfRangeException(nameof(currentValue));
            if (currentResourceValueByAttributeMap.TryGetValue(attribute, out float previous) &&
                Math.Abs(previous - currentValue) <= float.Epsilon)
                return;
            currentResourceValueByAttributeMap[attribute] = currentValue;
        }

        /// <summary>整体恢复已经校验的 Resource 快照，不发布高频资源变化事件。</summary>
        /// <param name="resourceValues">恢复后的资源值。</param>
        internal void RestoreResourceCurrentValues(IReadOnlyList<CharacterResourceValue> resourceValues)
        {
            currentResourceValueByAttributeMap.Clear();
            if (resourceValues == null) return;
            for (int index = 0; index < resourceValues.Count; index++)
                CommitResourceCurrentValue(resourceValues[index].Attribute, resourceValues[index].CurrentValue);
        }

        #endregion
    }
}
