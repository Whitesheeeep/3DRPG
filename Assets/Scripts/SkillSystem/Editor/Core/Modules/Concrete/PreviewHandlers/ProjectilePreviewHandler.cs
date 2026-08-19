#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace RPG.SkillSystem.Editor
{
    /// <summary>为 Projectile 轨道创建窗口私有的隐藏实例预览。</summary>
    internal sealed class ProjectilePreviewFactory : ITrackPreviewFactory
    {
        /// <summary>创建不与其他时间轴窗口共享实例的 Projectile Preview。</summary>
        public ITrackPreviewHandler Create() => new ProjectilePreviewHandler();
    }

    /// <summary>
    /// 根据 Projectile Clip 的起始帧、速度和寿命显示线性移动预览，不参与运行时池和物理结算。
    /// </summary>
    internal sealed class ProjectilePreviewHandler : ITrackPreviewHandler, ITrackPreviewStatusProvider
    {
        #region 状态

        private readonly Dictionary<string, PreviewProjectileInstance> instances = new();
        private readonly HashSet<string> visibleKeys = new();
        private bool disposed;

        /// <summary>获取最近一次预览中无法解析的 Projectile 状态。</summary>
        public string StatusMessage { get; private set; } = string.Empty;

        #endregion

        #region 预览操作

        /// <summary>配置内容变化时销毁旧副本，避免 Prefab 或 Spawn 参数继续复用。</summary>
        public void Invalidate() => Clear();

        /// <summary>按采样原因同步当前帧所有 Projectile 预览实例。</summary>
        /// <param name="context">当前 SkillConfig、角色姿态和时间轴帧上下文。</param>
        public void SampleFrame(in PreviewFrameContext context)
        {
            if (disposed || context.Config == null || context.Actor?.RootTransform == null) return;
            StatusMessage = string.Empty;
            if (context.Reason != PreviewSampleReason.PlaybackAdvance)
                ClearInstances();

            visibleKeys.Clear();
            for (int trackIndex = 0; trackIndex < context.Config.Tracks.Count; trackIndex++)
            {
                if (context.Config.Tracks[trackIndex] is not ProjectileTrackConfig track || track.Muted)
                    continue;
                for (int clipIndex = 0; clipIndex < track.Clips.Count; clipIndex++)
                    SampleClip(context, track.Clips[clipIndex]);
            }

            RemoveInvisibleInstances();
        }

        /// <summary>停止播放并清理全部编辑器隐藏副本。</summary>
        public void Stop() => ClearInstances();

        /// <summary>清理全部副本、键集合和状态栏错误。</summary>
        public void Clear()
        {
            ClearInstances();
            StatusMessage = string.Empty;
        }

        /// <summary>释放当前 Handler 持有的所有隐藏 Unity 对象。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Clear();
        }

        #endregion

        #region Clip 采样

        /// <summary>按起始帧、寿命和当前时间更新一个 Projectile Clip 的全部发射实例。</summary>
        /// <param name="context">当前预览帧上下文。</param>
        /// <param name="clip">待采样的 Projectile Clip。</param>
        private void SampleClip(in PreviewFrameContext context, ProjectileSkillClipConfig clip)
        {
            ProjectileSpawnConfig config = clip.SpawnConfig;
            if (config == null || context.Frame < clip.StartFrame) return;
            float elapsed = (context.Frame - clip.StartFrame) / (float)Mathf.Max(1, context.Config.FrameRate);
            if (elapsed >= config.Lifetime) return;
            if (config.FallbackPrefab == null)
            {
                RecordStatus("仅配置 Addressable Key 的 Projectile 暂不支持编辑器 Prefab 预览。");
                return;
            }
            if (!context.TryResolveBindingWorldMatrix(config.MarkerKey, clip.StartFrame,
                    out Matrix4x4 originMatrix))
            {
                RecordStatus($"Projectile Clip '{clip.Id}' 无法解析起始帧 Marker。");
                return;
            }

            for (int index = 0; index < config.ProjectileCount; index++)
            {
                string key = GetStableKey(clip, index);
                visibleKeys.Add(key);
                ProjectileSpawnPose pose = ProjectileSpawnUtility.CalculatePose(
                    originMatrix,
                    config.LocalPosition,
                    config.LocalEulerAngles,
                    config.SpreadAngle,
                    config.ProjectileCount,
                    index);
                PreviewProjectileInstance instance = GetOrCreate(key, config.FallbackPrefab, pose);
                if (instance == null) continue;
                instance.Apply(pose, elapsed, config.Speed);
            }
        }

        /// <summary>创建或复用一个 Prefab 隐藏副本，并关闭其运行时行为。</summary>
        /// <param name="key">Clip 与发射序号组成的稳定预览键。</param>
        /// <param name="prefab">用于表现采样的 Project Prefab。</param>
        /// <param name="pose">当前发射实例的起始 Pose。</param>
        /// <returns>可更新的预览实例；创建失败时返回 null。</returns>
        private PreviewProjectileInstance GetOrCreate(string key, GameObject prefab, ProjectileSpawnPose pose)
        {
            if (instances.TryGetValue(key, out PreviewProjectileInstance existing))
            {
                if (existing.Matches(prefab)) return existing;
                existing.Dispose();
                instances.Remove(key);
            }

            GameObject clone = Object.Instantiate(prefab);
            clone.name = $"Projectile Preview {key}";
            clone.hideFlags = HideFlags.HideAndDontSave;
            DisableRuntimeBehaviours(clone);
            PreviewProjectileInstance created = new(clone, prefab, pose);
            instances.Add(key, created);
            return created;
        }

        /// <summary>关闭 Projectile Behaviour、Rigidbody 物理和其他可能自行移动的脚本。</summary>
        /// <param name="clone">编辑器隐藏 Prefab 副本。</param>
        private static void DisableRuntimeBehaviours(GameObject clone)
        {
            MonoBehaviour[] behaviours = clone.GetComponentsInChildren<MonoBehaviour>(true);
            for (int index = 0; index < behaviours.Length; index++)
                if (behaviours[index] != null) behaviours[index].enabled = false;

            Rigidbody[] bodies = clone.GetComponentsInChildren<Rigidbody>(true);
            for (int index = 0; index < bodies.Length; index++)
            {
                bodies[index].isKinematic = true;
                bodies[index].detectCollisions = false;
                bodies[index].velocity = Vector3.zero;
                bodies[index].angularVelocity = Vector3.zero;
            }
        }

        /// <summary>移除当前帧不再存活的预览实例。</summary>
        private void RemoveInvisibleInstances()
        {
            var removal = new List<string>();
            foreach (KeyValuePair<string, PreviewProjectileInstance> pair in instances)
                if (!visibleKeys.Contains(pair.Key)) removal.Add(pair.Key);
            for (int index = 0; index < removal.Count; index++)
            {
                instances[removal[index]].Dispose();
                instances.Remove(removal[index]);
            }
        }

        /// <summary>销毁全部隐藏副本。</summary>
        private void ClearInstances()
        {
            foreach (PreviewProjectileInstance instance in instances.Values) instance.Dispose();
            instances.Clear();
            visibleKeys.Clear();
        }

        /// <summary>记录第一个预览错误，避免多个无效 Clip 覆盖状态栏。</summary>
        /// <param name="message">需要显示的局部预览错误。</param>
        private void RecordStatus(string message)
        {
            if (string.IsNullOrEmpty(StatusMessage)) StatusMessage = message;
        }

        /// <summary>生成稳定的 Clip/发射序号键。</summary>
        /// <param name="clip">Projectile Clip。</param>
        /// <param name="index">发射序号。</param>
        /// <returns>窗口内稳定的实例键。</returns>
        private static string GetStableKey(ProjectileSkillClipConfig clip, int index) =>
            $"{(string.IsNullOrEmpty(clip.Id) ? clip.GetHashCode().ToString() : clip.Id)}:{index}";

        #endregion

        #region 嵌套实例

        /// <summary>保存单个编辑器隐藏副本及其 Prefab 身份。</summary>
        private sealed class PreviewProjectileInstance
        {
            private readonly GameObject gameObject;
            private readonly GameObject prefab;

            /// <summary>创建一个待采样的编辑器 Projectile 副本。</summary>
            public PreviewProjectileInstance(GameObject gameObject, GameObject prefab, ProjectileSpawnPose pose)
            {
                this.gameObject = gameObject;
                this.prefab = prefab;
                Apply(pose, 0f, 0f);
            }

            /// <summary>判断当前实例是否仍来自同一个 Prefab。</summary>
            /// <param name="value">需要匹配的 Prefab。</param>
            /// <returns>Prefab 相同时返回 true。</returns>
            public bool Matches(GameObject value) => prefab == value;

            /// <summary>按起始 Pose、速度和时间更新隐藏副本 Transform。</summary>
            /// <param name="pose">投射物起始 Pose。</param>
            /// <param name="elapsed">从 Clip 起始帧开始经过的秒数。</param>
            /// <param name="speed">投射物速度。</param>
            public void Apply(ProjectileSpawnPose pose, float elapsed, float speed)
            {
                gameObject.transform.SetPositionAndRotation(
                    pose.Position + pose.Direction * (speed * elapsed), pose.Rotation);
            }

            /// <summary>立即销毁编辑器隐藏副本。</summary>
            public void Dispose()
            {
                if (gameObject != null) Object.DestroyImmediate(gameObject);
            }
        }

        #endregion
    }
}
#endif
