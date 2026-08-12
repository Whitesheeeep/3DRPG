using UnityEngine;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 提供 Physics 粗筛选之后的业务目标解析与合法性过滤，例如阵营、死亡、无敌或标签判断。
    /// </summary>
    public interface ISkillAttackTargetFilter
    {
        /// <summary>
        /// 将命中的 Collider 解析成去重与业务处理使用的目标对象。
        /// </summary>
        /// <param name="collider">Physics 查询返回的 Collider。</param>
        /// <returns>目标根对象；返回空表示忽略该 Collider。</returns>
        GameObject ResolveTarget(Collider collider);

        /// <summary>
        /// 判断已经解析出的目标是否允许被本次技能命中。
        /// </summary>
        /// <param name="owner">施法者。</param>
        /// <param name="target">目标过滤器解析出的业务对象。</param>
        /// <param name="collider">产生本次命中的具体 Collider。</param>
        /// <returns>允许发布命中时返回 true。</returns>
        bool CanHit(GameObject owner, GameObject target, Collider collider);
    }

    /// <summary>
    /// 保存 SkillRuntimeModule 初始化时使用的 Physics 查询范围和可选业务过滤器。
    /// </summary>
    public readonly struct SkillAttackSettings
    {
        public LayerMask LayerMask { get; }
        public QueryTriggerInteraction TriggerInteraction { get; }
        public ISkillAttackTargetFilter TargetFilter { get; }

        /// <summary>
        /// 创建攻击检测设置。
        /// </summary>
        /// <param name="layerMask">Physics 粗筛选使用的 LayerMask。</param>
        /// <param name="triggerInteraction">是否命中 Trigger 的查询规则。</param>
        /// <param name="targetFilter">可选业务目标过滤器。</param>
        public SkillAttackSettings(LayerMask layerMask,
            QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.UseGlobal,
            ISkillAttackTargetFilter targetFilter = null)
        {
            LayerMask = layerMask;
            TriggerInteraction = triggerInteraction;
            TargetFilter = targetFilter;
        }

        /// <summary>
        /// 创建只替换 LayerMask 的新设置快照。
        /// </summary>
        /// <param name="layerMask">新的 Physics LayerMask。</param>
        /// <returns>保留 Trigger 和 TargetFilter 的设置。</returns>
        public SkillAttackSettings WithLayerMask(LayerMask layerMask) =>
            new(layerMask, TriggerInteraction, TargetFilter);

        /// <summary>
        /// 创建只替换业务目标过滤器的新设置快照。
        /// </summary>
        /// <param name="filter">新的过滤器；为空表示只使用 LayerMask 和自身排除。</param>
        /// <returns>保留 LayerMask 与 Trigger 规则的设置。</returns>
        public SkillAttackSettings WithTargetFilter(ISkillAttackTargetFilter filter) =>
            new(LayerMask, TriggerInteraction, filter);
    }

    /// <summary>
    /// 描述一次已经通过 LayerMask、自身排除、业务过滤和 Clip 内去重的命中。
    /// </summary>
    public readonly struct SkillHitEventArgs
    {
        /// <summary>
        /// 所属技能执行标识，用于在运行时 Module 内部去重和外部事件订阅区分不同技能实例。
        /// </summary>
        public ulong ExecutionId { get; }
        public SkillConfig Config { get; }
        public GameObject Owner { get; }
        public AttackDetectionSkillClipConfig Clip { get; }
        public int Frame { get; }
        public GameObject Target { get; }
        public Collider Collider { get; }
        public Vector3 Point { get; }

        /// <summary>
        /// 创建不可变命中事件快照。
        /// </summary>
        /// <param name="executionId">所属技能执行标识。</param>
        /// <param name="config">所属技能配置。</param>
        /// <param name="owner">施法者。</param>
        /// <param name="clip">产生检测的攻击 Clip。</param>
        /// <param name="frame">产生检测的逻辑帧。</param>
        /// <param name="target">过滤器解析出的业务目标。</param>
        /// <param name="collider">命中的具体 Collider。</param>
        /// <param name="point">用于反馈和调试的近似世界命中点。</param>
        public SkillHitEventArgs(ulong executionId, SkillConfig config, GameObject owner,
            AttackDetectionSkillClipConfig clip, int frame, GameObject target,
            Collider collider, Vector3 point)
        {
            ExecutionId = executionId;
            Config = config;
            Owner = owner;
            Clip = clip;
            Frame = frame;
            Target = target;
            Collider = collider;
            Point = point;
        }
    }
}
