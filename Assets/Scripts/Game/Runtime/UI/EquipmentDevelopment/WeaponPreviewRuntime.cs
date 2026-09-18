using System;
using Cysharp.Threading.Tasks;
using RPG.Game.UI.Views.WeaponDevelopment;
using RPG.ItemSystem;
using UnityEngine;
using WS_Modules.ResLoadModule;

namespace RPG.Game.UI.EquipmentDevelopment
{
    /// <summary>
    /// 负责装备培养窗口武器模型的异步加载、离屏渲染、自动取景和资源释放。
    /// 实例只由窗口 Controller 在武器目标可见期间创建，不参与统一窗口预加载。
    /// </summary>
    public sealed class WeaponPreviewRuntime : IDisposable
    {
        #region 依赖与状态字段

        private readonly WeaponPreviewRenderRig renderRig;
        private readonly WeaponPreviewViewportView viewportView;
        private GameObject modelInstance;
        private string loadedPrefabAddress;
        private RenderTexture renderTexture;
        private Vector2Int renderTexturePixelSize;
        private Vector2Int pendingRenderTexturePixelSize;
        private float pendingRenderTextureSizeStableTime;
        private float fixedPreviewAspect;
        private bool hasPreviewFrame;
        private Bounds previewLocalBounds;
        private float frozenRotationRadiusXZ;
        private float frozenVerticalHalfExtent;
        private Vector3 frozenPivotPositionInRig;
        private bool hasFrozenRotationEnvelope;
        private Vector3 lastWorldUnitsPerLocalAxis;
        private string currentPreviewItemId;
        private int requestVersion;
        private bool disposed;

        private const float RenderTextureResizeStabilitySeconds = 0.1f;

        #endregion

        #region 初始化与释放

        /// <summary>
        /// 创建窗口级武器预览运行时；构造过程不加载模型、不创建 RenderTexture。
        /// </summary>
        /// <param name="rig">窗口 Prefab 中显式绑定的专用 Rig。</param>
        /// <param name="viewport">窗口中的 RawImage View。</param>
        public WeaponPreviewRuntime(WeaponPreviewRenderRig rig, WeaponPreviewViewportView viewport)
        {
            renderRig = rig ?? throw new ArgumentNullException(nameof(rig));
            viewportView = viewport ?? throw new ArgumentNullException(nameof(viewport));
            renderRig.ValidateConfiguration();
            viewportView.ValidateConfiguration();
        }

        /// <summary>
        /// 释放模型实例、资源引用、RenderTexture 和 Rig 渲染状态。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            requestVersion++;
            Clear();
        }

        #endregion

        #region 模型加载

