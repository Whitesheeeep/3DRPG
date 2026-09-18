using System;

namespace RPG.Game.Runtime.ArtifactDevelopment
{
    /// <summary>圣遗物升级提交的结果状态。</summary>
    public enum ArtifactDevelopmentOperationStatus
    {
        /// <summary>升级成功。</summary>
        Succeeded,
        /// <summary>目标实例不存在或类型不匹配。</summary>
        InvalidTarget,
        /// <summary>素材选择为空或包含非法数量。</summary>
        InvalidSelection,
        /// <summary>成长配置无效。</summary>
        InvalidConfiguration,
        /// <summary>经验素材库存不足。</summary>
        InsufficientMaterials,
        /// <summary>摩拉不足。</summary>
        InsufficientCurrency,
        /// <summary>库存或货币 Manager 拒绝提交。</summary>
        ManagerRejected
    }

    /// <summary>圣遗物升级提交的只读结果。</summary>
    public readonly struct ArtifactDevelopmentOperationResult
    {
        /// <summary>创建升级结果。</summary>
        /// <param name="status">结果状态。</param>
        /// <param name="message">供日志使用的诊断信息。</param>
        public ArtifactDevelopmentOperationResult(ArtifactDevelopmentOperationStatus status, string message)
        {
            Status = status;
            Message = message ?? string.Empty;
        }

        /// <summary>获取结果状态。</summary>
        public ArtifactDevelopmentOperationStatus Status { get; }

        /// <summary>获取诊断消息。</summary>
        public string Message { get; }

        /// <summary>判断操作是否成功。</summary>
        public bool Succeeded => Status == ArtifactDevelopmentOperationStatus.Succeeded;
    }

    /// <summary>圣遗物经验素材加入后的等级投影。</summary>
    public readonly struct ArtifactDevelopmentProjection
    {
        /// <summary>创建等级投影。</summary>
        /// <param name="level">预计等级。</param>
        /// <param name="currentExperience">预计等级内经验。</param>
        /// <param name="nextExperience">预计下一级经验。</param>
        /// <param name="progress">经验条归一化进度。</param>
        /// <param name="currencyCost">预计跨级货币消耗。</param>
        public ArtifactDevelopmentProjection(int level, int currentExperience, int nextExperience,
            float progress, long currencyCost)
        {
            Level = level;
            CurrentExperience = currentExperience;
            NextExperience = nextExperience;
            Progress = progress;
            CurrencyCost = currencyCost;
        }

        /// <summary>获取预计等级。</summary>
        public int Level { get; }
        /// <summary>获取预计等级内经验。</summary>
        public int CurrentExperience { get; }
        /// <summary>获取预计下一级经验。</summary>
        public int NextExperience { get; }
        /// <summary>获取经验条进度。</summary>
        public float Progress { get; }
        /// <summary>获取预计货币消耗。</summary>
        public long CurrencyCost { get; }
    }
}
