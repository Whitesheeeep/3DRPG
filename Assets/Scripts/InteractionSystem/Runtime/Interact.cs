using System;
using UnityEngine;

namespace RPG.InteractionSystem
{
    /// <summary>
    /// 在目标对象上维护固定为 Trigger 的 BoxCollider，并把 Enter/Exit 直接交给玩家 Interactor。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class Interact : MonoBehaviour
    {
        #region 序列化字段与属性
        [SerializeField] private InteractionData interactionData = new InteractionData();
        [SerializeField] private IInteractionTarget targetComponent;

        private BoxCollider triggerCollider;

        /// <summary>获取通用交互目标实现；未显式配置时查找同一物体上的适配组件。</summary>
        public IInteractionTarget Target => ResolveTarget();

        /// <summary>获取当前 Trigger BoxCollider。</summary>
        public BoxCollider TriggerCollider => triggerCollider;

        #endregion

        #region Unity 生命周期

        /// <summary>获取碰撞体并在运行时应用配置。</summary>
        private void Awake()
        {
            triggerCollider = GetComponent<BoxCollider>();
            ApplyInteractionData();
        }

        /// <summary>在编辑器中持续同步碰撞体尺寸和位置。</summary>
        private void OnValidate()
        {
            interactionData ??= new InteractionData();
            interactionData.Normalize();
            if (triggerCollider == null) triggerCollider = GetComponent<BoxCollider>();
            if (triggerCollider != null) ApplyInteractionData();
        }

        /// <summary>把 Trigger 事件直接转发给进入范围的玩家 Interactor。</summary>
        /// <param name="other">进入当前 Trigger 的碰撞体。</param>
        private void OnTriggerEnter(Collider other)
        {
            if (!interactionData.Enabled) return;
            if ((interactionData.Layer.value & (1 << other.gameObject.layer)) == 0) return;
            InteractionInteractor interactor = other.GetComponentInParent<InteractionInteractor>();
            interactor?.AddCandidate(this);
        }

        /// <summary>把 Trigger Exit 事件直接转发给离开范围的玩家 Interactor。</summary>
        /// <param name="other">离开当前 Trigger 的碰撞体。</param>
        private void OnTriggerExit(Collider other)
        {
            InteractionInteractor interactor = other.GetComponentInParent<InteractionInteractor>();
            interactor?.RemoveCandidate(this);
        }

        #endregion

        #region 组件配置

        /// <summary>应用 InteractionData；IsTrigger 是固定契约，不作为配置字段暴露。</summary>
        private void ApplyInteractionData()
        {
            if (triggerCollider == null || interactionData == null) return;
            triggerCollider.isTrigger = true;
            triggerCollider.center = interactionData.Center;
            triggerCollider.size = interactionData.Size;
            triggerCollider.enabled = interactionData.Enabled;
        }

        /// <summary>解析显式配置或同一物体上的 IInteractionTarget 组件。</summary>
        /// <returns>找到的交互目标；不存在时为空。</returns>
        private IInteractionTarget ResolveTarget()
        {
            if (targetComponent is IInteractionTarget explicitTarget) return explicitTarget;
            MonoBehaviour[] components = GetComponents<MonoBehaviour>();
            for (int index = 0; index < components.Length; index++)
                if (components[index] is IInteractionTarget target) return target;
            return null;
        }

        private void OnGUI()
        {

        }
        #endregion
    }

    /// <summary>
    /// 保存通用交互 Trigger 使用的 3D BoxCollider 配置。
    /// </summary>
    [Serializable]
    public sealed class InteractionData
    {
        #region 序列化字段

        [SerializeField] private Vector3 center;
        [SerializeField] private Vector3 size = new Vector3(2f, 2f, 2f);
        [SerializeField] private LayerMask layer = ~0;
        [SerializeField] private bool enabled = true;

        #endregion

        #region 属性

        /// <summary>获取碰撞体相对位置。</summary>
        public Vector3 Center => center;

        /// <summary>获取碰撞体大小。</summary>
        public Vector3 Size => size;

        /// <summary>获取交互目标层过滤。</summary>
        public LayerMask Layer => layer;

        /// <summary>获取交互是否启用。</summary>
        public bool Enabled => enabled;

        #endregion

        #region 数据校正

        /// <summary>将配置尺寸限制为非负值，避免物理组件接收非法范围。</summary>
        public void Normalize()
        {
            size = new Vector3(Mathf.Max(0f, size.x), Mathf.Max(0f, size.y), Mathf.Max(0f, size.z));
        }

        #endregion
    }
}