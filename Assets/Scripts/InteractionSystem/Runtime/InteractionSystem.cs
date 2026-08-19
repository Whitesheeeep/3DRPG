using System;
using System.Collections.Generic;
using UnityEngine;

namespace RPG.InteractionSystem
{
    #region 交互契约

    /// <summary>
    /// 定义可被通用交互选择器选中的目标入口。
    /// </summary>
    public interface IInteractionTarget
    {
        /// <summary>获取用于距离和视野评分的目标对象。</summary>
        GameObject InteractionObject { get; }

        /// <summary>获取目标在场景中的交互中心。</summary>
        Transform InteractionOrigin { get; }

        /// <summary>判断当前玩家是否可以提交交互。</summary>
        /// <param name="interactor">发起交互的玩家对象。</param>
        /// <returns>可以交互时返回 true。</returns>
        bool CanInteract(GameObject interactor);

        /// <summary>执行目标交互入口。</summary>
        /// <param name="interactor">发起交互的玩家对象。</param>
        void Interact(GameObject interactor);
    }
    #endregion

    #region Trigger 组件
    #endregion

    #region 玩家交互选择器

    /// <summary>
    /// 维护由 Trigger Enter/Exit 提供的候选目标，并按距离和视野角选择当前目标。
    /// </summary>
    public sealed class InteractionInteractor : MonoBehaviour
    {
        #region 序列化字段与状态

        [SerializeField] private Camera viewCamera;
        [SerializeField] private LayerMask occlusionMask = ~0;
        // 该距离只用于候选目标评分，不作为 Trigger 候选集合的再次范围筛选。
        [SerializeField, Min(0f)] private float maxTargetDistance = 8f;

        private readonly HashSet<Interact> candidates = new HashSet<Interact>();

        /// <summary>获取当前评分后选中的交互目标。</summary>
        public IInteractionTarget CurrentTarget { get; private set; }

        /// <summary>当前交互目标变化事件。</summary>
        public event Action<IInteractionTarget> TargetChanged;

        #endregion

        #region Unity 生命周期

        /// <summary>使用主摄像机作为默认视野来源。</summary>
        private void Awake()
        {
            if (viewCamera == null) viewCamera = Camera.main;
        }

        /// <summary>每帧从 Trigger 提供的候选集合中选择当前目标。</summary>
        private void Update() => RefreshCurrentTarget();

        #endregion

        #region 候选集合

        /// <summary>加入一个由 Trigger Enter 提供的候选，不执行物理范围扫描。</summary>
        /// <param name="interact">提供 Trigger 的交互组件。</param>
        public void AddCandidate(Interact interact)
        {
            if (interact == null || interact.Target == null) return;
            candidates.Add(interact);
            RefreshCurrentTarget();
        }

        /// <summary>移除一个由 Trigger Exit 提供的候选。</summary>
        /// <param name="interact">离开 Trigger 的交互组件。</param>
        public void RemoveCandidate(Interact interact)
        {
            if (interact == null) return;
            candidates.Remove(interact);
            RefreshCurrentTarget();
        }

        /// <summary>提交当前目标交互；目标自身负责构建具体业务请求。</summary>
        public void Submit()
        {
            if (CurrentTarget == null || !CurrentTarget.CanInteract(gameObject)) return;
            CurrentTarget.Interact(gameObject);
        }

        #endregion

        #region 目标评分

        /// <summary>在现有候选集合中执行视野、遮挡和距离评分。</summary>
        private void RefreshCurrentTarget()
        {
            IInteractionTarget bestTarget = null;
            float bestScore = float.MinValue;
            Vector3 origin = transform.position;
            foreach (Interact interact in candidates)
            {
                if (interact == null || interact.Target == null || !interact.Target.CanInteract(gameObject)) continue;
                Transform targetOrigin = interact.Target.InteractionOrigin;
                if (targetOrigin == null) continue;
                Vector3 toTarget = targetOrigin.position - origin;
                float distance = toTarget.magnitude;
                if (!IsVisible(targetOrigin, distance)) continue;

                float distanceScore = maxTargetDistance <= 0f ? 0f : 1f - distance / maxTargetDistance;
                float angleScore = viewCamera == null
                    ? 0f
                    : Vector3.Dot(viewCamera.transform.forward,
                        (targetOrigin.position - viewCamera.transform.position).normalized);
                float score = distanceScore + angleScore;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = interact.Target;
                }
            }

            if (ReferenceEquals(CurrentTarget, bestTarget)) return;
            CurrentTarget = bestTarget;
            TargetChanged?.Invoke(bestTarget);
        }

        /// <summary>判断目标是否在摄像机前方且未被遮挡。</summary>
        /// <param name="targetOrigin">目标中心。</param>
        /// <param name="distance">玩家到目标的距离。</param>
        /// <returns>可见时返回 true。</returns>
        private bool IsVisible(Transform targetOrigin, float distance)
        {
            if (viewCamera == null) return true;
            Vector3 viewport = viewCamera.WorldToViewportPoint(targetOrigin.position);
            if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f ||
                viewport.y < 0f || viewport.y > 1f)
                return false;
            if (occlusionMask.value == 0) return true;
            Vector3 direction = targetOrigin.position - viewCamera.transform.position;
            return !Physics.Raycast(viewCamera.transform.position, direction.normalized, distance,
                occlusionMask, QueryTriggerInteraction.Ignore);
        }

        #endregion

        #endregion
    }
}