        /// <summary>
        /// 异步加载并显示指定武器的世界模型。
        /// </summary>
        /// <param name="definition">当前武器 Definition。</param>
        /// <returns>模型加载、取景和显示流程完成的任务。</returns>
        public async UniTask ShowWeaponAsync(WeaponDefinition definition)
        {
            if (disposed) throw new ObjectDisposedException(nameof(WeaponPreviewRuntime));
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            Clear();
            // 清理会使所有在途请求失效；新请求必须在清理之后取得版本号，避免切换目标时旧结果回写。
            int currentRequestVersion = ++requestVersion;
            currentPreviewItemId = definition.ItemId.ToString();
            viewportView.SetLoading();
            renderRig.PrepareRuntimeFrame();

            string prefabAddress = definition.WorldPrefabAddress;
            if (string.IsNullOrWhiteSpace(prefabAddress))
            {
                Debug.LogWarning($"[WeaponPreviewRuntime] 武器 {definition.ItemId} 未配置世界模型地址，保持预览区空白。", definition);
                viewportView.SetUnavailable(string.Empty);
                return;
            }

            Debug.Log($"[WeaponPreviewRuntime] 开始加载武器模型：ItemId={definition.ItemId}，Address={prefabAddress}。", definition);
            GameObject loadedModel = null;
            try
            {
                loadedModel = await ResSystem.Instance.InstantiateAsync(prefabAddress, renderRig.ModelRoot);
            }
            catch (Exception exception)
            {
                if (currentRequestVersion == requestVersion && !disposed)
                {
                    Debug.LogException(exception);
                    viewportView.SetUnavailable(string.Empty);
                }

                return;
            }

            if (disposed || currentRequestVersion != requestVersion || loadedModel == null)
            {
                if (loadedModel != null) UnityEngine.Object.Destroy(loadedModel);
                UnloadPrefab(prefabAddress);
                return;
            }

            modelInstance = loadedModel;
            loadedPrefabAddress = prefabAddress;
            PrepareModelHierarchy(modelInstance);
            if (!TryCalculateLocalBounds(out Bounds initialLocalBounds))
            {
                Debug.LogError($"[WeaponPreviewRuntime] 武器模型没有有效 Renderer：ItemId={definition.ItemId}，Address={prefabAddress}。", definition);
                Clear();
                viewportView.SetUnavailable(string.Empty);
                return;
            }

            renderRig.ModelPivot.localRotation = Quaternion.identity;
            CenterModel(initialLocalBounds);
            if (!TryCalculateLocalBounds(out Bounds localBounds))
            {
                Clear();
                viewportView.SetUnavailable(string.Empty);
                return;
            }

            previewLocalBounds = localBounds;
            FreezeRotationEnvelope(localBounds);

            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
            if (disposed || currentRequestVersion != requestVersion)
            {
                // 过期请求只能清理自己刚创建的对象，不能清掉后来请求已经显示的新模型。
                if (modelInstance != null && ReferenceEquals(modelInstance, loadedModel) && loadedPrefabAddress == prefabAddress)
                {
                    DestroyModelAndUnloadPrefab();
                }
                return;
            }

            Vector2Int targetPixelSize = viewportView.GetTargetPixelSize();
            fixedPreviewAspect = CalculateAspect(targetPixelSize);
            CreateRenderTexture(CalculateRenderTextureSize(targetPixelSize, fixedPreviewAspect));
            ConfigureCamera(localBounds);
            hasPreviewFrame = true;
            renderRig.EnablePreview(renderTexture);
            viewportView.SetTexture(renderTexture);
            Debug.Log($"[WeaponPreviewRuntime] 武器模型预览完成：ItemId={definition.ItemId}，Address={prefabAddress}。", definition);
        }

        #endregion

        #region 清理与渲染

        /// <summary>
        /// 清理当前预览，但保留可复用的 Rig Component。
        /// </summary>
        public void Clear()
        {
            // 即使没有立即开始下一次加载，也要使迟到的异步结果失效，例如武器切换到圣遗物时。
            requestVersion++;
            pendingRenderTexturePixelSize = default;
            pendingRenderTextureSizeStableTime = 0f;
            renderRig.DisablePreview();
            viewportView.Clear();
            DestroyModelAndUnloadPrefab();
            ReleaseRenderTexture();
            renderRig.ResetModelRoot();
            fixedPreviewAspect = 0f;
            hasPreviewFrame = false;
            previewLocalBounds = default;
            frozenRotationRadiusXZ = 0f;
            frozenVerticalHalfExtent = 0f;
            frozenPivotPositionInRig = default;
            hasFrozenRotationEnvelope = false;
            lastWorldUnitsPerLocalAxis = default;
            currentPreviewItemId = string.Empty;
        }

        /// <summary>
        /// 检查 RawImage 像素尺寸变化并按需重建离屏纹理；不改变当前模型或培养页面状态。
        /// </summary>
        public void Tick()
        {
            if (disposed || modelInstance == null || renderTexture == null || !hasPreviewFrame)
                return;

            // Canvas 或窗口父级缩放变化时，局部 Bounds 不变但 Camera 的世界单位会变化；只补偿投影，不重新取景。
            RefreshCameraProjectionForWorldScale();

            Vector2Int targetSize = CalculateRenderTextureSize(viewportView.GetTargetPixelSize(), fixedPreviewAspect);
            if (targetSize == renderTexturePixelSize)
            {
                pendingRenderTexturePixelSize = default;
                pendingRenderTextureSizeStableTime = 0f;
                return;
            }

            if (targetSize != pendingRenderTexturePixelSize)
            {
                pendingRenderTexturePixelSize = targetSize;
                pendingRenderTextureSizeStableTime = 0f;
                return;
            }

            pendingRenderTextureSizeStableTime += Time.unscaledDeltaTime;
            if (pendingRenderTextureSizeStableTime < RenderTextureResizeStabilitySeconds)
                return;

            Debug.Log($"[WeaponPreviewRuntime] 预览区域尺寸变化，重建 RT：{renderTexturePixelSize.x}x{renderTexturePixelSize.y} -> {targetSize.x}x{targetSize.y}。", renderRig);
            renderRig.DisablePreview();
            viewportView.Clear();
            ReleaseRenderTexture();
            CreateRenderTexture(targetSize);
            // RT 只改变分辨率，Camera 的局部位置、旋转和正交尺寸保持首次取景结果。
            renderRig.PreviewCamera.aspect = renderTexture.width / (float)renderTexture.height;
            renderRig.EnablePreview(renderTexture);
            viewportView.SetTexture(renderTexture);
            pendingRenderTexturePixelSize = default;
            pendingRenderTextureSizeStableTime = 0f;
        }

