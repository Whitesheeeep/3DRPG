using System;
using System.Collections.Generic;
using UnityEngine;

namespace RPG.Character
{
    /// <summary>集中保存 CharacterConfig 并按稳定 CharacterId 建立运行时索引。</summary>
    [CreateAssetMenu(fileName = "CharacterDatabase", menuName = "RPG/Character/Character Database")]
    public sealed class CharacterDatabase : ScriptableObject
    {
        #region 字段与属性

        [SerializeField] private List<CharacterConfig> characters = new();
        // 角色编号只增不减，避免删除配置后重新占用已经发布或存档使用过的 ID。
        [SerializeField, HideInInspector] private int nextCharacterIdNumber = 1;
        // key：稳定 CharacterId；value：该角色对应的唯一 CharacterConfig。
        private Dictionary<CharacterId, CharacterConfig> configByCharacterIdMap;

        /// <summary>获取数据库中的角色配置，保持作者顺序。</summary>
        public IReadOnlyList<CharacterConfig> Characters => characters;

        #endregion

        #region 查询与校验

        /// <summary>验证全部角色配置并建立查询索引。</summary>
        /// <exception cref="InvalidOperationException">存在空、重复或非法角色配置时抛出。</exception>
        public void ValidateAndBuildIndex()
        {
            characters ??= new List<CharacterConfig>();
            var index = new Dictionary<CharacterId, CharacterConfig>();
            var configSet = new HashSet<CharacterConfig>();
            for (int position = 0; position < characters.Count; position++)
            {
                CharacterConfig config = characters[position];
                if (config == null) throw new InvalidOperationException($"CharacterDatabase '{name}' 的第 {position} 项为空。");
                if (!configSet.Add(config)) throw new InvalidOperationException($"CharacterDatabase '{name}' 重复引用 CharacterConfig '{config.name}'。");
                config.Validate();
                ValidateStableId(config);
                if (!index.TryAdd(config.CharacterId, config))
                    throw new InvalidOperationException($"CharacterDatabase '{name}' 包含重复 CharacterId：{config.CharacterId}。");
            }

            ValidateCounter();

            configByCharacterIdMap = index;
        }

        /// <summary>按 CharacterId 尝试查询角色配置。</summary>
        /// <param name="characterId">稳定角色标识。</param>
        /// <param name="config">找到的角色配置。</param>
        /// <returns>找到时返回 true。</returns>
        public bool TryGetConfig(CharacterId characterId, out CharacterConfig config)
        {
            EnsureIndex();
            return configByCharacterIdMap.TryGetValue(characterId, out config);
        }

        /// <summary>取得指定 CharacterId 的必需配置。</summary>
        /// <param name="characterId">稳定角色标识。</param>
        /// <returns>对应角色配置。</returns>
        public CharacterConfig GetRequiredConfig(CharacterId characterId)
        {
            if (TryGetConfig(characterId, out CharacterConfig config)) return config;
            throw new InvalidOperationException($"CharacterDatabase '{name}' 未配置 CharacterId '{characterId}'。");
        }

        /// <summary>确保查询前已经建立数据库索引。</summary>
        private void EnsureIndex()
        {
            if (configByCharacterIdMap == null) ValidateAndBuildIndex();
        }

        /// <summary>验证角色 ID 使用固定的 character_#### 格式。</summary>
        /// <param name="config">待验证角色配置。</param>
        private static void ValidateStableId(CharacterConfig config)
        {
            string id = config.CharacterId.ToString();
            if (!TryParseStableId(id, out _))
                throw new InvalidOperationException($"CharacterConfig '{config.name}' 的 CharacterId '{id}' 必须符合 character_0001 格式。");
        }

        /// <summary>验证数据库中的下一个角色编号大于所有已分配编号。</summary>
        private void ValidateCounter()
        {
            if (nextCharacterIdNumber < 1)
                throw new InvalidOperationException($"CharacterDatabase '{name}' 的下一个角色 ID 编号必须大于零。");
            for (int index = 0; index < characters.Count; index++)
            {
                CharacterConfig config = characters[index];
                if (config == null || !TryParseStableId(config.CharacterId.ToString(), out int number)) continue;
                if (number >= nextCharacterIdNumber)
                    throw new InvalidOperationException($"CharacterDatabase '{name}' 的角色 ID 计数器 {nextCharacterIdNumber} 不得小于等于现有编号 {number}。");
            }
        }

        /// <summary>解析固定四位数字角色 ID。</summary>
        /// <param name="id">待解析 ID。</param>
        /// <param name="number">解析出的编号。</param>
        /// <returns>格式正确时返回 true。</returns>
        private static bool TryParseStableId(string id, out int number)
        {
            number = 0;
            const string prefix = "character_";
            if (string.IsNullOrEmpty(id) || !id.StartsWith(prefix, StringComparison.Ordinal)) return false;
            string suffix = id.Substring(prefix.Length);
            if (suffix.Length != 4) return false;
            // 运行时校验与 Editor 分配器保持相同的 ASCII 格式契约。
            for (int index = 0; index < suffix.Length; index++)
                if (suffix[index] < '0' || suffix[index] > '9') return false;
            if (!int.TryParse(suffix, out number)) return false;
            return number >= 1 && number <= 9999;
        }

        #endregion
    }
}
