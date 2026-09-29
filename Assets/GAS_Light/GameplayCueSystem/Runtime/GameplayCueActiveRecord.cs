namespace WS_Modules.GAS.GameplayCue
{
    /// <summary>
    /// 记录单个 Handler 创建的持续 Cue 状态及其原始请求。
    /// </summary>
    public sealed class GameplayCueActiveRecord
    {
        #region 状态字段

        private bool releasing;

        #endregion

        #region 构造函数与属性

        /// <summary>创建归属于一个 Handler 的 Active 记录。</summary>
        /// <param name="handler">负责后续 Remove/Clear 的原 Handler。</param>
        /// <param name="data">本次 Active 使用的作者配置。</param>
        /// <param name="request">保存来源身份和空间参数的原始请求。</param>
        /// <param name="state">Handler 返回的自有运行时状态对象。</param>
        internal GameplayCueActiveRecord(IGameplayCueHandler handler, GameplayCueData data,
            GameplayCueRequest request, object state, GameplayCueCtrl controller)
        {
            Handler = handler;
            Data = data;
            Request = request;
            State = state;
            Controller = controller;
        }

        /// <summary>获取创建该持续状态的 Handler。</summary>
        public IGameplayCueHandler Handler { get; }
        /// <summary>获取创建该持续状态的 CueData。</summary>
        public GameplayCueData Data { get; }
        /// <summary>获取创建该持续状态的原始请求。</summary>
        public GameplayCueRequest Request { get; }
        /// <summary>获取由 Handler 自行管理的运行时状态。</summary>
        public object State { get; }
        /// <summary>获取登记该状态的目标 Controller。</summary>
        public GameplayCueCtrl Controller { get; }

        #endregion

        #region 释放控制

        /// <summary>尝试取得记录的唯一释放权，阻止同步 Remove 重入。</summary>
        /// <returns>本次调用取得释放权时返回 true。</returns>
        internal bool TryBeginRelease()
        {
            if (releasing) return false;
            releasing = true;
            return true;
        }

        #endregion
    }
}
