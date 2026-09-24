using System;
using System.Collections.Generic;

namespace RPG.Character
{
    /// <summary>保存当前唯一队伍的四个固定角色槽位。</summary>
    public sealed class CharacterParty
    {
        /// <summary>唯一队伍支持的固定槽位数量。</summary>
        public const int SlotCount = 4;

        private readonly CharacterId[] characterIdBySlot;

        /// <summary>创建唯一队伍并校验角色槽位。</summary>
        /// <param name="initialCharacterIds">按队伍顺序提供的角色标识；缺少的槽位为空。</param>
        public CharacterParty(IReadOnlyList<CharacterId> initialCharacterIds)
        {
            characterIdBySlot = new CharacterId[SlotCount];
            if (initialCharacterIds == null) return;
            if (initialCharacterIds.Count > SlotCount)
                throw new ArgumentException($"队伍最多包含 {SlotCount} 个角色。", nameof(initialCharacterIds));

            for (int slotIndex = 0; slotIndex < initialCharacterIds.Count; slotIndex++)
            {
                CharacterId characterId = initialCharacterIds[slotIndex];
                if (!characterId.IsValid) continue;
                if (FindSlot(characterId) >= 0) throw new ArgumentException($"队伍角色重复：{characterId}。", nameof(initialCharacterIds));
                characterIdBySlot[slotIndex] = characterId;
            }
        }

        /// <summary>按槽位读取角色标识；空槽位返回无效标识。</summary>
        /// <param name="slotIndex">零基槽位下标。</param>
        /// <returns>该槽位角色标识。</returns>
        public CharacterId GetCharacterIdAtSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SlotCount) return default;
            return characterIdBySlot[slotIndex];
        }

        /// <summary>查找角色所在队伍槽位。</summary>
        /// <param name="characterId">待查询角色标识。</param>
        /// <returns>零基槽位；不在队伍时返回负数。</returns>
        public int FindSlot(CharacterId characterId)
        {
            if (!characterId.IsValid) return -1;
            for (int slotIndex = 0; slotIndex < SlotCount; slotIndex++)
                if (characterIdBySlot[slotIndex] == characterId) return slotIndex;
            return -1;
        }

        /// <summary>复制当前队伍槽位，避免外部修改内部数组。</summary>
        /// <returns>四个固定槽位的只读快照。</returns>
        public IReadOnlyList<CharacterId> CreateSnapshot()
        {
            return Array.AsReadOnly((CharacterId[])characterIdBySlot.Clone());
        }
    }
}
