using System;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace RPG.Game.UI.EquipmentDevelopment
{
    /// <summary>
    /// 装备培养窗口的专用武器预览 Rig。
    /// Rig 固定在远离主场景的位置，只在有有效模型和 RenderTexture 时启用摄像机与灯光。
    /// </summary>
    [DisallowMultipleComponent]
    [InfoBox("加载的武器世界 Prefab 必须在自身或子节点中包含 Renderer；运行时会递归收集 Renderer 自动取景。")]
    public sealed class WeaponPreviewRenderRig : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField, Required] private Camera previewCamera;
        [SerializeField, Required] private Light previewLight;
        // Pivot 是位置，用于旋转
        [SerializeField, Required] private Transform modelPivot;
        [SerializeField, Required] private Transform modelRoot;
        [SerializeField, MinValue(1f)] private float framingPadding = 1.12f;
        [SerializeField, MinValue(0f)] private float rotationDegreesPerSecond = 12f;

        #endregion

        #region 状态字段

        private bool rotationEnabled;
        private bool runtimeFramePrepared;
        private float currentRotationDegrees;
        private Vector3 configuredModelRootLocalPosition;
        private Quaternion configuredModelRootLocalRotation;
        private Vector3 configuredModelRootLocalScale;

        #endregion

        #region 属性

        /// <summary>获取专用预览摄像机。</summary>
        public Camera PreviewCamera => previewCamera;

        /// <summary>获取专用预览灯光。</summary>
        public Light PreviewLight => previewLight;

        /// <summary>获取模型旋转根节点。</summary>
        public Transform ModelPivot => modelPivot;

        /// <summary>获取模型实例挂载节点。</summary>
        public Transform ModelRoot => modelRoot;

        /// <summary>获取 Prefab 配置的 ModelRoot 初始局部位置。</summary>
        public Vector3 ConfiguredModelRootLocalPosition => configuredModelRootLocalPosition;

        /// <summary>获取 Prefab 配置的 ModelRoot 初始局部旋转。</summary>
        public Quaternion ConfiguredModelRootLocalRotation => configuredModelRootLocalRotation;

        /// <summary>获取 Prefab 配置的 ModelRoot 初始局部缩放。</summary>
        public Vector3 ConfiguredModelRootLocalScale => configuredModelRootLocalScale;

        /// <summary>获取自动取景留白比例。</summary>
        public float FramingPadding => Mathf.Max(1f, framingPadding);

        /// <summary>获取模型自动旋转速度。</summary>
        public float RotationDegreesPerSecond => Mathf.Max(0f, rotationDegreesPerSecond);

        #endregion

        #region 生命周期与校验

        /// <summary>
        /// 初始化 Rig 的离屏渲染默认状态，避免窗口预加载阶段产生额外渲染。
        /// </summary>
        private void Awake()
        {
            ValidateConfiguration();
            // ModelRoot 是美术配置的统一展示姿态；必须在任何运行时复位前缓存，避免窗口重开时丢失 Prefab 旋转。
            configuredModelRootLocalPosition = modelRoot.localPosition;
            configuredModelRootLocalRotation = modelRoot.localRotation;
            configuredModelRootLocalScale = modelRoot.localScale;
            DisablePreview();
        }

        /// <summary>
        /// 按非缩放时间驱动武器模型缓慢旋转。
        /// </summary>
        private void LateUpdate()
        {
            if (!rotationEnabled || modelPivot == null) return;
            currentRotationDegrees = Mathf.Repeat(
                currentRotationDegrees + RotationDegreesPerSecond * Time.unscaledDeltaTime,
                360f);
            modelPivot.localRotation = Quaternion.Euler(0f, currentRotationDegrees, 0f);
        }

        /// <summary>
        /// 校验 Camera、Light、模型节点和 URP Camera 配置依赖。
        /// </summary>
        /// <exception cref="InvalidOperationException">Prefab 依赖未绑定或项目 Layer 缺失时抛出。</exception>
        public void ValidateConfiguration()
        {
            if (previewCamera == null || previewLight == null || modelPivot == null || modelRoot == null)
                throw new InvalidOperationException("[WeaponPreviewRenderRig] Camera、Light、ModelPivot 和 ModelRoot 必须全部绑定。");
            if (modelRoot.parent != modelPivot)
                throw new InvalidOperationException("[WeaponPreviewRenderRig] ModelRoot 必须是 ModelPivot 的直接子节点。");
            if (LayerMask.NameToLayer("EquipmentPreview") < 0)
                throw new InvalidOperationException("[WeaponPreviewRenderRig] 项目缺少 EquipmentPreview Layer。");
            if (previewCamera.GetComponent<UniversalAdditionalCameraData>() == null)
                throw new InvalidOperationException("[WeaponPreviewRenderRig] PreviewCamera 缺少 UniversalAdditionalCameraData。");
        }

        #endregion

        #region 运行时控制

        /// <summary>
        /// 在 WindowBase 完成窗口根节点 Reset 后建立独立的预览坐标系。
        /// </summary>
        public void PrepareRuntimeFrame()
        {
            ValidateConfiguration();

            // 该方法由窗口显示流程调用，调用时父级已经完成 Reset，不能在 Awake 中提前写世界坐标。
            transform.localPosition = new Vector3(10000f, 10000f, 10000f);
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            previewCamera.transform.localPosition = new Vector3(0f, 0f, -10f);
            previewCamera.transform.localRotation = Quaternion.identity;
            previewCamera.transform.localScale = Vector3.one;
            previewLight.transform.localScale = Vector3.one;
            modelPivot.localPosition = Vector3.zero;
            modelPivot.localRotation = Quaternion.identity;
            modelPivot.localScale = Vector3.one;
            RestoreConfiguredModelRootTransform();
            currentRotationDegrees = 0f;
            runtimeFramePrepared = true;

            Debug.Log("[WeaponPreviewRenderRig] 已在窗口根节点 Reset 后初始化独立预览坐标系。", this);
        }

        /// <summary>
        /// 将 Rig 设置为可见渲染状态，并绑定 RenderTexture。
        /// </summary>
        /// <param name="renderTexture">摄像机输出纹理。</param>
        public void EnablePreview(RenderTexture renderTexture)
        {
            if (renderTexture == null) throw new ArgumentNullException(nameof(renderTexture));
            if (!runtimeFramePrepared)
                throw new InvalidOperationException("[WeaponPreviewRenderRig] 启用预览前必须先调用 PrepareRuntimeFrame。");
            previewCamera.targetTexture = renderTexture;
            previewCamera.enabled = true;
            previewLight.enabled = true;
            rotationEnabled = true;
        }

        /// <summary>
        /// 关闭摄像机与灯光，并解除旋转状态和 RenderTexture 引用。
        /// </summary>
        public void DisablePreview()
        {
            rotationEnabled = false;
            if (previewCamera != null)
            {
                previewCamera.enabled = false;
                previewCamera.targetTexture = null;
            }

            if (previewLight != null)
                previewLight.enabled = false;
        }

        /// <summary>
        /// 清空模型节点的局部状态，为下一次实例化恢复稳定起点。
        /// </summary>
        public void ResetModelRoot()
        {
            modelPivot.localPosition = Vector3.zero;
            modelPivot.localRotation = Quaternion.identity;
            modelPivot.localScale = Vector3.one;
            RestoreConfiguredModelRootTransform();
            currentRotationDegrees = 0f;
        }

        /// <summary>
        /// 恢复 ModelRoot 在 Prefab 中配置的展示姿态，避免把统一预览旋转误清成单位旋转。
        /// </summary>
        private void RestoreConfiguredModelRootTransform()
        {
            modelRoot.localPosition = configuredModelRootLocalPosition;
            modelRoot.localRotation = configuredModelRootLocalRotation;
            modelRoot.localScale = configuredModelRootLocalScale;
        }

        #endregion
    }
}
