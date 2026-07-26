#if UNITY_EDITOR
using System;
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
    /// 按 VFX Clip GUID 管理不可保存实例，并通过绝对时间确定性采样 ParticleSystem。
    /// </summary>
    internal sealed class VfxPreviewHandler : ITrackPreviewHandler
    {
        #region 实例状态

        private readonly Dictionary<string, VfxPreviewInstance> instances = new();
        private readonly HashSet<string> visibleIds = new();
        private readonly List<string> removalBuffer = new();
        private bool disposed;

        #endregion

        #region 生命周期

        /// <summary>
        /// 释放所有窗口私有 VFX 实例。
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
        /// 配置字段可能改变 Prefab、区间或生命周期策略，因此立即清理旧派生实例。
        /// </summary>
        public void Invalidate() => Clear();

        /// <summary>
        /// 根据当前绝对帧同步所有未静音 VFX Clip，不依赖此前的采样顺序。
        /// </summary>
        public void SampleFrame(in PreviewFrameContext context)
        {
            if (disposed || context.Config == null || context.Actor?.RootTransform == null) return;
            visibleIds.Clear();

            foreach (VfxTrackConfig track in context.Config.VfxTracks)
            {
                if (track?.Header == null || track.Header.Muted) continue;
                foreach (VfxSkillClipConfig clip in track.Clips)
                {
                    if (!ShouldExist(clip, context.Frame)) continue;
                    string key = GetStableKey(clip);
                    visibleIds.Add(key);
                    VfxPreviewInstance preview = GetOrCreate(key, clip, context.Actor.RootTransform.gameObject.scene);
                    preview?.Sample(context, clip);
                }
            }

            RemoveInvisibleInstances();
        }

        /// <summary>
        /// 暂停现有粒子但保留当前采样画面，供时间轴 Pause 使用。
        /// </summary>
        public void Stop()
        {
            foreach (VfxPreviewInstance instance in instances.Values)
                instance?.Pause();
        }

        /// <summary>
        /// 销毁全部不可保存 VFX 实例并清空 GUID 缓存。
        /// </summary>
        public void Clear()
        {
            foreach (VfxPreviewInstance instance in instances.Values)
                instance?.Dispose();
            instances.Clear();
            visibleIds.Clear();
            removalBuffer.Clear();
        }

        #endregion

        #region 实例查询与回收

        // ReturnToPool 只在半开 Clip 区间内存在；其他结束模式需要保留结束后的粒子结果。
        private static bool ShouldExist(VfxSkillClipConfig clip, int frame)
        {
            if (clip?.Prefab == null || frame < clip.StartFrame) return false;
            return clip.StopMode != VfxStopMode.ReturnToPoolAtEnd || frame < clip.EndFrame;
        }

        // 正常资产使用稳定 GUID；异常空 GUID 仅以当前托管对象引用生成窗口内临时键，不写回配置。
        private static string GetStableKey(VfxSkillClipConfig clip) =>
            !string.IsNullOrEmpty(clip.Id)
                ? clip.Id
                : $"runtime:{RuntimeHelpers.GetHashCode(clip)}";

        // Prefab 变化时销毁旧实例并重建，避免复用与配置不一致的粒子层级。
        private VfxPreviewInstance GetOrCreate(string key, VfxSkillClipConfig clip, Scene scene)
        {
            if (instances.TryGetValue(key, out VfxPreviewInstance existing))
            {
                if (existing != null && existing.Matches(clip.Prefab)) return existing;
                existing?.Dispose();
                instances.Remove(key);
            }

            VfxPreviewInstance created = VfxPreviewInstance.Create(key, clip.Prefab, scene);
            if (created != null) instances.Add(key, created);
            return created;
        }

        // 在遍历结束后统一释放本帧不应存在的实例，避免修改正在枚举的字典。
        private void RemoveInvisibleInstances()
        {
            removalBuffer.Clear();
            foreach (KeyValuePair<string, VfxPreviewInstance> pair in instances)
            {
                if (!visibleIds.Contains(pair.Key)) removalBuffer.Add(pair.Key);
            }

            foreach (string key in removalBuffer)
            {
                instances[key]?.Dispose();
                instances.Remove(key);
            }
        }

        #endregion
    }

    /// <summary>
    /// 封装一个 VFX Clip 的不可保存克隆、稳定随机种子、变换和粒子绝对时间采样。
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

        // 在指定预览场景创建克隆；失败时返回 null 且不留下半初始化对象。
        internal static VfxPreviewInstance Create(string stableId, GameObject prefab, Scene scene)
        {
            if (prefab == null || !scene.IsValid() || !scene.isLoaded) return null;
            GameObject clone = null;
            try
            {
                clone = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                if (clone == null) clone = Object.Instantiate(prefab);
                if (clone.scene != scene) SceneManager.MoveGameObjectToScene(clone, scene);
                clone.name = $"{prefab.name} (VFX 预览)";
                clone.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor;
                return new VfxPreviewInstance(stableId, prefab, clone);
            }
            catch (Exception exception)
            {
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

        // 应用跟随策略后，从 Clip 起点按绝对秒数重建粒子状态。
        internal void Sample(in PreviewFrameContext context, VfxSkillClipConfig clip)
        {
            if (disposed || instance == null || clip == null) return;
            ApplyTransform(context, clip);
            float frameRate = Mathf.Max(1, context.Config.FrameRate);
            float elapsed = Mathf.Max(0f, (context.Frame - clip.StartFrame) / frameRate);
            float duration = Mathf.Max(0f, clip.DurationFrames / frameRate);

            if (clip.StopMode == VfxStopMode.StopEmissionAtEnd && elapsed >= duration)
                SimulateStoppedEmission(duration, elapsed - duration);
            else
                SimulateFromStart(elapsed);
        }

        // FollowBinding 使用当前角色根；KeepWorldPosition 使用 Clip 起始帧的 Root Motion 世界快照。
        private void ApplyTransform(in PreviewFrameContext context, VfxSkillClipConfig clip)
        {
            Transform target = instance.transform;
            if (clip.FollowMode == VfxFollowMode.FollowBinding)
            {
                target.SetParent(context.Actor.RootTransform, false);
                target.localPosition = clip.LocalPosition;
                target.localRotation = Quaternion.Euler(clip.LocalEulerAngles);
                target.localScale = clip.LocalScale;
                return;
            }

            target.SetParent(null, false);
            RootPose startPose = context.ResolveRootPose(clip.StartFrame);
            Matrix4x4 matrix = context.Actor.GetRootWorldMatrix(startPose) *
                               Matrix4x4.TRS(clip.LocalPosition,
                                   Quaternion.Euler(clip.LocalEulerAngles), clip.LocalScale);
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