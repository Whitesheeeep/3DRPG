using System;
using System.Collections.Generic;
using RPG.Character;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.AttributeSystem;

namespace RPG.NPC
{
    /// <summary>保存 NPC 按等级变化的 Attribute BaseValue 曲线与完整逐级烘焙快照。</summary>
    [CreateAssetMenu(fileName = "NPCGrowthProfile", menuName = "RPG/NPC/NPC Growth Profile", order = 30)]
    public sealed class NPCGrowthProfile : ScriptableObject
    {
        #region 配置与烘焙字段

        // 成长曲线决定等级覆盖值；逐级快照与指纹共同构成运行时唯一可信的数据源。
        [SerializeField, MinValue(1), LabelText("最大等级")]
        private int maxLevel = 90;
        [SerializeField, LabelText("Attribute BaseValue 曲线")]
        private List<CharacterAttributeGrowthCurve> attributeGrowthCurves = new();
        [SerializeField, ReadOnly, LabelText("已烘焙 Attribute 结果")]
        private List<BakedCharacterAttributeProgression> bakedAttributeProgressions = new();
        [SerializeField, HideInInspector]
        private int bakedInputHash;

        #endregion

        #region 查询属性

        /// <summary>获取 NPC 等级上限。</summary>
        public int MaxLevel => maxLevel;

        /// <summary>获取可为 NPC 独立配置的等级 Attribute 曲线。</summary>
        public IReadOnlyList<CharacterAttributeGrowthCurve> AttributeGrowthCurves =>
            (IReadOnlyList<CharacterAttributeGrowthCurve>)attributeGrowthCurves ??
            Array.Empty<CharacterAttributeGrowthCurve>();

        /// <summary>获取包含每个等级数值的完整 Attribute 烘焙结果。</summary>
        public IReadOnlyList<BakedCharacterAttributeProgression> BakedAttributeProgressions =>
            (IReadOnlyList<BakedCharacterAttributeProgression>)bakedAttributeProgressions ??
            Array.Empty<BakedCharacterAttributeProgression>();

        #endregion

        #region 烘焙状态与操作

        // 烘焙状态查询
        /// <summary>判断当前曲线、等级上限或初始 AttributeSet 是否使烘焙结果过期。</summary>
        /// <param name="initialAttributeSets">NPC 导入的初始 AttributeSet 列表。</param>
        /// <returns>结果缺失、结构不完整或输入指纹变化时返回 true。</returns>
        public bool NeedsRebake(IReadOnlyList<GameplayAttributeSet> initialAttributeSets)
        {
            if (bakedAttributeProgressions == null || bakedInputHash == 0 || attributeGrowthCurves == null)
                return true;

            List<GameplayAttributeDefinition> orderedDefinitions =
                BuildOrderedAttributeDefinitions(initialAttributeSets);
            if (bakedAttributeProgressions.Count != orderedDefinitions.Count)
                return true;

            for (int index = 0; index < orderedDefinitions.Count; index++)
            {
                BakedCharacterAttributeProgression baked = bakedAttributeProgressions[index];
                if (baked == null || baked.Attribute.Id != orderedDefinitions[index].Attribute.Id ||
                    baked.BaseValues == null || baked.BaseValues.Count != maxLevel)
                    return true;
            }

            return bakedInputHash != CalculateInputHash(initialAttributeSets, orderedDefinitions);
        }

        // 输入校验
        /// <summary>校验最大等级、曲线唯一性和曲线数值边界。</summary>
        /// <param name="initialAttributeSets">NPC 导入的初始 AttributeSet 列表。</param>
        /// <exception cref="InvalidOperationException">Profile 或曲线不满足 NPC 属性契约时抛出。</exception>
        public void Validate(IReadOnlyList<GameplayAttributeSet> initialAttributeSets)
        {
            if (maxLevel < 1)
                throw new InvalidOperationException($"NPCGrowthProfile '{name}' 的最大等级必须大于零。");

            List<GameplayAttributeDefinition> orderedDefinitions =
                BuildOrderedAttributeDefinitions(initialAttributeSets);
            var definitionByAttributeIdMap = new Dictionary<int, GameplayAttributeDefinition>();
            for (int index = 0; index < orderedDefinitions.Count; index++)
            {
                GameplayAttributeDefinition definition = orderedDefinitions[index];
                definitionByAttributeIdMap.Add(definition.Attribute.Id, definition);
            }

            ValidateAttributeGrowthCurves(definitionByAttributeIdMap);
        }

