using System.Collections.Generic;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>应用并精确持有 Passive Ability 产生的 Infinite GE Runtime。</summary>
    public sealed class PassiveGameplayAbilityRuntime : AsynchronousGameplayAbilityRuntime
    {
        #region 字段与属性
        private readonly List<GameEffectRuntime> appliedEffects = new();

        /// <summary>获取本次 Passive 成功应用且由本 Runtime 持有的 GE 句柄。</summary>
        public IReadOnlyList<GameEffectRuntime> AppliedEffects => appliedEffects;
        #endregion

        #region 构造
        /// <summary>创建 Passive Ability Runtime。</summary>
        /// <param name="activationId">Controller 分配的激活编号。</param>
        /// <param name="spec">创建本次运行的 Ability Spec。</param>
        /// <param name="source">释放能力的 ASC。</param>
        /// <param name="setByCaller">本次激活的动态参数快照。</param>
        /// <param name="data">Passive 作者数据。</param>
        internal PassiveGameplayAbilityRuntime(
            int activationId,
            GameplayAbilitySpec spec,
            AbilitySystemComponentBase source,
            IReadOnlyDictionary<GameplayTag, float> setByCaller,
            PassiveGameplayAbilityData data)
            : base(activationId, spec, source, setByCaller, data)
        {
        }
        #endregion

        #region 生命周期
        // 每个 GE 独立提交；失败项不阻止其他 Passive GE，也不产生回滚。
        protected override void OnStart()
        {
            PassiveGameplayAbilityData data = (PassiveGameplayAbilityData)Data;
            for (int i = 0; i < data.Effects.Count; i++)
            {
                GameplayEffectData effect = data.Effects[i];
                if (effect != null && Source.GameEffectCtrl.TryApply(
                    effect,
                    Source,
                    Level,
                    SetByCaller,
                    out GameEffectRuntime activeEffect) && activeEffect != null)
                    appliedEffects.Add(activeEffect);
            }

            base.OnStart();
        }

        // 先停止 Root Task，再移除本次精确保存的 GE，不影响同源其他 Ability 或外部效果。
        protected override void OnEnd()
        {
            base.OnEnd();
            RemoveAppliedEffects();
        }

        // 取消与正常结束使用同一组精确句柄清理，避免 Passive 留下 Infinite GE。
        protected override void OnCancel()
        {
            base.OnCancel();
            RemoveAppliedEffects();
        }
        #endregion

        #region 内部清理
        // 逆序移除本 Runtime 持有的句柄，并清空列表防止重复终止再次操作。
        private void RemoveAppliedEffects()
        {
            for (int i = appliedEffects.Count - 1; i >= 0; i--)
                Source.GameEffectCtrl.TryRemove(appliedEffects[i]);
            appliedEffects.Clear();
        }
        #endregion
    }
}