using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.Pooling;
using RPG.Markers;

namespace WS_Modules.GAS.GameplayCue
{
    /// <summary>订阅 ASC Cue 事件，负责查表、取出对象池实例、执行表现并回收对象。</summary>
    public sealed class GameplayCueCtrl : IGameplayCueCtrl
    {
        #region 字段
        // 还在“持续生效”的 Cue
        private readonly List<GameplayCueRuntime> activeCues = new();
        // 还没释放回对象池的所有 Cue
        private readonly List<GameplayCueRuntime> liveCues = new();
        private bool disposed;
        #endregion

        #region 构造函数与属性
        /// <summary>创建并绑定指定 ASC 的 Cue Controller。</summary>
        /// <param name="owner">发布 Cue 请求的 ASC。</param>
        public GameplayCueCtrl(GameplayAbilitySystemComponent owner)
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Owner.CueRequested += OnCueRequested;
        }

        /// <summary>获取所属 ASC。</summary>
        public GameplayAbilitySystemComponent Owner { get; }
        /// <summary>获取持续 Cue 的只读列表。</summary>
        public IReadOnlyList<GameplayCueRuntime> ActiveCues => activeCues;
        #endregion

        #region 公开操作
        /// <summary>移除指定的持续或一次性 Cue 句柄。</summary>
        public bool TryRemove(GameplayCueRuntime runtime)
        {
            if (runtime == null || !liveCues.Contains(runtime)) return false;
            return ReleaseRuntime(runtime, runtime.IsActive);
        }

        /// <summary>回收当前 ASC 管理的全部 Cue 对象，并保留事件订阅以支持显式重新初始化。</summary>
        public void Clear()
        {
            // 每次从当前末尾取得 Runtime；OnRemove 可能同步释放其他 Cue，不能保留旧索引。
            while (liveCues.Count > 0)
            {
                GameplayCueRuntime runtime = liveCues[liveCues.Count - 1];
                ReleaseRuntime(runtime, runtime.IsActive);
            }
            activeCues.Clear();
            liveCues.Clear();
        }

