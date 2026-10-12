namespace RPG.Combat
{
    /// <summary>接收已完成 GE 结算的 Gameplay 伤害结果并驱动所属玩法反馈。</summary>
    public interface ICombatDamageReceiver
    {
        /// <summary>根据本次伤害结果执行受击、跳字等 Gameplay 反馈。</summary>
        /// <param name="result">由伤害管线组合的 GE 输出与可选命中快照。</param>
        void ReceiveDamage(CombatDamageResult result);
    }
}