        // 编辑器烘焙入口
        /// <summary>按当前 NPC Profile 输入生成所有 Attribute 的逐级 BaseValue 烘焙结果。</summary>
        /// <param name="initialAttributeSets">NPC 导入的初始 AttributeSet 列表。</param>
        /// <exception cref="InvalidOperationException">输入配置非法时抛出，原烘焙数据保持不变。</exception>
        public void Bake(IReadOnlyList<GameplayAttributeSet> initialAttributeSets)
        {
            try
            {
                Validate(initialAttributeSets);
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogError(
                    $"[NPCGrowthProfile] '{name}' 烘焙校验失败，旧结果保持不变：{exception.Message}",
                    this);
                throw;
            }

            List<GameplayAttributeDefinition> orderedDefinitions =
                BuildOrderedAttributeDefinitions(initialAttributeSets);
            // key：稳定 AttributeId；value：该属性唯一配置的等级曲线。
            var growthCurveByAttributeIdMap = new Dictionary<int, CharacterAttributeGrowthCurve>();
            for (int index = 0; index < attributeGrowthCurves.Count; index++)
            {
                CharacterAttributeGrowthCurve growthCurve = attributeGrowthCurves[index];
                growthCurveByAttributeIdMap.Add(growthCurve.Attribute.Id, growthCurve);
            }

            var bakedResults = new List<BakedCharacterAttributeProgression>(orderedDefinitions.Count);
            for (int attributeIndex = 0; attributeIndex < orderedDefinitions.Count; attributeIndex++)
            {
                GameplayAttributeDefinition definition = orderedDefinitions[attributeIndex];
                var baseValues = new List<float>(maxLevel);
                for (int level = 1; level <= maxLevel; level++)
                {
                    float value = growthCurveByAttributeIdMap.TryGetValue(
                        definition.Attribute.Id,
                        out CharacterAttributeGrowthCurve growthCurve)
                        ? growthCurve.BaseValueCurve.Evaluate(level)
                        : definition.DefaultValue;
                    baseValues.Add(value);
                }

                bakedResults.Add(new BakedCharacterAttributeProgression(definition.Attribute, baseValues));
            }

            // 只在全部曲线和 Definition 已通过校验后替换快照，避免失败 Bake 留下半成品。
            bakedAttributeProgressions = bakedResults;
            bakedInputHash = CalculateInputHash(initialAttributeSets, orderedDefinitions);
            Debug.Log(
                $"[NPCGrowthProfile] '{name}' 完成逐级属性烘焙，levels={maxLevel}, attributes={bakedResults.Count}。",
                this);
        }

