using System;
using RPG.ItemSystem;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.TaskSystemNS
{
    /// <summary>配置通过成功强化武器达到指定次数的任务目标。</summary>
    [Serializable]
    public sealed class TaskWeaponUpgradeObjectiveDefinition : TaskObjectiveDefinition
    {
        #region 配置字段

        [SerializeField, AssetsOnly, LabelText("限定武器")]
        private WeaponDefinition weaponDefinition;

        #endregion

        #region 构造

        /// <summary>创建供 Unity SerializeReference 反序列化的武器升级目标。</summary>
        public TaskWeaponUpgradeObjectiveDefinition()
        {
        }

        /// <summary>创建指定需求次数和可选武器限定的升级目标。</summary>
        /// <param name="objectiveId">所属阶段内唯一的目标标识。</param>
        /// <param name="required">需要成功升级的次数。</param>
        /// <param name="weaponDefinition">可选的限定武器定义；为空时接受任意武器。</param>
        public TaskWeaponUpgradeObjectiveDefinition(
            string objectiveId,
            int required = 1,
            WeaponDefinition weaponDefinition = null)
            : base(objectiveId, required)
        {
            this.weaponDefinition = weaponDefinition;
        }

        #endregion

        #region 属性与校验

        /// <summary>获取可选的限定武器定义。</summary>
        public WeaponDefinition WeaponDefinition => weaponDefinition;

        /// <summary>创建监听升级事实的独立阶段目标运行时。</summary>
        /// <param name="context">当前任务阶段受限的进度上下文。</param>
        /// <returns>只统计当前监听期间升级事实的 Runtime。</returns>
        /// <exception cref="ArgumentNullException">上下文为空时抛出。</exception>
        public override ITaskObjectiveRuntime CreateRuntime(ITaskObjectiveRuntimeContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            ItemId weaponItemId = weaponDefinition == null ? default : weaponDefinition.ItemId;
            return new TaskWeaponUpgradeObjectiveRuntime(weaponItemId, context);
        }

        /// <summary>校验基础目标字段及可选武器定义。</summary>
        public override void Validate()
        {
            base.Validate();
            weaponDefinition?.Validate();
        }

        #endregion
    }
}
