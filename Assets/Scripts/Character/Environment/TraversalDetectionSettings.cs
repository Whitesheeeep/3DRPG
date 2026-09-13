using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.Character
{
    /// <summary>
    /// 保存 Traversal 环境检测使用的几何阈值、后沿细化参数和调试显示配置。
    /// 该对象由 PlayerFSMTransition 持有，检测器只读取它，不保存角色业务状态。
    /// </summary>
    [System.Serializable]
    public sealed class TraversalDetectionSettings
    {
        #region 几何检测配置

        [SerializeField, MinValue(0f), LabelText("最低障碍高度（米）"),
         Tooltip("低于该高度的障碍不会生成 Vault 或 Mantle 候选。")]
        private float minObstacleHeight = 0.7f;

        [SerializeField, MinValue(0f), LabelText("最高障碍高度（米）"),
         Tooltip("高于该高度的障碍不会生成 Vault 或 Mantle 候选。")]
        private float maxObstacleHeight = 2.2f;

        [SerializeField, MinValue(0f), LabelText("Vault 最大障碍高度（米）"),
         Tooltip("不高于该高度且厚度满足要求的障碍优先使用 Vault；更高障碍进入 Mantle 判断。")]
        private float vaultMaxObstacleHeight = 1.2f;

        [SerializeField, MinValue(0f), LabelText("前墙检测距离（米）"),
         Tooltip("从 CharacterRoot 向角色水平前方搜索前墙的最大距离。")]
        private float frontWallCastDistance = 1.15f;

        [SerializeField, MinValue(2), LabelText("前墙垂直采样数量"),
         Tooltip("沿角色高度执行的前墙射线数量。所有射线都会执行并保留 Debug 线。")]
        private int frontWallRayCount = 5;

        [SerializeField, MinValue(0f), MaxValue(1f), LabelText("墙面法线向上点积上限"),
         Tooltip("前墙法线与世界向上的点积上限；值越小越接近垂直墙面。")]
        private float maxWallNormalUpDot = 0.35f;

        [SerializeField, MinValue(0f), MaxValue(1f), LabelText("顶面法线向上点积下限"),
         Tooltip("顶部表面法线与世界向上的点积下限；值越大越接近平坦顶部。")]
        private float minTopNormalUpDot = 0.65f;

        [SerializeField, MinValue(0f), LabelText("顶部检测前向内缩（米）"),
         Tooltip("顶部向下探测点相对前墙命中点的前向偏移，避免射线落在墙面外。")]
        private float topCastForwardOffset = 0.04f;

        [SerializeField, MinValue(0f), LabelText("Vault 最大厚度（米）"),
         Tooltip("真实后沿深度不超过该值时，低矮障碍才允许使用 Vault。")]
        private float vaultMaxDepth = 0.55f;

        [SerializeField, MinValue(0.001f), LabelText("厚度初始采样间隔（米）"),
         Tooltip("沿顶部前向搜索后沿时的初始采样步长；首次失去支撑后还会进行二分细化。")]
        private float depthProbeStep = 0.05f;

        [SerializeField, MinValue(0f), LabelText("厚度探针起始高度（米）"),
         Tooltip("每个顶部厚度探针从表面上方该高度开始向下检测。")]
        private float depthProbeHeight = 0.08f;

        [SerializeField, MinValue(0.001f), LabelText("厚度探针向下距离（米）"),
         Tooltip("每个顶部厚度探针向下寻找支撑面的最大距离。")]
        private float depthProbeDistance = 0.3f;

        [SerializeField, MinValue(0), LabelText("后沿细化次数"),
         Tooltip("找到有支撑与无支撑的区间后执行的二分细化次数。")]
        private int farEdgeRefinementIterations = 5;

        [SerializeField, MinValue(0f), LabelText("Vault 后沿目标余量（米）"),
         Tooltip("Vault 目标修正点在真实后沿之外的前向距离，用于给动画结束姿态留下越过后沿的空间。")]
        private float vaultTargetForwardClearance = 0.2f;

        [SerializeField, MinValue(0f), LabelText("Mantle 顶部站立内缩（米）"),
         Tooltip("Mantle 优先采用的顶部站立内缩距离；检测器会在该期望点附近寻找最近的安全站位，不参与 Vault 后沿计算。")]
        private float mantleStandingInset = 0.20f;

        [SerializeField, MinValue(0.001f), LabelText("Mantle 目标搜索步长（米）"),
         Tooltip("Mantle 在期望内缩附近寻找安全目标时的搜索间隔；步长越小越精确，但会增加查询次数。")]
        private float mantleTargetSearchStep = 0.025f;

        [SerializeField, MinValue(0f), LabelText("Mantle 后沿安全余量（米）"),
         Tooltip("Mantle 搜索目标距离真实后沿的最小安全余量，避免把站位放在平台边缘或后沿阻挡附近。")]
        private float mantleFarEdgeClearance = 0.02f;

        [SerializeField, MinValue(0f), LabelText("Mantle 顶面高度容差（米）"),
         Tooltip("Mantle 支撑命中点相对顶部平面预测高度允许的最大偏差，避免误把下方地面当作顶部支撑。")]
        private float mantleSurfaceHeightTolerance = 0.08f;

        [SerializeField, MinValue(0f), LabelText("目标支撑探测高度（米）"),
         Tooltip("验证 Vault 远侧或 Mantle 顶部目标支撑时，向下探针的起始抬高。")]
        private float supportProbeHeight = 0.2f;

        [SerializeField, MinValue(0.001f), LabelText("目标支撑探测距离（米）"),
         Tooltip("验证目标下方是否存在有效支撑表面的向下检测距离。")]
        private float supportProbeDistance = 2.5f;

        [SerializeField, MinValue(0f), MaxValue(0.1f), LabelText("目标胶囊脚底间隙（米）"),
         Tooltip("仅将 Vault/Mantle 目标空间查询胶囊整体向上移动，避免脚底与支撑面接触时被 OverlapCapsule 判定为阻挡。不修改目标脚点、Traversal FinalPosition 或角色最终站立高度。")]
        private float targetCapsuleGroundClearance = 0.02f;

        #endregion

        #region Debug 配置

        [SerializeField, LabelText("开启 Traversal Debug"),
         Tooltip("开启后保留本次真实检测执行过的全部射线、探针和最终选中结果。")]
        private bool drawDebug = true;

        [SerializeField, MinValue(0f), LabelText("Debug 保持时间（秒）"),
         Tooltip("本次检测完成后，所有普通探测线和最终结果线保持显示的时间。")]
        private float debugDuration = 2f;

        #endregion

        #region 查询

        /// <summary>获取最低障碍高度。</summary>
        public float MinObstacleHeight => minObstacleHeight;
        /// <summary>获取最高障碍高度。</summary>
        public float MaxObstacleHeight => maxObstacleHeight;
        /// <summary>获取 Vault 最大障碍高度。</summary>
        public float VaultMaxObstacleHeight => vaultMaxObstacleHeight;
        /// <summary>获取前墙检测距离。</summary>
        public float FrontWallCastDistance => frontWallCastDistance;
        /// <summary>获取前墙垂直采样数量。</summary>
        public int FrontWallRayCount => frontWallRayCount;
        /// <summary>获取墙面法线向上点积上限。</summary>
        public float MaxWallNormalUpDot => maxWallNormalUpDot;
        /// <summary>获取顶面法线向上点积下限。</summary>
        public float MinTopNormalUpDot => minTopNormalUpDot;
        /// <summary>获取顶部检测前向内缩。</summary>
        public float TopCastForwardOffset => topCastForwardOffset;
        /// <summary>获取 Vault 最大厚度。</summary>
        public float VaultMaxDepth => vaultMaxDepth;
        /// <summary>获取厚度初始采样间隔。</summary>
        public float DepthProbeStep => depthProbeStep;
        /// <summary>获取厚度探针起始高度。</summary>
        public float DepthProbeHeight => depthProbeHeight;
        /// <summary>获取厚度探针向下距离。</summary>
        public float DepthProbeDistance => depthProbeDistance;
        /// <summary>获取后沿二分细化次数。</summary>
        public int FarEdgeRefinementIterations => farEdgeRefinementIterations;
        /// <summary>获取 Vault 后沿目标余量。</summary>
        public float VaultTargetForwardClearance => vaultTargetForwardClearance;
        /// <summary>获取 Mantle 顶部站立内缩。</summary>
        public float MantleStandingInset => mantleStandingInset;
        /// <summary>获取 Mantle 安全目标搜索步长。</summary>
        public float MantleTargetSearchStep => mantleTargetSearchStep;
        /// <summary>获取 Mantle 后沿安全余量。</summary>
        public float MantleFarEdgeClearance => mantleFarEdgeClearance;
        /// <summary>获取 Mantle 顶面高度容差。</summary>
        public float MantleSurfaceHeightTolerance => mantleSurfaceHeightTolerance;
        /// <summary>获取目标支撑探测高度。</summary>
        public float SupportProbeHeight => supportProbeHeight;
        /// <summary>获取目标支撑探测距离。</summary>
        public float SupportProbeDistance => supportProbeDistance;
        /// <summary>获取目标空间查询胶囊的脚底间隙。</summary>
        public float TargetCapsuleGroundClearance => targetCapsuleGroundClearance;
        /// <summary>获取是否绘制本次检测 Debug。</summary>
        public bool DrawDebug => drawDebug;
        /// <summary>获取 Debug 线保持时间。</summary>
        public float DebugDuration => debugDuration;

        #endregion

        #region 校验

        /// <summary>校验 Traversal 几何和调试配置满足检测器的输入契约。</summary>
        public void Validate()
        {
            if (minObstacleHeight <= 0f)
                throw new System.InvalidOperationException("Traversal 检测的最低障碍高度必须大于 0。 ");
            if (maxObstacleHeight <= minObstacleHeight)
                throw new System.InvalidOperationException("Traversal 检测的最高障碍高度必须大于最低障碍高度。 ");
            if (vaultMaxObstacleHeight < minObstacleHeight ||
                vaultMaxObstacleHeight > maxObstacleHeight)
                throw new System.InvalidOperationException(
                    "Traversal 检测的 Vault 最大障碍高度必须位于最低和最高障碍高度之间。 ");
            if (frontWallCastDistance <= 0f)
                throw new System.InvalidOperationException("Traversal 检测的前墙检测距离必须大于 0。 ");
            if (frontWallRayCount < 2)
                throw new System.InvalidOperationException("Traversal 检测的前墙垂直采样数量不能小于 2。 ");
            if (maxWallNormalUpDot < 0f || maxWallNormalUpDot > 1f)
                throw new System.InvalidOperationException("Traversal 检测的墙面法线向上点积上限必须位于 0 到 1。 ");
            if (minTopNormalUpDot < 0f || minTopNormalUpDot > 1f)
                throw new System.InvalidOperationException("Traversal 检测的顶面法线向上点积下限必须位于 0 到 1。 ");
            if (vaultMaxDepth <= 0f)
                throw new System.InvalidOperationException("Traversal 检测的 Vault 最大厚度必须大于 0。 ");
            if (depthProbeStep <= 0f || depthProbeStep >= vaultMaxDepth)
                throw new System.InvalidOperationException("Traversal 检测的厚度初始采样间隔必须大于 0 且小于 Vault 最大厚度。 ");
            if (depthProbeHeight < 0f || depthProbeDistance <= 0f)
                throw new System.InvalidOperationException("Traversal 检测的厚度探针高度和向下距离无效。 ");
            if (farEdgeRefinementIterations < 0)
                throw new System.InvalidOperationException("Traversal 检测的后沿细化次数不能小于 0。 ");
            if (vaultTargetForwardClearance < 0f || mantleStandingInset < 0f)
                throw new System.InvalidOperationException("Traversal 检测的 Vault 后沿目标余量和 Mantle 顶部内缩不能小于 0。 ");
            if (mantleTargetSearchStep <= 0f || mantleTargetSearchStep > vaultMaxDepth)
                throw new System.InvalidOperationException("Traversal 检测的 Mantle 目标搜索步长必须大于 0 且不大于 Vault 最大厚度。 ");
            if (mantleFarEdgeClearance < 0f || mantleFarEdgeClearance >= vaultMaxDepth)
                throw new System.InvalidOperationException("Traversal 检测的 Mantle 后沿安全余量必须大于等于 0 且小于 Vault 最大厚度。 ");
            if (mantleSurfaceHeightTolerance < 0f)
                throw new System.InvalidOperationException("Traversal 检测的 Mantle 顶面高度容差不能小于 0。 ");
            if (supportProbeHeight < 0f || supportProbeDistance <= 0f)
                throw new System.InvalidOperationException("Traversal 检测的目标支撑探针参数无效。 ");
            if (targetCapsuleGroundClearance < 0f || targetCapsuleGroundClearance > 0.1f)
                throw new System.InvalidOperationException("Traversal 检测的目标胶囊脚底间隙必须位于 0 到 0.1 米之间。 ");
            if (debugDuration < 0f)
                throw new System.InvalidOperationException("Traversal Debug 保持时间不能小于 0。 ");
        }

        #endregion
    }
}
