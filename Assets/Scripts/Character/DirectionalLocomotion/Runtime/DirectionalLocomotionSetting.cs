using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.Character.DirectionalLocomotion
{
    /// <summary>Animancer 方向起步 Demo 的动画和移动参数。</summary>
    [CreateAssetMenu(fileName = "DirectionalLocomotionSetting", menuName = "RPG/Character/Directional Locomotion Setting")]
    public sealed class DirectionalLocomotionSetting : ScriptableObject
    {
        [Title("移动参数")]
        [ReadOnly, MinValue(0.01f), LabelText("Walk 动画平均速度")]
        public float walkAverageSpeed;
        [MinValue(0.01f), LabelText("目标行走速度")] public float walkSpeed = 1f;
        [MinValue(0.01f), LabelText("起步追向速度")] public float startTurnSpeed = 180f;
        [MinValue(0.01f), LabelText("MoveLoop 转向锐度")] public float moveLoopTurnSharpness = 4f;
        [MinValue(0f)] public float gravity = 20f;
        [Range(0f, 1f)] public float inputDeadZone = 0.1f;
        [Range(0f, 0.5f), LabelText("动画淡入时间")] public float fadeDuration = 0.1f;
        [Range(0f, 1f), LabelText("起步追向开始时间")] public float startDirectionFollowTime = 0.4f;
        [Range(0.01f, 1f), LabelText("输入方向重算阈值")] public float inputDirectionChangeThreshold = 0.15f;

        [Title("基础动画")]
        [Required] public AnimationClip idle;
        [Required] public AnimationClip walkForward;

        [Title("方向起步动画")]
        [Required, LabelText("左 135°")] public AnimationClip startLeft135;
        [Required, LabelText("左 90°")] public AnimationClip startLeft90;
        [Required, LabelText("左 45°")] public AnimationClip startLeft45;
        [Required, LabelText("前方 0°")] public AnimationClip startForward;
        [Required, LabelText("右 45°")] public AnimationClip startRight45;
        [Required, LabelText("右 90°")] public AnimationClip startRight90;
        [Required, LabelText("右 135°")] public AnimationClip startRight135;
        [Required, LabelText("后方 180°")] public AnimationClip startRight180;

        public float WalkPlaybackSpeed => walkAverageSpeed > 0f ? walkSpeed / walkAverageSpeed : 1f;
        public bool IsValid => walkAverageSpeed > 0.001f && idle != null && walkForward != null &&
                               startLeft135 != null && startLeft90 != null && startLeft45 != null &&
                               startForward != null && startRight45 != null && startRight90 != null &&
                               startRight135 != null && startRight180 != null;

#if UNITY_EDITOR
        /// <summary>从 FemaleMovementAnimsetPro 更新动画引用和 Walk 平均速度。</summary>
        [Button("更新 Animancer Demo 配置", ButtonSizes.Large)]
        public void UpdateDemoSetting()
        {
            UnityEditor.Selection.activeObject = this;
            UnityEditor.EditorApplication.ExecuteMenuItem("Tools/RPG/Directional Locomotion/Update Animancer Demo Setting");
        }
#endif
    }
}


