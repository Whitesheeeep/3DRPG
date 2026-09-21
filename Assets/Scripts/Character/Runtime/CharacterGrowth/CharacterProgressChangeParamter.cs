using System;

namespace RPG.Character
{
    /// <summary>
    /// 描述一次提交到稳定角色实例的进度请求。
    /// </summary>
    public readonly struct CharacterProgressUpdate
    {
        /// <summary>
        /// 创建角色进度提交请求。
        /// </summary>
        /// <param name="level">目标等级。</param>
        /// <param name="currentExperience">目标等级内经验。</param>
        /// <param name="ascensionRank">目标突破阶数。</param>
        public CharacterProgressUpdate(int level, int currentExperience, int ascensionRank)
        {
            Level = level;
            CurrentExperience = currentExperience;
            AscensionRank = ascensionRank;
        }

        /// <summary>获取目标等级。</summary>
        public int Level { get; }

        /// <summary>获取目标等级内经验。</summary>
        public int CurrentExperience { get; }

        /// <summary>获取目标突破阶数。</summary>
        public int AscensionRank { get; }
    }

    /// <summary>
    /// 角色进度更新的结果状态。
    /// </summary>
    public enum CharacterProgressOperationStatus
    {
        /// <summary>进度已提交。</summary>
        Succeeded = 0,
        /// <summary>角色实例不存在。</summary>
        CharacterNotFound,
        /// <summary>等级超出角色配置范围。</summary>
        LevelOutOfRange,
        /// <summary>经验为负数。</summary>
        ExperienceOutOfRange,
        /// <summary>突破阶数超出角色配置范围。</summary>
        AscensionRankOutOfRange,
        /// <summary>等级超过当前突破阶数允许的等级上限。</summary>
        LevelExceedsAscensionCap,
        /// <summary>当前等级内经验超过该等级允许的经验。</summary>
        ExperienceExceedsCurrentLevel,
        /// <summary>获得顺序不符合实例状态契约。</summary>
        AcquisitionSequenceInvalid
    }

    /// <summary>
    /// 角色进度更新结果，携带成功后的实例或失败状态。
    /// </summary>
    public readonly struct CharacterProgressOperationResult
    {
        /// <summary>
        /// 创建角色进度更新结果。
        /// </summary>
        /// <param name="status">更新状态。</param>
        /// <param name="instance">成功后的角色实例；失败时为空。</param>
        public CharacterProgressOperationResult(
            CharacterProgressOperationStatus status,
            CharacterInstance instance)
        {
            Status = status;
            Instance = instance;
        }

        /// <summary>获取更新状态。</summary>
        public CharacterProgressOperationStatus Status { get; }

        /// <summary>获取成功后的角色实例。</summary>
        public CharacterInstance Instance { get; }

        /// <summary>判断本次更新是否成功。</summary>
        public bool Succeeded => Status == CharacterProgressOperationStatus.Succeeded;
    }

    /// <summary>
    /// 角色实例变化类型。
    /// </summary>
    public enum CharacterInstanceChangeType
    {
        /// <summary>新增角色实例。</summary>
        Added = 0,
        /// <summary>角色等级、经验或突破状态发生变化。</summary>
        ProgressUpdated,
        /// <summary>角色武器或圣遗物装备状态发生变化。</summary>
        EquipmentUpdated
    }

    /// <summary>
    /// 角色实例状态已经提交后的同步变化事件。
    /// </summary>
    public readonly struct CharacterInstanceChangedEvent
    {
        /// <summary>
        /// 创建角色实例变化事件。
        /// </summary>
        /// <param name="changeType">变化类型。</param>
        /// <param name="instance">变化后的完整实例。</param>
        public CharacterInstanceChangedEvent(
            CharacterInstanceChangeType changeType,
            CharacterInstance instance)
        {
            ChangeType = changeType;
            Instance = instance ?? throw new ArgumentNullException(nameof(instance));
        }

        /// <summary>获取变化类型。</summary>
        public CharacterInstanceChangeType ChangeType { get; }

        /// <summary>获取变化后的完整实例。</summary>
        public CharacterInstance Instance { get; }
    }
}
