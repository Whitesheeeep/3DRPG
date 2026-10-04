using System;
using System.Collections.Generic;
using RPG.Character;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.Singleton;

namespace RPG.Character.Combat
{
    /// <summary>保存玩家当前锁定目标，并提供屏幕候选选择、切换和目标变化通知。</summary>
    public sealed class LockTargetSystem : SingletonBase<LockTargetSystem>, IAttackLockTargetProvider
    {
        #region 常量与状态

        private const int InitialColliderCapacity = 32;

        // Physics 查询固定在 Unity 主线程；缓冲区供一次候选重建复用。
        private static Collider[] overlapColliderBuffer = new Collider[InitialColliderCapacity];

        private GameplayAbilitySystemComponent lockedTarget;

        #endregion

        #region 初始化与事件属性

        /// <summary>创建不依赖场景对象的全局锁定状态。</summary>
        private LockTargetSystem()
        {
        }

        /// <summary>当锁定目标实际改变时发送，携带旧目标、新目标和变化原因。</summary>
        public event Action<GameplayAbilitySystemComponent, GameplayAbilitySystemComponent, E_LockTargetChangeReason>
            LockTargetChanged;

        /// <summary>获取当前锁定目标；目标无效时先清理过期引用。</summary>
        public GameplayAbilitySystemComponent CurrentTarget
        {
            get
            {
                if (!IsUsableTarget(lockedTarget))
                    SetCurrentTarget(null, E_LockTargetChangeReason.TargetInvalidated);
                return lockedTarget;
            }
        }

        #endregion

        #region 锁定操作

        /// <inheritdoc />
        public bool TryGetAttackTarget(out GameplayAbilitySystemComponent target)
        {
            target = CurrentTarget;
            return target != null;
        }

