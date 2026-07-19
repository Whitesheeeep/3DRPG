using UnityEngine;

namespace RPG.Character.DirectionalLocomotion
{
    /// <summary>为 Cinemachine 提供独立于根运动瞬时旋转的平滑跟随目标。</summary>
    [DisallowMultipleComponent]
    public sealed class DirectionalCameraFollowTarget : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 positionOffset = new Vector3(0f, 1.5f, 0f);
        [SerializeField, Min(0.01f)] private float yawSmoothTime = 0.35f;

        private float _yawVelocity;

        public Transform Target => target;
        public float CurrentYaw => transform.eulerAngles.y;

        private void OnEnable()
        {
            SnapToTarget();
        }

        private void LateUpdate()
        {
            if (target == null) return;
            transform.position = target.position + positionOffset;
            float yaw = Mathf.SmoothDampAngle(
                transform.eulerAngles.y,
                target.eulerAngles.y,
                ref _yawVelocity,
                yawSmoothTime);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>设置需要跟随的角色，并立即同步位置与朝向。</summary>
        public void SetTarget(Transform value)
        {
            target = value;
            SnapToTarget();
        }

        /// <summary>立即同步到角色，供初始化或手动测试使用。</summary>
        public void SnapToTarget()
        {
            if (target == null) return;
            transform.position = target.position + positionOffset;
            transform.rotation = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
            _yawVelocity = 0f;
        }
    }
}
