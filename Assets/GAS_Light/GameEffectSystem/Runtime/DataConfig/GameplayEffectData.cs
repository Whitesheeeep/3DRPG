using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.TAG;
#if UNITY_EDITOR
using System.Globalization;
using WS_Modules.Baking;
using WS_Modules.GAS.GameplayEffect;
#endif
#if UNITY_EDITOR
using WS_Modules.GAS.AttributeSystem;
#endif

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>保存可复用的 Gameplay Effect 作者配置；运行时状态由 Runtime 独立承载。</summary>
    [CreateAssetMenu(fileName = "GameplayEffectData", menuName = "WSFrame/GAS/Gameplay Effect")]
#if UNITY_EDITOR
    public sealed class GameplayEffectData : ScriptableObject, IBakedResultDataSource
#else
    public sealed class GameplayEffectData : ScriptableObject
#endif
    {
        #region 基础配置

        [SerializeField, TextArea] private string description;
        [SerializeField, Tooltip("决定 GE 是立即结算、有限持续还是无限持续。")]
        private E_GameEffectDurationType durationType;
        [SerializeField, Min(0f), Tooltip("Duration GE 的完整持续时间；其他类型忽略。")]
        private float duration;
        [SerializeField, Min(0f), Tooltip("大于 0 时按周期执行 Instant 结算；支持 Duration 与 Infinite。")]
        private float period;
        [SerializeField, Tooltip("周期 GE 应用成功时是否立即执行第一轮。")]
        private bool executePeriodicOnApplication;

        #endregion

        #region Tag 与数值计算

        [SerializeField, Tooltip("目标必须满足该查询才能应用；空查询表示不限制。")]
        private GameplayTagQuery targetTagQuery;
        [SerializeField, Tooltip("非 Instant GE 激活期间赋予 Target 的标签。")]
        private GameplayTag[] grantedTags = Array.Empty<GameplayTag>();
        [SerializeField, Tooltip("GE 成功提交后发布的 GameplayCueTag 列表。")]
        private GameplayTag[] cueTags = Array.Empty<GameplayTag>();
        [SerializeReference, Tooltip("按列表顺序执行；每项生成一个最终 Attribute Modifier。")]
        private List<GameplayEffectModifier> modifiers = new();

        #endregion

#if UNITY_EDITOR
        #region Editor 校验范围

        [SerializeField, Tooltip("仅用于 GE Editor 校验；空列表表示扫描项目全部 AttributeSet。")]
        private List<GameplayAttributeSet> validationSets = new();

        [SerializeField, Tooltip("Curve 结果窗口中第一列的题头。")]
        private string curveBakeXAxisLabel = "Level / X";
        [SerializeField, Tooltip("Curve 结果预览的起始 x。")]
        private float curveBakeStartX = 1f;
        [SerializeField, Tooltip("Curve 结果预览的结束 x。")]
        private float curveBakeEndX = 90f;
        [SerializeField, Min(0.000001f), Tooltip("Curve 结果预览的采样步长。")]
        private float curveBakeStep = 1f;
        [SerializeField, HideInInspector]
        private string bakedCurveXAxisLabel = "Level / X";
        [SerializeField, HideInInspector]
        private List<string> bakedCurveHeaders = new();
        [SerializeField, HideInInspector]
        private List<BakedGameplayEffectCurveRow> bakedCurveRows = new();

        #endregion
#endif

        #region 叠层策略

        [SerializeField, Tooltip("决定重复应用是否合并到现有 Runtime。")]
        private E_GameEffectStackingType stackingType;
        [SerializeField, Min(1), Tooltip("合并叠层时允许的最大层数；None 时忽略。")]
        private int maxStackCount = 1;
        [SerializeField, Tooltip("达到最大层数后是否完全拒绝本次应用。")]
        private bool denyOverflowApplication = true;
        [SerializeField, Tooltip("成功重复应用时如何更新剩余持续时间。")]
        private E_GameEffectStackingDurationPolicy stackingDurationPolicy;
        [SerializeField, Tooltip("成功重复应用时如何更新下一次周期计时。")]
        private E_GameEffectStackingPeriodPolicy stackingPeriodPolicy;
        [SerializeField, Tooltip("Duration 到期时如何处理现有层数。")]
        private E_GameEffectStackingExpirationPolicy stackingExpirationPolicy;

        #endregion

#if UNITY_EDITOR
        #region Curve 烘焙预览

        /// <summary>获取 Curve 预览的 x 轴题头。</summary>
        public string CurveBakeXAxisLabel => curveBakeXAxisLabel;

        /// <summary>获取 Curve 预览起始 x。</summary>
        public float CurveBakeStartX => curveBakeStartX;

        /// <summary>获取 Curve 预览结束 x。</summary>
        public float CurveBakeEndX => curveBakeEndX;

        /// <summary>获取 Curve 预览采样步长。</summary>
        public float CurveBakeStep => curveBakeStep;

        /// <summary>获取本次 Bake 会修改的 GE 资产。</summary>
        public IReadOnlyList<UnityEngine.Object> BakeTargets => new UnityEngine.Object[] { this };

        /// <summary>获取结果窗口标题。</summary>
        public string BakedResultTitle => $"{name} - GameplayEffect Curve 烘焙结果";

        /// <summary>扫描 Curve Modifier 并生成编辑器预览快照。</summary>
        /// <exception cref="InvalidOperationException">采样配置或 Modifier 配置无效时抛出。</exception>
        public void Bake()
        {
            ValidateCurveBakeSettings();
            var curveModifiers = CollectCurveModifiers();
            if (curveModifiers.Count == 0)
                throw new InvalidOperationException($"GameplayEffectData '{name}' 没有可烘焙的 Curve Modifier。");

            List<float> sampleXs = BuildSampleXs();
            var headers = new List<string>(curveModifiers.Count);
            for (int index = 0; index < curveModifiers.Count; index++)
                headers.Add(BuildCurveHeader(curveModifiers[index].Modifier, curveModifiers[index].OriginalIndex));

            var rows = new List<BakedGameplayEffectCurveRow>(sampleXs.Count);
            for (int sampleIndex = 0; sampleIndex < sampleXs.Count; sampleIndex++)
            {
                float x = sampleXs[sampleIndex];
                var magnitudes = new List<float>(curveModifiers.Count);
                for (int modifierIndex = 0; modifierIndex < curveModifiers.Count; modifierIndex++)
                {
                    float magnitude = curveModifiers[modifierIndex].Modifier.CalculateConfiguredMagnitude(x);
                    if (float.IsNaN(magnitude) || float.IsInfinity(magnitude))
                        throw new InvalidOperationException($"GameplayEffectData '{name}' 的 Curve Modifier {curveModifiers[modifierIndex].OriginalIndex + 1} 在 x={x.ToString(CultureInfo.InvariantCulture)} 产生非有限 Magnitude。");
                    magnitudes.Add(magnitude);
                }

                rows.Add(new BakedGameplayEffectCurveRow(x, magnitudes));
            }

            // 所有采样成功后才替换快照，失败时保留上一次可用结果。
            bakedCurveXAxisLabel = string.IsNullOrWhiteSpace(curveBakeXAxisLabel)
                ? "Level / X"
                : curveBakeXAxisLabel.Trim();
            bakedCurveHeaders = headers;
            bakedCurveRows = rows;
        }

        /// <summary>创建最近一次保存的 GE Curve 最终结果表。</summary>
        /// <returns>包含 x 和每个 Curve Modifier 最终值的扁平表。</returns>
        public BakedResultTableData CreateBakedResultTableData()
        {
            var headers = new List<string>(1 + bakedCurveHeaders.Count)
            {
                string.IsNullOrWhiteSpace(bakedCurveXAxisLabel) ? "Level / X" : bakedCurveXAxisLabel
            };
            headers.AddRange(bakedCurveHeaders);

            var rows = new List<BakedResultRowData>(bakedCurveRows.Count);
            for (int rowIndex = 0; rowIndex < bakedCurveRows.Count; rowIndex++)
            {
                BakedGameplayEffectCurveRow row = bakedCurveRows[rowIndex];
                var cells = new List<string>(1 + row.Magnitudes.Count)
                {
                    row.X.ToString("0.###", CultureInfo.InvariantCulture)
                };
                for (int valueIndex = 0; valueIndex < row.Magnitudes.Count; valueIndex++)
                    cells.Add(row.Magnitudes[valueIndex].ToString("0.###", CultureInfo.InvariantCulture));
                rows.Add(new BakedResultRowData(cells));
            }

            return new BakedResultTableData(BakedResultTitle, headers, rows);
        }

        /// <summary>校验 Curve 预览采样范围。</summary>
        private void ValidateCurveBakeSettings()
        {
            if (float.IsNaN(curveBakeStartX) || float.IsInfinity(curveBakeStartX) ||
                float.IsNaN(curveBakeEndX) || float.IsInfinity(curveBakeEndX) ||
                float.IsNaN(curveBakeStep) || float.IsInfinity(curveBakeStep))
                throw new InvalidOperationException($"GameplayEffectData '{name}' 的 Curve 采样范围必须是有限数值。");
            if (curveBakeStep <= 0f) throw new InvalidOperationException($"GameplayEffectData '{name}' 的 Curve 采样步长必须大于零。");
            if (curveBakeEndX < curveBakeStartX) throw new InvalidOperationException($"GameplayEffectData '{name}' 的 Curve 结束 x 不能小于起始 x。");
        }

        /// <summary>收集当前 GE 中的 Curve Modifier 和原始数组索引。</summary>
        /// <returns>按作者顺序排列的 Curve Modifier。</returns>
        private List<(CurveGameplayEffectModifier Modifier, int OriginalIndex)> CollectCurveModifiers()
        {
            var result = new List<(CurveGameplayEffectModifier, int)>();
            for (int index = 0; index < modifiers.Count; index++)
            {
                GameplayEffectModifier modifier = modifiers[index];
                if (modifier == null) throw new InvalidOperationException($"GameplayEffectData '{name}' 的 Modifier {index + 1} 为空。");
                if (modifier is CurveGameplayEffectModifier curveModifier)
                    result.Add((curveModifier, index));
            }

            return result;
        }

        /// <summary>按 Start、End 和 Step 展开稳定的采样 x 列表。</summary>
        /// <returns>包含 End 的有序采样坐标。</returns>
        private List<float> BuildSampleXs()
        {
            const int MaxSampleCount = 10000;
            const float Tolerance = 0.00001f;
            var result = new List<float>();
            float current = curveBakeStartX;
            for (int index = 0; index < MaxSampleCount; index++)
            {
                result.Add(current);
                if (Mathf.Abs(current - curveBakeEndX) <= Tolerance) return result;
                float next = current + curveBakeStep;
                if (next <= current) throw new InvalidOperationException($"GameplayEffectData '{name}' 的 Curve 采样步长导致 x 无法递增。");
                if (next > curveBakeEndX || Mathf.Abs(next - curveBakeEndX) <= Tolerance)
                {
                    if (Mathf.Abs(current - curveBakeEndX) > Tolerance)
                    {
                        if (result.Count >= MaxSampleCount)
                            throw new InvalidOperationException($"GameplayEffectData '{name}' 的 Curve 采样点超过 {MaxSampleCount} 个，请增大步长或缩小范围。");
                        result.Add(curveBakeEndX);
                    }
                    return result;
                }

                current = next;
            }

            throw new InvalidOperationException($"GameplayEffectData '{name}' 的 Curve 采样点超过 {MaxSampleCount} 个，请增大步长或缩小范围。");
        }

        /// <summary>生成 Curve Modifier 的结果列标题。</summary>
        /// <param name="modifier">Curve Modifier。</param>
        /// <param name="originalIndex">Modifier 在原始列表中的索引。</param>
        /// <returns>稳定的列标题。</returns>
        private static string BuildCurveHeader(CurveGameplayEffectModifier modifier, int originalIndex)
        {
            string label = modifier.BakedResultLabel;
            return string.IsNullOrWhiteSpace(label)
                ? $"Modifier {originalIndex + 1} / AttributeId {modifier.Attribute.Id}"
                : label.Trim();
        }

        #endregion
#endif

        #region 属性

        /// <summary>获取供编辑器和日志显示的说明。</summary>
        public string Description => description;
        /// <summary>获取持续时间类型。</summary>
        public E_GameEffectDurationType DurationType => durationType;
        /// <summary>获取 Duration GE 的完整持续时间。</summary>
        public float Duration => duration;
        /// <summary>获取周期时长；零表示非周期持续效果。</summary>
        public float Period => period;
        /// <summary>获取周期 GE 是否在应用成功时立即执行第一轮。</summary>
        public bool ExecutePeriodicOnApplication => executePeriodicOnApplication;
        /// <summary>获取目标应用 Tag 查询。</summary>
        public GameplayTagQuery TargetTagQuery => targetTagQuery;
        /// <summary>获取激活期间赋予 Target 的标签。</summary>
        public IReadOnlyList<GameplayTag> GrantedTags => grantedTags;
        /// <summary>获取 GE 成功提交后发布的 CueTag 列表。</summary>
        public IReadOnlyList<GameplayTag> CueTags => cueTags;
        /// <summary>获取按顺序计算并提交的 GE Modifier 作者配置。</summary>
        public IReadOnlyList<GameplayEffectModifier> Modifiers => modifiers;
        /// <summary>获取重复应用时的合并规则。</summary>
        public E_GameEffectStackingType StackingType => stackingType;
        /// <summary>获取允许的最大叠层数。</summary>
        public int MaxStackCount => maxStackCount;
        /// <summary>获取达到最大层数时是否完全拒绝应用：不会刷新 Duration 和 Period</summary>
        public bool DenyOverflowApplication => denyOverflowApplication;
        /// <summary>获取成功重复应用时的持续时间规则。</summary>
        public E_GameEffectStackingDurationPolicy StackingDurationPolicy => stackingDurationPolicy;
        /// <summary>获取成功重复应用时的周期计时规则。</summary>
        public E_GameEffectStackingPeriodPolicy StackingPeriodPolicy => stackingPeriodPolicy;
        /// <summary>获取 Duration 到期时的叠层规则。</summary>
        public E_GameEffectStackingExpirationPolicy StackingExpirationPolicy => stackingExpirationPolicy;
        /// <summary>获取该配置是否为周期 GE。</summary>
        public bool IsPeriodic => period > 0f;

        #endregion

        #region Modifier 计算契约

        // 汇总所有 Modifier 声明的动态输入 Key，供 Controller 在唯一公开失败入口统一检查。
        internal void CollectRequiredSetByCallerKeys(ISet<GameplayTag> keys)
        {
            for (int i = 0; i < Modifiers.Count; i++)
                Modifiers[i].CollectRequiredSetByCallerKeys(keys);
        }

        #endregion
    }
}
