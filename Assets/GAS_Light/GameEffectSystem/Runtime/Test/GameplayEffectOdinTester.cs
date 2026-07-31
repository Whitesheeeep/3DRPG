#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>通过 Odin Inspector 按钮手动验证 GE 配置、运行时、计时和 Attribute 提交链路。</summary>
    public sealed class GameplayEffectOdinTester : MonoBehaviour
    {
        #region 嵌套类型

        /// <summary>保存 Inspector 可编辑的单项 SetByCaller Tag 与数值。</summary>
        [Serializable]
        public struct SetByCallerEntry
        {
            [SerializeField] private GameplayTag key;
            [SerializeField] private float value;

            /// <summary>获取稳定 Tag Key。</summary>
            public GameplayTag Key => key;
            /// <summary>获取调用方 Magnitude。</summary>
            public float Value => value;
        }

        /// <summary>为测试提供可实例化的最小 ASC，不复制任何生产逻辑。</summary>
        private sealed class TestAbilitySystemComponent : AbilitySystemComponentBase
        {
        }

        #endregion

        #region 测试输入与状态

        [Title("GE 配置")]
        [SerializeField] private GameplayEffectData effect;
        [SerializeField, Min(1)] private int level = 1;
        [SerializeField] private float tickDelta = 1f;
        [SerializeField] private List<SetByCallerEntry> setByCaller = new();

        [Title("Attribute Set")]
        [SerializeField] private List<GameplayAttributeSet> sourceSets = new();
        [SerializeField] private List<GameplayAttributeSet> targetSets = new();

        private TestAbilitySystemComponent source;
        private TestAbilitySystemComponent target;
        private GameEffectRuntime lastRuntime;

        #endregion

        #region Odin 测试按钮

        /// <summary>重新创建 Source/Target ASC，并导入 Inspector 配置的 AttributeSet。</summary>
        [Button("初始化 GE 测试")]
        public void InitializeTest()
        {
            source = new TestAbilitySystemComponent();
            target = new TestAbilitySystemComponent();
            bool sourceReady = source.Attributes.TryInitialize(sourceSets, out string sourceError);
            bool targetReady = target.Attributes.TryInitialize(targetSets, out string targetError);
            lastRuntime = null;
            Debug.Log($"[GETest][Initialize] Source={sourceReady} ({sourceError}), " +
                      $"Target={targetReady} ({targetError})");
        }

        /// <summary>使用真实 Controller API 应用一次当前 GE，并输出 Active 状态。</summary>
        [Button("应用一次 GE")]
        public void ApplyEffect()
        {
            if (!EnsureInitialized()) return;
            Dictionary<GameplayTag, float> values = BuildSetByCaller();
            bool success = target.GameEffectCtrl.TryApply(
                effect,
                source,
                level,
                values,
                out GameEffectRuntime runtime);
            if (runtime != null) lastRuntime = runtime;
            Debug.Log($"[GETest][Apply] Success={success}, Active={target.GameEffectCtrl.ActiveEffects.Count}, " +
                      $"Stack={lastRuntime?.StackCount ?? 0}");
            LogTargetAttributes();
        }

        /// <summary>推进一次配置的 deltaTime，验证 Period 与 Duration 到期。</summary>
        [Button("推进 GE Tick")]
        public void TickEffects()
        {
            if (!EnsureInitialized()) return;
            target.GameEffectCtrl.Tick(tickDelta);
            Debug.Log($"[GETest][Tick] Delta={tickDelta}, Active={target.GameEffectCtrl.ActiveEffects.Count}, " +
                      $"Duration={lastRuntime?.RemainingDuration ?? 0f}, Period={lastRuntime?.RemainingPeriod ?? 0f}");
            LogTargetAttributes();
        }

        /// <summary>精确移除最近一次返回的 Active Runtime。</summary>
        [Button("移除最近 Active GE")]
        public void RemoveLastEffect()
        {
            if (!EnsureInitialized()) return;
            bool success = lastRuntime != null && target.GameEffectCtrl.TryRemove(lastRuntime);
            Debug.Log($"[GETest][Remove] Success={success}, Active={target.GameEffectCtrl.ActiveEffects.Count}");
            if (success) lastRuntime = null;
            LogTargetAttributes();
        }

        /// <summary>清理全部 Active GE，验证 Modifier 与 GrantedTags 一并撤销。</summary>
        [Button("清理全部 GE")]
        public void ClearEffects()
        {
            if (!EnsureInitialized()) return;
            target.GameEffectCtrl.Clear();
            lastRuntime = null;
            Debug.Log("[GETest][Clear] Active=0");
            LogTargetAttributes();
        }

        #endregion

        #region 内部辅助

        // 测试按钮统一检查运行对象与配置，避免空引用掩盖业务结果。
        private bool EnsureInitialized()
        {
            if (source != null && target != null && effect != null) return true;
            Debug.LogError("[GETest] 请先指定 GameplayEffectData 并执行初始化。");
            return false;
        }

        // 将 Inspector List 转成 Runtime 字典；重复 Key 使用最后一项，和常规配置覆盖一致。
        private Dictionary<GameplayTag, float> BuildSetByCaller()
        {
            var values = new Dictionary<GameplayTag, float>();
            for (int i = 0; i < setByCaller.Count; i++)
                values[setByCaller[i].Key] = setByCaller[i].Value;
            return values;
        }

        // 输出 Target 的公开 CurrentValue，测试代码不读取内部 BaseValue。
        private void LogTargetAttributes()
        {
            IReadOnlyList<GameplayAttributeDefinition> definitions = target.Attributes.Attributes;
            for (int i = 0; i < definitions.Count; i++)
                Debug.Log($"[GETest][Attribute] {definitions[i].Attribute} = {definitions[i].CurrentValue}");
        }

        #endregion
    }
}
#endif
