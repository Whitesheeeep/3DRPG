#if UNITY_EDITOR
using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 创建窗口私有的 VFX 轨道预览处理器。
    /// </summary>
    internal sealed class VfxPreviewFactory : ITrackPreviewFactory
    {
        /// <summary>
        /// 每次调用都创建独立处理器，避免多个时间轴窗口共享粒子实例。
        /// </summary>
        public ITrackPreviewHandler Create() => new VfxPreviewHandler();
    }

    /// <summary>
    /// 管理确定性隐藏 VFX 实例、冻结绑定矩阵与相互独立的场景 Transform 编辑代理。
    /// </summary>
    internal sealed class VfxPreviewHandler : ITrackPreviewHandler, ITrackPreviewStatusProvider,
        IVfxSceneEditService
    {
        #region 实例与编辑状态

        private readonly Dictionary<string, VfxPreviewInstance> instances = new();
        private readonly Dictionary<string, Matrix4x4> frozenBindingMatrices = new();
        private readonly HashSet<string> visibleIds = new();
        private readonly List<string> removalBuffer = new();
        private VfxEditProxy editProxy;
        private PreviewFrameContext lastContext;
        private bool hasContext;
        private bool disposed;

        /// <summary>
        /// 获取本次采样中首个无法解析的 VFX Clip 挂点信息；空字符串表示全部有效。
        /// </summary>
        public string StatusMessage { get; private set; } = string.Empty;

        #endregion

        #region 生命周期

        /// <summary>
        /// 释放普通隐藏预览、独立编辑代理和全部绑定矩阵缓存。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Clear();
        }

        #endregion

        #region 预览操作

        /// <summary>
        /// 配置字段可能改变 Prefab、区间、绑定或生命周期策略，因此释放全部派生对象与草稿。
        /// </summary>
        public void Invalidate() => Clear();

        /// <summary>
        /// 根据当前绝对帧同步所有未静音 VFX Clip；任何新采样都会取消未应用的场景编辑代理。
        /// </summary>
        public void SampleFrame(in PreviewFrameContext context)
        {
            if (disposed || context.Config == null || context.Actor?.RootTransform == null) return;
            CancelEdit();
            lastContext = context;
            hasContext = true;
            StatusMessage = string.Empty;
            visibleIds.Clear();
            foreach (VfxTrackConfig track in context.Config.Tracks.OfType<VfxTrackConfig>())
            {
                if (track == null || track.Muted) continue;
                foreach (VfxSkillClipConfig clip in track.Clips)
                {
                    if (!ShouldExist(clip, context.Frame) ||
                        !TryResolveBindingMatrix(context, clip, out Transform bindingTransform,
                            out Matrix4x4 bindingMatrix))
                        continue;
                    string key = GetStableKey(clip);
                    visibleIds.Add(key);
                    VfxPreviewInstance preview = GetOrCreate(
                        key, clip, context.Actor.RootTransform.gameObject.scene);
                    preview?.Sample(context, clip, bindingTransform, bindingMatrix,
                        clip.FollowMode == VfxFollowMode.FollowBinding);
                }
            }

            RemoveInvisibleInstances();
        }

        /// <summary>
        /// 暂停普通粒子并销毁未提交编辑代理；播放头姿势仍保留在场景中。
        /// </summary>
        public void Stop()
        {
            CancelEdit();
            foreach (VfxPreviewInstance instance in instances.Values)
                instance?.Pause();
        }

        /// <summary>
        /// 销毁普通预览、编辑代理和最近采样上下文，不保留对旧角色副本的引用。
        /// </summary>
        public void Clear()
        {
            CancelEdit();
            ClearPreviewInstances();
            frozenBindingMatrices.Clear();
            StatusMessage = string.Empty;
            hasContext = false;
            lastContext = default;
        }

        #endregion

        #region 场景编辑代理

        /// <summary>
        /// 判断指定 Clip 是否拥有当前窗口的独立场景编辑代理。
        /// </summary>
        public bool IsEditing(string clipId) => editProxy != null && editProxy.ClipId == clipId;

        /// <summary>
        /// 在最近一次有效预览帧创建独立可编辑代理，并冻结本次世界到局部转换的绑定矩阵。
        /// </summary>
        public EditResult BeginEdit(VfxSkillClipConfig clip)
        {
            if (clip?.Prefab == null) return EditResult.Failure("请先为 VFX Clip 配置 Prefab。");
            if (!hasContext || lastContext.Actor?.RootTransform == null)
                return EditResult.Failure("请先加载预览场景并采样当前帧。");
            StatusMessage = string.Empty;
            if (!TryResolveBindingMatrix(lastContext, clip, out _, out Matrix4x4 referenceMatrix))
                return EditResult.Failure(string.IsNullOrEmpty(StatusMessage)
                    ? "无法解析 VFX 场景编辑所需的挂点姿态。"
                    : StatusMessage);

            CancelEdit();
            string key = GetStableKey(clip);
            RemoveInstance(key);
            editProxy = VfxEditProxy.Create(
                key, clip, lastContext, referenceMatrix,
                lastContext.Actor.RootTransform.gameObject.scene);
            if (editProxy != null) return EditResult.Success();
            return EditResult.Failure("无法创建 VFX 场景编辑代理。");
        }

        /// <summary>
        /// 重新选择并在 Scene View 中定位指定 Clip 当前持有的场景编辑代理。
        /// </summary>
        public EditResult SelectProxy(string clipId)
        {
            if (!IsEditing(clipId)) return EditResult.Failure("当前 VFX Clip 没有可选择的场景编辑代理。");
            editProxy.Select();
            return EditResult.Success();
        }

        /// <summary>
        /// 把代理世界 Transform 转换为创建时冻结的绑定空间局部快照。
        /// </summary>
        public EditResult Capture(string clipId, out VfxTransformSnapshot snapshot)
        {
            snapshot = default;
            if (!IsEditing(clipId)) return EditResult.Failure("当前 VFX Clip 没有可应用的场景编辑代理。");
            snapshot = editProxy.Capture();
            if (editProxy.HasTransformChanges(snapshot)) return EditResult.Success();
            editProxy.Select();
            return EditResult.Failure("代理 Transform 未发生变化，请先移动、旋转或缩放编辑代理根对象。");
        }

        /// <summary>
        /// 销毁独立场景编辑代理并丢弃尚未写入 Document 的 Transform 草稿。
        /// </summary>
        public void CancelEdit()
        {
            editProxy?.Dispose();
            editProxy = null;
        }

        #endregion

        #region 实例与绑定查询

        // ReturnToPool 只在半开 Clip 区间内存在；其他结束模式需要保留结束后的粒子结果。
        private static bool ShouldExist(VfxSkillClipConfig clip, int frame)
        {
            if (clip?.Prefab == null || frame < clip.StartFrame) return false;
            return clip.StopMode != VfxStopMode.ReturnToPoolAtEnd || frame < clip.EndFrame;
        }

        // 正常资产使用稳定 GUID；异常空 GUID 仅生成窗口内临时键，不写回配置。
        private static string GetStableKey(VfxSkillClipConfig clip) =>
            !string.IsNullOrEmpty(clip.Id)
                ? clip.Id
                : $"runtime:{RuntimeHelpers.GetHashCode(clip)}";

        /// <summary>
        /// 每个 Clip 先解析自己的 Marker；FollowBinding 使用当前姿态，KeepWorldPosition 冻结起始帧姿态。
        /// </summary>
        /// <param name="context">当前预览帧上下文。</param>
        /// <param name="clip">需要解析挂点的 VFX Clip。</param>
        /// <param name="bindingTransform">解析到的挂点。</param>
        /// <param name="matrix">用于生成或冻结 VFX 的世界矩阵。</param>
        /// <returns>挂点和世界矩阵解析成功时返回 true。</returns>
        private bool TryResolveBindingMatrix(in PreviewFrameContext context,
            VfxSkillClipConfig clip, out Transform bindingTransform, out Matrix4x4 matrix)
        {
            if (!context.TryGetBindingTransform(clip.MarkerKey, out bindingTransform))
            {
                RecordBindingError(clip, "预览角色无法解析该 Clip 的 MarkerKey，请查看 Console 中的 MarkerProvider 诊断。");
                matrix = Matrix4x4.identity;
                return false;
            }

            if (clip.FollowMode == VfxFollowMode.FollowBinding)
            {
                matrix = bindingTransform.localToWorldMatrix;
                return true;
            }

            string key = GetStableKey(clip);
            if (frozenBindingMatrices.TryGetValue(key, out matrix)) return true;
            if (!context.TryResolveBindingWorldMatrix(clip.MarkerKey, clip.StartFrame, out matrix))
            {
                RecordBindingError(clip, "无法读取 Clip 起始帧的挂点世界姿态。");
                return false;
            }

            frozenBindingMatrices.Add(key, matrix);
            return true;
        }

        // 保留本次采样的首个绑定错误，避免多个无效 Clip 反复覆盖状态栏。
        private void RecordBindingError(VfxSkillClipConfig clip, string error)
        {
            if (!string.IsNullOrEmpty(StatusMessage)) return;
            string clipName = clip.Prefab != null ? clip.Prefab.name : clip.Id;
            StatusMessage = $"VFX Clip“{clipName}”无法解析挂点：{error}";
        }

        // Prefab 变化时销毁旧实例并重建，避免复用与配置不一致的粒子层级。
        private VfxPreviewInstance GetOrCreate(string key, VfxSkillClipConfig clip, Scene scene)
        {
            if (instances.TryGetValue(key, out VfxPreviewInstance existing))
            {
                if (existing.Matches(clip.Prefab)) return existing;
                existing.Dispose();
                instances.Remove(key);
            }

            VfxPreviewInstance created = VfxPreviewInstance.Create(key, clip.Prefab, scene);
            if (created != null) instances.Add(key, created);
            return created;
        }

        // 删除指定普通预览实例，进入编辑模式时避免同一 Clip 显示两个结果。
        private void RemoveInstance(string key)
        {
            if (!instances.Remove(key, out VfxPreviewInstance instance)) return;
            instance.Dispose();
        }

        // 在遍历结束后统一释放本帧不应存在的实例，避免修改正在枚举的字典。
        private void RemoveInvisibleInstances()
        {
            removalBuffer.Clear();
            foreach (KeyValuePair<string, VfxPreviewInstance> pair in instances)
            {
                if (!visibleIds.Contains(pair.Key)) removalBuffer.Add(pair.Key);
            }

            foreach (string key in removalBuffer) RemoveInstance(key);
        }

        // 仅清理确定性隐藏实例，场景编辑代理由独立生命周期方法负责。
        private void ClearPreviewInstances()
        {
            foreach (VfxPreviewInstance instance in instances.Values)
                instance.Dispose();
            instances.Clear();
            visibleIds.Clear();
            removalBuffer.Clear();
        }

        #endregion
    }
    /// <summary>
    /// 封装一个 VFX Clip 的不可保存预览根、稳定随机种子、变换和粒子绝对时间采样。
    /// </summary>
    internal sealed class VfxPreviewInstance : IDisposable
    {
        #region 克隆与粒子状态

        private readonly GameObject prefab;
        private readonly GameObject instance;
        private readonly ParticleSystem[] particleSystems;
        private readonly ParticleSystem[] rootParticleSystems;
        private readonly ParticleEmissionState[] emissionStates;
        private bool disposed;

        internal Transform RootTransform => !disposed && instance != null ? instance.transform : null;
        internal GameObject RootObject => !disposed ? instance : null;

        #endregion

        #region 创建与生命周期

        // 创建不可保存 Prefab 克隆并关闭业务脚本，只保留静态层级和 ParticleSystem 供编辑器采样。
        private VfxPreviewInstance(string stableId, GameObject prefab, GameObject instance)
        {
            this.prefab = prefab;
            this.instance = instance;
            particleSystems = instance.GetComponentsInChildren<ParticleSystem>(true);
            rootParticleSystems = FindRootParticleSystems(particleSystems);
            emissionStates = new ParticleEmissionState[particleSystems.Length];
            uint seed = CalculateStableSeed(stableId);

            foreach (MonoBehaviour behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour != null) behaviour.enabled = false;
            }

            for (int index = 0; index < particleSystems.Length; index++)
            {
                ParticleSystem particle = particleSystems[index];
                ParticleSystem.MainModule main = particle.main;
                main.playOnAwake = false;
                particle.useAutoRandomSeed = false;
                particle.randomSeed = seed + (uint)(index * 16777619);
                emissionStates[index] = new ParticleEmissionState(particle.emission.enabled);
            }

            instance.SetActive(true);
            ResetParticles();
        }

        // 在指定预览场景创建隐藏克隆；失败时返回 null 且不留下半初始化对象。
        internal static VfxPreviewInstance Create(string stableId, GameObject prefab, Scene scene) =>
            CreateCore(stableId, prefab, scene, false);

        // 创建不含 ParticleSystem 的可编辑空根，并把不可保存的 Prefab 克隆作为其子内容。
        internal static VfxPreviewInstance CreateEditable(
            string stableId, GameObject prefab, Scene scene) =>
            CreateCore(stableId, prefab, scene, true);

        // 普通预览直接使用克隆根；编辑预览增加空根，避免选中粒子对象后触发 Unity 自动播放。
        private static VfxPreviewInstance CreateCore(string stableId, GameObject prefab,
            Scene scene, bool editable)
        {
            if (prefab == null || !scene.IsValid() || !scene.isLoaded) return null;
            GameObject clone = null;
            GameObject previewRoot = null;
            try
            {
                clone = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                if (clone == null) clone = Object.Instantiate(prefab);
                if (clone.scene != scene) SceneManager.MoveGameObjectToScene(clone, scene);

                if (!editable)
                {
                    clone.name = $"{prefab.name} (VFX 预览)";
                    clone.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor;
                    previewRoot = clone;
                }
                else
                {
                    previewRoot = new GameObject($"{prefab.name} (VFX 场景编辑)");
                    SceneManager.MoveGameObjectToScene(previewRoot, scene);
                    previewRoot.hideFlags = HideFlags.DontSaveInEditor;

                    clone.name = $"{prefab.name} (VFX 编辑内容)";
                    clone.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor;
                    Transform content = clone.transform;
                    content.SetParent(previewRoot.transform, false);
                    content.localPosition = Vector3.zero;
                    content.localRotation = Quaternion.identity;
                    content.localScale = Vector3.one;
                    clone.SetActive(true);
                }

                return new VfxPreviewInstance(stableId, prefab, previewRoot);
            }
            catch (Exception exception)
            {
                if (previewRoot != null) Object.DestroyImmediate(previewRoot);
                if (clone != null) Object.DestroyImmediate(clone);
                Debug.LogException(exception);
                return null;
            }
        }

        /// <summary>
        /// 销毁不可保存的预览克隆。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (instance != null) Object.DestroyImmediate(instance);
        }

        #endregion

        #region 采样与变换

        // 仅在缓存实例仍对应同一 Prefab 时允许复用。
        internal bool Matches(GameObject value) => !disposed && instance != null && prefab == value;

        // 使用调用方已经解析的绑定矩阵，从 Clip 起点按独立播放倍率换算的绝对秒数重建粒子状态。
        internal void Sample(in PreviewFrameContext context, VfxSkillClipConfig clip,
            Transform bindingTransform, Matrix4x4 bindingMatrix, bool parentToCurrentBinding)
        {
            if (disposed || instance == null || clip == null) return;
            ApplyTransform(clip, bindingTransform, bindingMatrix, parentToCurrentBinding);
            float frameRate = Mathf.Max(1, context.Config.FrameRate);
            float playbackSpeed = Mathf.Max(0.01f, clip.PlaybackSpeed);
            float elapsed = Mathf.Max(0f, (context.Frame - clip.StartFrame) / frameRate) * playbackSpeed;
            float duration = Mathf.Max(0f, clip.DurationFrames / frameRate) * playbackSpeed;

            if (clip.StopMode == VfxStopMode.StopEmissionAtEnd && elapsed >= duration)
                SimulateStoppedEmission(duration, elapsed - duration);
            else
                SimulateFromStart(elapsed);
        }

        // 普通 FollowBinding 保持父子关系；冻结代理和 KeepWorldPosition 使用世界矩阵定位。
        private void ApplyTransform(VfxSkillClipConfig clip, Transform bindingTransform,
            Matrix4x4 bindingMatrix, bool parentToCurrentBinding)
        {
            Transform target = instance.transform;
            if (parentToCurrentBinding)
            {
                target.SetParent(bindingTransform, false);
                target.localPosition = clip.LocalPosition;
                target.localRotation = Quaternion.Euler(clip.LocalEulerAngles);
                target.localScale = clip.LocalScale;
                return;
            }

            target.SetParent(null, false);
            Matrix4x4 matrix = bindingMatrix * Matrix4x4.TRS(
                clip.LocalPosition, Quaternion.Euler(clip.LocalEulerAngles), clip.LocalScale);
            ApplyWorldMatrix(target, matrix);
        }

        // 将无切变 TRS 矩阵拆回 Transform；技能预览根节点只允许位置、旋转与缩放组合。
        private static void ApplyWorldMatrix(Transform target, Matrix4x4 matrix)
        {
            Vector3 scale = new(
                matrix.GetColumn(0).magnitude,
                matrix.GetColumn(1).magnitude,
                matrix.GetColumn(2).magnitude);
            target.SetPositionAndRotation(matrix.GetColumn(3), matrix.rotation);
            target.localScale = scale;
        }

        // 从起点重启全部粒子并推进到指定绝对时间，随后暂停以阻止 Editor 自行累积。
        private void SimulateFromStart(float elapsed)
        {
            ResetParticles();
            foreach (ParticleSystem root in rootParticleSystems)
            {
                root.Simulate(elapsed, true, true, false);
                root.Pause(true);
            }
        }

        // 先带发射模拟到 Clip 结束，再关闭发射并仅推进已有粒子尾迹。
        private void SimulateStoppedEmission(float duration, float tailTime)
        {
            ResetParticles();
            foreach (ParticleSystem root in rootParticleSystems)
                root.Simulate(duration, true, true, false);
            SetEmissionEnabled(false);
            foreach (ParticleSystem root in rootParticleSystems)
            {
                root.Simulate(tailTime, true, false, false);
                root.Pause(true);
            }
        }

        // 清空旧粒子并恢复 Prefab 原始发射开关，保证不同采样顺序得到相同结果。
        private void ResetParticles()
        {
            for (int index = 0; index < particleSystems.Length; index++)
            {
                ParticleSystem.EmissionModule emission = particleSystems[index].emission;
                emission.enabled = emissionStates[index].Enabled;
            }

            foreach (ParticleSystem root in rootParticleSystems)
                root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        // 同步修改所有子粒子的发射模块，StopEmission 模式仍保留已经生成的粒子。
        private void SetEmissionEnabled(bool enabled)
        {
            foreach (ParticleSystem particle in particleSystems)
            {
                ParticleSystem.EmissionModule emission = particle.emission;
                emission.enabled = enabled;
            }
        }

        // 暂停全部根粒子并保留当前画面。
        internal void Pause()
        {
            if (disposed) return;
            foreach (ParticleSystem root in rootParticleSystems)
                root.Pause(true);
        }

        #endregion

        #region 粒子层级辅助

        // 只对没有 ParticleSystem 祖先的系统执行 withChildren 采样，避免子系统被重复推进。
        private static ParticleSystem[] FindRootParticleSystems(ParticleSystem[] particles)
        {
            List<ParticleSystem> roots = new();
            foreach (ParticleSystem particle in particles)
            {
                Transform parent = particle.transform.parent;
                bool hasParticleAncestor = false;
                while (parent != null)
                {
                    if (parent.GetComponent<ParticleSystem>() != null)
                    {
                        hasParticleAncestor = true;
                        break;
                    }
                    parent = parent.parent;
                }

                if (!hasParticleAncestor) roots.Add(particle);
            }
            return roots.ToArray();
        }

        // 使用 FNV-1a 生成跨刷新稳定的非零粒子随机种子，不依赖进程随机化的 string.GetHashCode。
        private static uint CalculateStableSeed(string value)
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;
            uint hash = offsetBasis;
            foreach (char character in value ?? string.Empty)
            {
                hash ^= character;
                hash *= prime;
            }
            return hash == 0 ? 1u : hash;
        }

        #endregion
    }

    /// <summary>
    /// 持有一个与普通确定性预览相互独立的可选择 VFX 克隆及其冻结绑定矩阵。
    /// </summary>
    internal sealed class VfxEditProxy : IDisposable
    {
        #region 编辑草稿状态

        private const float PositionEpsilon = 0.0001f;
        private const float ScaleEpsilon = 0.0001f;
        private const float RotationEpsilonDegrees = 0.01f;

        private readonly VfxPreviewInstance preview;
        private readonly Matrix4x4 referenceMatrix;
        private readonly VfxTransformSnapshot initialSnapshot;
        private bool disposed;

        internal string ClipId { get; }

        #endregion

        #region 创建与生命周期

        // 创建可见代理、固定当前粒子画面并主动选中根对象，便于直接使用 Scene View Transform 工具。
        internal static VfxEditProxy Create(string clipId, VfxSkillClipConfig clip,
            in PreviewFrameContext context, Matrix4x4 referenceMatrix, Scene scene)
        {
            VfxPreviewInstance preview = VfxPreviewInstance.CreateEditable(clipId, clip.Prefab, scene);
            if (preview == null) return null;
            preview.Sample(context, clip, null, referenceMatrix, false);
            preview.Pause();
            VfxEditProxy proxy = new(clipId, preview, referenceMatrix);
            proxy.Select();
            return proxy;
        }

        // 保存独立代理和冻结矩阵；二者的生命周期只属于当前时间轴窗口。
        private VfxEditProxy(string clipId, VfxPreviewInstance preview,
            Matrix4x4 referenceMatrix)
        {
            ClipId = clipId;
            this.preview = preview;
            this.referenceMatrix = referenceMatrix;
            initialSnapshot = Capture();
        }

        /// <summary>
        /// 销毁不可保存的编辑代理；未应用 Transform 不会写入 SkillConfig。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (Selection.activeGameObject == preview.RootObject)
                Selection.activeGameObject = null;
            preview.Dispose();
        }

        #endregion

        #region 草稿读取

        // 选择前后均暂停粒子；空代理根不含 ParticleSystem，避免 Unity Inspector 重启预览。
        internal void Select()
        {
            preview.Pause();
            Selection.activeGameObject = preview.RootObject;
            EditorGUIUtility.PingObject(preview.RootObject);
            SceneView.lastActiveSceneView?.FrameSelected();
            preview.Pause();
            SceneView.RepaintAll();
        }

        // 将当前代理世界矩阵变换回创建时冻结的绑定空间，并只提取无切变 TRS 数据。
        internal VfxTransformSnapshot Capture()
        {
            Matrix4x4 localMatrix = referenceMatrix.inverse * preview.RootTransform.localToWorldMatrix;
            Vector3 scale = new(
                localMatrix.GetColumn(0).magnitude,
                localMatrix.GetColumn(1).magnitude,
                localMatrix.GetColumn(2).magnitude);
            return new VfxTransformSnapshot(
                localMatrix.GetColumn(3), localMatrix.rotation.eulerAngles, scale);
        }

        // 使用具名位置、缩放分量阈值及四元数夹角判断根代理是否具有可提交变化。
        internal bool HasTransformChanges(in VfxTransformSnapshot snapshot)
        {
            return ExceedsComponentEpsilon(snapshot.LocalPosition, initialSnapshot.LocalPosition,
                       PositionEpsilon) ||
                   ExceedsComponentEpsilon(snapshot.LocalScale, initialSnapshot.LocalScale,
                       ScaleEpsilon) ||
                   Quaternion.Angle(
                       Quaternion.Euler(snapshot.LocalEulerAngles),
                       Quaternion.Euler(initialSnapshot.LocalEulerAngles)) > RotationEpsilonDegrees;
        }

        // 逐分量比较可避免某个轴上的有效变化被其他轴抵消或因欧氏距离聚合而改变阈值语义。
        private static bool ExceedsComponentEpsilon(Vector3 current, Vector3 initial, float epsilon)
        {
            return Mathf.Abs(current.x - initial.x) > epsilon ||
                   Mathf.Abs(current.y - initial.y) > epsilon ||
                   Mathf.Abs(current.z - initial.z) > epsilon;
        }

        #endregion
    }
    /// <summary>
    /// 保存 Prefab 中一个 ParticleSystem 原始的发射启用状态。
    /// </summary>
    internal readonly struct ParticleEmissionState
    {
        public bool Enabled { get; }

        // 创建不可变的发射状态快照。
        internal ParticleEmissionState(bool enabled)
        {
            Enabled = enabled;
        }
    }
}
#endif
