using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.TAG;

namespace RPG.Character
{
    /// <summary>仲裁 GAS 与 Locomotion 的控制请求，并通过共享 CharacterController 移动 CharacterRoot。</summary>
    [Serializable]
    public sealed class MotionDriver : IMotionDriver
    {
        #region 配置与状态

        [SerializeField, Tooltip("拥有任意配置 Tag 或其子 Tag 时清零 X/Z 位移，但保留 Y 位移与根旋转。")]
        private GameplayTag[] horizontalMovementBlockedTags = Array.Empty<GameplayTag>();
        [SerializeField, Tooltip("拥有任意配置 Tag 或其子 Tag 时阻止全部位移与 Animator 根旋转。")]
        private GameplayTag[] allMovementBlockedTags = Array.Empty<GameplayTag>();

        [NonSerialized] private readonly Dictionary<int, ControlEntry> controls = new();
        [NonSerialized] private readonly List<FixedSubmission> fixedSubmissions = new();

        // 依赖
        [NonSerialized] private CharacterController characterController;
        [NonSerialized] private IGameplayAbilitySystemOwner activeOwner;
        [NonSerialized] private GameplayAbilitySystemComponent activeAbilitySystem;

        // 运行时状态
        [NonSerialized] private int nextControlId;
        [NonSerialized] private long nextSequence;
        [NonSerialized] private bool suspended;

        #endregion

        #region 查询与初始化

        /// <inheritdoc />
        public bool CanMoveHorizontally => !HasAnyTag(allMovementBlockedTags) &&
                                           !HasAnyTag(horizontalMovementBlockedTags);
        /// <inheritdoc />
        public bool IsGrounded => characterController.isGrounded;

        /// <summary>注入 CharacterRoot 上唯一负责最终移动的 CharacterController。</summary>
        /// <param name="sourceCharacterController">共享 CharacterController。</param>
        public void Initialize(CharacterController sourceCharacterController)
        {
            characterController = sourceCharacterController ??
                throw new ArgumentNullException(nameof(sourceCharacterController));
        }

        /// <summary>切换唯一允许参与运动仲裁的 Character Owner 与 Tag 来源。</summary>
        /// <param name="owner">新的 ActiveCharacter Owner。</param>
        /// <param name="abilitySystem">新角色 ASC，用于最终 Tag 限制。</param>
        internal void SetActiveOwner(IGameplayAbilitySystemOwner owner,
            GameplayAbilitySystemComponent abilitySystem)
        {
            activeOwner = owner;
            activeAbilitySystem = abilitySystem;
            fixedSubmissions.Clear();
        }

        #endregion

        #region 请求与释放

        /// <inheritdoc />
        public MotionControlHandle RequestControl(MotionControlRequest request)
        {
            if (request.Owner == null)
                throw new ArgumentException("运动控制请求必须声明 Character Owner。", nameof(request));
            if (request.Channels == MotionChannels.None)
                throw new ArgumentException("运动控制请求必须至少包含一个通道。", nameof(request));
            int id = ++nextControlId;
            var handle = new MotionControlHandle(this, id, request.Owner);
            controls.Add(id, new ControlEntry(handle, request, ++nextSequence));
            return handle;
        }

        /// <inheritdoc />
        public void SubmitFixed(MotionControlHandle handle, FixedMotionRequest request)
        {
            if (handle == null) throw new ArgumentNullException(nameof(handle));
            if (!handle.IsValid || !controls.TryGetValue(handle.Id, out ControlEntry entry) ||
                !ReferenceEquals(entry.Handle, handle))
                throw new InvalidOperationException("不能使用已经释放或不属于当前 MotionDriver 的 Handle 提交运动。");
            fixedSubmissions.Add(new FixedSubmission(handle, request));
        }

        /// <summary>释放单个 Handle 对应请求。</summary>
        /// <param name="handle">正在释放的句柄。</param>
        internal void Release(MotionControlHandle handle)
        {
            controls.Remove(handle.Id);
            fixedSubmissions.RemoveAll(submission => ReferenceEquals(submission.Handle, handle));
        }

        /// <summary>释放指定 Owner 的全部持续请求并清除其瞬时提交。</summary>
        /// <param name="owner">需要清理的旧 Character Owner。</param>
        internal void ReleaseAll(IGameplayAbilitySystemOwner owner)
        {
            if (owner == null) return;
            var removeIds = new List<int>();
            foreach (KeyValuePair<int, ControlEntry> pair in controls)
            {
                if (ReferenceEquals(pair.Value.Request.Owner, owner)) removeIds.Add(pair.Key);
            }
            foreach (int id in removeIds)
            {
                ControlEntry entry = controls[id];
                controls.Remove(id);
                entry.Handle.Invalidate();
            }
            fixedSubmissions.RemoveAll(submission => ReferenceEquals(submission.Handle.Owner, owner));
        }

        #endregion

        #region 阶段结算

        /// <summary>仲裁当前物理步提交并最多执行一次代码位移。</summary>
        internal void ResolveFixedMotion()
        {
            try
            {
                ControlEntry horizontal = FindWinner(MotionChannels.Horizontal);
                ControlEntry vertical = FindWinner(MotionChannels.Vertical);
                ControlEntry rotation = FindWinner(MotionChannels.Rotation);
                Vector3 translation = Vector3.zero;
                Quaternion finalRotation = Quaternion.identity;
                foreach (FixedSubmission submission in fixedSubmissions)
                {
                    if (horizontal != null && ReferenceEquals(horizontal.Handle, submission.Handle))
                    {
                        Vector3 value = submission.Request.Translation;
                        translation += new Vector3(value.x, 0f, value.z);
                    }
                    if (vertical != null && ReferenceEquals(vertical.Handle, submission.Handle))
                        translation.y += submission.Request.Translation.y;
                    if (rotation != null && ReferenceEquals(rotation.Handle, submission.Handle))
                        finalRotation *= submission.Request.Rotation;
                }
                ApplyResolvedMotion(translation, finalRotation);
            }
            finally
            {
                // 即使最终 CharacterController.Move 抛出异常，旧提交也不能泄漏到下一物理步。
                fixedSubmissions.Clear();
            }
        }

