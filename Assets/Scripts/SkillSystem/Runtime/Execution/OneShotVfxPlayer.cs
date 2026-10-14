using System;
using RPG.Markers;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.Pooling;

namespace RPG.SkillSystem
{
    /// <summary>从角色语义 Marker 播放一次性非循环粒子，并在尾迹结束后归还对象池。</summary>
    internal static class OneShotVfxPlayer
    {
        #region 播放

        /// <summary>解析 Marker、取出 Prefab 池实例并启动非循环粒子播放。</summary>
        /// <param name="owner">提供 Marker Provider 的技能 Owner。</param>
        /// <param name="markerKey">特效要跟随的语义挂点。</param>
        /// <param name="prefab">从对象池取出的特效 Prefab。</param>
        /// <param name="localPosition">相对 Marker 的本地位置偏移。</param>
        /// <param name="localEulerAngles">相对 Marker 的本地欧拉角偏移。</param>
        /// <param name="scaleMultiplier">Prefab 根节点缩放倍率。</param>
        /// <returns>特效成功从池中取出并启动时返回 true。</returns>
        internal static bool TryPlay(
            IGameplayAbilitySystemOwner owner,
            MarkerKey markerKey,
            GameObject prefab,
            Vector3 localPosition,
            Vector3 localEulerAngles,
            float scaleMultiplier)
        {
            IMarkerProvider provider = owner.MarkerProvider;
            if (markerKey == null || provider == null || !provider.TryGetMarker(markerKey, out Transform marker))
            {
                Debug.LogWarning(
                    $"[OneShotVfxPlayer] 无法解析释放特效 Marker，key={markerKey?.name ?? "<null>"}。",
                    owner.RootTransform);
                return false;
            }

            GameObject pooledObject = PoolManager.Instance.Get(prefab, marker);
            if (pooledObject == null)
            {
                Debug.LogWarning($"[OneShotVfxPlayer] 对象池未能创建释放特效，prefab={prefab.name}。", marker);
                return false;
            }

            Transform effectTransform = pooledObject.transform;
            effectTransform.localPosition = localPosition;
            effectTransform.localRotation = Quaternion.Euler(localEulerAngles);
            ParticleSystem[] particles = pooledObject.GetComponentsInChildren<ParticleSystem>(true);
            if (particles.Length == 0)
            {
                Debug.LogWarning($"[OneShotVfxPlayer] 释放特效没有 ParticleSystem，已立即回池，prefab={prefab.name}。", pooledObject);
                PoolManager.Instance.Recycle(pooledObject);
                return false;
            }

            bool[] originalLoopByParticle = new bool[particles.Length];
            for (int index = 0; index < particles.Length; index++)
            {
                ParticleSystem.MainModule main = particles[index].main;
                originalLoopByParticle[index] = main.loop;
                main.loop = false;
                particles[index].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particles[index].Play(true);
            }

            effectTransform.localScale = Vector3.Scale(prefab.transform.localScale, Vector3.one * scaleMultiplier);
            OneShotVfxRecycler recycler = pooledObject.GetComponent<OneShotVfxRecycler>();
            if (recycler == null) recycler = pooledObject.AddComponent<OneShotVfxRecycler>();
            recycler.Begin(pooledObject, particles, originalLoopByParticle);
            Debug.Log($"[OneShotVfxPlayer] 已播放右手释放特效，prefab={prefab.name}，particleCount={particles.Length}。", pooledObject);
            return true;
        }

        #endregion
    }

    /// <summary>等待非循环粒子全部自然结束，恢复 Prefab 循环配置并归还池实例。</summary>
    internal sealed class OneShotVfxRecycler : MonoBehaviour
    {
        #region 回收状态

        private GameObject pooledObject;
        private ParticleSystem[] particles;
        private bool[] originalLoopByParticle;
        private bool isMonitoring;

        #endregion

        #region 生命周期

        /// <summary>开始监视单次播放粒子并保存每个系统原始的 Loop 设置。</summary>
        /// <param name="targetObject">需要归还的对象池实例。</param>
        /// <param name="targetParticles">实例内全部粒子系统。</param>
        /// <param name="sourceLoopSettings">与粒子数组一一对应的原 Loop 配置。</param>
        public void Begin(GameObject targetObject, ParticleSystem[] targetParticles, bool[] sourceLoopSettings)
        {
            pooledObject = targetObject;
            particles = targetParticles;
            originalLoopByParticle = sourceLoopSettings;
            isMonitoring = true;
        }

        /// <summary>所有粒子自然结束后恢复 Loop 属性并将实例归还对象池。</summary>
        private void Update()
        {
            if (!isMonitoring) return;
            for (int index = 0; index < particles.Length; index++)
                if (particles[index] != null && particles[index].IsAlive(true))
                    return;

            isMonitoring = false;
            for (int index = 0; index < particles.Length; index++)
            {
                if (particles[index] == null) continue;
                ParticleSystem.MainModule main = particles[index].main;
                main.loop = originalLoopByParticle[index];
                particles[index].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            GameObject completedObject = pooledObject;
            pooledObject = null;
            particles = Array.Empty<ParticleSystem>();
            originalLoopByParticle = Array.Empty<bool>();
            PoolManager.Instance.Recycle(completedObject);
            Debug.Log($"[OneShotVfxRecycler] 非循环释放特效自然结束并已回池，object={completedObject.name}。", completedObject);
        }

        #endregion
    }
}