        // 运行时等级解析
        /// <summary>解析指定等级的完整 BaseValue 列表；调用方须先完成 Config 与 Bake 状态校验。</summary>
        /// <param name="level">从 1 开始的 NPC 等级。</param>
        /// <param name="initialAttributeSets">NPC 导入的初始 AttributeSet 列表。</param>
        /// <returns>按初始 AttributeSet 顺序排列的全部等级 BaseValue。</returns>
        /// <exception cref="ArgumentOutOfRangeException">等级不在烘焙区间内时抛出。</exception>
        /// <exception cref="InvalidOperationException">烘焙快照结构与等级配置不一致时抛出。</exception>
        internal IReadOnlyList<GameplayAttributeValue> ResolveBaseValues(
            int level,
            IReadOnlyList<GameplayAttributeSet> initialAttributeSets)
        {
            if (level < 1 || level > maxLevel)
                throw new ArgumentOutOfRangeException(
                    nameof(level), level, $"NPCGrowthProfile '{name}' 支持等级 1 到 {maxLevel}。");

            List<GameplayAttributeDefinition> orderedDefinitions =
                BuildOrderedAttributeDefinitions(initialAttributeSets);
            if (bakedAttributeProgressions == null || bakedAttributeProgressions.Count != orderedDefinitions.Count)
                throw new InvalidOperationException($"NPCGrowthProfile '{name}' 的烘焙结果结构不完整，请重新 Bake。");

            var values = new List<GameplayAttributeValue>(orderedDefinitions.Count);
            for (int index = 0; index < orderedDefinitions.Count; index++)
            {
                BakedCharacterAttributeProgression progression = bakedAttributeProgressions[index];
                if (progression == null || progression.Attribute.Id != orderedDefinitions[index].Attribute.Id ||
                    progression.BaseValues == null || progression.BaseValues.Count != maxLevel)
                    throw new InvalidOperationException(
                        $"NPCGrowthProfile '{name}' 的 Attribute 烘焙行 {index} 不完整，请重新 Bake。");

                values.Add(new GameplayAttributeValue(progression.Attribute, progression.BaseValues[level - 1]));
            }

            return values;
        }

        #endregion

        #region 校验与定义收集

        // 曲线验证
        /// <summary>校验每条等级曲线的属性归属、唯一性、关键帧和采样边界。</summary>
        /// <param name="definitionByAttributeIdMap">按 AttributeId 索引的输入 Attribute Definition。</param>
        private void ValidateAttributeGrowthCurves(
            IReadOnlyDictionary<int, GameplayAttributeDefinition> definitionByAttributeIdMap)
        {
            if (attributeGrowthCurves == null)
                throw new InvalidOperationException($"NPCGrowthProfile '{name}' 的 Attribute 曲线列表为空引用。");

            var configuredAttributeIdSet = new HashSet<int>();
            for (int index = 0; index < attributeGrowthCurves.Count; index++)
            {
                CharacterAttributeGrowthCurve growthCurve = attributeGrowthCurves[index];
                if (growthCurve == null || !growthCurve.Attribute.IsValid)
                    throw new InvalidOperationException(
                        $"NPCGrowthProfile '{name}' 的 Attribute 曲线第 {index} 项为空或使用无效 Attribute。");
                if (!configuredAttributeIdSet.Add(growthCurve.Attribute.Id))
                    throw new InvalidOperationException(
                        $"NPCGrowthProfile '{name}' 重复配置 AttributeId {growthCurve.Attribute.Id}。");
                if (!definitionByAttributeIdMap.TryGetValue(
                        growthCurve.Attribute.Id,
                        out GameplayAttributeDefinition definition))
                    throw new InvalidOperationException(
                        $"NPCGrowthProfile '{name}' 的 Attribute '{growthCurve.Attribute}' 不存在于 NPC 初始 AttributeSet。");
                if (growthCurve.BaseValueCurve == null)
                    throw new InvalidOperationException(
                        $"NPCGrowthProfile '{name}' 的 Attribute '{growthCurve.Attribute}' 缺少 BaseValue 曲线。");

                ValidateCurveKeyframes(growthCurve, index);
                for (int level = 1; level <= maxLevel; level++)
                {
                    float value = growthCurve.BaseValueCurve.Evaluate(level);
                    if (!IsFinite(value) || value < definition.MinValue || value > definition.MaxValue)
                        throw new InvalidOperationException(
                            $"NPCGrowthProfile '{name}' 的 Attribute '{growthCurve.Attribute}' 在等级 {level} 产生非法或超出 Definition 边界的 BaseValue。");
                }
            }
        }

