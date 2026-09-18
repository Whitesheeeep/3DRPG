using System;

namespace RPG.Game.Runtime.WeaponDevelopment
{
    /// <summary>武器培养业务操作的结果状态。</summary>
    public enum WeaponDevelopmentOperationStatus
    {
        /// <summary>操作成功。</summary>
        Succeeded = 0,
        /// <summary>目标武器不存在或目标定义不是武器。</summary>
        InvalidTarget,
        /// <summary>培养配置不完整或成本定义不合法。</summary>
        InvalidConfiguration,
        /// <summary>当前选择不满足该操作的要求。</summary>
        InvalidSelection,
        /// <summary>背包中的培养素材数量不足。</summary>
        InsufficientMaterials,
        /// <summary>货币余额不足。</summary>
        InsufficientCurrency,
        /// <summary>底层库存或货币 Manager 拒绝提交。</summary>
        ManagerRejected
    }

    /// <summary>武器培养业务操作的不可变结果。</summary>
    public readonly struct WeaponDevelopmentOperationResult
    {
        /// <summary>创建培养操作结果。</summary>
        /// <param name="status">操作状态。</param>
        /// <param name="message">供日志使用的原因说明。</param>
        public WeaponDevelopmentOperationResult(WeaponDevelopmentOperationStatus status, string message)
        {
            Status = status;
            Message = message ?? string.Empty;
        }

        /// <summary>获取操作状态。</summary>
        public WeaponDevelopmentOperationStatus Status { get; }

        /// <summary>获取结果说明。</summary>
        public string Message { get; }

        /// <summary>判断操作是否成功。</summary>
        public bool Succeeded => Status == WeaponDevelopmentOperationStatus.Succeeded;

        /// <summary>创建成功结果。</summary>
        /// <returns>成功结果。</returns>
        public static WeaponDevelopmentOperationResult Success() =>
            new WeaponDevelopmentOperationResult(WeaponDevelopmentOperationStatus.Succeeded, string.Empty);

        /// <summary>创建失败结果。</summary>
        /// <param name="status">失败状态。</param>
        /// <param name="message">失败原因。</param>
        /// <returns>失败结果。</returns>
        public static WeaponDevelopmentOperationResult Failure(
            WeaponDevelopmentOperationStatus status, string message) =>
            new WeaponDevelopmentOperationResult(status, message);
    }
}
