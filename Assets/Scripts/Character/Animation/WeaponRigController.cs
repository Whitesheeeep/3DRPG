using System;
using Animancer;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace RPG.Character.Animation
{
    /// <summary>只通过 MultiParentConstraint 权重切换武器手持与背负骨骼姿态。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖通节点或者父节点的 AnimancerComponent，以及序列化绑定的 MultiParentConstraint 与固定 WeaponRigTarget；约束来源需包含手部和背部两个挂点。该组件不加载、替换武器模型，也不维护技能 Marker。")]
    public sealed class WeaponRigController : MonoBehaviour, IWeaponSwitch
    {
        #region 依赖与配置字段

        // 依赖字段：约束目标由角色 Prefab 显式绑定，Animancer 输出用于让 Rig 参数在图连接后保持最新值。
        [SerializeField, Required] private AnimancerComponent animancer;
        [SerializeField, Required, LabelText("武器父级约束")]
        private MultiParentConstraint weaponConstraint;
        [SerializeField, Required, LabelText("固定武器 Rig 目标")]
        private Transform weaponRigTarget;
        [SerializeField, Required, LabelText("手部来源索引"), MinValue(0)]
        private int handSourceIndex;
        [SerializeField, Required, LabelText("背部来源索引"), MinValue(0)]
        private int backSourceIndex = 1;
        [SerializeField, LabelText("初始持握")]
        private bool startsHolding = true;

        // 约束姿态只记录当前来源，装备与模型状态由独立表现组件读取 CharacterInstance 管理。
        private bool isHolding;

        #endregion

        #region Unity 生命周期

        /// <summary>根据 Prefab 配置初始化手持或背负的约束来源权重。</summary>
        private void Awake()
        {
            if (weaponConstraint == null || weaponRigTarget == null)
                throw new InvalidOperationException($"[WeaponRigController] 角色 {name} 未配置约束或固定 Rig 目标。");
            animancer ??= GetComponentInParent<AnimancerComponent>();
            if (animancer == null)
                throw new InvalidOperationException($"[WeaponRigController] 角色 {name} 缺少同节点 AnimancerComponent，无法同步 Rig 权重与动画输出。");

            // 先确保输出已建立，后续刷新才能在技能播放切换图节点时重新捕获约束参数。
            animancer.InitializeGraph();

            // 反向预置缓存状态，使初始化也经过同一条来源权重更新路径。
            isHolding = !startsHolding;
            ApplyGripState(startsHolding);
            Debug.Log($"[WeaponRigController] 角色 {name} 初始化骨骼姿态，state={(isHolding ? "手持" : "背负")}，target={weaponRigTarget.name}。", this);
        }

        /// <summary>记录骨骼姿态控制器结束生命周期。</summary>
        private void OnDestroy()
        {
            Debug.Log($"[WeaponRigController] 角色 {name} 释放武器骨骼姿态控制器。", this);
        }

        #endregion

        #region 公开姿态操作

        /// <summary>立即将固定武器目标切换至手部来源。</summary>
        public void HoldWeapon()
        {
            ApplyGripState(true);
        }

        /// <summary>立即将固定武器目标切换至背部来源。</summary>
        public void CarryWeaponOnBack()
        {
            ApplyGripState(false);
        }

        #endregion

        #region 约束权重刷新

        /// <summary>更新手部与背部两个约束来源的权重，并按缓存姿态跳过重复调用。</summary>
        /// <param name="holdInHand">为 true 时启用手部来源，否则启用背部来源。</param>
        private void ApplyGripState(bool holdInHand)
        {
            MultiParentConstraintData constraintData = weaponConstraint.data;
            WeightedTransformArray sourceObjects = constraintData.sourceObjects;
            int requiredCount = Math.Max(handSourceIndex, backSourceIndex) + 1;
            if (handSourceIndex < 0 || backSourceIndex < 0 ||
                sourceObjects.Count < requiredCount || handSourceIndex == backSourceIndex)
            {
                string message = $"[WeaponRigController] 角色 {name} 的 MultiParentConstraint 来源配置无效：需要 {requiredCount} 个且手背索引不同，实际来源={sourceObjects.Count}，handIndex={handSourceIndex}，backIndex={backSourceIndex}。";
                Debug.LogError(message, weaponConstraint);
                throw new InvalidOperationException(message);
            }

            float targetHandWeight = holdInHand ? 1f : 0f;
            float targetBackWeight = holdInHand ? 0f : 1f;
            if (isHolding == holdInHand)
                return;

            // 两个来源互斥，固定 Target 与武器模型层级都不随姿态切换而变化。
            sourceObjects.SetWeight(handSourceIndex, targetHandWeight);
            sourceObjects.SetWeight(backSourceIndex, targetBackWeight);
            constraintData.sourceObjects = sourceObjects;
            weaponConstraint.data = constraintData;

            // Animancer 连接或切换 Playable 时可能让 Animation Rigging 恢复旧参数；刷新输出会重新捕获刚写入的权重。
            animancer.InitializeGraph();
            new PlayableOutputRefresher(animancer.Graph).Refresh();

            isHolding = holdInHand;
            WeightedTransformArray appliedSources = weaponConstraint.data.sourceObjects;
            Debug.Log($"[WeaponRigController] 角色 {name} 武器骨骼姿态切换为{(isHolding ? "手持" : "背负")}，constraint={weaponConstraint.name}，{FormatSourceWeights(appliedSources)}。", this);
        }

        /// <summary>格式化手部与背部挂点名称及约束实际权重，便于将日志和 Inspector 对照。</summary>
        /// <param name="sourceObjects">约束当前读取到的 Source 数组。</param>
        /// <returns>包含手部与背部挂点名称和权重的诊断文本。</returns>
        private string FormatSourceWeights(WeightedTransformArray sourceObjects)
        {
            if (handSourceIndex < 0 || backSourceIndex < 0 ||
                sourceObjects.Count <= Math.Max(handSourceIndex, backSourceIndex))
                return $"sourceCount={sourceObjects.Count}，handIndex={handSourceIndex}，backIndex={backSourceIndex}";

            Transform handSource = sourceObjects[handSourceIndex].transform;
            Transform backSource = sourceObjects[backSourceIndex].transform;
            return $"hand={handSource?.name ?? "<null>"}:{sourceObjects.GetWeight(handSourceIndex):0.##}，back={backSource?.name ?? "<null>"}:{sourceObjects.GetWeight(backSourceIndex):0.##}";
        }

        #endregion
    }
}
