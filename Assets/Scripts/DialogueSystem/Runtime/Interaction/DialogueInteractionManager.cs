using System;
using System.Collections.Generic;
using RPG.NPC;
using RPG.SaveSystem;
using WS_Modules.BusinessArchitecture;

namespace RPG.DialogueSystemModule
{
    /// <summary>持有已完成的一次性对话事实，并将其适配到统一存档流程。</summary>
    public sealed class DialogueInteractionManager : AbstractManager
    {
        #region 状态

        // key：稳定 NPCId；value：该 NPC 已完成的 OptionId 集合。
        private readonly Dictionary<string, HashSet<string>> completedOptionIdsByNpcIdMap =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        // key：当前未结束的 DialogueSession；value：该会话结束时要提交的 NPC 与选项身份。
        private readonly Dictionary<DialogueSession, DialogueInteractionCompletionRecord>
            completionRecordBySessionMap = new Dictionary<DialogueSession, DialogueInteractionCompletionRecord>();

        #endregion

        #region 依赖字段

        // 依赖字段：模块在 Manager 初始化阶段注册到游戏统一存档编排器。
        private readonly SaveManager saveManager;

        #endregion

        #region 构造与生命周期

        /// <summary>创建对话交互状态管理器。</summary>
        /// <param name="saveManager">负责统一存档操作的 Manager。</param>
        /// <exception cref="ArgumentNullException">存档 Manager 为空时抛出。</exception>
        public DialogueInteractionManager(SaveManager saveManager)
        {
            this.saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
        }

        /// <summary>初始化状态并将对话交互快照模块注册到 SaveManager。</summary>
        protected override void OnInit()
        {
            saveManager.RegisterModule(new DialogueInteractionSaveModule(this));
            UnityEngine.Debug.Log("[DialogueInteractionManager] 已注册 Toggle 对话完成状态存档模块。");
        }

        /// <summary>架构注销时解除所有未完成会话订阅，不把中断对话记录为完成。</summary>
        protected override void OnDeinit()
        {
            foreach (DialogueSession session in completionRecordBySessionMap.Keys)
                session.Ended -= OnTrackedSessionEnded;

            int detachedCount = completionRecordBySessionMap.Count;
            completionRecordBySessionMap.Clear();
            completedOptionIdsByNpcIdMap.Clear();
            UnityEngine.Debug.Log($"[DialogueInteractionManager] 架构注销，已解除 Toggle 会话订阅，count={detachedCount}。");
        }

        #endregion

        #region 查询与会话跟踪

        /// <summary>查询指定 NPC 的指定对话选项是否已正常完成。</summary>
        /// <param name="npcId">场景 NPC 的稳定标识。</param>
        /// <param name="optionId">配置项持有的稳定标识。</param>
        /// <returns>此前已到达正常结束节点时返回 true。</returns>
        public bool IsCompleted(NPCId npcId, string optionId)
        {
            return npcId.IsValid &&
                   completedOptionIdsByNpcIdMap.TryGetValue(npcId.Value, out HashSet<string> optionIds) &&
                   optionIds.Contains(optionId);
        }

        /// <summary>跟踪一次性选项对应的会话，并在其正常结束时提交完成事实。</summary>
        /// <param name="npcId">选项所属 NPC 的稳定标识。</param>
        /// <param name="optionId">配置项持有的稳定标识。</param>
        /// <param name="session">已经成功启动的对话会话。</param>
        /// <exception cref="ArgumentException">NPC 或选项身份无效时抛出。</exception>
        /// <exception cref="InvalidOperationException">会话已结束或已被重复跟踪时抛出。</exception>
        public void TrackToggleSession(NPCId npcId, string optionId, DialogueSession session)
        {
            if (!npcId.IsValid)
                throw new ArgumentException("Toggle 对话必须具有有效 NPCId。", nameof(npcId));
            if (!Guid.TryParseExact(optionId, "N", out _))
                throw new ArgumentException("Toggle 对话必须具有有效 OptionId。", nameof(optionId));
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (session.IsEnded || completionRecordBySessionMap.ContainsKey(session))
                throw new InvalidOperationException($"Toggle 对话会话状态无效或已被跟踪，sessionId={session.SessionId}。");

            var completionRecord = new DialogueInteractionCompletionRecord(npcId.Value, optionId);
            completionRecordBySessionMap.Add(session, completionRecord);
            session.Ended += OnTrackedSessionEnded;
            UnityEngine.Debug.Log(
                $"[DialogueInteractionManager] 开始跟踪 Toggle 对话，npcId={npcId}, optionId={optionId}, sessionId={session.SessionId}。");
        }