        // 曲线关键帧验证
        /// <summary>拒绝非有限曲线关键帧，避免非法切线只在运行时采样后暴露。</summary>
        /// <param name="growthCurve">正在校验的属性曲线。</param>
        /// <param name="curveIndex">曲线在 Profile 中的下标。</param>
        private void ValidateCurveKeyframes(CharacterAttributeGrowthCurve growthCurve, int curveIndex)
        {
            Keyframe[] keys = growthCurve.BaseValueCurve.keys;
            if (keys.Length == 0)
                throw new InvalidOperationException(
                    $"NPCGrowthProfile '{name}' 的 Attribute 曲线[{curveIndex}]没有关键帧。未配置曲线时应使用 Definition.DefaultValue 回退。");

            for (int keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            {
                Keyframe key = keys[keyIndex];
                if (!IsFinite(key.time) || !IsFinite(key.value) || !IsFinite(key.inTangent) ||
                    !IsFinite(key.outTangent) || !IsFinite(key.inWeight) || !IsFinite(key.outWeight))
                    throw new InvalidOperationException(
                        $"NPCGrowthProfile '{name}' 的 Attribute 曲线[{curveIndex}]关键帧[{keyIndex}]包含非有限数值。");
            }
        }

        // Definition 输入收集
        /// <summary>按 AttributeSet 作者顺序收集所有 Definition 并拒绝重复集合与属性。</summary>
        /// <param name="initialAttributeSets">NPC 导入的初始 AttributeSet。</param>
        /// <returns>用于烘焙列与 ASC 初始化的稳定 Definition 顺序。</returns>
        private List<GameplayAttributeDefinition> BuildOrderedAttributeDefinitions(
            IReadOnlyList<GameplayAttributeSet> initialAttributeSets)
        {
            if (initialAttributeSets == null || initialAttributeSets.Count == 0)
                throw new InvalidOperationException(
                    $"NPCGrowthProfile '{name}' 必须接收至少一个初始 AttributeSet。");

            var orderedDefinitions = new List<GameplayAttributeDefinition>();
            var uniqueAttributeSetReferences = new HashSet<GameplayAttributeSet>();
            var definitionByAttributeIdMap = new Dictionary<int, GameplayAttributeDefinition>();
            for (int setIndex = 0; setIndex < initialAttributeSets.Count; setIndex++)
            {
                GameplayAttributeSet attributeSet = initialAttributeSets[setIndex];
                if (attributeSet == null)
                    throw new InvalidOperationException($"NPC 初始 AttributeSet[{setIndex}] 为空。");
                if (!uniqueAttributeSetReferences.Add(attributeSet))
                    throw new InvalidOperationException(
                        $"NPC 初始 AttributeSet '{attributeSet.name}' 被重复引用。");

                IReadOnlyList<GameplayAttributeDefinition> definitions = attributeSet.Definitions;
                if (definitions == null)
                    throw new InvalidOperationException(
                        $"NPC 初始 AttributeSet '{attributeSet.name}' 的 Definition 列表为空引用。");
                for (int definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
                {
                    GameplayAttributeDefinition definition = definitions[definitionIndex];
                    if (definition == null)
                        throw new InvalidOperationException(
                            $"NPC 初始 AttributeSet '{attributeSet.name}' 的 Definition[{definitionIndex}] 为空。");
                    if (!definition.TryValidateTemplate(out string error))
                        throw new InvalidOperationException($"NPC 初始 AttributeSet '{attributeSet.name}'：{error}");
                    if (!definitionByAttributeIdMap.TryAdd(definition.Attribute.Id, definition))
                        throw new InvalidOperationException(
                            $"NPC 初始 AttributeSet 重复配置 AttributeId {definition.Attribute.Id}。");

                    orderedDefinitions.Add(definition);
                }
            }

            if (orderedDefinitions.Count == 0)
                throw new InvalidOperationException($"NPCGrowthProfile '{name}' 的 AttributeSet 没有 Attribute Definition。");
            return orderedDefinitions;
        }

        #endregion

        #region 输入指纹

        /// <summary>计算等级上限、全部曲线和 Attribute Definition 的确定性输入指纹。</summary>
        /// <param name="initialAttributeSets">NPC 初始 AttributeSet 列表。</param>
        /// <param name="orderedDefinitions">已校验且按配置顺序排列的 Definition。</param>
        /// <returns>可持久化到 Unity 资产的整数指纹。</returns>
        private int CalculateInputHash(
            IReadOnlyList<GameplayAttributeSet> initialAttributeSets,
            IReadOnlyList<GameplayAttributeDefinition> orderedDefinitions)
        {
            unchecked
            {
                int hash = CombineHash(17, maxLevel);
                hash = CombineHash(hash, initialAttributeSets.Count);
                hash = CombineHash(hash, attributeGrowthCurves.Count);
                for (int index = 0; index < attributeGrowthCurves.Count; index++)
                {
                    CharacterAttributeGrowthCurve growthCurve = attributeGrowthCurves[index];
                    hash = CombineHash(hash, growthCurve.Attribute.Id);
                    hash = CombineCurveHash(hash, growthCurve.BaseValueCurve);
                }

                hash = CombineHash(hash, orderedDefinitions.Count);
                for (int index = 0; index < orderedDefinitions.Count; index++)
                {
                    GameplayAttributeDefinition definition = orderedDefinitions[index];
                    hash = CombineHash(hash, definition.Attribute.Id);
                    hash = CombineStringHash(hash, definition.Attribute.Name);
                    hash = CombineStringHash(hash, definition.Attribute.DisplayName);
                    hash = CombineHash(hash, (int)definition.Type);
                    hash = CombineHash(hash, definition.DefaultValue.GetHashCode());
                    hash = CombineHash(hash, definition.MinValue.GetHashCode());
                    hash = CombineHash(hash, definition.MaxValue.GetHashCode());
                }

                // 0 作为“尚未烘焙”哨兵，因此将偶然碰撞到零的有效指纹映射到非零值。
                return hash == 0 ? int.MinValue : hash;
            }
        }

        /// <summary>将整数写入简单确定性乘法指纹。</summary>
        /// <param name="hash">当前输入指纹。</param>
        /// <param name="value">需要纳入指纹的整数。</param>
        /// <returns>合并后的指纹。</returns>
        private static int CombineHash(int hash, int value)
        {
            unchecked
            {
                return (hash * 31) + value;
            }
        }

        /// <summary>按 UTF-16 字符顺序合并名称，避免运行时随机化字符串 HashCode。</summary>
        /// <param name="hash">当前输入指纹。</param>
        /// <param name="value">Attribute 代码名或展示名。</param>
        /// <returns>合并后的指纹。</returns>
        private static int CombineStringHash(int hash, string value)
        {
            value ??= string.Empty;
            hash = CombineHash(hash, value.Length);
            for (int index = 0; index < value.Length; index++)
                hash = CombineHash(hash, value[index]);
            return hash;
        }

        /// <summary>将动画曲线的完整关键帧输入合并到指纹。</summary>
        /// <param name="hash">当前输入指纹。</param>
        /// <param name="curve">被采样的 BaseValue 曲线。</param>
        /// <returns>合并后的指纹。</returns>
        private static int CombineCurveHash(int hash, AnimationCurve curve)
        {
            Keyframe[] keys = curve.keys;
            hash = CombineHash(hash, curve.preWrapMode.GetHashCode());
            hash = CombineHash(hash, curve.postWrapMode.GetHashCode());
            hash = CombineHash(hash, keys.Length);
            for (int index = 0; index < keys.Length; index++)
            {
                Keyframe key = keys[index];
                hash = CombineHash(hash, key.time.GetHashCode());
                hash = CombineHash(hash, key.value.GetHashCode());
                hash = CombineHash(hash, key.inTangent.GetHashCode());
                hash = CombineHash(hash, key.outTangent.GetHashCode());
                hash = CombineHash(hash, key.weightedMode.GetHashCode());
                hash = CombineHash(hash, key.inWeight.GetHashCode());
                hash = CombineHash(hash, key.outWeight.GetHashCode());
            }

            return hash;
        }

        /// <summary>判断浮点值是否可用于烘焙。</summary>
        /// <param name="value">待检查数值。</param>
        /// <returns>不是 NaN 或正负无穷时返回 true。</returns>
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        #endregion
    }
}
