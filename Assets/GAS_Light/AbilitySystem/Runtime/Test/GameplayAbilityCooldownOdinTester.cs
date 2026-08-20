#if UNITY_EDITOR
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.GameplayCue;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>通过 Odin Inspector 验证 Cooldown Tag 事件与 Ability 生命周期型 Infinite GE 清理。</summary>
    public sealed class GameplayAbilityCooldownOdinTester : MonoBehaviour
    {
        #region 测试输入
        [Title("Cooldown 测试依赖")]
        [SerializeField, AssetsOnly, Required]
        private GameplayAttributeSet attributeSet;
        [SerializeField, AssetsOnly, Required]
        private GameplayTagDatabase tagDatabase;
        [SerializeField, AssetsOnly, Required]
        private GameplayAbilityDatabase abilityDatabase;
        [SerializeField, AssetsOnly, Required]
        private GameplayCueDatabase cueDatabase;
        [SerializeField, AssetsOnly, Required]
        private PassiveGameplayAbilityData passiveAbility;
        [SerializeField, AssetsOnly]
        private ToggleGameplayAbilityData toggleAbility;
        #endregion

        #region 测试状态
        private GameObject testObject;
        private GameplayAbilitySystemComponent source;
        private readonly List<string> eventLog = new();
        #endregion

        #region 测试入口
        /// <summary>验证 Cooldown Tag 阻止重复激活、到期通知和 Infinite GE 所有权清理。</summary>
        [Button("测试 Cooldown Tag 与 Owned Infinite GE", ButtonSizes.Large)]
        public void RunCooldownTest()
        {
            Cleanup();
            bool configured = attributeSet != null && tagDatabase != null &&
                              abilityDatabase != null && cueDatabase != null && passiveAbility != null;
            if (!configured)
            {
                Debug.LogError("[CooldownTest] 缺少 AttributeSet、TagDatabase、AbilityDatabase、CueDatabase 或 Passive Ability。", this);
                return;
            }

            GameplayTagManager.Instance.Initialize(tagDatabase);
            GameplayAbilityManager.Instance.Initialize(abilityDatabase);
            GameplayCueManager.Instance.Initialize(cueDatabase);
            testObject = new GameObject("Gameplay Ability Cooldown Test");
            testObject.hideFlags = HideFlags.HideAndDontSave;
            testObject.AddComponent<GameplayAbilitySystemTestOwner>();
            source = testObject.AddComponent<GameplayAbilitySystemComponent>();
            source.Initialize(new[] { attributeSet });
            if (!source.IsInitialized)
            {
                Debug.LogError("[CooldownTest] ASC AttributeSet 初始化失败。", this);
                Cleanup();
                return;
            }

            Subscribe();
            GameplayAbilityHandle handle = source.GiveAbility(passiveAbility, 1);
            bool activated = source.TryActivateAbility(handle, out GameplayAbilityRuntime runtime);
            Debug.Log($"[CooldownTest] Activate={activated}, Ability='{passiveAbility.name}', AbilityId={passiveAbility.AbilityId}, " +
                      $"CooldownTags={FormatTags(passiveAbility.CooldownEffect.GrantedTags)}, OwnedEffects={runtime?.OwnedEffects.Count ?? 0}", this);
            if (!activated || runtime == null)
            {
                Debug.LogError("[CooldownTest] Passive 激活失败。", this);
                Cleanup();
                return;
            }

            bool duplicateRejected = !source.TryActivateAbility(handle, out _);
            Debug.Log($"[CooldownTest] Cooldown/重复激活拒绝={duplicateRejected}, StartedCount={CountEvents("Started")}", this);
            source.Tick(passiveAbility.CooldownEffect.Duration);
            Debug.Log($"[CooldownTest] Cooldown Tick 完成, EndedCount={CountEvents("Ended")}, ActiveEffects={source.ActiveEffects.Count}", this);

            bool ended = source.TryEndAbility(runtime);
            Debug.Log($"[CooldownTest] Passive Ended={ended}, OwnedEffectsAfterEnd={runtime.OwnedEffects.Count}, ActiveEffects={source.ActiveEffects.Count}", this);
            if (toggleAbility != null) RunToggleCleanupCheck();
            Cleanup();
        }
        #endregion

        #region 事件观察
        /// <summary>订阅 Ability 和 Cooldown 事件并记录最小日志序列。</summary>
        private void Subscribe()
        {
            source.Abilities.CooldownStarted += OnCooldownStarted;
            source.Abilities.CooldownEnded += OnCooldownEnded;
            source.Abilities.AbilityActivated += OnAbilityActivated;
            source.Abilities.AbilityEnded += OnAbilityEnded;
        }

        /// <summary>解除测试事件订阅，防止临时 ASC 生命周期结束后保留委托。</summary>
        private void Unsubscribe()
        {
            if (source == null) return;
            source.Abilities.CooldownStarted -= OnCooldownStarted;
            source.Abilities.CooldownEnded -= OnCooldownEnded;
            source.Abilities.AbilityActivated -= OnAbilityActivated;
            source.Abilities.AbilityEnded -= OnAbilityEnded;
        }

        /// <summary>记录 Cooldown 开始事件，并输出技能身份和剩余时间。</summary>
        private void OnCooldownStarted(GameplayAbilityCooldownEventArgs args)
        {
            eventLog.Add("Started");
            Debug.Log($"[CooldownTest][Started] Ability='{args.AbilityData.name}', AbilityId={args.AbilityData.AbilityId}, " +
                      $"Handle={args.Handle}, Duration={args.Duration}, Remaining={args.RemainingDuration}", this);
        }

        /// <summary>记录 Cooldown 结束事件，并输出关联技能身份。</summary>
        private void OnCooldownEnded(GameplayAbilityCooldownEventArgs args)
        {
            eventLog.Add("Ended");
            Debug.Log($"[CooldownTest][Ended] Ability='{args.AbilityData.name}', AbilityId={args.AbilityData.AbilityId}, " +
                      $"Remaining={args.RemainingDuration}", this);
        }

        /// <summary>记录 Ability Activated 事件顺序。</summary>
        private void OnAbilityActivated(GameplayAbilityRuntime runtime) => eventLog.Add("Activated");

        /// <summary>记录 Ability Ended 事件顺序。</summary>
        private void OnAbilityEnded(GameplayAbilityRuntime runtime) => eventLog.Add("AbilityEnded");
        #endregion

        #region Toggle 验证
        /// <summary>验证 Toggle End 时清理 Runtime 持有的 Infinite GE。</summary>
        private void RunToggleCleanupCheck()
        {
            GameplayAbilityHandle handle = source.GiveAbility(toggleAbility, 1);
            if (!source.TryActivateAbility(handle, out GameplayAbilityRuntime runtime))
            {
                Debug.LogError("[CooldownTest] Toggle 激活失败。", this);
                return;
            }

            int ownedBefore = runtime.OwnedEffects.Count;
            bool ended = source.TryActivateAbility(handle, out GameplayAbilityRuntime toggledRuntime);
            Debug.Log($"[CooldownTest] Toggle Off={ended}, SameRuntime={ReferenceEquals(runtime, toggledRuntime)}, " +
                      $"OwnedBefore={ownedBefore}, OwnedAfter={runtime.OwnedEffects.Count}, State={runtime.State}", this);
        }
        #endregion

        #region 清理与辅助
        /// <summary>清理事件、临时 ASC 和全局测试 Manager。</summary>
        private void Cleanup()
        {
            Unsubscribe();
            source = null;
            if (testObject != null)
                DestroyImmediate(testObject);
            testObject = null;
            GameplayCueManager.Instance.Reset();
            GameplayAbilityManager.Instance.Reset();
            GameplayTagManager.Instance.Reset();
            eventLog.Clear();
        }

        /// <summary>统计测试事件中指定名称出现的次数。</summary>
        /// <param name="eventName">需要统计的事件名称。</param>
        /// <returns>事件出现次数。</returns>
        private int CountEvents(string eventName)
        {
            int count = 0;
            for (int i = 0; i < eventLog.Count; i++)
                if (eventLog[i] == eventName) count++;
            return count;
        }

        /// <summary>格式化 Cooldown Tag 的稳定 ID，便于无需依赖 Tag 名称的日志检查。</summary>
        /// <param name="tags">待格式化的 Tag 列表。</param>
        /// <returns>稳定 ID 列表文本。</returns>
        private static string FormatTags(IReadOnlyList<GameplayTag> tags)
        {
            if (tags == null || tags.Count == 0) return "[]";
            var values = new string[tags.Count];
            for (int i = 0; i < tags.Count; i++) values[i] = tags[i].Id.ToString();
            return $"[{string.Join(", ", values)}]";
        }
        #endregion
    }
}
#endif
