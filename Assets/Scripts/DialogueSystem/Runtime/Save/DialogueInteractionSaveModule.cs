using System;
using System.Collections.Generic;
using RPG.NPC;
using RPG.SaveSystem;

namespace RPG.DialogueSystemModule
{
    #region 快照数据

    // 强类型快照：只包含可序列化的 NPC 与 Option 稳定身份。
    /// <summary>保存已正常完成的一次性对话选项身份。</summary>
    [Serializable]
    public sealed class DialogueInteractionSaveSnapshot : ISaveModuleSnapshot
    {
        /// <summary>按 NPC 与 Option 身份保存的完成记录。</summary>
        public List<DialogueInteractionCompletionRecord> CompletedOptions { get; set; } =
            new List<DialogueInteractionCompletionRecord>();

        /// <summary>验证集合完整性、身份格式和重复项。</summary>
        /// <exception cref="InvalidOperationException">快照存在缺失、非法或重复记录时抛出。</exception>
        public void ValidateShape()
        {
            if (CompletedOptions == null)
                throw new InvalidOperationException("对话交互存档的完成记录列表不能为 null。");

            // 记录按 NPC 分组检测，避免用分隔符拼接两个任意长度身份造成键碰撞。
            var optionIdsByNpcIdMap = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (DialogueInteractionCompletionRecord record in CompletedOptions)
            {
                if (record == null)
                    throw new InvalidOperationException("对话交互存档包含空完成记录。");

                _ = new NPCId(record.NpcId);
                if (!Guid.TryParseExact(record.OptionId, "N", out _))
                    throw new InvalidOperationException($"对话交互存档包含非法 OptionId：{record.OptionId}。");

                if (!optionIdsByNpcIdMap.TryGetValue(record.NpcId, out HashSet<string> optionIds))
                {
                    optionIds = new HashSet<string>(StringComparer.Ordinal);
                    optionIdsByNpcIdMap.Add(record.NpcId, optionIds);
                }

                if (!optionIds.Add(record.OptionId))
                    throw new InvalidOperationException(
                        $"对话交互存档存在重复完成记录，npcId={record.NpcId}, optionId={record.OptionId}。");
            }
        }
    }

    // 单条事实记录：不持有场景组件或运行时会话引用。
    /// <summary>表示存档中一条 NPC 与 Toggle 选项的完成事实。</summary>
    [Serializable]
    public sealed class DialogueInteractionCompletionRecord
    {
        /// <summary>创建 JSON 序列化使用的空记录。</summary>
        public DialogueInteractionCompletionRecord()
        {
        }

        /// <summary>创建一条明确绑定 NPC 和选项身份的完成记录。</summary>
        /// <param name="npcId">稳定 NPC 标识。</param>
        /// <param name="optionId">稳定选项 GUID。</param>
        public DialogueInteractionCompletionRecord(string npcId, string optionId)
        {
            NpcId = npcId;
            OptionId = optionId;
        }

        /// <summary>稳定 NPC 标识字符串。</summary>
        public string NpcId { get; set; } = string.Empty;

        /// <summary>稳定选项 GUID 字符串。</summary>
        public string OptionId { get; set; } = string.Empty;
    }

    #endregion

    /// <summary>将 DialogueInteractionManager 的完成状态接入 SaveManager。</summary>
    public sealed class DialogueInteractionSaveModule : SaveModule<DialogueInteractionSaveSnapshot>
    {
        #region 模块身份

        /// <summary>对话交互存档模块的稳定 ID。</summary>
        public static readonly SaveModuleId StableModuleId = new SaveModuleId("dialogue-interaction");

        #endregion

        #region 依赖字段

        // 依赖字段：管理器持有运行态集合；该适配器只负责 DTO 转换和恢复约束。
        private readonly DialogueInteractionManager dialogueInteractionManager;

        #endregion

        #region 构造与快照操作

        /// <summary>创建对话交互状态存档模块。</summary>
        /// <param name="manager">状态拥有者。</param>
        /// <exception cref="ArgumentNullException">状态管理器为空时抛出。</exception>
        public DialogueInteractionSaveModule(DialogueInteractionManager manager)
            : base(StableModuleId, 1, SaveMissingModulePolicy.CreateDefault)
        {
            dialogueInteractionManager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        /// <summary>从状态管理器采集完成记录。</summary>
        /// <returns>确定排序的对话交互快照。</returns>
        protected override DialogueInteractionSaveSnapshot CaptureTypedSnapshot() =>
            dialogueInteractionManager.CaptureSnapshot();

        /// <summary>为旧存档缺少此模块时创建空完成记录。</summary>
        /// <returns>没有 Toggle 已完成记录的默认快照。</returns>
        protected override DialogueInteractionSaveSnapshot CreateDefaultTypedSnapshot() =>
            new DialogueInteractionSaveSnapshot();

        /// <summary>验证快照结构，并确认恢复时没有活动 Toggle 会话。</summary>
        /// <param name="snapshot">待恢复快照。</param>
        protected override void ValidateTypedSnapshot(DialogueInteractionSaveSnapshot snapshot)
        {
            snapshot.ValidateShape();
            dialogueInteractionManager.ValidateRestore();
        }

        /// <summary>整体恢复完成记录，不重播对话 Action 或事件。</summary>
        /// <param name="snapshot">已通过校验的快照。</param>
        protected override void RestoreTypedSnapshot(DialogueInteractionSaveSnapshot snapshot) =>
            dialogueInteractionManager.RestoreSnapshot(snapshot);

        #endregion
    }
}