        #endregion

        #region 快照转换

        /// <summary>采集并按 NPCId、OptionId 排序的一次性对话完成快照。</summary>
        /// <returns>与场景对象和会话解耦的序列化快照。</returns>
        internal DialogueInteractionSaveSnapshot CaptureSnapshot()
        {
            var snapshot = new DialogueInteractionSaveSnapshot();
            foreach (KeyValuePair<string, HashSet<string>> npcEntry in completedOptionIdsByNpcIdMap)
            {
                foreach (string optionId in npcEntry.Value)
                    snapshot.CompletedOptions.Add(new DialogueInteractionCompletionRecord(npcEntry.Key, optionId));
            }

            snapshot.CompletedOptions.Sort(CompareCompletionRecords);
            return snapshot;
        }

        /// <summary>校验恢复时没有活动 Toggle 会话，避免旧会话结束后覆盖新槽位状态。</summary>
        /// <exception cref="InvalidOperationException">当前仍跟踪活动 Toggle 会话时抛出。</exception>
        internal void ValidateRestore()
        {
            if (completionRecordBySessionMap.Count > 0)
                throw new InvalidOperationException(
                    $"当前有 {completionRecordBySessionMap.Count} 个 Toggle 对话会话正在运行，不能恢复对话交互存档。");
        }

        /// <summary>以快照内容整体替换当前完成记录，不触发对话或业务事件。</summary>
        /// <param name="snapshot">已通过形状校验的快照。</param>
        internal void RestoreSnapshot(DialogueInteractionSaveSnapshot snapshot)
        {
            completedOptionIdsByNpcIdMap.Clear();
            foreach (DialogueInteractionCompletionRecord record in snapshot.CompletedOptions)
            {
                if (!completedOptionIdsByNpcIdMap.TryGetValue(record.NpcId, out HashSet<string> optionIds))
                {
                    optionIds = new HashSet<string>(StringComparer.Ordinal);
                    completedOptionIdsByNpcIdMap.Add(record.NpcId, optionIds);
                }

                optionIds.Add(record.OptionId);
            }

            UnityEngine.Debug.Log(
                $"[DialogueInteractionManager] 已整体恢复 Toggle 对话完成状态，optionCount={snapshot.CompletedOptions.Count}。");
        }

        #endregion

        #region 会话结束处理

        /// <summary>解除会话订阅，并只将正常结束的 Toggle 标记为完成。</summary>
        /// <param name="eventArgs">已结束的对话会话事实。</param>
        private void OnTrackedSessionEnded(DialogueEndedEvent eventArgs)
        {
            DialogueSession session = eventArgs.Session;
            session.Ended -= OnTrackedSessionEnded;
            if (!completionRecordBySessionMap.Remove(session, out DialogueInteractionCompletionRecord record))
                return;

            if (session.EndStatus == DialogueEndStatus.Completed)
            {
                AddCompleted(record.NpcId, record.OptionId);
                UnityEngine.Debug.Log(
                    $"[DialogueInteractionManager] Toggle 对话正常完成，npcId={record.NpcId}, optionId={record.OptionId}。");
                return;
            }

            UnityEngine.Debug.Log(
                $"[DialogueInteractionManager] Toggle 对话失败，保留重试机会，npcId={record.NpcId}, optionId={record.OptionId}。");
        }

        /// <summary>幂等地登记一个 NPC 对话选项的完成记录。</summary>
        /// <param name="npcId">稳定 NPC 标识。</param>
        /// <param name="optionId">稳定交互选项标识。</param>
        private void AddCompleted(string npcId, string optionId)
        {
            if (!completedOptionIdsByNpcIdMap.TryGetValue(npcId, out HashSet<string> optionIds))
            {
                optionIds = new HashSet<string>(StringComparer.Ordinal);
                completedOptionIdsByNpcIdMap.Add(npcId, optionIds);
            }

            optionIds.Add(optionId);
        }

        /// <summary>按 Ordinal 顺序比较快照记录，保证同一状态生成确定性序列。</summary>
        /// <param name="left">左侧完成记录。</param>
        /// <param name="right">右侧完成记录。</param>
        /// <returns>NPCId 优先、OptionId 次之的比较结果。</returns>
        private static int CompareCompletionRecords(
            DialogueInteractionCompletionRecord left,
            DialogueInteractionCompletionRecord right)
        {
            int npcComparison = string.Compare(left.NpcId, right.NpcId, StringComparison.Ordinal);
            return npcComparison != 0
                ? npcComparison
                : string.Compare(left.OptionId, right.OptionId, StringComparison.Ordinal);
        }

        #endregion
    }
}