        /// <summary>根据当前获胜控制请求过滤并应用一次 Animator 根运动。</summary>
        /// <param name="deltaPosition">Animator 本次求值产生的位移增量。</param>
        /// <param name="deltaRotation">Animator 本次求值产生的旋转增量。</param>
        internal void ResolveAnimatorMotion(Vector3 deltaPosition, Quaternion deltaRotation)
        {
            ControlEntry horizontal = FindWinner(MotionChannels.Horizontal);
            ControlEntry vertical = FindWinner(MotionChannels.Vertical);
            ControlEntry rotation = FindWinner(MotionChannels.Rotation);
            Vector3 translation = Vector3.zero;
            if (horizontal?.Request.ConsumeAnimatorMotion == true)
                translation += new Vector3(deltaPosition.x, 0f, deltaPosition.z);
            if (vertical?.Request.ConsumeAnimatorMotion == true) translation.y = deltaPosition.y;
            Quaternion finalRotation = rotation?.Request.ConsumeAnimatorMotion == true
                ? deltaRotation : Quaternion.identity;
            ApplyResolvedMotion(translation, finalRotation);
        }

        /// <summary>暂停最终运动并丢弃尚未结算的瞬时提交。</summary>
        internal void Suspend()
        {
            suspended = true;
            ClearTransientRequests();
        }

        /// <summary>恢复接受和结算运动请求。</summary>
        internal void Resume() => suspended = false;

        /// <summary>清除只属于当前阶段的 Fixed 运动提交。</summary>
        internal void ClearTransientRequests() => fixedSubmissions.Clear();

        #endregion

        #region 仲裁与 Tag 限制

        /// <summary>选出指定通道当前最高优先级且最后建立的有效请求。</summary>
        /// <param name="channel">单个待仲裁通道。</param>
        /// <returns>获胜请求；当前没有合法请求时返回 null。</returns>
        private ControlEntry FindWinner(MotionChannels channel)
        {
            ControlEntry winner = null;
            foreach (ControlEntry candidate in controls.Values)
            {
                if (!ReferenceEquals(candidate.Request.Owner, activeOwner) ||
                    (candidate.Request.Channels & channel) == 0) continue;
                if (winner == null || candidate.Request.Priority > winner.Request.Priority ||
                    candidate.Request.Priority == winner.Request.Priority && candidate.Sequence > winner.Sequence)
                    winner = candidate;
            }
            return winner;
        }

        /// <summary>应用最终 Tag 约束、旋转和 CharacterController 位移。</summary>
        /// <param name="translation">仲裁后的世界空间位移。</param>
        /// <param name="rotation">仲裁后的附加旋转。</param>
        private void ApplyResolvedMotion(Vector3 translation, Quaternion rotation)
        {
            if (suspended || characterController == null || !characterController.enabled) return;
            if (HasAnyTag(allMovementBlockedTags)) return;
            if (HasAnyTag(horizontalMovementBlockedTags))
                translation = new Vector3(0f, translation.y, 0f);
            characterController.transform.rotation *= rotation;
            if (translation != Vector3.zero) characterController.Move(translation);
        }

        /// <summary>判断 ActiveCharacter ASC 是否拥有配置数组中任一 Tag 或其子 Tag。</summary>
        /// <param name="tags">作者配置的移动限制标签。</param>
        /// <returns>任一标签匹配时返回 true。</returns>
        private bool HasAnyTag(GameplayTag[] tags)
        {
            if (activeAbilitySystem == null || tags == null) return false;
            for (int index = 0; index < tags.Length; index++)
            {
                if (activeAbilitySystem.HasTag(tags[index])) return true;
            }
            return false;
        }

        #endregion

        #region 嵌套类型

        /// <summary>保存驱动器内部的持续控制请求与建立顺序。</summary>
        private sealed class ControlEntry
        {
            /// <summary>创建内部控制请求条目。</summary>
            /// <param name="handle">公开生命周期句柄。</param>
            /// <param name="request">请求值。</param>
            /// <param name="sequence">单调建立顺序。</param>
            public ControlEntry(MotionControlHandle handle, MotionControlRequest request, long sequence)
            {
                Handle = handle;
                Request = request;
                Sequence = sequence;
            }
            /// <summary>获取请求句柄。</summary>
            public MotionControlHandle Handle { get; }
            /// <summary>获取请求值。</summary>
            public MotionControlRequest Request { get; }
            /// <summary>获取建立顺序。</summary>
            public long Sequence { get; }
        }

        /// <summary>保存当前物理步的一次瞬时运动提交。</summary>
        private readonly struct FixedSubmission
        {
            /// <summary>创建瞬时运动提交。</summary>
            /// <param name="handle">提交控制句柄。</param>
            /// <param name="request">运动数据。</param>
            public FixedSubmission(MotionControlHandle handle, FixedMotionRequest request)
            {
                Handle = handle;
                Request = request;
            }
            /// <summary>获取控制句柄。</summary>
            public MotionControlHandle Handle { get; }
            /// <summary>获取运动数据。</summary>
            public FixedMotionRequest Request { get; }
        }

        #endregion
    }
}
