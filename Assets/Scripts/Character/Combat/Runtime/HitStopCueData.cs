using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.GameplayCue;

namespace RPG.Character.Combat
{
    /// <summary>
    /// 配置命中后对攻击双方施加的局部卡帧时长。
    /// </summary>
    [CreateAssetMenu(fileName = "HitStopCueData", menuName = "RPG/Gameplay Cue/Hit Stop")]
    public sealed class HitStopCueData : GameplayCueData
    {
        #region 配置字段

        [SerializeField, MinValue(1), Tooltip("角色动作暂停时长，单位为毫秒。")]
        private int durationMilliseconds = 80;

        #endregion

        #region 属性

        /// <summary>获取卡帧时长，单位为毫秒。</summary>
        public int DurationMilliseconds => durationMilliseconds;

        #endregion

#if UNITY_EDITOR
        #region 编辑器校验

        /// <summary>检查作者输入的卡帧时长为正数。</summary>
        protected override void OnValidate()
        {
            base.OnValidate();
            if (durationMilliseconds < 1)
                Debug.LogError($"HitStopCueData '{name}' 的时长必须至少为 1 毫秒。", this);
        }

        #endregion
#endif
    }
}
