using System;
using RPG.Character.Animation;
using RPG.Markers;
using UnityEngine;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 保存 SkillRunner 初始化后稳定不变的角色依赖；动画退出策略仍由外部状态机负责。
    /// </summary>
    public readonly struct SkillActorContext
    {
        public GameObject Owner { get; }
        public Transform Origin { get; }
        public IAnimationPlayer AnimationPlayer { get; }
        public AnimationLayerType SkillAnimationLayer { get; }
        public IMarkerProvider MarkerProvider { get; }

        /// <summary>
        /// 创建角色执行上下文。
        /// </summary>
        /// <param name="owner">施法者根对象。</param>
        /// <param name="origin">普通检测和空 Marker VFX 使用的空间基准。</param>
        /// <param name="animationPlayer">播放技能动画的角色动画服务；无动画角色可为空。</param>
        /// <param name="skillAnimationLayer">技能动画使用的固定层，技能结束时不会由 Runner 停止该层。</param>
        /// <param name="markerProvider">解析 VFX 语义挂点的实例 Provider；仅使用根节点时可为空。</param>
        /// <exception cref="ArgumentNullException">Owner 或 Origin 为空。</exception>
        public SkillActorContext(GameObject owner, Transform origin, IAnimationPlayer animationPlayer,
            AnimationLayerType skillAnimationLayer = AnimationLayerType.Action,
            IMarkerProvider markerProvider = null)
        {
            Owner = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
            Origin = origin != null ? origin : throw new ArgumentNullException(nameof(origin));
            AnimationPlayer = animationPlayer;
            SkillAnimationLayer = skillAnimationLayer;
            MarkerProvider = markerProvider;
        }
    }

    /// <summary>
    /// 为一次 SkillExecution 聚合稳定角色依赖、动态播放请求和攻击检测服务。
    /// </summary>
    internal sealed class SkillRuntimeContext
    {
        public ulong ExecutionId { get; }
        public SkillActorContext Actor { get; }
        public SkillPlayRequest Request { get; }
        public SkillAttackSettings AttackSettings { get; }
        public PhysicsAttackDetectionService AttackDetectionServices { get; }
        public Action<SkillHitEventArgs> HitPublisher { get; }

        /// <summary>
        /// 创建一次执行期间共享的运行时上下文。
        /// </summary>
        /// <param name="executionId">本次执行稳定标识。</param>
        /// <param name="actor">角色稳定依赖。</param>
        /// <param name="request">本次播放动态输入。</param>
        /// <param name="attackSettings">开始执行时冻结的攻击设置快照。</param>
        /// <param name="hitPublisher">将去重后的命中发布给所属 Runner 的回调。</param>
        public SkillRuntimeContext(ulong executionId, SkillActorContext actor, SkillPlayRequest request,
            SkillAttackSettings attackSettings, Action<SkillHitEventArgs> hitPublisher)
        {
            ExecutionId = executionId;
            Actor = actor;
            Request = request;
            AttackSettings = attackSettings;
            HitPublisher = hitPublisher;
            AttackDetectionServices = new PhysicsAttackDetectionService(this);
        }

    }
}
