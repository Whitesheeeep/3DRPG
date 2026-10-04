using System;
using System.Collections.Generic;
using RPG.Character;
using RPG.Character.Combat;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>在攻击播放前依次采用当前玩家锁定、最近敌人或世界移动输入确定朝向。</summary>
    public sealed class AttackFacingGameplayAbilityTask : GameplayAbilityTask
    {
        #region 配置快照与共享查询缓冲

        private const int InitialColliderBufferCapacity = 32;

        // Unity 物理查询只能在主线程执行；同步 OnStart 查询复用此缓冲，避免每段攻击分配 Collider 数组。
        private static Collider[] overlapColliderBuffer = new Collider[InitialColliderBufferCapacity];

        // 每次激活复制配置，运行中修改 Ability 资产不会改变当前扫描范围。
        private readonly float scanRadius;
        private readonly int targetLayerMask;

        #endregion

        #region 构造与生命周期

        /// <summary>创建带有扫描半径和敌人 Layer 快照的攻击转向 Task。</summary>
        /// <param name="runtime">拥有该 Task 的异步 Ability Runtime。</param>
        /// <param name="requestedScanRadius">自动选敌扫描半径，单位为 Unity 世界单位。</param>
        /// <param name="requestedTargetLayers">自动选敌使用的 Unity LayerMask。</param>
        public AttackFacingGameplayAbilityTask(
            AsynchronousGameplayAbilityRuntime runtime,
            float requestedScanRadius,
            LayerMask requestedTargetLayers)
            : base(runtime)
        {
            scanRadius = requestedScanRadius;
            targetLayerMask = requestedTargetLayers.value;
        }

        /// <summary>读取锁定目标或最近敌人，水平转向后立即完成以推进 Sequence。</summary>
        protected override void OnStart()
        {
            GameplayAbilitySystemComponent sourceAsc = GARuntime.SourceASC;
            IGameplayAbilitySystemOwner sourceOwner = sourceAsc.Owner;
            Transform sourceRoot = sourceOwner.RootTransform;
            if (sourceRoot == null)
                throw new InvalidOperationException(
                    $"[AttackFacingGameplayAbilityTask] Ability '{GARuntime.Data.name}' 的 Owner 没有有效 RootTransform。");

            bool isActivePlayer = sourceOwner is CharacterActor sourceActor &&
                                  ReferenceEquals(PlayerController.Instance?.CharacterManager?.ActiveCharacter, sourceActor);
            GameplayAbilitySystemComponent targetAsc = isActivePlayer
                ? TryResolveLockedTarget(sourceAsc)
                : null;
            string targetSource = "Locked";
            if (targetAsc == null)
            {
                targetAsc = FindNearestEnemy(sourceAsc, sourceRoot);
                targetSource = "NearestEnemy";
            }

            if (targetAsc != null)
                TryFaceTarget(sourceRoot, targetAsc, targetSource);
            else if (isActivePlayer && sourceOwner is CharacterActor activePlayerActor)
                TryFaceDirection(sourceRoot, activePlayerActor.StateBlackboard.MoveWorldInput, "MoveInput");

            // Sequence 会在当前调用栈继续启动 PlaySkillConfig Task，不额外等待一帧。
            Complete();
        }

        #endregion

        #region 目标解析

        /// <summary>读取全局锁定单例并验证目标仍可参与攻击。</summary>
        /// <param name="sourceAsc">排除自身的 Source ASC。</param>
        /// <returns>有效锁定目标；未接入锁定系统或目标无效时返回 null。</returns>
        private static GameplayAbilitySystemComponent TryResolveLockedTarget(
            GameplayAbilitySystemComponent sourceAsc)
        {
            if (!LockTargetSystem.Instance.TryGetAttackTarget(out GameplayAbilitySystemComponent targetAsc))
                return null;

            return IsUsableTarget(targetAsc, sourceAsc) ? targetAsc : null;
        }

        /// <summary>全方向查询指定半径内的 Enemy Layer，并选择 RootTransform 最近的唯一 ASC。</summary>
        /// <param name="sourceAsc">本次攻击的 Source ASC。</param>
        /// <param name="sourceRoot">用于距离和转向计算的攻击者根节点。</param>
        /// <returns>最近有效目标；范围内没有有效敌人时返回 null。</returns>
        private GameplayAbilitySystemComponent FindNearestEnemy(
            GameplayAbilitySystemComponent sourceAsc,
            Transform sourceRoot)
        {
            int overlapCount;
            while (true)
            {
                overlapCount = Physics.OverlapSphereNonAlloc(
                    sourceRoot.position,
                    scanRadius,
                    overlapColliderBuffer,
                    targetLayerMask,
                    QueryTriggerInteraction.Collide);
                if (overlapCount < overlapColliderBuffer.Length) break;

                // 满缓冲无法判断查询是否被截断，扩容后重扫，避免漏掉更近目标。
                Array.Resize(ref overlapColliderBuffer, checked(overlapColliderBuffer.Length * 2));
            }

            var visitedTargetInstanceIds = new HashSet<int>();
            GameplayAbilitySystemComponent nearestTargetAsc = null;
            float nearestDistanceSquared = scanRadius * scanRadius;
            int nearestTargetInstanceId = int.MaxValue;
            try
            {
                // Collider 缓冲区可能包含 null、重复或同一 ASC 的多个 Collider，需排除这些情况。
                // 挑选距离最近的 ASC，若距离相同则选择 InstanceId 更小的 ASC，保证结果唯一。
                for (int colliderIndex = 0; colliderIndex < overlapCount; colliderIndex++)
                {
                    Collider candidateCollider = overlapColliderBuffer[colliderIndex];
                    if (candidateCollider == null) continue;

                    GameplayAbilitySystemComponent candidateAsc =
                        candidateCollider.GetComponentInParent<GameplayAbilitySystemComponent>();
                    if (!IsUsableTarget(candidateAsc, sourceAsc) ||
                        !visitedTargetInstanceIds.Add(candidateAsc.GetInstanceID()))
                        continue;

                    Transform candidateRoot = candidateAsc.Owner.RootTransform;
                    float distanceSquared = (candidateRoot.position - sourceRoot.position).sqrMagnitude;
                    int candidateInstanceId = candidateAsc.GetInstanceID();
                    if (distanceSquared > nearestDistanceSquared ||
                        (distanceSquared == nearestDistanceSquared && candidateInstanceId >= nearestTargetInstanceId))
                        continue;

                    nearestTargetAsc = candidateAsc;
                    nearestDistanceSquared = distanceSquared;
                    nearestTargetInstanceId = candidateInstanceId;
                }
            }
            finally
            {
                // 扩容重扫时旧结果可能留在新缓冲区尾部，清空整个缓冲避免延长对象生命周期。
                Array.Clear(overlapColliderBuffer, 0, overlapColliderBuffer.Length);
            }

            return nearestTargetAsc;
        }

        /// <summary>确认候选 ASC 有活动 GameObject、有效 Owner 和空间根节点，并排除攻击者自身。</summary>
        /// <param name="candidateAsc">候选目标 ASC。</param>
        /// <param name="sourceAsc">当前攻击的 Source ASC。</param>
        /// <returns>候选可用于锁定或自动选敌时返回 true。</returns>
        private static bool IsUsableTarget(
            GameplayAbilitySystemComponent candidateAsc,
            GameplayAbilitySystemComponent sourceAsc)
        {
            if (candidateAsc == null || candidateAsc == sourceAsc || !candidateAsc.isActiveAndEnabled ||
                !candidateAsc.gameObject.activeInHierarchy)
                return false;

            IGameplayAbilitySystemOwner targetOwner = candidateAsc.Owner;
            if (targetOwner == null || targetOwner is UnityEngine.Object ownerObject && ownerObject == null)
                return false;

            Transform targetRoot = targetOwner.RootTransform;
            return targetRoot != null && targetRoot.gameObject.activeInHierarchy;
        }

        /// <summary>将攻击者根节点朝向目标的水平投影方向，并记录实际发生的转向。</summary>
        /// <param name="sourceRoot">攻击者空间根节点。</param>
        /// <param name="targetAsc">已验证的目标 ASC。</param>
        /// <param name="targetSource">目标来源标记，用于区分锁定和最近敌人扫描。</param>
        private static void TryFaceTarget(
            Transform sourceRoot,
            GameplayAbilitySystemComponent targetAsc,
            string targetSource)
        {
            Transform targetRoot = targetAsc.Owner.RootTransform;
            TryFaceDirection(sourceRoot, targetRoot.position - sourceRoot.position, targetSource, targetAsc);
        }

        /// <summary>把世界方向投影到水平面并应用朝向；零向量时保持当前旋转。</summary>
        /// <param name="sourceRoot">攻击者空间根节点。</param>
        /// <param name="worldDirection">目标方向或已结算世界移动输入。</param>
        /// <param name="directionSource">诊断用来源名称。</param>
        /// <param name="targetAsc">目标 ASC；输入方向来源时为空。</param>
        private static void TryFaceDirection(
            Transform sourceRoot,
            Vector3 worldDirection,
            string directionSource,
            GameplayAbilitySystemComponent targetAsc = null)
        {
            Vector3 horizontalDirection = Vector3.ProjectOnPlane(worldDirection, Vector3.up);
            if (horizontalDirection.sqrMagnitude <= 0.0001f) return;

            Quaternion targetRotation = Quaternion.LookRotation(horizontalDirection.normalized, Vector3.up);
            if (Quaternion.Angle(sourceRoot.rotation, targetRotation) <= 0.01f) return;

            sourceRoot.rotation = targetRotation;
            string targetDescription = targetAsc == null
                ? string.Empty
                : $", target={targetAsc.name}, distance={Vector3.Distance(sourceRoot.position, targetAsc.Owner.RootTransform.position):F2}";
            Debug.Log($"[AttackFacingGameplayAbilityTask] 攻击前更新朝向，source={directionSource}{targetDescription}。", sourceRoot);
        }

        #endregion
    }
}
