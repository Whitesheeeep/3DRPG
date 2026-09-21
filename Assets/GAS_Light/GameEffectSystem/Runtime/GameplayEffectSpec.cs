using System;
using System.Collections.Generic;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayEffect
{
    /// <summary>
    /// 保存一次 Gameplay Effect 应用意图的稳定 Spec；Source 身份固定，但属性数值在结算时实时读取。
    /// </summary>
    public sealed class GameplayEffectSpec : IModifierSource
    {
        #region 字段

        // key：SetByCaller GameplayTag；value：本次 Spec 封存的 Magnitude。
        private readonly Dictionary<GameplayTag, float> setByCallerMagnitudeByTagMap;

        #endregion

        #region 属性

        /// <summary>获取本次应用使用的 GE 作者配置。</summary>
        public GameplayEffectData Data { get; }

        /// <summary>获取固定的 Source ASC 身份。</summary>
        public GameplayAbilitySystemComponent Source { get; }

        /// <summary>获取本次应用固定的 GE/Ability 等级。</summary>
        public int Level { get; }

        /// <summary>获取封存后的 SetByCaller 只读数据。</summary>
        public IReadOnlyDictionary<GameplayTag, float> SetByCaller => setByCallerMagnitudeByTagMap;

        /// <summary>获取 Spec 是否已经禁止继续修改 SetByCaller。</summary>
        public bool IsSealed { get; private set; }

        #endregion

        #region 构造

        /// <summary>创建尚未封存的 outgoing GameplayEffectSpec。</summary>
        /// <param name="data">GE 作者配置。</param>
        /// <param name="source">固定的 Source ASC。</param>
        /// <param name="level">本次应用等级。</param>
        /// <param name="setByCallerMagnitudeByTagMap">初始 SetByCaller 值，可以为 null。</param>
        internal GameplayEffectSpec(
            GameplayEffectData data,
            GameplayAbilitySystemComponent source,
            int level,
            IReadOnlyDictionary<GameplayTag, float> setByCallerMagnitudeByTagMap)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Source = source ?? throw new ArgumentNullException(nameof(source));
            if (level < 1) throw new ArgumentOutOfRangeException(nameof(level));
            Level = level;
            this.setByCallerMagnitudeByTagMap = CopyValues(setByCallerMagnitudeByTagMap);
        }

        #endregion

        #region SetByCaller 与封存

        /// <summary>在封存前增加或覆盖一个 SetByCaller 值。</summary>
        /// <param name="key">稳定的 SetByCaller Tag Key。</param>
        /// <param name="magnitude">需要保存的有限数值。</param>
        /// <returns>Key、数值和 Spec 状态都有效且写入成功时返回 true。</returns>
        public bool TrySetSetByCaller(GameplayTag key, float magnitude)
        {
            if (IsSealed || !GameplayTagManager.Instance.IsValidTag(key) || !IsFinite(magnitude))
                return false;
            setByCallerMagnitudeByTagMap[key] = magnitude;
            return true;
        }

        /// <summary>在封存前移除一个 SetByCaller 值。</summary>
        /// <param name="key">需要移除的稳定 SetByCaller Tag Key。</param>
        /// <returns>Spec 未封存且该 Key 存在时返回 true。</returns>
        public bool TryRemoveSetByCaller(GameplayTag key)
        {
            if (IsSealed || !GameplayTagManager.Instance.IsValidTag(key)) return false;
            return setByCallerMagnitudeByTagMap.Remove(key);
        }

        /// <summary>
        /// 校验 GE 的动态输入并封存 Spec；缺少必需 Key 时保留可编辑状态供调用方补齐。
        /// </summary>
        /// <returns>Spec 已经封存或本次校验成功封存时返回 true。</returns>
        public bool TrySeal()
        {
            if (IsSealed) return true;
            if (!Data.TryValidateApplicationConfiguration()) return false;

            // 先检查调用方携带的全部输入，避免未声明的非法 Tag 或非有限数值在封存后潜伏。
            foreach (KeyValuePair<GameplayTag, float> pair in setByCallerMagnitudeByTagMap)
                if (!GameplayTagManager.Instance.IsValidTag(pair.Key) || !IsFinite(pair.Value))
                    return false;

            var requiredKeys = new HashSet<GameplayTag>();
            Data.CollectRequiredSetByCallerKeys(requiredKeys);
            foreach (GameplayTag key in requiredKeys)
                if (!GameplayTagManager.Instance.IsValidTag(key) ||
                    !setByCallerMagnitudeByTagMap.TryGetValue(key, out float value) ||
                    !IsFinite(value))
                    return false;

            IsSealed = true;
            return true;
        }

        #endregion

        #region 内部辅助

        /// <summary>复制调用方字典，避免封存后外部修改 Spec 的输入。</summary>
        /// <param name="setByCallerMagnitudeByTagMap">调用方初始值。</param>
        /// <returns>独立的 SetByCaller 字典。</returns>
        private static Dictionary<GameplayTag, float> CopyValues(
            IReadOnlyDictionary<GameplayTag, float> setByCallerMagnitudeByTagMap)
        {
            var copiedSetByCallerMagnitudeByTagMap = new Dictionary<GameplayTag, float>();
            if (setByCallerMagnitudeByTagMap == null) return copiedSetByCallerMagnitudeByTagMap;
            foreach (KeyValuePair<GameplayTag, float> pair in setByCallerMagnitudeByTagMap)
                copiedSetByCallerMagnitudeByTagMap[pair.Key] = pair.Value;
            return copiedSetByCallerMagnitudeByTagMap;
        }

        /// <summary>判断输入值是否能安全进入 Spec。</summary>
        /// <param name="value">待校验值。</param>
        /// <returns>不是 NaN 且不是 Infinity 时返回 true。</returns>
        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        #endregion
    }
}
