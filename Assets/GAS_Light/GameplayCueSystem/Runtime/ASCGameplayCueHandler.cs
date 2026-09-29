using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayCue
{
    /// <summary>
    /// 保存于单个 ASC 的托管引用 Handler 基类，按配置的 CueTag 精确响应请求。
    /// </summary>
    [Serializable]
    public abstract class ASCGameplayCueHandler : IGameplayCueHandler
    {
        #region 配置字段

        [SerializeField, Tooltip("此 ASC Handler 精确关注的 CueTag 列表。")]
        private List<GameplayTag> cueTags = new();

        #endregion

        #region 依赖字段

        // 该引用只在所属 ASC 成功初始化期间有效，解绑时清空以支持 Clear 后重新初始化。
        [NonSerialized] private GameplayAbilitySystemComponent owner;

        #endregion

        #region 属性与绑定

        /// <summary>获取当前 Handler 绑定的 ASC。</summary>
        protected GameplayAbilitySystemComponent Owner => owner;

        /// <summary>判断 Handler 是否使用精确 CueTag 列表关注指定标签。</summary>
        /// <param name="cueTag">本次请求的 CueTag。</param>
        /// <returns>配置中存在相同有效标签时返回 true。</returns>
        public bool HandlesTag(GameplayTag cueTag)
        {
            for (int index = 0; index < cueTags.Count; index++)
                if (cueTags[index].IsValid && cueTags[index] == cueTag)
                    return true;
            return false;
        }

        /// <summary>在 ASC 成功完成核心初始化后绑定其私有 Handler。</summary>
        /// <param name="asc">当前 Handler 唯一所属的 ASC。</param>
        public void Bind(GameplayAbilitySystemComponent asc)
        {
            if (owner != null && !ReferenceEquals(owner, asc))
                throw new InvalidOperationException("ASC GameplayCue Handler 已绑定到其他 ASC。");
            owner = asc ?? throw new ArgumentNullException(nameof(asc));
            OnBind();
        }

        /// <summary>清理綁定期间的 Handler 私有状态并解除 ASC 引用。</summary>
        public void Unbind()
        {
            if (owner == null) return;
            OnUnbind();
            owner = null;
        }

        /// <summary>ASC 绑定后由具体 Handler 初始化自身依赖。</summary>
        protected virtual void OnBind()
        {
        }

        /// <summary>ASC 清理前由具体 Handler 释放自身依赖。</summary>
        protected virtual void OnUnbind()
        {
        }

        #endregion

        #region Cue 生命周期

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
