#if UNITY_EDITOR
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.CameraSystem.Tests
{
    /// <summary>
    /// 通过 Odin 按钮调用真实 Camera Shake API，帮助观察持续 Noise 与瞬时 Impulse 的差异。
    /// </summary>
    public sealed class CameraShakeOdinTester : MonoBehaviour
    {
        #region 测试输入与状态

        [Title("持续 Noise")]
        [SerializeField] private CameraShakeProfile shakeProfile;
        [SerializeField, MinValue(0f)] private float shakeStrength = 1f;
        [SerializeField] private int seed;

        [Title("瞬时 Impulse")]
        [SerializeField] private CameraImpulseProfile impulseProfile;
        [SerializeField, MinValue(0f)] private float impulseAmplitude = 1f;
        [SerializeField] private Vector3 impulseDirection = Vector3.down;

        [Title("运行状态")]
        [ShowInInspector, ReadOnly] private CameraShakeHandle activeShake;
        [ShowInInspector, ReadOnly] private CameraImpulseHandle activeImpulse;

        #endregion

        #region Odin 测试入口

        /// <summary>
        /// 使用当前 Profile、强度和 Seed 播放一条持续 Noise。
        /// </summary>
        [Button("播放持续 Shake", ButtonSizes.Large)]
        public void PlayShake()
        {
            activeShake = CinemachineManager.Instance.PlayShake(shakeProfile, shakeStrength, seed);
            Debug.Log($"播放 Shake：{shakeProfile.name}，Strength={shakeStrength}，Seed={seed}");
        }

        /// <summary>
        /// 将 Inspector 当前强度写入活动 Shake，不改变请求创建层级。
        /// </summary>
        [Button("应用 Shake 强度")]
        public void ApplyShakeStrength()
        {
            bool result = CinemachineManager.Instance.TrySetShakeStrength(activeShake, shakeStrength);
            Debug.Log($"调整 Shake 强度：Result={result}，Strength={shakeStrength}");
        }

        /// <summary>
        /// 使用 Profile 的 FadeOut 平滑停止当前持续 Shake。
        /// </summary>
        [Button("淡出停止 Shake")]
        public void StopShake()
        {
            bool result = CinemachineManager.Instance.TryStopShake(activeShake);
            Debug.Log($"淡出停止 Shake：Result={result}");
        }

        /// <summary>
        /// 不经过 FadeOut，当帧移除当前持续 Shake。
        /// </summary>
        [Button("立即停止 Shake")]
        public void StopShakeImmediately()
        {
            bool result = CinemachineManager.Instance.TryStopShake(activeShake, true);
            Debug.Log($"立即停止 Shake：Result={result}");
        }

        /// <summary>
        /// 使用 Inspector 指定方向发射一次 Uniform Impulse。
        /// </summary>
        [Button("发射 Impulse", ButtonSizes.Large)]
        public void EmitImpulse()
        {
            activeImpulse = CinemachineManager.Instance.EmitImpulse(
                impulseProfile, impulseDirection, impulseAmplitude);
            Debug.Log($"发射 Impulse：{impulseProfile.name}，Amplitude={impulseAmplitude}，Direction={impulseDirection}");
        }

        /// <summary>
        /// 只取消 Tester 最近一次发射且仍在有效期内的 Impulse。
        /// </summary>
        [Button("取消 Impulse")]
        public void CancelImpulse()
        {
            bool result = CinemachineManager.Instance.TryCancelImpulse(activeImpulse);
            Debug.Log($"取消 Impulse：Result={result}");
        }

        #endregion
    }
}
#endif
