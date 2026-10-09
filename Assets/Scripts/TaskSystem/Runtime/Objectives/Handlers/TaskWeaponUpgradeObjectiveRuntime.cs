using System;
using RPG.Game.Runtime.WeaponDevelopment;
using RPG.ItemSystem;
using UnityEngine;
using WS_Modules.CustomEventSystem;

namespace RPG.TaskSystemNS
{
    /// <summary>在所属阶段监听成功武器升级事实并按可选武器定义筛选。</summary>
    public sealed class TaskWeaponUpgradeObjectiveRuntime : ITaskObjectiveRuntime
    {
        #region 依赖字段

        // 依赖字段：无效 ItemId 表示不限定武器；上下文将进度限制在当前任务目标。
        private readonly ItemId weaponDefinitionItemId;
        private readonly ITaskObjectiveRuntimeContext context;

        #endregion

        #region 监听状态

        private IUnRegister weaponUpgradedEventUnregister;

        #endregion

        #region 构造与监听生命周期

        /// <summary>创建尚未监听升级事件的阶段目标运行时。</summary>
        /// <param name="weaponDefinitionItemId">限定武器 ItemId；无效值表示任意武器。</param>
        /// <param name="context">所属任务阶段受限的进度上下文。</param>
        /// <exception cref="ArgumentNullException">进度上下文为空时抛出。</exception>
        public TaskWeaponUpgradeObjectiveRuntime(
            ItemId weaponDefinitionItemId,
            ITaskObjectiveRuntimeContext context)
        {
            this.weaponDefinitionItemId = weaponDefinitionItemId;
            this.context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>阶段激活时订阅已提交的武器升级事实。</summary>
        public void StartListening()
        {
            if (weaponUpgradedEventUnregister != null)
                return;

            weaponUpgradedEventUnregister = EventSystem.Register_Type<WeaponUpgradedEventArgs>(
                typeof(WeaponUpgradedEventArgs),
                OnWeaponUpgraded);
            Debug.Log(
                $"[TaskWeaponUpgradeObjectiveRuntime] 开始监听武器升级，taskId={context.TaskId}, " +
                $"objectiveId={context.ObjectiveId}, weaponItemId={weaponDefinitionItemId}。");
        }

        /// <summary>阶段停止或目标完成时注销武器升级监听。</summary>
        public void StopListening()
        {
            if (weaponUpgradedEventUnregister == null)
                return;

            weaponUpgradedEventUnregister.UnRegister();
            weaponUpgradedEventUnregister = null;
            Debug.Log(
                $"[TaskWeaponUpgradeObjectiveRuntime] 停止监听武器升级，taskId={context.TaskId}, " +
                $"objectiveId={context.ObjectiveId}。");
        }

        #endregion

        #region 事件处理

        /// <summary>仅累计等级真实提高且符合可选定义限制的强化提交。</summary>
        /// <param name="eventArgs">武器培养服务在库存提交成功后发布的事实。</param>
        private void OnWeaponUpgraded(WeaponUpgradedEventArgs eventArgs)
        {
            if (eventArgs.NewLevel <= eventArgs.PreviousLevel ||
                (weaponDefinitionItemId.IsValid && eventArgs.DefinitionItemId != weaponDefinitionItemId))
                return;

            context.AddProgress(1);
            Debug.Log(
                $"[TaskWeaponUpgradeObjectiveRuntime] 武器升级目标进度增加，taskId={context.TaskId}, " +
                $"objectiveId={context.ObjectiveId}, instanceId={eventArgs.InstanceId}, " +
                $"level={eventArgs.PreviousLevel}->{eventArgs.NewLevel}。");
        }

        #endregion
    }
}