        /// <summary>
        /// 将模型及其所有子节点设置到专用预览 Layer，并禁用模型运行时脚本。
        /// </summary>
        /// <param name="instance">待处理的模型实例。</param>
        private void PrepareModelHierarchy(GameObject instance)
        {
            int previewLayer = LayerMask.NameToLayer("EquipmentPreview");
            Transform[] transforms = instance.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < transforms.Length; index++)
                transforms[index].gameObject.layer = previewLayer;

            Behaviour[] behaviours = instance.GetComponentsInChildren<Behaviour>(true);
            for (int index = 0; index < behaviours.Length; index++)
                behaviours[index].enabled = false;

            Rigidbody[] rigidbodies = instance.GetComponentsInChildren<Rigidbody>(true);
            for (int index = 0; index < rigidbodies.Length; index++)
            {
                rigidbodies[index].isKinematic = true;
                rigidbodies[index].useGravity = false;
                rigidbodies[index].detectCollisions = false;
            }

            Rigidbody2D[] rigidbodies2D = instance.GetComponentsInChildren<Rigidbody2D>(true);
            for (int index = 0; index < rigidbodies2D.Length; index++)
                rigidbodies2D[index].simulated = false;

            ParticleSystem[] particleSystems = instance.GetComponentsInChildren<ParticleSystem>(true);
            for (int index = 0; index < particleSystems.Length; index++)
                particleSystems[index].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        /// <summary>
        /// 将模型 Rig 局部包围盒中心移动到 ModelPivot 中心，只修改外层 ModelRoot 的局部位置。
        /// </summary>
        /// <param name="bounds">模型当前 Rig 局部空间 Bounds。</param>
        private void CenterModel(Bounds bounds)
        {
            Transform modelParent = renderRig.ModelRoot.parent;
            if (modelParent == null)
                throw new InvalidOperationException("[WeaponPreviewRuntime] ModelRoot 必须挂在 ModelPivot 下，才能完成预览居中。");

            // Bounds.center 是 Rig 局部点；先还原到世界，再转换到 ModelRoot 父节点局部空间，避免 Canvas 缩放混入位置计算。
            Vector3 boundsCenterWorld = renderRig.transform.TransformPoint(bounds.center);
            Vector3 centerInModelParentLocal = modelParent.InverseTransformPoint(boundsCenterWorld);
            renderRig.ModelRoot.localPosition =
                renderRig.ConfiguredModelRootLocalPosition - centerInModelParentLocal;
        }

        /// <summary>
        /// 在自动旋转开启前冻结相对 Pivot 的 XZ 旋转半径和 Y 半高度。
        /// </summary>
        /// <param name="localBounds">模型居中后的 Rig 局部 Bounds。</param>
        private void FreezeRotationEnvelope(Bounds localBounds)
        {
            Transform rigTransform = renderRig.transform;
            frozenPivotPositionInRig = rigTransform.InverseTransformPoint(renderRig.ModelPivot.position);
            frozenRotationRadiusXZ = 0f;
            frozenVerticalHalfExtent = 0f;

            // 绕本地 Y 轴旋转时，XZ 偏移会在水平面内转成一个圆；Y 偏移保持不变。
            for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
            {
                Vector3 offset = GetBoundsCorner(localBounds, cornerIndex) - frozenPivotPositionInRig;
                frozenRotationRadiusXZ = Mathf.Max(
                    frozenRotationRadiusXZ,
                    Mathf.Sqrt(offset.x * offset.x + offset.z * offset.z));
                frozenVerticalHalfExtent = Mathf.Max(
                    frozenVerticalHalfExtent,
                    Mathf.Abs(offset.y));
            }

            hasFrozenRotationEnvelope = true;
        }

