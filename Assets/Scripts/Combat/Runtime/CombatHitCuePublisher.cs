using System.Collections.Generic;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.GameplayCue;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;

namespace RPG.Combat
{
    /// <summary>把一次命中快照映射到现有通用 GameplayCueRequest。</summary>
    public static class CombatHitCuePublisher
    {
        #region 公开操作

        /// <summary>合并 GA 与 Clip CueTags，并为本次 GE 结算发布一次去重后的命中 Cue。</summary>
        /// <param name="abilityCueTags">GA 配置的 CueTag。</param>
        /// <param name="clipCueTags">当前攻击检测片段配置的 CueTag。</param>
        /// <param name="snapshot">在伤害应用前构造的命中快照。</param>
        /// <param name="abilityRuntime">产生本次命中的 GA Runtime。</param>
        /// <param name="applicationResult">对应 GE 的应用结果；无成功 GE 时为空。</param>
        public static void Publish(
            IReadOnlyList<GameplayTag> abilityCueTags,
            IReadOnlyList<GameplayTag> clipCueTags,
            CombatHitSnapshot snapshot,
            GameplayAbilityRuntime abilityRuntime,
            GameplayEffectApplicationResult applicationResult)
        {
            var publishedCueTagSet = new HashSet<GameplayTag>();
            PublishTags(abilityCueTags, snapshot, abilityRuntime, applicationResult, publishedCueTagSet);
            PublishTags(clipCueTags, snapshot, abilityRuntime, applicationResult, publishedCueTagSet);
        }

        /// <summary>发布投射物配置中的命中 CueTag。</summary>
        /// <param name="cueTags">投射物运行时配置的 CueTag。</param>
        /// <param name="snapshot">投射物命中前构造的快照。</param>
        /// <param name="abilityRuntime">产生投射物的 GA Runtime。</param>
        /// <param name="applicationResult">当前 GE 结算结果；无成功 GE 时为空。</param>
        public static void Publish(
            IReadOnlyList<GameplayTag> cueTags,
            CombatHitSnapshot snapshot,
            GameplayAbilityRuntime abilityRuntime,
            GameplayEffectApplicationResult applicationResult)
        {
            PublishTags(cueTags, snapshot, abilityRuntime, applicationResult, new HashSet<GameplayTag>());
        }

        #endregion

        #region Cue 构造

        /// <summary>发布单个来源中尚未在本次 GE 命中中发布的 Tag。</summary>
        /// <param name="cueTags">当前来源配置的 CueTags。</param>
        /// <param name="snapshot">本次命中的稳定空间与 ASC 快照。</param>
        /// <param name="abilityRuntime">产生命中的 Ability Runtime。</param>
        /// <param name="applicationResult">本次成功 GE 的原始结果。</param>
        /// <param name="publishedCueTagSet">GA 与 Clip 共享的精确去重集合。</param>
        private static void PublishTags(
            IReadOnlyList<GameplayTag> cueTags,
            CombatHitSnapshot snapshot,
            GameplayAbilityRuntime abilityRuntime,
            GameplayEffectApplicationResult applicationResult,
            HashSet<GameplayTag> publishedCueTagSet)
        {
            for (int index = 0; index < cueTags.Count; index++)
            {
                GameplayTag cueTag = cueTags[index];
                if (!publishedCueTagSet.Add(cueTag)) continue;

                // Cue 请求只使用 GAS 已有的通用字段，不让 Cue 层依赖 Gameplay 快照类型。
                snapshot.Target.PublishGameplayCue(new GameplayCueRequest(
                    cueTag,
                    GameplayCueEventType.Execute,
                    snapshot.Source,
                    snapshot.Target,
                    effectRuntime: null,
                    abilityRuntime: abilityRuntime,
                    position: snapshot.HitPoint,
                    rotation: snapshot.CueRotation,
                    attachTransform: null,
                    effectSpec: applicationResult?.Spec,
                    applicationResult: applicationResult));
            }
        }

        #endregion
    }
}
