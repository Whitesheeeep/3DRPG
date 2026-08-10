using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayCue
{
    /// <summary>单次 Cue 表现的运行时句柄，负责幂等释放对象池实例。</summary>
    public sealed class GameplayCueRuntime
    {
        #region 字段
        private readonly GameplayCueCtrl owner;
        private bool released;
        private bool releasing;
        #endregion

        #region 构造函数
        /// <summary>创建 Cue 运行时句柄。</summary>
        internal GameplayCueRuntime(
            GameplayCueCtrl owner,
            GameplayCueData data,
            GameplayCueRequest request,
            GameObject cueObject,
            GameplayCueBehaviour behaviour)
        {
            this.owner = owner;
            CueData = data;
            CueTag = request.CueTag;
            Source = request.Source;
            Target = request.Target;
            EffectRuntime = request.EffectRuntime;
            AbilityRuntime = request.AbilityRuntime;
            Position = request.Position;
            Rotation = request.Rotation;
            AttachTransform = request.AttachTransform;
            CueObject = cueObject;
            Behaviour = behaviour;
            EventType = request.EventType;
        }
        #endregion

        #region 属性
        /// <summary>对应的作者 CueData。</summary>
        public GameplayCueData CueData { get; }
        /// <summary>本次表现的 CueTag。</summary>
        public GameplayTag CueTag { get; }
        /// <summary>表现来源 ASC。</summary>
        public GameplayAbilitySystemComponent Source { get; }
        /// <summary>表现目标 ASC。</summary>
        public GameplayAbilitySystemComponent Target { get; }
        /// <summary>对应的 GE Runtime。</summary>
        public GameEffectRuntime EffectRuntime { get; }
        /// <summary>对应的 GA Runtime。</summary>
        public GameplayAbilityRuntime AbilityRuntime { get; }
        /// <summary>对象池取出的表现对象。</summary>
        public GameObject CueObject { get; }
        /// <summary>对象上的表现行为。</summary>
        public GameplayCueBehaviour Behaviour { get; }
        /// <summary>请求阶段。</summary>
        public GameplayCueEventType EventType { get; }

        // 以下是通过代码直接请求的位置与旋转
        /// <summary>本次请求的世界位置。</summary>
        public Vector3 Position { get; }
        /// <summary>本次请求的世界旋转。</summary>
        public Quaternion Rotation { get; }
        /// <summary>本次请求的动态挂点。</summary>
        public Transform AttachTransform { get; }
        /// <summary>是否仍由 CueController 管理。</summary>
        public bool IsActive { get; internal set; }
        /// <summary>是否已经归还对象池。</summary>
        public bool IsReleased => released;
        #endregion

        #region 生命周期
        /// <summary>归还表现对象；重复调用不会重复回收。</summary>
        public void Release()
        {
            if (released) return;
            // 表现脚本主动结束只请求回收；持续 Cue 的 OnRemove 由 Controller 移除入口负责发送。
            owner.ReleaseRuntime(this, false);
        }

        /// <summary>
        /// 在对象池回收和表现重置完成后标记 Runtime，阻止后续重复释放。
        /// </summary>
        internal void MarkReleased()
        {
            released = true;
            releasing = false;
            IsActive = false;
        }

        /// <summary>
        /// 尝试取得释放事务的唯一执行权，防止同步回收回调再次进入 Release。
        /// </summary>
        internal bool TryBeginRelease()
        {
            if (released || releasing) return false;
            releasing = true;
            return true;
        }
        #endregion
    }
}