        /// <summary>解除 ASC 事件订阅并清理控制器，供 ASC 销毁时调用。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Owner.CueRequested -= OnCueRequested;
            Clear();
        }
        #endregion

        #region 事件处理
        /// <summary>
        /// 接收 ASC 发布的 Cue 请求，并根据请求阶段创建、激活或移除表现对象。
        /// </summary>
        private void OnCueRequested(GameplayCueRequest request)
        {
            if (disposed) return;
            if (!ReferenceEquals(request.Target, Owner))
            {
                Debug.LogError("GameplayCue 请求的 Target 不是当前 ASC。", Owner);
                return;
            }

            if (!GameplayCueManager.Instance.IsInitialized)
            {
                Debug.LogError("GameplayCueManager 尚未初始化 CueDatabase，无法处理 Cue 请求。", Owner);
                return;
            }

            if (!GameplayCueManager.Instance.TryGetCue(request.CueTag, out GameplayCueData data))
            {
                Debug.LogError($"未找到 CueTag 对应的 GameplayCueData：{request.CueTag}", Owner);
                return;
            }

            switch (request.EventType)
            {
                case GameplayCueEventType.Execute:
                    CreateExecute(data, request);
                    break;
                case GameplayCueEventType.Active:
                    CreateActive(data, request);
                    break;
                case GameplayCueEventType.Remove:
                    RemoveOriginCues(request);
                    break;
            }
        }

        /// <summary>
        /// 执行一次性 Cue；表现脚本可以主动释放，Controller 会在表现脚本未释放时直接兜底回收。
        /// </summary>
        private void CreateExecute(GameplayCueData data, GameplayCueRequest request)
        {
            GameplayCueRuntime cueRuntime = CreateRuntime(data, request);
            if (cueRuntime == null) return;
            cueRuntime.Behaviour.InvokeCueSpawn(cueRuntime);
            if (!cueRuntime.IsReleased) cueRuntime.Behaviour.InvokeExecute(cueRuntime);
            if (!cueRuntime.IsReleased) ReleaseRuntime(cueRuntime, false);
        }

        /// <summary>
        /// 创建持续表现，并按来源和 CueTag 防止同一个 GE 或 GA Runtime 重复创建 Active Cue。
        /// </summary>
        private void CreateActive(GameplayCueData data, GameplayCueRequest request)
        {
            if (FindOriginCue(request) != null) return;
            GameplayCueRuntime runtime = CreateRuntime(data, request);
            if (runtime == null) return;
            runtime.IsActive = true;
            activeCues.Add(runtime);
            runtime.Behaviour.InvokeCueSpawn(runtime);
            // 不是 Instant 的 Cue
            if (!runtime.IsReleased) runtime.Behaviour.InvokeActive(runtime);
        }

        /// <summary>
        /// 移除请求来源对应的持续 Cue；叠层减少但 Runtime 仍存在时不会触发回收。
        /// </summary>
        private void RemoveOriginCues(GameplayCueRequest request)
        {
            GameplayCueRuntime runtime = FindOriginCue(request);
            if (runtime == null) return;
            ReleaseRuntime(runtime, true);
        }
        #endregion

        #region 内部辅助
        /// <summary>
        /// 按 GE 或 GA Runtime 引用查找对应表现，避免相同配置的其他实例被误删。
        /// </summary>
        private GameplayCueRuntime FindOriginCue(GameplayCueRequest request)
        {
            for (int i = 0; i < activeCues.Count; i++)
            {
                GameplayCueRuntime runtime = activeCues[i];
                if (request.EffectRuntime != null && ReferenceEquals(runtime.EffectRuntime, request.EffectRuntime) && runtime.CueTag == request.CueTag)
                    return runtime;
                if (request.AbilityRuntime != null && ReferenceEquals(runtime.AbilityRuntime, request.AbilityRuntime) && runtime.CueTag == request.CueTag)
                    return runtime;
            }
            return null;
        }

        /// <summary>
        /// 从对象池获取表现对象，应用请求空间信息，并创建由 Controller 管理的 Runtime 句柄。
        /// </summary>
        private GameplayCueRuntime CreateRuntime(GameplayCueData data, GameplayCueRequest request)
        {
            Transform parent = request.AttachTransform;
            GameObject cueObject = null;
            // 检查资源 GO 或者特效路径是否有效
            if (!string.IsNullOrWhiteSpace(data.AddressableKey))
                cueObject = PoolManager.Instance.Get(data.AddressableKey, parent);
            if (cueObject == null && data.FallbackPrefab != null)
                cueObject = PoolManager.Instance.Get(data.FallbackPrefab, parent);
            if (cueObject == null)
            {
                Debug.LogError($"GameplayCue 无法获取表现对象：{data.name}", Owner);
                return null;
            }

            GameplayCueBehaviour behaviour = cueObject.GetComponent<GameplayCueBehaviour>();
            if (behaviour == null)
            {
                Debug.LogError($"GameplayCue 表现对象缺少 GameplayCueBehaviour：{cueObject.name}", cueObject);
                PoolManager.Instance.Recycle(cueObject);
                return null;
            }

            ApplyPlacement(data, request, cueObject.transform);
            var runtime = new GameplayCueRuntime(this, data, request, cueObject, behaviour);
            liveCues.Add(runtime);
            return runtime;
        }

        /// <summary>
        /// 按请求挂点、显式世界位置和 CueData 默认模式设置表现对象的父节点与变换。
        /// </summary>
        private void ApplyPlacement(GameplayCueData data, GameplayCueRequest request, Transform cueTransform)
        {
            // 自行指定 AttachTransform
            if (request.AttachTransform != null)
            {
                cueTransform.SetParent(request.AttachTransform, false);
                cueTransform.localPosition = data.LocalPosition;
                cueTransform.localRotation = data.LocalRotation;
                return;
            }

            // 没有指定，进行解析
            // world 模式
            if (request.HasExplicitPlacement)
            {
                cueTransform.SetParent(null, true);
                cueTransform.SetPositionAndRotation(request.Position + data.LocalPosition, request.Rotation * data.LocalRotation);
                return;
            }

            Transform anchor = ResolveDefaultAnchor(data, request);
            if (anchor != null && data.FollowAnchor)
            {
                cueTransform.SetParent(anchor, false);
                cueTransform.localPosition = data.LocalPosition;
                cueTransform.localRotation = data.LocalRotation;
                return;
            }

            // world 配置下的位置
            cueTransform.SetParent(null, true);
            Vector3 position = anchor == null ? data.LocalPosition : anchor.TransformPoint(data.LocalPosition);
            Quaternion rotation = anchor == null ? data.LocalRotation : anchor.rotation * data.LocalRotation;
            cueTransform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>
        /// 根据 DefaultAnchor 选择 Source 或 Target ASC，并在其根节点查找 Marker；Marker 不可用时回退到同一 ASC Transform。
        /// </summary>
        private Transform ResolveDefaultAnchor(GameplayCueData data, GameplayCueRequest request)
        {
            if (data.DefaultAnchor == GameplayCueAnchor.World) return null;

            GameplayAbilitySystemComponent anchorAsc = data.DefaultAnchor == GameplayCueAnchor.Source
                ? request.Source
                : request.Target;
            if (anchorAsc == null) return null;

            if (data.MarkerKey == null) return anchorAsc.transform;

            IMarkerProvider provider = anchorAsc.GetComponent<IMarkerProvider>();
            if (provider != null && provider.TryGetMarker(data.MarkerKey, out Transform marker))
                return marker;

            Debug.LogWarning(
                $"GameplayCueData '{data.name}' 无法在 ASC '{anchorAsc.name}' 解析 MarkerKey '{data.MarkerKey.name}'，将回退到 ASC Transform。",
                anchorAsc);
            return anchorAsc.transform;
        }

        /// <summary>
        /// 执行唯一的内部回收事务；在 OnRemove 前取得释放权并移出集合，阻止同步回调重入。
        /// </summary>
        /// <param name="runtime">需要归还对象池的 Cue Runtime。</param>
        /// <param name="invokeRemove">是否先发送持续表现的移除回调。</param>
        /// <returns>本次调用取得释放权并完成回收时返回 true。</returns>
        internal bool ReleaseRuntime(GameplayCueRuntime runtime, bool invokeRemove)
        {
            if (runtime == null || !runtime.TryBeginRelease()) return false;
            // 必须先解除 Controller 所有权，OnRemove 内再次 TryRemove 才会立即失败。
            activeCues.Remove(runtime);
            liveCues.Remove(runtime);
            if (invokeRemove && runtime.IsActive) runtime.Behaviour.InvokeRemove(runtime);
            runtime.Behaviour.InvokeCueRecycle(runtime);
            PoolManager.Instance.Recycle(runtime.CueObject);
            runtime.MarkReleased();
            return true;
        }
        #endregion
    }
}
