using System;
using System.Collections.Generic;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>保存单次 Gameplay Ability 激活的公共快照并约束状态迁移。</summary>
    public abstract class GameplayAbilityRuntime
    {
        #region 字段与事件
        private readonly Dictionary<GameplayTag, float> setByCaller;

        // Controller 监听唯一终态通知，以维护 Active 集合和公开事件顺序。
        internal event Action<GameplayAbilityRuntime> Finished;
        #endregion

        #region 属性
        /// <summary>获取所属 Controller 单调分配的单次激活标识。</summary>
        public int ActivationId { get; }
        /// <summary>获取创建该 Runtime 的长期 Ability Spec。</summary>
        public GameplayAbilitySpec Spec { get; }
        /// <summary>获取释放本次 Ability 的 ASC。</summary>
        public GameplayAbilitySystemComponent SourceASC { get; }
        /// <summary>获取激活时从 Spec 复制的等级快照。</summary>
        public int Level { get; }
        /// <summary>获取当前生命周期状态。</summary>
        public GameplayAbilityRuntimeState State { get; private set; }
        /// <summary>获取激活时复制的 SetByCaller 只读数据。</summary>
        public IReadOnlyDictionary<GameplayTag, float> SetByCaller => setByCaller;
        #endregion

        #region 构造与查询
        // 子类构造阶段只保存不可变快照，不启动 Task 或产生外部副作用。
        /// <summary>创建尚未提交 Cost/Cooldown 的 Runtime 候选。</summary>
        protected GameplayAbilityRuntime(
            int activationId,
            GameplayAbilitySpec spec,
            GameplayAbilitySystemComponent source,
            IReadOnlyDictionary<GameplayTag, float> values)
        {
            ActivationId = activationId;
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            SourceASC = source ?? throw new ArgumentNullException(nameof(source));
            Level = spec.Level;
            State = GameplayAbilityRuntimeState.Created;
            setByCaller = CopyValues(values);
        }

        /// <summary>尝试读取本次激活以稳定 GameplayTag Key 提供的动态值。</summary>
        public bool TryGetSetByCaller(GameplayTag key, out float value) =>
            setByCaller.TryGetValue(key, out value);
        #endregion

        #region 生命周期模板
        // Cost/Cooldown 提交后由 Controller 将候选切换为 Active。
        internal void Activate() => State = GameplayAbilityRuntimeState.Active;

        // Activated 事件发送后才进入具体执行，保证同步完成也遵循事件顺序。
        internal void Start() => OnStart();

        // 外部正常结束仅允许从 Active 迁移一次。
        internal bool End()
        {
            if (State != GameplayAbilityRuntimeState.Active) return false;
            State = GameplayAbilityRuntimeState.Ended;
            OnEnd();
            Finished?.Invoke(this);
            return true;
        }

        // 外部打断或 Clear 仅允许从 Active 迁移一次。
        internal bool Cancel()
        {
            if (State != GameplayAbilityRuntimeState.Active) return false;
            State = GameplayAbilityRuntimeState.Cancelled;
            OnCancel();
            Finished?.Invoke(this);
            return true;
        }

        // 具体同步执行或 Root Task 完成时复用正常结束路径。
        protected bool Complete() => End();

        /// <summary>按需处理普通更新阶段；同步 Runtime 默认不需要逐帧推进。</summary>
        /// <param name="deltaTime">普通更新阶段的秒数。</param>
        internal virtual void Tick(float deltaTime) { }

        /// <summary>按需处理固定更新阶段。</summary>
        /// <param name="fixedDeltaTime">固定更新阶段的秒数。</param>
        internal virtual void FixedTick(float fixedDeltaTime) { }

        /// <summary>按需处理延迟更新阶段。</summary>
        /// <param name="deltaTime">延迟更新阶段使用的秒数。</param>
        internal virtual void LateTick(float deltaTime) { }

        // 子类实现同步逻辑或启动 Root Task。
        protected abstract void OnStart();

        // 子类可在正常结束时停止仍在运行的异步资源。
        protected virtual void OnEnd() { }

        // 子类可在取消时传播中断并释放异步资源。
        protected virtual void OnCancel() { }
        #endregion

        #region 内部辅助
        // 复制调用方字典，确保 Runtime 的激活输入不会被外部后续修改。
        private static Dictionary<GameplayTag, float> CopyValues(
            IReadOnlyDictionary<GameplayTag, float> values)
        {
            var copy = new Dictionary<GameplayTag, float>();
            if (values == null) return copy;
            foreach (KeyValuePair<GameplayTag, float> pair in values)
                copy.Add(pair.Key, pair.Value);
            return copy;
        }
        #endregion
    }
}
