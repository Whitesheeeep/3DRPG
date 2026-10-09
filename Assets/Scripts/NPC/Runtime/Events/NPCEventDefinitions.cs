namespace RPG.NPC
{
    /// <summary>表示一个稳定身份的 NPC 已进入死亡状态。</summary>
    public readonly struct NPCDefeatedEventArgs
    {
        #region 事件数据

        /// <summary>获取被击败 NPC 的稳定身份。</summary>
        public NPCId NPCId { get; }

        #endregion

        #region 构造

        /// <summary>创建 NPC 击败事实事件。</summary>
        /// <param name="npcId">进入死亡状态的 NPC 身份。</param>
        public NPCDefeatedEventArgs(NPCId npcId)
        {
            NPCId = npcId;
        }

        #endregion
    }
}
