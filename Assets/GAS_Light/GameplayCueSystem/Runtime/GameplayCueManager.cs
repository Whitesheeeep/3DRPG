using WS_Modules.Singleton;
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

        // SingletonBase 通过反射创建实例，保持与 GameplayTagManager 一致。
        private GameplayCueManager()
        {
        }

        #region 生命周期
        /// <summary>绑定并构建当前 GameplayCueDatabase 的运行时索引。</summary>
        /// <param name="cueDatabase">项目当前使用的 CueDatabase。</param>
        public void Initialize(GameplayCueDatabase cueDatabase)
        {
            database = cueDatabase;
            database?.BuildRuntimeIndex();
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
        #endregion
    }
}
