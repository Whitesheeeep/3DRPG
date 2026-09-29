using UnityEngine;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayCue
{
    /// <summary>
    /// 保存各种 Gameplay Cue 共用的精确匹配标签。
    /// </summary>
    public abstract class GameplayCueData : ScriptableObject
    {
        #region 配置字段

        [SerializeField, Tooltip("用于 CueDatabase 精确查找的稳定 GameplayTag。")]
        private GameplayTag cueTag;

        #endregion

        #region 属性

        /// <summary>获取当前 Cue 配置使用的精确匹配标签。</summary>
        public GameplayTag CueTag => cueTag;

        #endregion

#if UNITY_EDITOR
        #region 编辑器校验

        /// <summary>在资产修改时检查公共 CueTag，避免将无效标签留到运行时。</summary>
        protected virtual void OnValidate()
        {
            if (!cueTag.IsValid)
                Debug.LogError($"GameplayCueData '{name}' 的 CueTag 无效。", this);
        }

        #endregion
#endif
    }
}
