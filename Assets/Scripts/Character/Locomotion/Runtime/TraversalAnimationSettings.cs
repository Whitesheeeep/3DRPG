using Animancer;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.Character
{
    /// <summary>保存一段 Traversal 动画的目标高度、修正窗口、输入开放时间和位置曲线。</summary>
    [System.Serializable]
    public sealed class TraversalAnimationSettings
    {
        #region 配置字段

        [SerializeField, LabelText("动画过渡")]
        private ClipTransition transition;
        [SerializeField, MinValue(0f), LabelText("作者高度（米）")]
        private float authoredHeight;
        [SerializeField, LabelText("入口修正归一化区间")]
        private Vector2 entryCorrectionNormalizedRange;
        [SerializeField, LabelText("目标修正归一化区间")]
        private Vector2 targetCorrectionNormalizedRange;
        [SerializeField, LabelText("入口距离偏移（米）")]
        [Tooltip("入口位置在动画作者位置基础上增加该偏移后，才允许进入翻越。")]
        private float entryDistanceOffset;
        [SerializeField, LabelText("目标高度偏移（米）")]
        [Tooltip("在冻结的 Traversal 最终位置上额外增加的世界 Y 高度偏移。")]
        private float targetHeightOffset;
        [SerializeField, LabelText("位置修正权重曲线"),
         Tooltip("横轴为当前修正区间的局部进度，纵轴为累计完成的位置修正比例。两者范围均为 0 到 1。")]
        private AnimationCurve positionCorrectionWeightCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField, MinValue(0f), MaxValue(1f), LabelText("输入开放时间"),
         Tooltip("动画达到该归一化进度后，允许 Jump 或已接地的 Move 退出当前 Traversal；Move 会进入 WalkStart 或 RunStart。该值不是秒数。")]
        private float inputOpenNormalizedTime = 0.7f;
        [SerializeField, MinValue(0f), MaxValue(1f), LabelText("下落检测开放时间"),
         Tooltip("动画达到该归一化进度后，才允许根据未接地和实际垂直速度进入 Fall。")]
        private float fallDetectionNormalizedTime = 0.85f;

        #endregion

        #region 查询

        /// <summary>获取实际播放的动画过渡。</summary>
        public ClipTransition Transition => transition;
        /// <summary>获取动画作者高度。</summary>
        public float AuthoredHeight => authoredHeight;
        /// <summary>获取入口方向修正归一化区间。</summary>
        public Vector2 EntryCorrectionNormalizedRange => entryCorrectionNormalizedRange;
        /// <summary>获取目标位置修正归一化区间。</summary>
        public Vector2 TargetCorrectionNormalizedRange => targetCorrectionNormalizedRange;
        /// <summary>获取入口距离偏移。</summary>
        public float EntryDistanceOffset => entryDistanceOffset;
        /// <summary>获取目标高度偏移。</summary>
        public float TargetHeightOffset => targetHeightOffset;
        /// <summary>获取位置修正累计权重曲线。</summary>
        public AnimationCurve PositionCorrectionWeightCurve => positionCorrectionWeightCurve;
        /// <summary>获取输入开放归一化时间。</summary>
        public float InputOpenNormalizedTime => inputOpenNormalizedTime;
        /// <summary>获取下落检测开放归一化时间。</summary>
        public float FallDetectionNormalizedTime => fallDetectionNormalizedTime;

        #endregion

        #region 校验

        /// <summary>校验该动画配置在序列化边界上可以参与 Traversal。</summary>
        /// <param name="fieldName">配置槽位名称。</param>
        public void Validate(string fieldName)
        {
            if (transition == null || !transition.IsValid)
                throw new System.InvalidOperationException(
                    $"PlayerFSMTransition 的 Traversal 动画槽位 '{fieldName}' 未配置有效动画。 ");
            if (authoredHeight <= 0f)
                throw new System.InvalidOperationException(
                    $"PlayerFSMTransition 的 Traversal 动画槽位 '{fieldName}' 作者高度必须大于 0。 ");
            ValidateCorrectionRange(entryCorrectionNormalizedRange, fieldName, "入口");
            ValidateCorrectionRange(targetCorrectionNormalizedRange, fieldName, "目标");
            if (entryCorrectionNormalizedRange.y > targetCorrectionNormalizedRange.x)
                throw new System.InvalidOperationException(
                    $"PlayerFSMTransition 的 Traversal 动画槽位 '{fieldName}' 入口修正区间必须早于或紧邻目标修正区间。 ");
            ValidateCorrectionWeightCurve(fieldName);
            if (inputOpenNormalizedTime < 0f || inputOpenNormalizedTime > 1f)
                throw new System.InvalidOperationException(
                    $"PlayerFSMTransition 的 Traversal 动画槽位 '{fieldName}' 输入开放时间必须位于 0 到 1。 ");
            if (fallDetectionNormalizedTime < inputOpenNormalizedTime ||
                fallDetectionNormalizedTime > 1f)
                throw new System.InvalidOperationException(
                    $"PlayerFSMTransition 的 Traversal 动画槽位 '{fieldName}' 下落检测时间必须不早于输入开放时间且不大于 1。 ");
        }

        /// <summary>校验根运动位置修正区间位于单次动画归一化时间范围内。</summary>
        /// <param name="range">待校验的归一化时间区间。</param>
        /// <param name="fieldName">配置槽位名称。</param>
        /// <param name="rangeName">区间用途名称。</param>
        private static void ValidateCorrectionRange(Vector2 range, string fieldName, string rangeName)
        {
            if (range.x < 0f || range.x > 1f || range.y < 0f || range.y > 1f || range.y < range.x)
                throw new System.InvalidOperationException(
                    $"PlayerFSMTransition 的 Traversal 动画槽位 '{fieldName}' {rangeName}修正区间必须满足 0 <= 开始 <= 结束 <= 1。 ");
        }

        /// <summary>校验位置修正曲线从零单调过渡到一。</summary>
        /// <param name="fieldName">用于异常上下文的动画槽位名称。</param>
        private void ValidateCorrectionWeightCurve(string fieldName)
        {
            if (positionCorrectionWeightCurve == null || positionCorrectionWeightCurve.length < 2)
                throw new System.InvalidOperationException(
                    $"PlayerFSMTransition 的 Traversal 动画槽位 '{fieldName}' 未配置有效的位置修正权重曲线。 ");

            const float tolerance = 0.001f;
            if (Mathf.Abs(positionCorrectionWeightCurve.Evaluate(0f)) > tolerance ||
                Mathf.Abs(positionCorrectionWeightCurve.Evaluate(1f) - 1f) > tolerance)
                throw new System.InvalidOperationException(
                    $"PlayerFSMTransition 的 Traversal 动画槽位 '{fieldName}' 位置修正权重曲线必须从 0 开始并在 1 结束。 ");

            float previous = 0f;
            for (int sampleIndex = 0; sampleIndex <= 32; sampleIndex++)
            {
                float progress = sampleIndex / 32f;
                float value = positionCorrectionWeightCurve.Evaluate(progress);
                if (value < -tolerance || value > 1f + tolerance || value + tolerance < previous)
                    throw new System.InvalidOperationException(
                        $"PlayerFSMTransition 的 Traversal 动画槽位 '{fieldName}' 位置修正权重曲线必须在 0 到 1 内单调递增。 ");
                previous = value;
            }
        }

        #endregion
    }
}
