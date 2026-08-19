using System;
using System.Collections.Generic;
using RPG.Markers;
using UnityEngine;
using WS_Modules.Pooling;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 使用 WSFrame Prefab 池创建 VFX，并按 Marker、播放倍率和结束策略管理实例生命周期。
    /// </summary>
    internal sealed class VfxRuntimeHandler : TrackRuntimeHandler<VfxTrackConfig>
    {
        #region 运行时状态

        private readonly List<VfxRuntimeInstance> instances = new();
        private float globalPlaybackSpeed = 1f;

        #endregion

        #region 处理器契约

        /// <summary>
        /// 立即更新当前执行仍持有的全部粒子实例，并保存给后续新实例使用。
        /// </summary>
        /// <param name="playbackSpeed">当前 Module 的 0 到 2 倍率。</param>
        public override void SetPlaybackSpeed(float playbackSpeed)
        {
            globalPlaybackSpeed = playbackSpeed;
            for (int index = 0; index < instances.Count; index++)
                instances[index].SetPlaybackSpeed(playbackSpeed);
        }

        /// <summary>
        /// 在 Clip 起始帧创建实例，并在排他结束帧执行对应 StopMode。
        /// </summary>
        /// <param name="frame">当前整数帧。</param>
        public override void ProcessFrame(int frame)
        {
            RecycleCompletedInstances();
            for (int trackIndex = 0; trackIndex < Tracks.Count; trackIndex++)
            {
                VfxTrackConfig track = Tracks[trackIndex];
                for (int clipIndex = 0; clipIndex < track.Clips.Count; clipIndex++)
                {
                    VfxSkillClipConfig clip = track.Clips[clipIndex];
                    if (clip.StartFrame == frame && clip.Prefab != null) CreateInstance(clip);
                    if (clip.EndFrame == frame) EndClip(clip);
                }
            }
        }

        /// <summary>
        /// FollowBinding 通过父子关系自动跟随，不需要 LateUpdate 手动更新。
        /// </summary>
        /// <param name="frame">当前整数帧。</param>
        public override void ProcessLateFrame(int frame)
        {
        }

        /// <summary>
        /// Cancel 立即回收；自然结束或 Stop 停止发射并将尾迹交给独立回收监视器。
        /// </summary>
        /// <param name="reason">技能结束原因。</param>
        public override void Complete(SkillCompletionReason reason)
        {
            for (int index = instances.Count - 1; index >= 0; index--)
            {
                VfxRuntimeInstance instance = instances[index];
                if (reason == SkillCompletionReason.Cancelled) instance.RecycleImmediately();
                else instance.ReleaseToTail(stopEmission: true);
            }
            instances.Clear();
        }

        #endregion

        #region 实例生命周期

        /// <summary>
        /// 解析 Clip Marker 并从 PoolManager 获取、定位和启动独立 VFX 实例。
        /// </summary>
        /// <param name="clip">到达起始帧的 VFX Clip。</param>
        private void CreateInstance(VfxSkillClipConfig clip)
        {
            Transform binding = ResolveBinding(clip.MarkerKey);
            if (binding == null) return;

            GameObject gameObject = PoolManager.Instance.Get(clip.Prefab,
                clip.FollowMode == VfxFollowMode.FollowBinding ? binding : null);
            Transform transform = gameObject.transform;
            if (clip.FollowMode == VfxFollowMode.FollowBinding)
            {
                transform.localPosition = clip.LocalPosition;
                transform.localRotation = Quaternion.Euler(clip.LocalEulerAngles);
                transform.localScale = clip.LocalScale;
            }
            else
            {
                transform.SetParent(null, false);
                transform.position = binding.TransformPoint(clip.LocalPosition);
                transform.rotation = binding.rotation * Quaternion.Euler(clip.LocalEulerAngles);
                transform.localScale = Vector3.Scale(binding.lossyScale, clip.LocalScale);
            }

            VfxRuntimeInstance instance = new(clip, gameObject);
            instance.Play(globalPlaybackSpeed);
            instances.Add(instance);
        }

        /// <summary>
        /// 按 MarkerKey 解析实例级 Socket；空 Key 明确使用角色 Origin。
        /// </summary>
        /// <param name="key">VFX Clip 配置的语义挂点。</param>
        /// <returns>世界变换基准；非空 Key 未解析时返回空并跳过该 Clip。</returns>
        private Transform ResolveBinding(MarkerKey key)
        {
            if (key == null) return Context.Actor.Origin;
            IMarkerProvider provider = Context.Actor.MarkerProvider;
            return provider != null && provider.TryGetMarker(key, out Transform marker) ? marker : null;
        }

        /// <summary>
        /// 处理单个 Clip 的排他结束边界。
        /// </summary>
        /// <param name="clip">到达结束边界的配置。</param>
        private void EndClip(VfxSkillClipConfig clip)
        {
            for (int index = instances.Count - 1; index >= 0; index--)
            {
                VfxRuntimeInstance instance = instances[index];
                if (instance.Clip != clip) continue;

                switch (clip.StopMode)
                {
                    case VfxStopMode.ReturnToPoolAtEnd:
                        instance.RecycleImmediately();
                        instances.RemoveAt(index);
                        break;
                    case VfxStopMode.StopEmissionAtEnd:
                        // 执行仍持有已经停止发射的尾迹，使后续 Cancel 可以立即回收本次技能创建的全部资源。
                        instance.StopEmission();
                        break;
                    case VfxStopMode.KeepAlive:
                        // KeepAlive 由技能整体结束统一停止循环发射，确保持续时间语义独立于粒子素材长度。
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        /// <summary>
        /// 回收已经自然结束的非循环实例；仍存活的尾迹继续由当前执行持有。
        /// </summary>
        private void RecycleCompletedInstances()
        {
            for (int index = instances.Count - 1; index >= 0; index--)
            {
                if (instances[index].IsAlive()) continue;
                instances[index].RecycleImmediately();
                instances.RemoveAt(index);
            }
        }

        #endregion

        #region 嵌套实例

        /// <summary>
        /// 保存单个池化 VFX 实例及其 ParticleSystem 原始速度，确保复用时不会重复乘倍率。
        /// </summary>
        private sealed class VfxRuntimeInstance
        {
            private readonly GameObject gameObject;
            private readonly ParticleSystem[] particles;
            private readonly float[] originalSpeeds;

            public VfxSkillClipConfig Clip { get; }

            /// <summary>
            /// 捕获池实例中全部粒子系统的原始 simulationSpeed。
            /// </summary>
            /// <param name="clip">所属 VFX Clip。</param>
            /// <param name="gameObject">从 PoolManager 获取的实例。</param>
            public VfxRuntimeInstance(VfxSkillClipConfig clip, GameObject gameObject)
            {
                Clip = clip;
                this.gameObject = gameObject;
                particles = gameObject.GetComponentsInChildren<ParticleSystem>(true);
                originalSpeeds = new float[particles.Length];
                for (int index = 0; index < particles.Length; index++)
                    originalSpeeds[index] = particles[index].main.simulationSpeed;
            }

            /// <summary>
            /// 从缓存的原始速度计算本次实例速度并重新播放粒子系统。
            /// </summary>
            /// <param name="globalPlaybackSpeed">当前 Module 的全局倍率。</param>
            public void Play(float globalPlaybackSpeed)
            {
                SetPlaybackSpeed(globalPlaybackSpeed);
                for (int index = 0; index < particles.Length; index++) particles[index].Clear(true);
                for (int index = 0; index < particles.Length; index++) particles[index].Play(true);
            }

            /// <summary>
            /// 从 Prefab 原始值重新计算全部粒子速度，避免多次倍率变化发生累计乘法。
            /// </summary>
            /// <param name="globalPlaybackSpeed">当前 Module 的全局倍率。</param>
            public void SetPlaybackSpeed(float globalPlaybackSpeed)
            {
                for (int index = 0; index < particles.Length; index++)
                {
                    ParticleSystem.MainModule main = particles[index].main;
                    main.simulationSpeed = originalSpeeds[index] * Clip.PlaybackSpeed * globalPlaybackSpeed;
                }
            }

            /// <summary>
            /// 立即停止并恢复粒子速度，然后归还 PoolManager。
            /// </summary>
            public void RecycleImmediately()
            {
                for (int index = 0; index < particles.Length; index++) particles[index].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                RestoreSpeeds();
                PoolManager.Instance.Recycle(gameObject);
            }

            /// <summary>
            /// 停止全部发射但保留当前粒子尾迹，实例仍归当前 SkillExecution 所有。
            /// </summary>
            public void StopEmission()
            {
                for (int index = 0; index < particles.Length; index++)
                    particles[index].Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            /// <summary>
            /// 判断实例根及子粒子系统是否仍有活动粒子或循环发射。
            /// </summary>
            /// <returns>至少一个粒子系统存活时返回 true。</returns>
            public bool IsAlive()
            {
                for (int index = 0; index < particles.Length; index++)
                {
                    if (particles[index].IsAlive(true)) return true;
                }
                return false;
            }

            /// <summary>
            /// 将实例交给独立 MonoBehaviour 监视尾迹，SkillExecution 可以立即释放自身状态。
            /// </summary>
            /// <param name="stopEmission">交接前是否停止全部粒子发射。</param>
            public void ReleaseToTail(bool stopEmission)
            {
                // 尾迹脱离 SkillExecution 后不再接收全局倍率，先恢复为 Clip 自身速度以避免 0 倍率永久冻结。
                SetPlaybackSpeed(1f);
                VfxTailRecycler recycler = gameObject.GetComponent<VfxTailRecycler>();
                if (recycler == null) recycler = gameObject.AddComponent<VfxTailRecycler>();
                recycler.Begin(particles, originalSpeeds, stopEmission);
            }

            /// <summary>
            /// 恢复所有粒子系统在 Prefab 上的原始速度。
            /// </summary>
            private void RestoreSpeeds()
            {
                for (int index = 0; index < particles.Length; index++)
                {
                    ParticleSystem.MainModule main = particles[index].main;
                    main.simulationSpeed = originalSpeeds[index];
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// 在技能执行结束后独立等待粒子尾迹消亡，并在回池前恢复 Prefab 原始播放速度。
    /// </summary>
    internal sealed class VfxTailRecycler : MonoBehaviour
    {
        private ParticleSystem[] particles;
        private float[] originalSpeeds;
        private bool monitoring;

        /// <summary>
        /// 开始监视当前池实例的全部粒子系统。
        /// </summary>
        /// <param name="particles">需要等待的根及子粒子系统。</param>
        /// <param name="originalSpeeds">与粒子数组一一对应的 Prefab 原始速度。</param>
        /// <param name="stopEmission">是否立即停止发射但保留现有粒子。</param>
        public void Begin(ParticleSystem[] particles, float[] originalSpeeds, bool stopEmission)
        {
            this.particles = particles;
            this.originalSpeeds = originalSpeeds;
            monitoring = true;
            if (!stopEmission) return;
            for (int index = 0; index < particles.Length; index++)
                particles[index].Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        /// <summary>
        /// 等待所有粒子系统不再存活后恢复速度并归还池。
        /// </summary>
        private void Update()
        {
            if (!monitoring) return;
            for (int index = 0; index < particles.Length; index++)
            {
                if (particles[index].IsAlive(true)) return;
            }

            monitoring = false;
            for (int index = 0; index < particles.Length; index++)
            {
                ParticleSystem.MainModule main = particles[index].main;
                main.simulationSpeed = originalSpeeds[index];
            }
            PoolManager.Instance.Recycle(gameObject);
        }
    }
}
