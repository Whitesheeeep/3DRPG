using WS_Modules.Singleton;
using System;
using System.Collections.Generic;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayCue
{
    /// <summary>管理当前项目的 GameplayCueDatabase，并提供运行时 CueTag 查表入口。</summary>
    public sealed class GameplayCueManager : SingletonBase<GameplayCueManager>
    {
        #region 字段与属性
        private GameplayCueDatabase database;
        /// <summary>获取当前 CueDatabase 是否已经初始化。</summary>
        public bool IsInitialized => database != null;
        /// <summary>获取当前使用的 CueDatabase。</summary>
        public GameplayCueDatabase Database => database;
        #endregion

        /// <summary>构造空 Manager；SingletonBase 负责通过反射创建唯一实例。</summary>
        private GameplayCueManager()
        {
        }

        #region 生命周期
        /// <summary>绑定并构建当前 GameplayCueDatabase 的运行时索引。</summary>
        /// <param name="cueDatabase">项目当前使用的 CueDatabase。</param>
        public void Initialize(GameplayCueDatabase cueDatabase)
        {
            if (cueDatabase == null) throw new ArgumentNullException(nameof(cueDatabase));
            database = cueDatabase;
            int registeredCueCount = database.BuildRuntimeIndex();
            WS_Modules.LogModule.WSLog.Log(
                $"[GameplayCueManager] 已注册 CueDatabase '{database.name}'，Cue={registeredCueCount}，Handler={database.Handlers.Count}。");
        }

        /// <summary>清除当前数据库引用，供测试隔离和退出流程使用。</summary>
        public void Reset() => database = null;
        #endregion

        #region 查询
        /// <summary>尝试按 CueTag 查询具体 CueData。</summary>
        /// <param name="cueTag">待查询的 CueTag。</param>
        /// <param name="cue">查询到的 CueData。</param>
        /// <returns>数据库已初始化且存在对应 Cue 时返回 true。</returns>
        public bool TryGetCue(GameplayTag cueTag, out GameplayCueData cue)
        {
            if (database != null && database.TryGetCue(cueTag, out cue)) return true;
            cue = null;
            return false;
        }

        /// <summary>尝试按 CueData 的具体类型获取数据库中的共享 Handler。</summary>
        /// <param name="dataType">CueData 的具体运行时类型。</param>
        /// <param name="handler">找到的共享 Handler SO。</param>
        /// <returns>共享 Handler 已登记时返回 true。</returns>
        public bool TryGetHandler(Type dataType, out GameplayCueHandlerSO handler)
        {
            if (database != null && database.TryGetHandler(dataType, out handler)) return true;
            handler = null;
            return false;
        }

        /// <summary>获取当前数据库登记的 CueData 列表，用于测试和运行时诊断。</summary>
        public IReadOnlyList<GameplayCueData> Cues => database?.Cues;
        #endregion
    }
}