        /// <summary>由其他锁定业务显式设置有效目标，并以指定原因广播目标变化。</summary>
        /// <param name="target">需要锁定的有效目标 ASC。</param>
        /// <param name="reason">设置目标对应的变化原因。</param>
        /// <returns>当前锁定目标实际改变时返回 true。</returns>
        /// <exception cref="ArgumentNullException">目标参数为空时抛出。</exception>
        public bool SetLockedTarget(
            GameplayAbilitySystemComponent target,
            E_LockTargetChangeReason reason)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target), "显式锁定目标不能为空；解锁请调用 ClearLockedTarget。");
            return SetCurrentTarget(target, reason);
        }

        /// <summary>在给定玩家位置和摄像机下切换锁定；未锁定时选取最近可见敌人，已锁定时解锁。</summary>
        /// <param name="player">当前 Active 玩家角色。</param>
        /// <param name="gameplayCamera">当前游戏摄像机。</param>
        /// <param name="lockRadius">锁定候选半径，单位为世界单位。</param>
        /// <param name="targetLayers">候选敌人 Layer。</param>
        /// <returns>锁定状态实际改变时返回 true。</returns>
        public bool ToggleLock(CharacterActor player, Camera gameplayCamera, float lockRadius, LayerMask targetLayers)
        {
            if (CurrentTarget != null)
            {
                return SetCurrentTarget(null, E_LockTargetChangeReason.ManualToggle);
            }

            List<TargetCandidate> candidateList = CollectVisibleCandidates(player, gameplayCamera, lockRadius, targetLayers);
            if (candidateList.Count == 0)
            {
                Debug.Log(
                    $"[LockTargetSystem] 中键锁定未改变状态，radius={lockRadius:F1}m 内没有屏幕可见的 Enemy。");
                return false;
            }

            TargetCandidate nearestCandidate = candidateList[0];
            for (int index = 1; index < candidateList.Count; index++)
            {
                TargetCandidate candidate = candidateList[index];
                if (candidate.DistanceSquared < nearestCandidate.DistanceSquared ||
                    candidate.DistanceSquared == nearestCandidate.DistanceSquared &&
                    candidate.AbilitySystemComponent.GetInstanceID() < nearestCandidate.AbilitySystemComponent.GetInstanceID())
                    nearestCandidate = candidate;
            }

            return SetCurrentTarget(nearestCandidate.AbilitySystemComponent, E_LockTargetChangeReason.Acquired);
        }

        /// <summary>按屏幕横向排序切换目标；右侧为正方向，左侧为负方向，到达边缘时循环。</summary>
        /// <param name="player">当前 Active 玩家角色。</param>
        /// <param name="gameplayCamera">当前游戏摄像机。</param>
        /// <param name="horizontalDirection">向右为 1，向左为 -1。</param>
        /// <param name="lockRadius">锁定候选半径，单位为世界单位。</param>
        /// <param name="targetLayers">候选敌人 Layer。</param>
        /// <returns>锁定目标实际切换时返回 true。</returns>
        public bool SwitchTarget(
            CharacterActor player,
            Camera gameplayCamera,
            int horizontalDirection,
            float lockRadius,
            LayerMask targetLayers)
        {
            if (horizontalDirection != -1 && horizontalDirection != 1)
                throw new ArgumentOutOfRangeException(nameof(horizontalDirection), "切换方向只能为 -1 或 1。");

            GameplayAbilitySystemComponent currentTarget = CurrentTarget;
            if (currentTarget == null)
                return false;

            List<TargetCandidate> candidateList = CollectVisibleCandidates(player, gameplayCamera, lockRadius, targetLayers);
            if (candidateList.Count <= 1)
                return false;

            int currentIndex = candidateList.FindIndex(
                candidate => ReferenceEquals(candidate.AbilitySystemComponent, currentTarget));
            int nextIndex;
            if (currentIndex >= 0)
            {
                nextIndex = (currentIndex + horizontalDirection + candidateList.Count) % candidateList.Count;
            }
            else
            {
                Vector3 lockedViewportPosition = gameplayCamera.WorldToViewportPoint(
                    currentTarget.Owner.RootTransform.position);
                nextIndex = FindWrappedCandidateIndex(candidateList, lockedViewportPosition.x, horizontalDirection);
            }

            return SetCurrentTarget(
                candidateList[nextIndex].AbilitySystemComponent,
                horizontalDirection > 0 ? E_LockTargetChangeReason.SwitchedRight : E_LockTargetChangeReason.SwitchedLeft);
        }

        /// <summary>按玩家当前位置检查锁定目标的有效性和 15 米距离边界。</summary>
        /// <param name="player">当前 Active 玩家；不存在时会清除锁定。</param>
        /// <param name="lockRadius">锁定最大距离，单位为世界单位。</param>
        public void ValidateTarget(CharacterActor player, float lockRadius)
        {
            GameplayAbilitySystemComponent target = CurrentTarget;
            if (target == null)
                return;

            if (player == null || !IsUsableTarget(player.AbilitySystemComponent) || lockRadius <= 0f ||
                Vector3.Distance(player.RootTransform.position, target.Owner.RootTransform.position) > lockRadius)
                SetCurrentTarget(null, E_LockTargetChangeReason.TargetInvalidated);
        }

        /// <summary>清空当前目标，用于 Player 销毁或角色队伍失去 Active 角色。</summary>
        /// <param name="reason">本次清空的业务原因。</param>
        public void ClearLockedTarget(E_LockTargetChangeReason reason)
        {
            SetCurrentTarget(null, reason);
        }

        #endregion

        #region 候选查询与内部校验

        /// <summary>只在目标身份变化时提交状态并广播一次领域事件。</summary>
        /// <param name="target">新的锁定 ASC；null 表示解锁。</param>
        /// <param name="reason">目标变化的原因。</param>
        /// <returns>目标身份改变时返回 true。</returns>
        private bool SetCurrentTarget(
            GameplayAbilitySystemComponent target,
            E_LockTargetChangeReason reason)
        {
            if (target != null && !IsUsableTarget(target))
                return false;
            if (ReferenceEquals(lockedTarget, target))
                return false;

            GameplayAbilitySystemComponent previousTarget = lockedTarget;
            lockedTarget = target;
            Debug.Log(
                $"[LockTargetSystem] 锁定目标已{(target == null ? "清除" : "更新")}，" +
                $"previous={GetTargetName(previousTarget)}, current={GetTargetName(target)}, reason={reason}。");
            LockTargetChanged?.Invoke(previousTarget, target, reason);
            return true;
        }

        /// <summary>收集距离内且位于摄像机前方视口内的唯一 Enemy ASC，并按屏幕横向和距离排序。</summary>
        /// <param name="player">候选查询的玩家角色。</param>
        /// <param name="gameplayCamera">决定可见视口的游戏摄像机。</param>
        /// <param name="lockRadius">最大世界距离。</param>
        /// <param name="targetLayers">允许参与锁定的碰撞层。</param>
        /// <returns>排好序的可见目标快照。</returns>
        private static List<TargetCandidate> CollectVisibleCandidates(
            CharacterActor player,
            Camera gameplayCamera,
            float lockRadius,
            LayerMask targetLayers)
        {
            var candidateList = new List<TargetCandidate>();
            if (player == null || gameplayCamera == null || player.RootTransform == null || lockRadius <= 0f)
                return candidateList;

            HashSet<int> visitedAbilitySystemIds = new();
            int overlapCount;
            try
            {
                while (true)
                {
                    overlapCount = Physics.OverlapSphereNonAlloc(
                        player.RootTransform.position,
                        lockRadius,
                        overlapColliderBuffer,
                        targetLayers.value,
                        QueryTriggerInteraction.Collide);
                    if (overlapCount < overlapColliderBuffer.Length)
                        break;

                    Array.Resize(ref overlapColliderBuffer, overlapColliderBuffer.Length * 2);
                }

                for (int index = 0; index < overlapCount; index++)
                {
                    Collider candidateCollider = overlapColliderBuffer[index];
                    if (candidateCollider == null)
                        continue;
                    GameplayAbilitySystemComponent candidateAsc =
                        candidateCollider.GetComponentInParent<GameplayAbilitySystemComponent>();
                    if (!IsUsableTarget(candidateAsc) || candidateAsc == player.AbilitySystemComponent ||
                        !visitedAbilitySystemIds.Add(candidateAsc.GetInstanceID()))
                        continue;

                    Vector3 targetPosition = candidateAsc.Owner.RootTransform.position;
                    float distanceSquared = (targetPosition - player.RootTransform.position).sqrMagnitude;
                    if (distanceSquared > lockRadius * lockRadius)
                        continue;

                    Vector3 viewportPosition = gameplayCamera.WorldToViewportPoint(targetPosition);
                    if (viewportPosition.z <= 0f || viewportPosition.x < 0f || viewportPosition.x > 1f ||
                        viewportPosition.y < 0f || viewportPosition.y > 1f)
                        continue;

                    candidateList.Add(new TargetCandidate(candidateAsc, viewportPosition.x, distanceSquared));
                }
            }
            finally
            {
                Array.Clear(overlapColliderBuffer, 0, overlapColliderBuffer.Length);
            }

            candidateList.Sort(CompareCandidates);
            return candidateList;
        }

        /// <summary>找出相对屏幕横坐标的下一项；没有更靠该方向的候选时绕回边缘项。</summary>
        /// <param name="candidateList">已按横坐标排序的候选列表。</param>
        /// <param name="currentViewportX">当前锁定目标横向视口位置。</param>
        /// <param name="direction">向右为 1，向左为 -1。</param>
        /// <returns>下一候选的列表索引。</returns>
        private static int FindWrappedCandidateIndex(
            List<TargetCandidate> candidateList,
            float currentViewportX,
            int direction)
        {
            if (direction > 0)
            {
                for (int index = 0; index < candidateList.Count; index++)
                    if (candidateList[index].ViewportX > currentViewportX)
                        return index;
                return 0;
            }

            for (int index = candidateList.Count - 1; index >= 0; index--)
                if (candidateList[index].ViewportX < currentViewportX)
                    return index;
            return candidateList.Count - 1;
        }

        /// <summary>比较候选横向屏幕位置、世界距离和 ASC 实例 ID，确保顺序稳定。</summary>
        /// <param name="left">第一个候选。</param>
        /// <param name="right">第二个候选。</param>
        /// <returns>符合排序顺序时的比较结果。</returns>
        private static int CompareCandidates(TargetCandidate left, TargetCandidate right)
        {
            int horizontalComparison = left.ViewportX.CompareTo(right.ViewportX);
            if (horizontalComparison != 0)
                return horizontalComparison;
            int distanceComparison = left.DistanceSquared.CompareTo(right.DistanceSquared);
            return distanceComparison != 0
                ? distanceComparison
                : left.AbilitySystemComponent.GetInstanceID().CompareTo(right.AbilitySystemComponent.GetInstanceID());
        }

        /// <summary>检查 ASC、Owner 和世界根节点仍处于可查询状态。</summary>
        /// <param name="abilitySystemComponent">待验证 ASC。</param>
        /// <returns>仍有效时返回 true。</returns>
        private static bool IsUsableTarget(GameplayAbilitySystemComponent abilitySystemComponent)
        {
            if (abilitySystemComponent == null || !abilitySystemComponent.isActiveAndEnabled ||
                !abilitySystemComponent.gameObject.activeInHierarchy)
                return false;

            IGameplayAbilitySystemOwner owner = abilitySystemComponent.Owner;
            if (owner == null || owner is UnityEngine.Object ownerObject && ownerObject == null)
                return false;
            Transform rootTransform = owner.RootTransform;
            return rootTransform != null && rootTransform.gameObject.activeInHierarchy;
        }

        /// <summary>返回日志使用的稳定目标名称。</summary>
        /// <param name="target">目标 ASC，可能为空或已销毁。</param>
        /// <returns>目标名称或 None。</returns>
        private static string GetTargetName(GameplayAbilitySystemComponent target)
        {
            return target == null ? "None" : target.name;
        }

        #endregion

        #region 候选数据

        /// <summary>保存一次屏幕候选的 ASC、横向坐标及根节点距离平方。</summary>
        private readonly struct TargetCandidate
        {
            /// <summary>创建锁定候选快照。</summary>
            /// <param name="abilitySystemComponent">目标 ASC。</param>
            /// <param name="viewportX">目标横向归一化屏幕坐标。</param>
            /// <param name="distanceSquared">玩家与目标根节点的世界距离平方。</param>
            public TargetCandidate(
                GameplayAbilitySystemComponent abilitySystemComponent,
                float viewportX,
                float distanceSquared)
            {
                AbilitySystemComponent = abilitySystemComponent;
                ViewportX = viewportX;
                DistanceSquared = distanceSquared;
            }

            /// <summary>候选目标 ASC。</summary>
            public GameplayAbilitySystemComponent AbilitySystemComponent { get; }
            /// <summary>候选目标屏幕横坐标。</summary>
            public float ViewportX { get; }
            /// <summary>玩家与目标根节点距离的平方。</summary>
            public float DistanceSquared { get; }
        }

        #endregion
    }

    /// <summary>描述锁定目标变化的来源。</summary>
    public enum E_LockTargetChangeReason
    {
        /// <summary>首次通过中键锁定。</summary>
        Acquired,
        /// <summary>通过中键主动解锁。</summary>
        ManualToggle,
        /// <summary>滚轮向屏幕右侧切换。</summary>
        SwitchedRight,
        /// <summary>滚轮向屏幕左侧切换。</summary>
        SwitchedLeft,
        /// <summary>目标失效、离开距离或玩家不可用。</summary>
        TargetInvalidated,
        /// <summary>Player 被销毁时清理跨域静态状态。</summary>
        PlayerDestroyed
    }
}
