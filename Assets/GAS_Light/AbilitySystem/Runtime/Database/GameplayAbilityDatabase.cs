using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.Serialization;
using UnityEngine;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>统一保存 Gameplay Ability 的运行时查询表与 Editor 稳定 ID 烘焙历史。</summary>
    [CreateAssetMenu(fileName = "GameplayAbilityDatabase", menuName = "WSFrame/GAS/Gameplay Ability Database")]
    public sealed class GameplayAbilityDatabase : SerializedScriptableObject
    {
        #region 运行时数据

        [OdinSerialize, ReadOnly]
        [DictionaryDrawerSettings(KeyLabel = "Ability ID", ValueLabel = "Gameplay Ability")]
        private Dictionary<int, GameplayAbilityData> abilitiesById = new();

        #endregion

        #region 运行时属性与查询

        /// <summary>获取已烘焙的 Gameplay Ability 数量。</summary>
        public int Count => abilitiesById?.Count ?? 0;

        /// <summary>按全局稳定 AbilityId 查询 GameplayAbilityData。</summary>
        /// <param name="abilityId">由当前 Database Bake 的稳定 ID。</param>
        /// <param name="ability">查询成功时返回对应 Ability 资产。</param>
        /// <returns>数据库包含该 ID 时返回 true。</returns>
        public bool TryGetAbility(int abilityId, out GameplayAbilityData ability)
        {
            if (abilitiesById != null && abilitiesById.TryGetValue(abilityId, out ability))
                return true;

            ability = null;
            return false;
        }

        #endregion

#if UNITY_EDITOR

        #region Editor 烘焙数据

        [OdinSerialize, ReadOnly]
        private Dictionary<string, int> bakedIdHistory = new();

        [SerializeField, ReadOnly]
        private List<int> retiredAbilityIds = new();

        [SerializeField, MinValue(0)]
        private int nextAbilityId;

        [SerializeField, ReadOnly]
        private bool bakeDirty = true;

        #endregion

        #region Editor 属性

        /// <summary>获取 Asset GUID 到稳定 AbilityId 的只读烘焙历史。</summary>
        public IReadOnlyDictionary<string, int> BakedIdHistory => bakedIdHistory;

        /// <summary>获取已永久废弃且不得复用的 AbilityId。</summary>
        public IReadOnlyList<int> RetiredAbilityIds => retiredAbilityIds;

        /// <summary>获取下一次 Bake 可分配的 AbilityId。</summary>
        public int NextAbilityId => nextAbilityId;

        /// <summary>获取当前作者资产集合是否存在尚未 Bake 的变化。</summary>
        public bool BakeDirty => bakeDirty;

        /// <summary>获取 Editor 校验使用的运行时 AbilityId 索引。</summary>
        public IReadOnlyDictionary<int, GameplayAbilityData> BakedAbilitiesById => abilitiesById;

        #endregion

        #region Editor 烘焙操作

        /// <summary>标记 Gameplay Ability 资产集合发生变化，需要重新 Bake。</summary>
        public void MarkBakeDirty() => bakeDirty = true;

        /// <summary>迁移旧资产中被 Odin 持久化的非默认字符串比较器。</summary>
        /// <returns>字典被初始化或更换比较器时返回 true。</returns>
        public bool NormalizeBakedIdHistoryComparer()
        {
            if (bakedIdHistory == null)
            {
                bakedIdHistory = new Dictionary<string, int>();
                return true;
            }

            if (bakedIdHistory.Comparer?.GetType() == EqualityComparer<string>.Default.GetType())
                return false;

            // Odin 不应持久化运行时内部 comparer 类型，统一复制为默认比较器字典。
            bakedIdHistory = new Dictionary<string, int>(bakedIdHistory);
            return true;
        }

        /// <summary>原子提交 Baker 已完成校验的运行时索引与稳定 ID 历史。</summary>
        /// <param name="abilities">AbilityId 到 GameplayAbilityData 的运行时索引。</param>
        /// <param name="idHistory">当前有效资产 GUID 到 AbilityId 的历史。</param>
        /// <param name="retiredIds">已经废弃且不得复用的 AbilityId。</param>
        /// <param name="followingAbilityId">下一次 Bake 的 ID 分配起点。</param>
        public void ApplyBake(
            Dictionary<int, GameplayAbilityData> abilities,
            Dictionary<string, int> idHistory,
            List<int> retiredIds,
            int followingAbilityId)
        {
            abilitiesById = abilities ?? new Dictionary<int, GameplayAbilityData>();
            bakedIdHistory = idHistory == null
                ? new Dictionary<string, int>()
                : new Dictionary<string, int>(idHistory);
            retiredAbilityIds = retiredIds ?? new List<int>();
            nextAbilityId = followingAbilityId;
            bakeDirty = false;
        }

        #endregion

#endif
    }
}
