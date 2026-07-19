using System;

namespace WS_Modules.FSM
{
    /// <summary>适合简单状态和测试场景的链式状态。</summary>
    public class CustomState<TStateId, TOwner> : StateBase<TStateId, TOwner>
    {
        private Func<TOwner, bool> mCanEnter;
        private Action<CustomState<TStateId, TOwner>> mOnEnter;
        private Action<CustomState<TStateId, TOwner>> mOnUpdate;
        private Action<CustomState<TStateId, TOwner>> mOnFixedUpdate;
        private Action<CustomState<TStateId, TOwner>> mOnLateUpdate;
        private Action<CustomState<TStateId, TOwner>> mOnAnimationMove;
        private Action<CustomState<TStateId, TOwner>> mOnExit;

        public CustomState(TStateId stateId) : base(stateId) { }

        public CustomState<TStateId, TOwner> OnCanEnter(Func<TOwner, bool> callback)
        {
            mCanEnter = callback;
            return this;
        }

        public CustomState<TStateId, TOwner> OnEnter(Action<CustomState<TStateId, TOwner>> callback)
        {
            mOnEnter = callback;
            return this;
        }

        public CustomState<TStateId, TOwner> OnUpdate(Action<CustomState<TStateId, TOwner>> callback)
        {
            mOnUpdate = callback;
            return this;
        }

        public CustomState<TStateId, TOwner> OnFixedUpdate(Action<CustomState<TStateId, TOwner>> callback)
        {
            mOnFixedUpdate = callback;
            return this;
        }

        public CustomState<TStateId, TOwner> OnLateUpdate(Action<CustomState<TStateId, TOwner>> callback)
        {
            mOnLateUpdate = callback;
            return this;
        }

        public CustomState<TStateId, TOwner> OnAnimationMove(Action<CustomState<TStateId, TOwner>> callback)
        {
            mOnAnimationMove = callback;
            return this;
        }

        public CustomState<TStateId, TOwner> OnExit(Action<CustomState<TStateId, TOwner>> callback)
        {
            mOnExit = callback;
            return this;
        }

        public override bool CanEnter() => mCanEnter == null || mCanEnter(Owner);
        public override void OnEnter() => mOnEnter?.Invoke(this);
        public override void OnUpdate() => mOnUpdate?.Invoke(this);
        public override void OnFixedUpdate() => mOnFixedUpdate?.Invoke(this);
        public override void OnLateUpdate() => mOnLateUpdate?.Invoke(this);
        public override void OnAnimationMove() => mOnAnimationMove?.Invoke(this);
        public override void OnExit() => mOnExit?.Invoke(this);
    }
}