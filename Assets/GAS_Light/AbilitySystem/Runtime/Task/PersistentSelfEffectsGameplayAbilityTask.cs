using System.Collections.Generic;
using WS_Modules.GAS.GameplayCue;
using WS_Modules.GAS.GameplayEffect;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>持有本次 Ability 对 Source 应用的持续 GE，并在停止或取消时精确清理。</summary>
    public class PersistentSelfEffectsGameplayAbilityTask : GameplayAbilityTask
    {
        #region 字段与属性

        private readonly List<GameEffectRuntime> appliedEffects = new();
        private bool cuesActive;

        /// <summary>获取本次 Task 成功持有的 Active GE Runtime。</summary>
        public IReadOnlyList<GameEffectRuntime> AppliedEffects => appliedEffects;

        #endregion

        #region 构造

        /// <summary>创建尚未应用持续效果的 Task。</summary>
        /// <param name="runtime">承载该 Task 的异步 Ability Runtime。</param>
        public PersistentSelfEffectsGameplayAbilityTask(AsynchronousGameplayAbilityRuntime runtime)
            : base(runtime)
        {
        }

        #endregion

        #region 生命周期

        /// <summary>应用 Source 持续效果并保持 Running，等待 Runtime End 或 Cancel。</summary>
        protected override void OnStart()
        {
            GameplayAbilityData data = Runtime.Data;
            data.ApplyConfiguredEffects(
                Runtime.Source,
                Runtime.Source,
                Runtime.Level,
                Runtime.SetByCaller,
                appliedEffects);
            data.PublishConfiguredCues(
                GameplayCueEventType.Active,
                Runtime.Source,
                Runtime.Source,
                abilityRuntime: Runtime);
            cuesActive = true;
        }

        /// <summary>正常结束时移除本次持续效果与 Active Cue。</summary>
        protected override void OnStop() => RemoveOwnedState();

        /// <summary>取消或 ASC Clear 时移除本次持续效果与 Active Cue。</summary>
        protected override void OnCancel() => RemoveOwnedState();

        #endregion

        #region 清理

        /// <summary>只清理当前 Task 保存的精确 GE 句柄，并保证 Remove Cue 只发布一次。</summary>
        private void RemoveOwnedState()
        {
            for (int i = appliedEffects.Count - 1; i >= 0; i--)
                Runtime.Source.GameEffectCtrl.TryRemove(appliedEffects[i]);
            appliedEffects.Clear();

            if (!cuesActive) return;
            Runtime.Data.PublishConfiguredCues(
                GameplayCueEventType.Remove,
                Runtime.Source,
                Runtime.Source,
                abilityRuntime: Runtime);
            cuesActive = false;
        }

        #endregion
    }
}