        /// <summary>
        /// 从 Renderer 自身局部 Bounds 构建 Rig 局部包围盒，保留 ModelRoot 的预览姿态并避免世界 AABB 二次膨胀。
        /// </summary>
        /// <param name="localBounds">输出的 Rig 局部空间 Bounds。</param>
        /// <returns>存在至少一个有效 Renderer 时返回 true。</returns>
        private bool TryCalculateLocalBounds(out Bounds localBounds)
        {
            Renderer[] renderers = modelInstance == null
                ? Array.Empty<Renderer>()
                : modelInstance.GetComponentsInChildren<Renderer>(true);
            localBounds = default;
            bool hasBounds = false;
            Transform rigTransform = renderRig.transform;
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null || !renderer.enabled) continue;

                if (!TryGetRendererLocalBounds(renderer, out Bounds rendererLocalBounds))
                    continue;

                for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
                {
                    Vector3 rendererLocalCorner = GetBoundsCorner(rendererLocalBounds, cornerIndex);
                    Vector3 worldCorner = renderer.transform.TransformPoint(rendererLocalCorner);
                    Vector3 localCorner = rigTransform.InverseTransformPoint(worldCorner);
                    if (!hasBounds)
                    {
                        localBounds = new Bounds(localCorner, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(localCorner);
                    }
                }
            }

