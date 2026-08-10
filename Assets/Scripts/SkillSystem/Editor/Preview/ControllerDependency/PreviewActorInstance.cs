#if UNITY_EDITOR
using System;
using Animancer;
using Animancer.Editor.Previews;
using RPG.Markers;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 保存相对预览角色初始根变换的累计位置与旋转。
    /// </summary>
    internal readonly struct RootPose
    {
        public static RootPose Identity => new(Vector3.zero, Quaternion.identity);

        public Vector3 Position { get; }
        public Quaternion Rotation { get; }

        // 创建相对初始根变换的不可变姿态值。
        internal RootPose(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        // 按当前累计旋转组合下一段局部根位移和旋转。
        internal RootPose Apply(RootDelta delta) => new(
            Position + Rotation * delta.Position,
            Rotation * delta.Rotation);
    }

    /// <summary>
    /// 保存一段动画根曲线从起点到终点产生的局部位移与旋转差。
    /// </summary>
    internal readonly struct RootDelta
    {
        public static RootDelta Identity => new(Vector3.zero, Quaternion.identity);

        public Vector3 Position { get; }
        public Quaternion Rotation { get; }

        // 创建可被连续组合的根运动增量。
        internal RootDelta(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        // 依次组合两段局部根运动，用于跨循环累计动画位移。
        internal RootDelta Then(RootDelta next) => new(
            Position + Rotation * next.Position,
            Rotation * next.Rotation);
    }

    /// <summary>
    /// 在已加载的固定预览场景中创建不可保存的隔离角色副本。
    /// </summary>
    internal sealed class PreviewActorFactory
    {
        // 创建角色副本并返回明确失败原因；失败路径不会留下场景对象或隐藏状态。
        internal bool TryCreate(GameObject source, out PreviewActorInstance result, out string error)
        {
            result = null;
            error = string.Empty;
            if (source == null)
            {
                error = "请先选择演示角色。";
                return false;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                error = "当前没有可用于技能预览的已加载场景。";
                return false;
            }

            GameObject instance = null;
            try
            {
                instance = EditorUtility.IsPersistent(source)
                    ? PrefabUtility.InstantiatePrefab(source, scene) as GameObject
                    : Object.Instantiate(source);
                if (instance == null)
                {
                    error = "无法创建演示角色的预览副本。";
                    return false;
                }

                if (instance.scene != scene)
                    SceneManager.MoveGameObjectToScene(instance, scene);
                instance.name = $"{source.name} (技能预览)";
                instance.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor;

                foreach (MonoBehaviour behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour != null) behaviour.enabled = false;
                }

                Animator animator = instance.GetComponent<Animator>() ??
                                    instance.GetComponentInChildren<Animator>(true);
                if (animator == null)
                {
                    error = "演示角色及其子对象中没有 Animator，无法预览动画。";
                    Object.DestroyImmediate(instance);
                    return false;
                }

                result = new PreviewActorInstance(source, instance, animator);
                return true;
            }
            catch (Exception exception)
            {
                if (instance != null) Object.DestroyImmediate(instance);
                error = $"创建演示角色预览副本失败：{exception.Message}";
                return false;
            }
        }
    }

    /// <summary>
    /// 管理隔离角色副本、实例级 MarkerProvider、AnimancerGraph 与绝对根变换。
    /// </summary>
    internal sealed class PreviewActorInstance : IDisposable
    {
        #region 角色与图状态

        private readonly GameObject source;
        private readonly GameObject instance;
        private readonly Animator animator;
        private readonly AnimancerGraph graph;
        private readonly MarkerProvider rootMarkerProvider;
        private readonly Vector3 initialPosition;
        private readonly Quaternion initialRotation;
        private readonly Vector3 initialScale;
        private readonly bool sourceWasHidden;
        private bool disposed;

        internal GameObject Source => source;
        internal Transform RootTransform => !disposed && instance != null ? instance.transform : null;
        internal bool IsValid => !disposed && instance != null && animator != null &&
                                 graph != null && graph.IsValidOrDispose();

        #endregion

        #region 生命周期

        /// <summary>
        /// 创建独立 AnimancerGraph，并仅在 Scene View 中隐藏场景源对象。
        /// </summary>
        /// <param name="source">预览使用的源对象。</param>
        /// <param name="instance">已经放入预览场景的隔离副本。</param>
        /// <param name="animator">隔离副本上的 Animator。</param>
        internal PreviewActorInstance(GameObject source, GameObject instance, Animator animator)
        {
            this.source = source;
            this.instance = instance ?? throw new ArgumentNullException(nameof(instance));
            this.animator = animator ?? throw new ArgumentNullException(nameof(animator));
            RebuildMarkerProviders(instance, out rootMarkerProvider);
            initialPosition = instance.transform.position;
            initialRotation = instance.transform.rotation;
            initialScale = instance.transform.localScale;

            sourceWasHidden = source != null && !EditorUtility.IsPersistent(source) &&
                              SceneVisibilityManager.instance.IsHidden(source);

            AnimancerGraph.SetNextGraphName($"{instance.name} Animancer Preview");
            graph = new AnimancerGraph();
            graph.Initialize(new DummyAnimancerComponent(animator, graph));
            graph.PauseGraph();
            RestoreBindPose();

            if (source != null && !EditorUtility.IsPersistent(source) && !sourceWasHidden)
                SceneVisibilityManager.instance.Hide(source, true);
        }

        /// <summary>
        /// 销毁 Graph 和不可保存副本，并恢复源场景对象原有的可见状态。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (graph != null && graph.IsValidOrDispose()) graph.Destroy();
            if (source != null && !EditorUtility.IsPersistent(source) && !sourceWasHidden)
                SceneVisibilityManager.instance.Show(source, true);
            if (instance != null) Object.DestroyImmediate(instance);
        }

        #endregion

        #region 绑定解析与姿势采样

        /// <summary>
        /// 解析角色根 Provider 中的语义 Socket；空 Key 明确返回角色根节点。
        /// </summary>
        /// <param name="key">要查询的 MarkerKey。</param>
        /// <param name="marker">成功时返回对应的 Transform。</param>
        /// <returns>找到有效挂点时返回 true。</returns>
        internal bool TryGetMarker(MarkerKey key, out Transform marker)
        {
            if (key == null)
            {
                marker = RootTransform;
                return marker != null;
            }

            if (rootMarkerProvider == null)
            {
                marker = null;
                Debug.LogError("预览角色根节点没有 MarkerProvider，无法解析非空 MarkerKey。", instance);
                return false;
            }

            if (rootMarkerProvider.TryGetMarker(key, out marker))
            {
                return true;
            }

            Debug.LogError($"预览角色根 MarkerProvider 中不存在 MarkerKey“{key.name}”。", instance);
            return false;
        }

        // 从激活 Provider 中解析唯一同时包含刀根和刀尖 Key 的单刃 Socket 作用域。
        internal bool TryGetWeaponTraceMarkers(MarkerKey rootKey, MarkerKey tipKey,
            out Transform root, out Transform tip, out string error)
        {
            root = null;
            tip = null;
            if (disposed || instance == null)
            {
                error = "预览角色已经失效。";
                return false;
            }

            if (rootKey == null || tipKey == null)
            {
                error = "EditorConfig 尚未配置 WeaponTrace 刀根或刀尖 MarkerKey。";
                return false;
            }

            if (rootKey == tipKey)
            {
                error = "WeaponTrace 刀根和刀尖不能使用相同的 MarkerKey。";
                return false;
            }

            MarkerProvider matchedProvider = null;
            foreach (MarkerProvider provider in instance.GetComponentsInChildren<MarkerProvider>(true))
            {
                if (!provider.gameObject.activeInHierarchy) continue;

                if (!provider.TryGetMarker(rootKey, out Transform candidateRoot) ||
                    !provider.TryGetMarker(tipKey, out Transform candidateTip))
                    continue;
                if (matchedProvider != null)
                {
                    root = null;
                    tip = null;
                    error = $"预览角色中存在多个激活的 MarkerProvider 同时提供“{rootKey.name}”和“{tipKey.name}”。";
                    return false;
                }

                matchedProvider = provider;
                root = candidateRoot;
                tip = candidateTip;
            }

            if (matchedProvider == null)
            {
                error = $"没有激活的 MarkerProvider 同时提供“{rootKey.name}”和“{tipKey.name}”。";
                return false;
            }

            if (root == tip)
            {
                root = null;
                tip = null;
                error = "WeaponTrace 刀根和刀尖解析到了同一个 Transform。";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// 显式重建预览副本中的全部 MarkerProvider；重建失败由 Provider 直接记录诊断。
        /// </summary>
        /// <param name="actor">需要重建 Marker 索引的角色副本。</param>
        /// <param name="rootProvider">角色根节点上的 MarkerProvider，可能为空。</param>
        private static void RebuildMarkerProviders(GameObject actor,
            out MarkerProvider rootProvider)
        {
            rootProvider = actor.GetComponent<MarkerProvider>();
            foreach (MarkerProvider provider in actor.GetComponentsInChildren<MarkerProvider>(true))
                provider.TryRebuild();
        }
        // 使用 AnimancerGraph 绝对定位源动画时间；根节点位移由 RootMotionCache 单独应用。
        internal void SamplePose(AnimationSample sample)
        {
            if (disposed || sample.Clip == null) return;
            ResetRootTransform();
            animator.applyRootMotion = false;
            graph.PauseGraph();
            graph.Stop();
            AnimancerState state = graph.Layers[0].Play(sample.Clip);
            state.Speed = 0f;
            state.Time = sample.SampleTime;
            graph.Evaluate();
            ResetRootTransform();
        }

        // 使用 Unity 的确定性动画采样读取根曲线值，仅供绝对帧 Root Motion 缓存计算。
        internal RootPose SampleRootCurve(AnimationClip clip, float time)
        {
            if (disposed || clip == null) return RootPose.Identity;
            graph.PauseGraph();
            graph.Stop();
            animator.applyRootMotion = true;
            animator.Rebind();
            Transform root = instance.transform;
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.localScale = initialScale;
            clip.SampleAnimation(instance, Mathf.Clamp(time, 0f, Mathf.Max(0f, clip.length)));
            RootPose pose = new(root.position, root.rotation);
            ResetRootTransform();
            return pose;
        }

        // 将相对根姿态转换到角色副本创建时的世界空间，保证任意跳帧结果一致。
        internal void ApplyAbsoluteRootPose(RootPose pose)
        {
            if (disposed) return;
            instance.transform.SetPositionAndRotation(
                initialPosition + initialRotation * pose.Position,
                initialRotation * pose.Rotation);
            instance.transform.localScale = initialScale;
        }

        // 将指定相对根姿态转换成角色创建场景中的世界矩阵，供不跟随角色的预览对象冻结生成姿态。
        internal Matrix4x4 GetRootWorldMatrix(RootPose pose) =>
            Matrix4x4.TRS(
                initialPosition + initialRotation * pose.Position,
                initialRotation * pose.Rotation,
                initialScale);

        // 停止全部动画状态并恢复角色初始绑定姿势和根变换。
        internal void RestoreBindPose()
        {
            if (disposed) return;
            graph.PauseGraph();
            graph.Stop();
            ResetRootTransform();
            animator.Rebind();
            animator.Update(0f);
            ResetRootTransform();
        }

        // 暂停 Graph 而不重置已显示姿势，供时间轴 Pause 和末帧停止使用。
        internal void StopGraph()
        {
            if (!disposed) graph.PauseGraph();
        }

        // 恢复副本创建时的根节点世界变换，消除重复采样产生的累计漂移。
        private void ResetRootTransform()
        {
            Transform root = instance.transform;
            root.SetPositionAndRotation(initialPosition, initialRotation);
            root.localScale = initialScale;
        }

        #endregion
    }
}
#endif
