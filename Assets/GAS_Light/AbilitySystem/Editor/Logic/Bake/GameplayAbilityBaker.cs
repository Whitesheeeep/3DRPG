#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace WS_Modules.GAS.Editor
{
    /// <summary>扫描 GameplayAbilityData 资产，并向单一 Database 烘焙稳定 ID 与运行时索引。</summary>
    public static class GameplayAbilityBaker
    {
        #region Bake

        /// <summary>对项目中的全部 GameplayAbilityData 执行一次完整 Bake。</summary>
        /// <param name="database">同时保存 ID 历史与运行时索引的 Database。</param>
        /// <returns>用于 Editor 显示的 Bake 摘要。</returns>
        /// <exception cref="InvalidOperationException">Database 或已有 ID 历史非法。</exception>
        public static string Bake(GameplayAbilityDatabase database)
        {
            List<string> errors = ValidateForBake(database);
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join("\n", errors));

            List<GameplayAbilityData> abilities = FindAllAbilities();
            int nextId = database.NextAbilityId;
            var activeGuids = new HashSet<string>(StringComparer.Ordinal);
            var assigned = new List<AssignedAbility>(abilities.Count);
            var history = new Dictionary<string, int>();

            for (int i = 0; i < abilities.Count; i++)
            {
                GameplayAbilityData ability = abilities[i];
                string path = AssetDatabase.GetAssetPath(ability);
                string guid = AssetDatabase.AssetPathToGUID(path);
                int id = database.BakedIdHistory.TryGetValue(guid, out int previousId)
                    ? previousId
                    : nextId++;

                activeGuids.Add(guid);
                history.Add(guid, id);
                assigned.Add(new AssignedAbility(ability, id));
            }

            var retired = new HashSet<int>(database.RetiredAbilityIds);
            foreach (KeyValuePair<string, int> pair in database.BakedIdHistory)
                if (!activeGuids.Contains(pair.Key)) retired.Add(pair.Value);

            assigned.Sort((left, right) => left.Id.CompareTo(right.Id));
            var retiredList = new List<int>(retired);
            retiredList.Sort();
            var runtimeIndex = new Dictionary<int, GameplayAbilityData>(assigned.Count);

            // 全部分配和校验先在内存完成，确认无冲突后再一次性写回资产。
            for (int i = 0; i < assigned.Count; i++)
            {
                AssignedAbility item = assigned[i];
                if (!runtimeIndex.TryAdd(item.Id, item.Ability))
                    throw new InvalidOperationException($"Bake 产生了重复 AbilityId {item.Id}。");
            }

            for (int i = 0; i < assigned.Count; i++)
            {
                AssignedAbility item = assigned[i];
                Undo.RecordObject(item.Ability, "Bake Gameplay Ability ID");
                var serialized = new SerializedObject(item.Ability);
                SerializedProperty idProperty = serialized.FindProperty("abilityId");
                idProperty.intValue = item.Id;
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(item.Ability);
            }

            Undo.RecordObject(database, "Bake Gameplay Ability Database");
            database.ApplyBake(runtimeIndex, history, retiredList, nextId);
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();
            return $"Bake 成功：{abilities.Count} 个 Gameplay Ability，NextId={nextId}。";
        }

        #endregion

        #region 校验与查询

        /// <summary>校验 Database 的稳定 ID 历史，但允许当前 Ability 资产尚未 Bake。</summary>
        /// <param name="database">待 Bake 的 Database。</param>
        /// <returns>错误列表；空列表表示可执行 Bake。</returns>
        public static List<string> ValidateForBake(GameplayAbilityDatabase database)
        {
            var errors = new List<string>();
            if (database == null)
            {
                errors.Add("未选择 GameplayAbilityDatabase。");
                return errors;
            }

            var allocatedIds = new HashSet<int>();
            int highestId = GameplayAbilityData.InvalidId;
            foreach (KeyValuePair<string, int> pair in database.BakedIdHistory)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    errors.Add("Ability ID 历史包含空 Asset GUID。");
                if (pair.Value < 0 || !allocatedIds.Add(pair.Value))
                    errors.Add($"Ability ID 历史包含非法或重复 ID：{pair.Value}。");
                highestId = Math.Max(highestId, pair.Value);
            }

            var retired = new HashSet<int>();
            for (int i = 0; i < database.RetiredAbilityIds.Count; i++)
            {
                int id = database.RetiredAbilityIds[i];
                if (id < 0 || !retired.Add(id))
                    errors.Add($"废弃 AbilityId 列表包含非法或重复 ID：{id}。");
                if (allocatedIds.Contains(id))
                    errors.Add($"AbilityId {id} 同时位于有效历史与废弃列表。");
                highestId = Math.Max(highestId, id);
            }

            if (database.NextAbilityId < 0 || database.NextAbilityId <= highestId)
                errors.Add(
                    $"nextAbilityId {database.NextAbilityId} 必须大于全部已分配或废弃 ID（当前最大 {highestId}）。");
            return errors;
        }

        /// <summary>校验当前 Data、ID 历史与运行时索引是否完全一致。</summary>
        /// <param name="database">待检查的 Database。</param>
        /// <returns>错误列表；空列表表示可直接运行。</returns>
        public static List<string> ValidateBakedState(GameplayAbilityDatabase database)
        {
            List<string> errors = ValidateForBake(database);
            if (database == null) return errors;
            if (database.BakeDirty)
                errors.Add("Gameplay Ability 资产集合已变化但尚未 Bake。");

            List<GameplayAbilityData> abilities = FindAllAbilities();
            var current = new HashSet<GameplayAbilityData>();
            var currentIds = new HashSet<int>();
            for (int i = 0; i < abilities.Count; i++)
            {
                GameplayAbilityData ability = abilities[i];
                current.Add(ability);
                string path = AssetDatabase.GetAssetPath(ability);
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (!database.BakedIdHistory.TryGetValue(guid, out int id))
                {
                    errors.Add($"{path}: 资产尚未记录稳定 AbilityId。");
                    continue;
                }

                if (ability.AbilityId != id)
                    errors.Add($"{path}: Data AbilityId={ability.AbilityId}，Database 历史={id}。");
                if (!currentIds.Add(ability.AbilityId))
                    errors.Add($"{path}: AbilityId {ability.AbilityId} 与其他资产重复。");
            }

            if (database.BakedIdHistory.Count != abilities.Count)
                errors.Add(
                    $"Database 有效历史数 {database.BakedIdHistory.Count} 与 Ability 资产数 {abilities.Count} 不一致。");

            IReadOnlyDictionary<int, GameplayAbilityData> runtime = database.BakedAbilitiesById;
            if (runtime == null)
            {
                errors.Add("GameplayAbilityDatabase 运行时索引为 null。");
                return errors;
            }

            foreach (KeyValuePair<int, GameplayAbilityData> pair in runtime)
            {
                if (pair.Value == null)
                    errors.Add($"GameplayAbilityDatabase 的 AbilityId {pair.Key} 指向空资产。");
                else if (pair.Value.AbilityId != pair.Key)
                    errors.Add(
                        $"GameplayAbilityDatabase 的 Key {pair.Key} 与 {pair.Value.name}.AbilityId {pair.Value.AbilityId} 不一致。");
                else if (!current.Contains(pair.Value))
                    errors.Add($"GameplayAbilityDatabase 包含已不在项目中的资产：{pair.Value.name}。");
            }

            if (runtime.Count != current.Count)
                errors.Add(
                    $"GameplayAbilityDatabase 运行时索引数 {runtime.Count} 与项目 Ability 资产数 {current.Count} 不一致。");
            return errors;
        }

        /// <summary>扫描并按资产路径排序项目中的全部 GameplayAbilityData。</summary>
        /// <returns>保存真实资产引用的稳定列表。</returns>
        public static List<GameplayAbilityData> FindAllAbilities()
        {
            string[] guids = AssetDatabase.FindAssets("t:GameplayAbilityData");
            var abilities = new List<GameplayAbilityData>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                GameplayAbilityData ability = AssetDatabase.LoadAssetAtPath<GameplayAbilityData>(path);
                if (ability != null) abilities.Add(ability);
            }

            abilities.Sort((left, right) => string.Compare(
                AssetDatabase.GetAssetPath(left),
                AssetDatabase.GetAssetPath(right),
                StringComparison.Ordinal));
            return abilities;
        }

        #endregion

        #region 嵌套类型

        /// <summary>保存一次 Bake 计算出的 Ability 资产与稳定 ID。</summary>
        private readonly struct AssignedAbility
        {
            /// <summary>获取待写入 ID 的 Ability 资产。</summary>
            internal GameplayAbilityData Ability { get; }

            /// <summary>获取分配给该资产的稳定 ID。</summary>
            internal int Id { get; }

            /// <summary>创建尚未提交的 Bake 分配结果。</summary>
            /// <param name="ability">待写入的 Ability 资产。</param>
            /// <param name="id">分配的稳定 ID。</param>
            internal AssignedAbility(GameplayAbilityData ability, int id)
            {
                Ability = ability;
                Id = id;
            }
        }

        #endregion
    }
}
#endif
