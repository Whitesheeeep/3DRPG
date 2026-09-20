using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;
using WS_Modules.LogModule;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>保存单次 Gameplay Ability 激活的公共快照、Phase 标签和生命周期状态。</summary>
    public abstract class GameplayAbilityRuntime
    {
        #region 字段与事件
        private readonly Dictionary<GameplayTag, float> setByCaller;
        private readonly List<GameEffectRuntime> ownedEffects = new();
        private readonly GameplayTagContainer abilityTags = new();
        // RuntimeTags 仅由当前 ActionPhase 所有；Phase 切换按新旧快照差量更新，外部只能读取。
        private readonly GameplayTagContainer runtimeTags = new();
        private readonly GameplayTagContainer blockAbilityTags = new();
        private readonly GameplayTagContainer initialBlockAbilityTags = new();
        private readonly GameplayTagContainer cancelTags = new();
        private bool isCancelable;

        // Controller 监听唯一终态通知，以维护 Active 集合、Block 计数和公开事件顺序。
        internal event Action<GameplayAbilityRuntime> Finished;
        #endregion

        #region 属性
        /// <summary>获取所属 Controller 单调分配的单次激活标识。</summary>
        public int ActivationId { get; }
        /// <summary>获取创建该 Runtime 的长期 Ability Spec。</summary>
        public GameplayAbilitySpec Spec { get; }
        /// <summary>获取释放本次 Ability 的 ASC。</summary>
        public GameplayAbilitySystemComponent SourceASC { get; }
        /// <summary>获取释放本次 Ability 的稳定宿主接口。</summary>
        public IGameplayAbilitySystemOwner SourceOwner => SourceASC.Owner;
        /// <summary>获取激活时从 Spec 复制的等级快照。</summary>
        public int Level { get; }
        /// <summary>获取当前生命周期状态。</summary>
        public GameplayAbilityRuntimeState State { get; private set; }
        /// <summary>获取激活时复制的 SetByCaller 只读数据。</summary>
        public IReadOnlyDictionary<GameplayTag, float> SetByCaller => setByCaller;
        /// <summary>获取当前 Runtime 的 Ability 身份标签。</summary>
        public IReadOnlyGameplayTagContainer AbilityTags => abilityTags;
        /// <summary>获取当前 ActionPhase 的有效 RuntimeTags；相邻 Phase 共有标签会保持连续。</summary>
        public IReadOnlyGameplayTagContainer RuntimeTags => runtimeTags;
        /// <summary>获取当前 Runtime 正在生效的 Ability 阻断标签。</summary>
        public IReadOnlyGameplayTagContainer BlockAbilityTags => blockAbilityTags;
        /// <summary>获取当前 Runtime 请求取消的 Ability 标签。</summary>
        public IReadOnlyGameplayTagContainer CancelTags => cancelTags;
        /// <summary>获取当前 Runtime 是否接受普通取消请求。</summary>
        public bool IsCancelable => isCancelable;
        /// <summary>获取由本次 Ability 生命周期持有的 Active GE Runtime。</summary>
        public IReadOnlyList<GameEffectRuntime> OwnedEffects => ownedEffects;

        /// <summary>提供给生命周期型 Task 登记 GE 的内部可变集合。</summary>
        internal ICollection<GameEffectRuntime> OwnedEffectsInternal => ownedEffects;
        #endregion

        #region 构造与查询
        // 子类构造阶段只保存不可变快照，不启动 Task 或产生外部副作用。
        /// <summary>创建尚未提交 Cost/Cooldown 的 Runtime 候选。</summary>
        /// <param name="activationId">当前 Controller 分配的激活标识。</param>
        /// <param name="spec">被激活的已授予 Spec。</param>
        /// <param name="source">拥有并激活 Ability 的 Source ASC。</param>
        /// <param name="values">本次激活的 SetByCaller 数据。</param>
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
            CopyTags(spec.Data.AbilityTags, abilityTags);
            CopyTags(spec.Data.BlockAbilityTags, blockAbilityTags);
            CopyTags(spec.Data.BlockAbilityTags, initialBlockAbilityTags);
            CopyTags(spec.Data.CancelTags, cancelTags);
            isCancelable = spec.Data.IsCancelable;
        }

        /// <summary>尝试读取本次激活以稳定 GameplayTag Key 提供的动态值。</summary>
        /// <param name="key">SetByCaller 的 GameplayTag Key。</param>
        /// <param name="value">找到时返回对应数值。</param>
        /// <returns>存在对应 Key 时返回 true。</returns>
        public bool TryGetSetByCaller(GameplayTag key, out float value) =>
            setByCaller.TryGetValue(key, out value);
        #endregion

        #region 动态标签与取消权限
        /// <summary>尝试增加当前 Runtime 的 Ability 身份标签。</summary>
        /// <param name="tag">需要增加的有效 AbilityTag。</param>
        /// <returns>标签有效且本次确实增加时返回 true。</returns>
        public bool TryAddAbilityTag(GameplayTag tag) =>
            TryChangeTag(abilityTags, tag, true, "AbilityTags");

        /// <summary>尝试移除当前 Runtime 的 Ability 身份标签。</summary>
        /// <param name="tag">需要移除的 AbilityTag。</param>
        /// <returns>标签存在且本次确实移除时返回 true。</returns>
        public bool TryRemoveAbilityTag(GameplayTag tag) =>
            TryChangeTag(abilityTags, tag, false, "AbilityTags");

        /// <summary>增加一份当前 Runtime 的 Ability 阻断标签。</summary>
        /// <param name="tag">需要阻断的 AbilityTag 查询标签。</param>
        /// <returns>标签有效、状态可修改且本次确实增加时返回 true。</returns>
        public bool TryAddBlockAbilityTag(GameplayTag tag)
        {
            if (!CanModifyRuntimeTags() || !GameplayTagManager.Instance.IsValidTag(tag) ||
                blockAbilityTags.HasTagExact(tag))
                return false;
            if (State == GameplayAbilityRuntimeState.Active &&
                !SourceASC.UpdateAbilityBlockTagCount(this, tag, 1))
                return false;

            blockAbilityTags.AddTag(tag);
            LogTagChange("BlockAbilityTags", tag, true);
            return true;
        }

        /// <summary>移除一份当前 Runtime 的 Ability 阻断标签。</summary>
        /// <param name="tag">需要移除的 AbilityTag 查询标签。</param>
        /// <returns>标签存在、状态可修改且本次确实移除时返回 true。</returns>
        public bool TryRemoveBlockAbilityTag(GameplayTag tag)
        {
            if (!CanModifyRuntimeTags() || !blockAbilityTags.HasTagExact(tag)) return false;
            if (State == GameplayAbilityRuntimeState.Active &&
                !SourceASC.UpdateAbilityBlockTagCount(this, tag, -1))
                return false;

            blockAbilityTags.RemoveTag(tag);
            LogTagChange("BlockAbilityTags", tag, false);
            return true;
        }

        /// <summary>增加 CancelTag，并按参数决定是否立即重新扫描可取消 Runtime。</summary>
        /// <param name="tag">需要增加的取消查询标签。</param>
        /// <param name="cancelAbilitiesImmediately">新标签成功增加且 Runtime Active 时是否立即触发一次取消匹配。</param>
        /// <returns>标签有效、状态可修改且本次确实增加时返回 true。</returns>
        public bool TryAddCancelTag(GameplayTag tag, bool cancelAbilitiesImmediately = false)
        {
            if (!CanModifyRuntimeTags() || !GameplayTagManager.Instance.IsValidTag(tag) ||
                !cancelTags.AddTag(tag))
                return false;

            LogTagChange("CancelTags", tag, true);
            if (cancelAbilitiesImmediately && State == GameplayAbilityRuntimeState.Active)
                RetryCancelAbilitiesMatching();
            return true;
        }

        /// <summary>
        /// 显式重试当前 Runtime 的 CancelTags 匹配扫描。
        /// 相同标签已存在时业务可调用此入口再次请求扫描，仍由 Controller 的唯一算法完成匹配。
        /// </summary>
        /// <returns>当前 Runtime 处于 Active 且已提交扫描请求时返回 true。</returns>
        internal bool RetryCancelAbilitiesMatching()
        {
            if (State != GameplayAbilityRuntimeState.Active) return false;
            SourceASC.RequestCancelAbilitiesMatching(this);
            return true;
        }

        /// <summary>移除当前 Runtime 的 CancelTag。</summary>
        /// <param name="tag">需要移除的取消查询标签。</param>
        /// <returns>标签存在、状态可修改且本次确实移除时返回 true。</returns>
        public bool TryRemoveCancelTag(GameplayTag tag) =>
            TryChangeTag(cancelTags, tag, false, "CancelTags");

        /// <summary>修改当前 Runtime 的普通取消权限。</summary>
        /// <param name="cancelable">新的普通取消权限。</param>
        /// <returns>状态可修改且值发生变化时返回 true。</returns>
        public bool TrySetCancelable(bool cancelable)
        {
            if (!CanModifyRuntimeTags() || isCancelable == cancelable) return false;
            isCancelable = cancelable;
            WSLog.Log($"[GameplayAbilityRuntime] ActivationId={ActivationId} 设置 IsCancelable={cancelable}。");
            return true;
        }

        /// <summary>由 Skill ActionPhase 应用一次 Phase RuntimeTags 和阻断策略快照。</summary>
        /// <param name="phaseTags">当前 Clip 的 RuntimeTags；空区间传空容器。</param>
        /// <param name="phaseBlockTags">当前 Clip 的完整阻断快照；空区间传初始快照。</param>
        /// <param name="cancelable">当前 Phase 的普通取消权限。</param>
        /// <param name="hasPolicy">是否存在有效 Clip 策略；false 时恢复初始策略。</param>
        internal void ApplyPhasePolicy(
            IReadOnlyGameplayTagContainer phaseTags,
            IReadOnlyGameplayTagContainer phaseBlockTags,
            bool cancelable,
            bool hasPolicy)
        {
            if (State != GameplayAbilityRuntimeState.Active) return;

            ReplacePhaseRuntimeTags(phaseTags);
            IReadOnlyGameplayTagContainer targetBlockTags = hasPolicy
                ? phaseBlockTags
                : initialBlockAbilityTags;
            ReplaceBlockAbilityTags(targetBlockTags);
            bool targetCancelable = hasPolicy ? cancelable : Spec.Data.IsCancelable;
            if (isCancelable != targetCancelable)
            {
                isCancelable = targetCancelable;
                WSLog.Log($"[GameplayAbilityRuntime] ActivationId={ActivationId} Phase 更新 IsCancelable={targetCancelable}。");
            }
        }
        #endregion

        #region 生命周期模板
        /// <summary>
        /// 判断当前候选 Runtime 是否满足该 Ability 独有的动态激活条件。
        /// 该方法由 GameplayAbilityCtrl 在提交 Cost 和 Cooldown 前调用，且实现必须是无副作用查询。
        /// </summary>
        /// <returns>当前运行时上下文允许激活时返回 true。</returns>
        protected internal virtual bool CanActivate() => true;

        /// <summary>由 Controller 在 Cost/Cooldown 提交后将候选切换为 Active。</summary>
        internal void Activate() => State = GameplayAbilityRuntimeState.Active;

        /// <summary>在 Activated 事件完成后启动具体 Task 或同步执行。</summary>
        internal void Start() => OnStart();

        /// <summary>按正常 End 语义结束当前 Active Runtime。</summary>
        /// <returns>Runtime 仍处于 Active 且已结束时返回 true。</returns>
        internal bool End()
        {
            if (State != GameplayAbilityRuntimeState.Active) return false;
            State = GameplayAbilityRuntimeState.Ended;
            OnEnd();
            RemoveOwnedEffects();
            SourceASC.ReleaseAbilityBlockTags(this);
            ClearRuntimeTags();
            Finished?.Invoke(this);
            return true;
        }

        /// <summary>按普通打断语义取消当前 Runtime，并尊重 IsCancelable。</summary>
        /// <returns>Runtime 可取消且已进入 Cancelled 时返回 true。</returns>
        internal bool Cancel() => CancelCore(false);

        /// <summary>由 Controller 清理/Owner 销毁使用的内部强制取消路径。</summary>
        /// <returns>Runtime 仍处于 Active 且已完成强制取消时返回 true。</returns>
        internal bool ForceCancel() => CancelCore(true);

        /// <summary>供同步执行或 Root Task 复用正常结束路径。</summary>
        /// <returns>Runtime 成功结束时返回 true。</returns>
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

        /// <summary>按需处理 Animator 求值后的根运动阶段。</summary>
        /// <param name="deltaPosition">Animator 根位移增量。</param>
        /// <param name="deltaRotation">Animator 根旋转增量。</param>
        internal virtual void UpdateAnimationMove(Vector3 deltaPosition, Quaternion deltaRotation) { }

        /// <summary>由具体 Runtime 子类实现同步逻辑或启动 Root Task。</summary>
        protected abstract void OnStart();

        /// <summary>由子类在正常结束时停止仍在运行的异步资源。</summary>
        protected virtual void OnEnd() { }

        /// <summary>由子类在取消时传播中断并释放异步资源。</summary>
        protected virtual void OnCancel() { }
        #endregion

        #region 持续效果所有权
        /// <summary>登记一个由当前生命周期型 Task 成功应用的 Active GE。</summary>
        /// <param name="effectRuntime">需要随本次 Ability 结束而移除的 GE Runtime。</param>
        internal void RetainOwnedEffect(GameEffectRuntime effectRuntime)
        {
            if (effectRuntime == null || ownedEffects.Contains(effectRuntime)) return;
            ownedEffects.Add(effectRuntime);
        }

        /// <summary>移除本次 Runtime 持有的全部 GE，并清空所有权快照。</summary>
        private void RemoveOwnedEffects()
        {
            // 复制倒序快照，允许 GE Remove Cue 或其他回调重入 Ability 生命周期。
            for (int i = ownedEffects.Count - 1; i >= 0; i--)
                SourceASC.GameEffectCtrl.TryRemove(ownedEffects[i]);
            ownedEffects.Clear();
        }
        #endregion

        #region 内部标签辅助
        /// <summary>判断 Runtime 当前是否仍允许修改动态标签。</summary>
        /// <returns>Created 或 Active 状态返回 true。</returns>
        private bool CanModifyRuntimeTags() =>
            State is GameplayAbilityRuntimeState.Created or GameplayAbilityRuntimeState.Active;

        /// <summary>将当前 Phase RuntimeTags 按新旧快照差量更新，不整体清空有效集合。</summary>
        /// <param name="nextTags">新的 Phase 标签快照。</param>
        private void ReplacePhaseRuntimeTags(IReadOnlyGameplayTagContainer nextTags)
        {
            // 先复制旧快照，避免移除标签时修改正在遍历的只读集合。
            var oldTags = new List<GameplayTag>(runtimeTags.Tags);
            for (int i = 0; i < oldTags.Count; i++)
                if (nextTags == null || !nextTags.HasTagExact(oldTags[i]))
                    runtimeTags.RemoveTag(oldTags[i]);

            if (nextTags == null) return;

            // 只补充新区间独有标签；新旧区间共有的标签保持原有存在状态。
            foreach (GameplayTag tag in nextTags.Tags)
                if (!runtimeTags.HasTagExact(tag))
                    runtimeTags.AddTag(tag);
        }

        /// <summary>按差量替换当前 Block 快照，并同步 Active Controller 的引用计数。</summary>
        /// <param name="targetTags">新的完整阻断标签快照。</param>
        private void ReplaceBlockAbilityTags(IReadOnlyGameplayTagContainer targetTags)
        {
            var oldTags = new List<GameplayTag>(blockAbilityTags.Tags);
            for (int i = 0; i < oldTags.Count; i++)
                if (targetTags == null || !targetTags.HasTagExact(oldTags[i]))
                    TryRemoveBlockAbilityTag(oldTags[i]);

            if (targetTags == null) return;
            foreach (GameplayTag tag in targetTags.Tags)
                if (!blockAbilityTags.HasTagExact(tag))
                    TryAddBlockAbilityTag(tag);
        }

        /// <summary>清理 Runtime 终态后的所有动态标签，避免对象被外部继续观察到旧状态。</summary>
        private void ClearRuntimeTags()
        {
            runtimeTags.Reset();
            abilityTags.Reset();
            blockAbilityTags.Reset();
            cancelTags.Reset();
        }

        /// <summary>执行普通或强制取消，并保持唯一的终态通知顺序。</summary>
        /// <param name="force">是否绕过 IsCancelable，用于系统级清理。</param>
        /// <returns>Runtime 仍为 Active 且成功进入 Cancelled 时返回 true。</returns>
        private bool CancelCore(bool force)
        {
            if (State != GameplayAbilityRuntimeState.Active || (!force && !isCancelable)) return false;
            State = GameplayAbilityRuntimeState.Cancelled;
            OnCancel();
            RemoveOwnedEffects();
            SourceASC.ReleaseAbilityBlockTags(this);
            ClearRuntimeTags();
            Finished?.Invoke(this);
            return true;
        }

        /// <summary>对一个标签容器执行受状态约束的单标签修改。</summary>
        /// <param name="container">需要修改的目标容器。</param>
        /// <param name="tag">需要修改的标签。</param>
        /// <param name="add">true 表示增加，false 表示移除。</param>
        /// <param name="sourceName">日志中的来源容器名称。</param>
        /// <returns>标签有效、状态允许且内容实际变化时返回 true。</returns>
        private bool TryChangeTag(GameplayTagContainer container, GameplayTag tag, bool add, string sourceName)
        {
            if (!CanModifyRuntimeTags() || !GameplayTagManager.Instance.IsValidTag(tag)) return false;
            bool changed = add ? container.AddTag(tag) : container.RemoveTag(tag);
            if (changed) LogTagChange(sourceName, tag, add);
            return changed;
        }

        /// <summary>记录一次动态标签的实际变化，不为失败或重复操作刷日志。</summary>
        /// <param name="sourceName">发生变化的标签层。</param>
        /// <param name="tag">发生变化的标签。</param>
        /// <param name="added">true 表示增加，false 表示移除。</param>
        private void LogTagChange(string sourceName, GameplayTag tag, bool added) =>
            WSLog.Log($"[GameplayAbilityRuntime] ActivationId={ActivationId} {sourceName} {(added ? "添加" : "移除")} TagId={tag.Id}。");

        /// <summary>把作者数据标签复制到运行时容器，忽略未通过 Bake 的非法 Tag。</summary>
        /// <param name="source">作者配置标签集合。</param>
        /// <param name="target">目标运行时容器。</param>
        private static void CopyTags(IReadOnlyList<GameplayTag> source, GameplayTagContainer target)
        {
            if (source == null) return;
            for (int i = 0; i < source.Count; i++) target.AddTag(source[i]);
        }

        /// <summary>复制调用方字典，隔离 Runtime 激活输入与外部后续修改。</summary>
        /// <param name="values">调用方提供的 SetByCaller 数据。</param>
        /// <returns>Runtime 私有的字典快照。</returns>
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
