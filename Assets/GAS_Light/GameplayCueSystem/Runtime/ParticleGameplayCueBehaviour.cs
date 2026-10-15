using Sirenix.OdinInspector;
using UnityEngine;

namespace WS_Modules.GAS.GameplayCue
{
    /// <summary>按攻击者指向受击者的水平方向播放命中粒子，并在全部粒子自然结束后归还对象池。</summary>
    [InfoBox("依赖此对象根节点或子节点中的 ParticleSystem；命中特效 Prefab 缺少粒子系统时无法播放。")]
    public sealed class ParticleGameplayCueBehaviour : GameplayCueBehaviour, IGameplayCueDirectionProvider
    {
        #region 依赖字段

        // 缓存 Prefab 中的粒子组件，供对象池取出、播放和回收时使用。
        private ParticleSystem[] particleSystems = System.Array.Empty<ParticleSystem>();

        #endregion

        #region 运行时状态

        // 首次播放命中粒子时记录日志，避免重复打印。
        private static bool firstPlaybackLogged;
        private GameplayCueRuntime currentRuntime;

        #endregion

        #region Execute 释放策略

        /// <summary>粒子仍在播放时保留 Cue Runtime，由本行为在结束后释放。</summary>
        public override bool ReleaseAfterExecute => false;

        #endregion

        #region 命中方向解析

        /// <summary>从本次请求的双方 Root 位置解析攻击者指向受击者的世界水平单位向量。</summary>
        /// <param name="request">本次 Cue 的来源与目标；不从 Marker 或池化实例读取旧方向。</param>
        /// <param name="direction">有效时返回攻击者指向受击者的世界水平单位向量，否则为零。</param>
        /// <returns>双方 Root 有效且水平位置不重合时返回 true。</returns>
        public bool TryGetWorldDirection(GameplayCueRequest request, out Vector3 direction)
        {
            direction = Vector3.zero;
            Transform sourceRoot = request.Source == null ? null : request.Source.Owner?.RootTransform;
            Transform targetRoot = request.Target == null ? null : request.Target.Owner?.RootTransform;
            if (sourceRoot == null || targetRoot == null) return false;

            // 受击来向与特效传播方向相反；这里从攻击者指向目标，忽略双方高度差。
            Vector3 planarDirection = Vector3.ProjectOnPlane(
                targetRoot.position - sourceRoot.position, Vector3.up);
            if (planarDirection.sqrMagnitude <= 0.0001f) return false;

            direction = planarDirection.normalized;
            return true;
        }

        #endregion

        #region Unity 生命周期

        /// <summary>缓存包含非激活节点在内的全部粒子系统，供对象池重复使用。</summary>
        protected override void Awake()
        {
            base.Awake();
            particleSystems = GetComponentsInChildren<ParticleSystem>(true);
        }

        /// <summary>监视所有粒子的自然结束，并在最后一个系统结束后释放本次 Cue。</summary>
        private void Update()
        {
            if (currentRuntime == null) return;
            if (currentRuntime.IsReleased)
            {
                currentRuntime = null;
                return;
            }

            for (int index = 0; index < particleSystems.Length; index++)
            {
                ParticleSystem particleSystem = particleSystems[index];
                if (particleSystem != null && particleSystem.IsAlive(true)) return;
            }

            GameplayCueRuntime finishedRuntime = currentRuntime;
            currentRuntime = null;
            finishedRuntime.Release();
        }

        #endregion

        #region Cue 生命周期

        /// <inheritdoc />
        protected override void OnCueSpawn(GameplayCueRuntime runtime)
        {
            currentRuntime = runtime;
            StopAndClearParticles();
        }

        /// <inheritdoc />
        protected override void OnExecute(GameplayCueRuntime runtime)
        {
            if (particleSystems.Length == 0)
            {
                Debug.LogError(
                    $"[ParticleGameplayCueBehaviour] Cue '{runtime.CueTag}' 的 Prefab 没有 ParticleSystem，立即释放实例。",
                    this);
                runtime.Release();
                return;
            }

            // 来源特效含有循环粒子；仅修改对象池实例，确保本次命中能自然结束，不改动源资源。
            for (int index = 0; index < particleSystems.Length; index++)
            {
                ParticleSystem particleSystem = particleSystems[index];
                if (particleSystem == null) continue;
                ParticleSystem.MainModule main = particleSystem.main;
                main.loop = false;
                particleSystem.Play(true);
            }

            if (firstPlaybackLogged) return;
            firstPlaybackLogged = true;
            Debug.Log(
                $"[ParticleGameplayCueBehaviour] 首次播放命中粒子，cue='{runtime.CueTag}'，particleSystems={particleSystems.Length}。",
                this);
        }

        /// <inheritdoc />
        protected override void OnCueRecycle(GameplayCueRuntime runtime)
        {
            currentRuntime = null;
            StopAndClearParticles();
        }

        #endregion

        #region 内部粒子重置

        /// <summary>停止整个粒子层级并清除残留，避免对象池复用时出现上一击的画面。</summary>
        private void StopAndClearParticles()
        {
            for (int index = 0; index < particleSystems.Length; index++)
            {
                ParticleSystem particleSystem = particleSystems[index];
                if (particleSystem != null)
                    particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        #endregion
    }
}
