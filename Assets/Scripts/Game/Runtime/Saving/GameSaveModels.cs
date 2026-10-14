using System;
using System.Collections.Generic;
using RPG.SaveSystem;

namespace RPG.Game.Saving
{
    /// <summary>描述一次游戏会话的可观察阶段。</summary>
    public enum E_GameSaveSessionState
    {
        /// <summary>位于开始界面，没有活动游戏。</summary>
        Title,
        /// <summary>正在恢复存档并准备进入场景。</summary>
        Starting,
        /// <summary>活动档对应的游戏已经就绪。</summary>
        Playing,
        /// <summary>正在保存并返回主界面。</summary>
        Returning,
        /// <summary>恢复或清理失败，需要重启会话。</summary>
        Failed
    }

    /// <summary>RPG 专属摘要，与框架 SaveSlotSummary 组合。</summary>
    [Serializable]
    public sealed class GameSaveSummary : ISaveBusinessSummary
    {
        /// <summary>按队伍槽位顺序保存四项，空项使用空角色 ID。</summary>
        public List<GameSavePartyMemberSummary> PartyMembers { get; set; } = new List<GameSavePartyMemberSummary>();

        /// <summary>校验摘要形状，资源是否存在由展示层单独处理。</summary>
        public void ValidateShape()
        {
            if (PartyMembers == null || PartyMembers.Count != 4 || PartyMembers.Exists(member => member == null))
                throw new InvalidOperationException("[GameSaveSystem] 队伍摘要必须包含四个非空槽位条目。");
        }
    }

    /// <summary>保存角色身份与保存时名字；头像从当前配置解析。</summary>
    [Serializable]
    public sealed class GameSavePartyMemberSummary
    {
        /// <summary>稳定角色 ID，空文本表示队伍空槽。</summary>
        public string CharacterId { get; set; } = string.Empty;
        /// <summary>保存时的展示名称。</summary>
        public string CharacterName { get; set; } = string.Empty;
    }

    /// <summary>组合一个固定卡位的身份和可选文件详情。</summary>
    public sealed class GameSaveSlot
    {
        /// <summary>创建固定卡位。</summary>
        /// <param name="index">零基卡位索引。</param>
        /// <param name="slotId">持久槽位 ID。</param>
        /// <param name="details">文件详情，没有文件时为空。</param>
        public GameSaveSlot(int index, SaveSlotId slotId, SaveSlotDetails details)
        {
            Index = index;
            SlotId = slotId;
            Details = details;
        }
        /// <summary>零基卡位索引。</summary>
        public int Index { get; }
        /// <summary>持久槽位 ID。</summary>
        public SaveSlotId SlotId { get; }
        /// <summary>现有文件详情，空值代表新游戏卡。</summary>
        public SaveSlotDetails Details { get; }
        /// <summary>是否不存在正式文件。</summary>
        public bool IsEmpty => Details == null;
        /// <summary>是否允许进入；完整模块校验仍在正式读档执行。</summary>
        public bool CanStart => IsEmpty || Details.SummaryResult.IsSuccess && Details.SummaryResult.Value.BusinessSummary is GameSaveSummary;
    }
}
