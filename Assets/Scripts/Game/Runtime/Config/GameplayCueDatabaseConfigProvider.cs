using System;
using UnityEngine;
using WS_Modules.ConfigInstaller;
using WS_Modules.GAS.GameplayCue;
using WS_Modules.LogModule;

namespace RPG.Game.Config
{
    /// <summary>
    /// 在框架配置树注册阶段初始化全局 GameplayCueManager 数据库索引。
    /// </summary>
    [CreateAssetMenu(fileName = "GameplayCueDatabaseConfigProvider", menuName = "RPG/Config/Gameplay Cue Database")]
    public sealed class GameplayCueDatabaseConfigProvider : ConfigRegisterNodeBase
    {
        #region 配置字段

        [SerializeField, Tooltip("当前项目战斗使用的全局 CueData 与共享 Handler 数据库。")]
        private GameplayCueDatabase database;

        #endregion

        #region 注册

        /// <summary>建立 CueTag 与 Handler 类型索引；此时只注册配置，不触发任何表现。</summary>
        public override void Register()
        {
            if (database == null)
                throw new InvalidOperationException("[GameplayCueDatabaseConfigProvider] 没有配置 GameplayCueDatabase。");

            GameplayCueManager.Instance.Initialize(database);
            WSLog.Log(
                $"[GameplayCueDatabaseConfigProvider] 已注册数据库 '{database.name}'，Cue={database.Count}，Handler={database.Handlers.Count}。");
        }

        #endregion
    }
}
