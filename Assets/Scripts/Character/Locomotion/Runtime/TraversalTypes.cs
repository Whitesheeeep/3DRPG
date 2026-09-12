using RPG.PlayerInputSystem;
using UnityEngine;

namespace RPG.Character
{
    /// <summary>描述一次环境穿越候选的表现类型。</summary>
    public enum TraversalCandidateKind
    {
        /// <summary>没有满足条件的穿越候选。</summary>
        None,
        /// <summary>低矮且较薄的跨越候选。</summary>
        Vault,
        /// <summary>可以站上顶部的平台攀爬候选。</summary>
        Mantle
    }

    /// <summary>保存环境检测得到的一次冻结世界空间穿越目标。</summary>
    public readonly struct TraversalCandidate
    {
        /// <summary>创建穿越候选。</summary>
        /// <param name="kind">候选类型。</param>
        /// <param name="obstacleHeight">障碍高度。</param>
        /// <param name="obstacleDepth">障碍真实后沿深度；Mantle 没有可用后沿时为正无穷。</param>
        /// <param name="wallPoint">前墙命中点。</param>
        /// <param name="wallNormal">前墙法线。</param>
        /// <param name="topPoint">近端顶部命中点。</param>
        /// <param name="farEdgePoint">Vault 的真实后沿点；Mantle 时无后沿为零。</param>
        /// <param name="entryPosition">动画入口目标位置。</param>
        /// <param name="finalPosition">动画结束目标位置。</param>
        /// <param name="targetRotation">动画结束目标旋转。</param>
        public TraversalCandidate(
            TraversalCandidateKind kind,
            float obstacleHeight,
            float obstacleDepth,
            Vector3 wallPoint,
            Vector3 wallNormal,
            Vector3 topPoint,
            Vector3 farEdgePoint,
            Vector3 entryPosition,
            Vector3 finalPosition,
            Quaternion targetRotation)
        {
            Kind = kind;
            ObstacleHeight = obstacleHeight;
            ObstacleDepth = obstacleDepth;
            WallPoint = wallPoint;
            WallNormal = wallNormal;
            TopPoint = topPoint;
            FarEdgePoint = farEdgePoint;
            EntryPosition = entryPosition;
            FinalPosition = finalPosition;
            TargetRotation = targetRotation;
        }

        /// <summary>获取候选类型。</summary>
        public TraversalCandidateKind Kind { get; }
        /// <summary>获取障碍高度。</summary>
        public float ObstacleHeight { get; }
        /// <summary>获取障碍厚度。</summary>
        public float ObstacleDepth { get; }
        /// <summary>获取前墙命中点。</summary>
        public Vector3 WallPoint { get; }
        /// <summary>获取前墙法线。</summary>
        public Vector3 WallNormal { get; }
        /// <summary>获取顶部采样点。</summary>
        public Vector3 TopPoint { get; }
        /// <summary>获取沿角色前向细化得到的障碍后沿点。</summary>
        public Vector3 FarEdgePoint { get; }
        /// <summary>获取动画入口目标位置。</summary>
        public Vector3 EntryPosition { get; }
        /// <summary>获取动画结束目标位置。</summary>
        public Vector3 FinalPosition { get; }
        /// <summary>获取动画结束目标旋转。</summary>
        public Quaternion TargetRotation { get; }
    }

    /// <summary>保存 Grounded 分支准备好的 Jump Press 与首次穿越检测结果。</summary>
    public readonly struct PreparedTraversalAttempt
    {
        /// <summary>创建已准备的穿越尝试。</summary>
        public PreparedTraversalAttempt(
            InputRequestHandle jumpPressHandle,
            TraversalCandidate candidate)
        {
            JumpPressHandle = jumpPressHandle;
            Candidate = candidate;
        }

        /// <summary>获取触发本次检测的原始 Jump PressHandle。</summary>
        public InputRequestHandle JumpPressHandle { get; }
        /// <summary>获取本次检测得到的候选。</summary>
        public TraversalCandidate Candidate { get; }
    }
}