            return hasBounds && localBounds.size.sqrMagnitude > 0.000001f;
        }

        /// <summary>
        /// 读取 MeshRenderer 或 SkinnedMeshRenderer 的原始局部 Bounds。
        /// </summary>
        /// <param name="renderer">待读取的 Renderer。</param>
        /// <param name="localBounds">输出的 Renderer 局部 Bounds。</param>
        /// <returns>存在有效 Mesh Bounds 时返回 true。</returns>
        private static bool TryGetRendererLocalBounds(Renderer renderer, out Bounds localBounds)
        {
            if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
            {
                localBounds = skinnedMeshRenderer.localBounds;
                return localBounds.size.sqrMagnitude > 0.000001f;
            }

            if (renderer is MeshRenderer meshRenderer)
            {
                MeshFilter meshFilter = meshRenderer.GetComponent<MeshFilter>();
                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    localBounds = meshFilter.sharedMesh.bounds;
                    return localBounds.size.sqrMagnitude > 0.000001f;
                }
            }

            localBounds = default;
            return false;
        }

        /// <summary>
        /// 根据索引读取包围盒的八个角之一；返回值保持在传入 Bounds 的原坐标空间。
        /// </summary>
        /// <param name="bounds">目标包围盒。</param>
        /// <param name="cornerIndex">范围为 0 到 7 的角索引。</param>
        /// <returns>对应坐标空间中的角点。</returns>
        private static Vector3 GetBoundsCorner(Bounds bounds, int cornerIndex)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            return new Vector3(
                (cornerIndex & 1) == 0 ? min.x : max.x,
                (cornerIndex & 2) == 0 ? min.y : max.y,
                (cornerIndex & 4) == 0 ? min.z : max.z);
        }

        /// <summary>
        /// 读取目标尺寸对应的固定预览宽高比。
        /// </summary>
        /// <param name="requestedSize">RawImage 的目标像素尺寸。</param>
        /// <returns>大于零的宽高比。</returns>
        private static float CalculateAspect(Vector2Int requestedSize)
        {
            return Mathf.Max(0.01f, requestedSize.x / (float)Mathf.Max(1, requestedSize.y));
        }

        /// <summary>
        /// 按固定预览宽高比限制 RenderTexture 的分辨率范围，避免窗口缩放改变 Camera 构图。
        /// </summary>
        /// <param name="requestedSize">RawImage 的目标像素尺寸。</param>
        /// <param name="previewAspect">首次取景时固定的宽高比。</param>
        /// <returns>限制在最短边 256、最长边 1024 内的纹理尺寸。</returns>
        private static Vector2Int CalculateRenderTextureSize(Vector2Int requestedSize, float previewAspect)
        {
            int width = Mathf.Max(1, requestedSize.x);
            int height = Mathf.Max(1, requestedSize.y);
            previewAspect = Mathf.Max(0.01f, previewAspect);

            if (width / (float)height > previewAspect)
                width = Mathf.Max(1, Mathf.RoundToInt(height * previewAspect));
            else
                height = Mathf.Max(1, Mathf.RoundToInt(width / previewAspect));

            const int minimumSide = 256;
            const int maximumSide = 1024;
            // 使用同一个缩放因子同时满足短边下限和长边上限，保持固定宽高比。
            float scaleForMinimumSide = minimumSide / (float)Mathf.Min(width, height);
            float scaleForMaximumSide = maximumSide / (float)Mathf.Max(width, height);
            float scale = Mathf.Max(1f, scaleForMinimumSide);
            scale = Mathf.Min(scale, scaleForMaximumSide);
            width = Mathf.Max(1, Mathf.RoundToInt(width * scale));
            height = Mathf.Max(1, Mathf.RoundToInt(height * scale));
            return new Vector2Int(width, height);
        }

        /// <summary>
        /// 使用指定尺寸创建 ARGB32、24 位深度和 4x MSAA 的离屏纹理。
        /// </summary>
        /// <param name="textureSize">已限制范围的纹理尺寸。</param>
        private void CreateRenderTexture(Vector2Int textureSize)
        {
            renderTexture = new RenderTexture(textureSize.x, textureSize.y, 24, RenderTextureFormat.ARGB32)
            {
                name = "EquipmentDevelopmentWeaponPreviewRT",
                antiAliasing = 4,
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            renderTexture.Create();
            renderTexturePixelSize = textureSize;
            Debug.Log($"[WeaponPreviewRuntime] 创建武器预览 RT：{textureSize.x}x{textureSize.y}。", renderRig);
        }

        /// <summary>按冻结的模型旋转包络首次配置正交摄像机和投影参数。</summary>
        /// <param name="localBounds">模型在 Rig 局部空间的 Bounds。</param>
        private void ConfigureCamera(Bounds localBounds)
        {
            Camera camera = renderRig.PreviewCamera;
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.cullingMask = 1 << LayerMask.NameToLayer("EquipmentPreview");
            camera.aspect = renderTexture.width / (float)renderTexture.height;
            camera.transform.localScale = Vector3.one;

            float depth = Mathf.Max(
                1f,
                localBounds.extents.z * 10f,
                frozenRotationRadiusXZ * 2f);
            // 模型和 Camera 共用 Rig 局部坐标；父级 Canvas 的位置变化不会改变二者相对关系。
            camera.transform.localPosition = frozenPivotPositionInRig - Vector3.forward * depth;
            camera.transform.localRotation = Quaternion.identity;
            ApplyCameraProjection();
        }

        /// <summary>
        /// 根据冻结的 Pivot 相对旋转包络刷新 Camera 投影，不改变 Camera 局部构图。
        /// </summary>
        /// <exception cref="InvalidOperationException">旋转包络尚未冻结时抛出。</exception>
        private void ApplyCameraProjection()
        {
            if (!hasFrozenRotationEnvelope)
                throw new InvalidOperationException("[WeaponPreviewRuntime] 配置 Camera 投影前必须先冻结武器旋转包络。");

            Camera camera = renderRig.PreviewCamera;
            Vector3 worldUnitsPerLocalAxis = new(
                renderRig.transform.TransformVector(Vector3.right).magnitude,
                renderRig.transform.TransformVector(Vector3.up).magnitude,
                renderRig.transform.TransformVector(Vector3.forward).magnitude);

            float horizontalWorldRadius = frozenRotationRadiusXZ * worldUnitsPerLocalAxis.x;
            float verticalWorldRadius = frozenVerticalHalfExtent * worldUnitsPerLocalAxis.y;
            float depthWorldRadius = frozenRotationRadiusXZ * worldUnitsPerLocalAxis.z;
            float horizontalRequiredSize =
                horizontalWorldRadius / Mathf.Max(0.01f, camera.aspect);
            float orthographicSize = Mathf.Max(horizontalRequiredSize, verticalWorldRadius)
                * renderRig.FramingPadding;
            camera.orthographicSize = Mathf.Max(0.01f, orthographicSize);

            Vector3 cameraWorldPosition = camera.transform.position;
            Vector3 cameraWorldForward = camera.transform.forward.normalized;
            float cameraToPivotDepth = Vector3.Dot(
                renderRig.ModelPivot.position - cameraWorldPosition,
                cameraWorldForward);
            float depthPadding = Mathf.Max(0.01f, depthWorldRadius * 0.1f);
            camera.nearClipPlane = Mathf.Max(
                0.01f,
                cameraToPivotDepth - depthWorldRadius - depthPadding);
            camera.farClipPlane = Mathf.Max(
                camera.nearClipPlane + 0.1f,
                cameraToPivotDepth + depthWorldRadius + depthPadding);
            lastWorldUnitsPerLocalAxis = worldUnitsPerLocalAxis;

            if (!hasPreviewFrame)
            {
                Debug.Log(
                    $"[WeaponPreviewRuntime] 完成武器预览取景：ItemId={currentPreviewItemId}，ModelRootConfiguredRotation={renderRig.ConfiguredModelRootLocalRotation.eulerAngles}，LocalBounds={previewLocalBounds.size}，RotationRadiusXZ={frozenRotationRadiusXZ:F3}，VerticalHalfExtent={frozenVerticalHalfExtent:F3}，WorldUnitsPerLocal={worldUnitsPerLocalAxis}，HorizontalWorldRadius={horizontalWorldRadius:F3}，VerticalWorldRadius={verticalWorldRadius:F3}，DepthWorldRadius={depthWorldRadius:F3}，CameraToPivotDepth={cameraToPivotDepth:F3}，Aspect={camera.aspect:F3}，OrthoSize={camera.orthographicSize:F3}，NearClip={camera.nearClipPlane:F3}，FarClip={camera.farClipPlane:F3}。",
                    renderRig);
            }
        }

        /// <summary>
        /// 当 Canvas/Rig 世界缩放变化时，只使用冻结的局部包络补偿 Camera 投影尺寸。
        /// </summary>
        private void RefreshCameraProjectionForWorldScale()
        {
            if (!hasFrozenRotationEnvelope) return;

            Vector3 currentWorldUnitsPerLocalAxis = new(
                renderRig.transform.TransformVector(Vector3.right).magnitude,
                renderRig.transform.TransformVector(Vector3.up).magnitude,
                renderRig.transform.TransformVector(Vector3.forward).magnitude);
            if (Approximately(currentWorldUnitsPerLocalAxis, lastWorldUnitsPerLocalAxis))
                return;

            ApplyCameraProjection();
        }

        /// <summary>
        /// 判断三轴世界单位比例是否在可忽略误差内保持不变。
        /// </summary>
        /// <param name="left">当前世界单位比例。</param>
        /// <param name="right">上次记录的世界单位比例。</param>
        /// <returns>三轴均足够接近时返回 true。</returns>
        private static bool Approximately(Vector3 left, Vector3 right)
        {
            return Mathf.Abs(left.x - right.x) < 0.0001f
                && Mathf.Abs(left.y - right.y) < 0.0001f
                && Mathf.Abs(left.z - right.z) < 0.0001f;
        }

        /// <summary>
        /// 销毁实例后释放对应的世界 Prefab 加载引用。
        /// </summary>
        private void DestroyModelAndUnloadPrefab()
        {
            if (modelInstance != null)
            {
                UnityEngine.Object.Destroy(modelInstance);
                modelInstance = null;
            }

            if (!string.IsNullOrWhiteSpace(loadedPrefabAddress))
            {
                UnloadPrefab(loadedPrefabAddress);
                loadedPrefabAddress = null;
            }
        }

        /// <summary>
        /// 通过 ResSystem 释放一个已加载的世界 Prefab 地址。
        /// </summary>
        /// <param name="address">Addressable 地址。</param>
        private static void UnloadPrefab(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return;
            ResSystem.Instance.UnLoad<GameObject>(address);
        }

        /// <summary>
        /// 释放当前 RenderTexture，避免窗口反复打开造成离屏纹理泄漏。
        /// </summary>
        private void ReleaseRenderTexture()
        {
            if (renderTexture == null) return;
            RenderTexture texture = renderTexture;
            renderTexture = null;
            renderTexturePixelSize = default;
            texture.Release();
            UnityEngine.Object.Destroy(texture);
            Debug.Log("[WeaponPreviewRuntime] 释放武器预览 RT。", renderRig);
        }

        #endregion
    }
}
