using System;
using UnityEngine;

namespace WS_Modules.GAS.GameplayCue
{
    /// <summary>
    /// 作为数据库注册的共享 Cue Handler 资产基类，不保存角色运行时状态。
    /// </summary>
    public abstract class GameplayCueHandlerSO : ScriptableObject, IGameplayCueHandler
    {
        #region Handler 契约

        /// <summary>获取此 Handler 处理的具体 CueData 类型。</summary>
        public abstract Type CueDataType { get; }

        /// <inheritdoc />
        public abstract void Execute(GameplayCueData data, GameplayCueRequest request, GameplayCueCtrl controller);

        /// <inheritdoc />
        public virtual object Active(GameplayCueData data, GameplayCueRequest request, GameplayCueCtrl controller) => null;

        /// <inheritdoc />
        public virtual void Remove(GameplayCueActiveRecord record)
        {
        }

        /// <inheritdoc />
        public virtual void Clear(GameplayCueActiveRecord record) => Remove(record);

        #endregion
    }
}
