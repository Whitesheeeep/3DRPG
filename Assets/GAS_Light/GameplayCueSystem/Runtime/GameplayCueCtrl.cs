using System;
using System.Collections.Generic;
using RPG.Markers;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.Pooling;

namespace WS_Modules.GAS.GameplayCue
{
    /// <summary>
    /// 接收 ASC Cue 请求，协调共享与本地 Handler，并收口持续 Handler 状态。
    /// </summary>
    public sealed class GameplayCueCtrl : IGameplayCueCtrl
    {
        #region 依赖字段

        // Controller 依赖所属 ASC 作为请求边界，视觉 Handler 依赖池管理器创建和回收对象。
        private readonly GameplayAbilitySystemComponent owner;

        #endregion

        #region 状态字段

        private readonly List<GameplayCueRuntime> activeVisualCues = new();
        private readonly List<GameplayCueRuntime> liveVisualCues = new();
        private readonly List<GameplayCueActiveRecord> activeRecords = new();
        private bool disposed;

        #endregion

        #region 构造函数与属性

        /// <summary>创建并订阅指定 ASC 的 Cue 请求。</summary>
        /// <param name="owner">发布 Cue 请求的 ASC。</param>
        public GameplayCueCtrl(GameplayAbilitySystemComponent owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            owner.CueRequested += OnCueRequested;
        }

        /// <summary>获取仍处于 Active 状态的 Visual Cue 句柄。</summary>
        public IReadOnlyList<GameplayCueRuntime> ActiveCues => activeVisualCues;

        #endregion

        #region ASC Handler 绑定

        /// <summary>在 ASC 核心初始化成功后绑定本地 Handler 实例。</summary>
        public void BindLocalHandlers()
        {
            IReadOnlyList<ASCGameplayCueHandler> handlers = owner.LocalCueHandlers;
            for (int index = 0; index < handlers.Count; index++)
            {
                ASCGameplayCueHandler handler = handlers[index];
                if (handler == null)
                    throw new InvalidOperationException($"ASC '{owner.name}' 的本地 Cue Handler[{index}] 为空。");
            }

            int bindingIndex = 0;
            try
            {
                for (; bindingIndex < handlers.Count; bindingIndex++)
                {
                    ASCGameplayCueHandler handler = handlers[bindingIndex];
                    handler.Bind(owner);
                }
            }
            catch
            {
                // Bind 回调可能已经订阅了外部服务；失败时解除已尝试绑定的实例，再把原错误交给 ASC。
                for (int index = bindingIndex; index >= 0; index--)
                    if (index < handlers.Count) handlers[index]?.Unbind();
                throw;
            }

            Debug.Log($"[GameplayCueCtrl] ASC '{owner.name}' 已绑定本地 Handler，数量={handlers.Count}。", owner);
        }

        /// <summary>清理 Active 状态后解除当前 ASC 的本地 Handler 绑定。</summary>
        public void UnbindLocalHandlers()
        {
            IReadOnlyList<ASCGameplayCueHandler> handlers = owner.LocalCueHandlers;
            for (int index = handlers.Count - 1; index >= 0; index--)
                handlers[index]?.Unbind();
            Debug.Log($"[GameplayCueCtrl] ASC '{owner.name}' 已解绑本地 Handler，数量={handlers.Count}。", owner);
        }

        #endregion

        #region 公开生命周期

        /// <summary>移除一个 Visual Cue Runtime；非 Visual Handler 状态由原 Handler 生命周期管理。</summary>
        /// <param name="runtime">需要归还的 Visual Runtime。</param>
        /// <returns>Runtime 属于当前 Controller 且已释放时返回 true。</returns>
        public bool TryRemove(GameplayCueRuntime runtime)
        {
            if (runtime == null || !liveVisualCues.Contains(runtime)) return false;
            for (int index = activeRecords.Count - 1; index >= 0; index--)
            {
                GameplayCueActiveRecord record = activeRecords[index];
                if (ReferenceEquals(record.State, runtime))
                {
                    ReleaseActiveRecord(record, true);
                    return runtime.IsReleased;
                }
            }
            return ReleaseRuntime(runtime, runtime.IsActive);
        }

        /// <summary>清理全部 Active Handler 状态和仍存活的 Visual 对象。</summary>
        public void Clear()
        {
            while (activeRecords.Count > 0)
                ReleaseActiveRecord(activeRecords[activeRecords.Count - 1], false);

            while (liveVisualCues.Count > 0)
                ReleaseRuntime(liveVisualCues[liveVisualCues.Count - 1], false);

            activeVisualCues.Clear();
            liveVisualCues.Clear();
            activeRecords.Clear();
        }

        /// <summary>解除 ASC 事件订阅，并清理 Controller 仍持有的状态。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            owner.CueRequested -= OnCueRequested;
            Clear();
        }

        #endregion

        #region 请求分发

        /// <summary>接收 ASC 请求，完成精确查表并分发到共享与本地 Handler。</summary>
        /// <param name="request">由 GE、GA 或命中检测发布的 Cue 请求。</param>
        private void OnCueRequested(GameplayCueRequest request)
        {
            if (disposed) return;
            if (!ReferenceEquals(request.Target, owner))
            {
                Debug.LogError("[GameplayCueCtrl] Cue 请求 Target 与所属 ASC 不匹配。", owner);
                return;
            }

            // Remove 直接使用 Active 时保存的 Handler 记录，不重新依赖当前数据库内容或注册表。
            if (request.EventType == GameplayCueEventType.Remove)
            {
                RemoveOriginCues(request);
                return;
            }

            GameplayCueManager manager = GameplayCueManager.Instance;
            if (!manager.TryGetCue(request.CueTag, out GameplayCueData data))
            {
                Debug.LogError($"[GameplayCueCtrl] 找不到 CueTag 对应的数据：{request.CueTag}。", owner);
                return;
            }

            List<IGameplayCueHandler> handlers = CollectHandlers(data, request.CueTag, manager);
            if (handlers.Count == 0) return;

            switch (request.EventType)
            {
                case GameplayCueEventType.Execute:
                    for (int index = 0; index < handlers.Count; index++)
                        handlers[index].Execute(data, request, this);
                    break;
                case GameplayCueEventType.Active:
                    for (int index = 0; index < handlers.Count; index++)
                        CreateActive(handlers[index], data, request);
                    break;
            }
        }

        /// <summary>按数据具体类型收集唯一共享 Handler，再附加关注同一 Tag 的 ASC Handler。</summary>
        /// <param name="data">已经精确匹配的 CueData。</param>
        /// <param name="cueTag">请求使用的精确 CueTag。</param>
        /// <param name="manager">已初始化的 Cue Manager。</param>
        /// <returns>共享优先、本地随后调用的 Handler 列表。</returns>
        private List<IGameplayCueHandler> CollectHandlers(GameplayCueData data,
            WS_Modules.GAS.TAG.GameplayTag cueTag, GameplayCueManager manager)
        {
            var handlers = new List<IGameplayCueHandler>();
            if (manager.TryGetHandler(data.GetType(), out GameplayCueHandlerSO sharedHandler))
                handlers.Add(sharedHandler);
            else
                Debug.LogError(
                    $"[GameplayCueCtrl] CueData 类型 '{data.GetType().Name}' 没有共享 Handler 注册。", data);

            IReadOnlyList<ASCGameplayCueHandler> localHandlers = owner.LocalCueHandlers;
            for (int index = 0; index < localHandlers.Count; index++)
            {
                ASCGameplayCueHandler localHandler = localHandlers[index];
                if (localHandler != null && localHandler.HandlesTag(cueTag))
                    handlers.Add(localHandler);
            }
            return handlers;
        }

        /// <summary>启动一个 Handler 的 Active 并记下其独立运行时句柄。</summary>
        /// <param name="handler">负责创建和最终释放状态的 Handler。</param>
        /// <param name="data">本次 Active 的 CueData。</param>
        /// <param name="request">原始 Cue 请求。</param>
        private void CreateActive(IGameplayCueHandler handler, GameplayCueData data,
            GameplayCueRequest request)
        {
            if (FindHandlerRecord(handler, request) != null) return;

            object state = handler.Active(data, request, this);
            if (state is GameplayCueRuntime runtime && runtime.IsReleased) return;

            activeRecords.Add(new GameplayCueActiveRecord(handler, data, request, state, this));
            Debug.Log(
                $"[GameplayCueCtrl] ASC '{owner.name}' 激活 Cue '{request.CueTag}'，Handler={handler.GetType().Name}。",
                owner);
        }

        /// <summary>移除请求来源对应的全部共享及本地 Active Handler 记录。</summary>
        /// <param name="request">包含 GE/GA 来源身份的 Remove 请求。</param>
        private void RemoveOriginCues(GameplayCueRequest request)
        {
            for (int index = activeRecords.Count - 1; index >= 0; index--)
            {
                GameplayCueActiveRecord record = activeRecords[index];
                if (record.Request.CueTag == request.CueTag && IsSameOrigin(record.Request, request))
                    ReleaseActiveRecord(record, true);
            }
        }

        /// <summary>寻找相同 Handler、来源 Runtime 与 Tag 的 Active 记录。</summary>
        /// <param name="handler">待查 Handler。</param>
        /// <param name="request">待查来源请求。</param>
        /// <returns>已经存在的 Active 记录，或空引用。</returns>
        private GameplayCueActiveRecord FindHandlerRecord(IGameplayCueHandler handler,
            GameplayCueRequest request)
        {
            for (int index = 0; index < activeRecords.Count; index++)
            {
                GameplayCueActiveRecord record = activeRecords[index];
                if (ReferenceEquals(record.Handler, handler) &&
                    record.Request.CueTag == request.CueTag && IsSameOrigin(record.Request, request))
                    return record;
            }
            return null;
        }

        /// <summary>在来源至少包含 GE 或 GA Runtime 身份时按对象引用匹配生命周期。</summary>
        /// <param name="left">已有 Active 请求。</param>
        /// <param name="right">新请求。</param>
        /// <returns>相同 GE 或 GA Runtime 产生的请求时返回 true。</returns>
        private static bool IsSameOrigin(GameplayCueRequest left, GameplayCueRequest right)
        {
            if (left.EffectRuntime != null && ReferenceEquals(left.EffectRuntime, right.EffectRuntime)) return true;
            if (left.AbilityRuntime != null && ReferenceEquals(left.AbilityRuntime, right.AbilityRuntime)) return true;
            return left.EffectRuntime == null && left.AbilityRuntime == null &&
                   right.EffectRuntime == null && right.AbilityRuntime == null;
        }

        /// <summary>释放一条 Active 记录并把 Remove 或 Clear 交还给原 Handler。</summary>
        /// <param name="record">需要释放的 Handler 记录。</param>
        /// <param name="invokeRemove">为 Remove 请求时调用 Remove；Clear 使用 Handler.Clear。</param>
        private void ReleaseActiveRecord(GameplayCueActiveRecord record, bool invokeRemove)
        {
            if (record == null || !record.TryBeginRelease()) return;
            activeRecords.Remove(record);
            if (invokeRemove) record.Handler.Remove(record);
            else record.Handler.Clear(record);
            Debug.Log(
                $"[GameplayCueCtrl] ASC '{owner.name}' {(invokeRemove ? "移除" : "清理")} Cue '{record.Request.CueTag}'，Handler={record.Handler.GetType().Name}。",
                owner);
        }

        #endregion

        #region Visual Handler 支持

        /// <summary>由共享 Visual Handler 获取对象池实例并建立兼容现有 Behaviour 的运行时句柄。</summary>
        /// <param name="data">视觉资源与摆放配置。</param>
        /// <param name="request">表现来源与空间请求。</param>
        /// <returns>取得并配置的 Visual Runtime；资源或 Behaviour 缺失时返回空。</returns>
        internal GameplayCueRuntime CreateVisualRuntime(VisualGameplayCueData data, GameplayCueRequest request)
        {
            Transform parent = request.AttachTransform;
            GameObject cueObject = null;
            if (!string.IsNullOrWhiteSpace(data.AddressableKey))
                cueObject = PoolManager.Instance.Get(data.AddressableKey, parent);
            if (cueObject == null && data.FallbackPrefab != null)
                cueObject = PoolManager.Instance.Get(data.FallbackPrefab, parent);
            if (cueObject == null)
            {
                Debug.LogError($"[GameplayCueCtrl] Visual Cue '{data.name}' 无法从对象池获取表现对象。", owner);
                return null;
            }

            GameplayCueBehaviour behaviour = cueObject.GetComponent<GameplayCueBehaviour>();
            if (behaviour == null)
            {
                Debug.LogError($"[GameplayCueCtrl] 表现对象 '{cueObject.name}' 缺少 GameplayCueBehaviour。", cueObject);
                PoolManager.Instance.Recycle(cueObject);
                return null;
            }

            ApplyPlacement(data, request, cueObject.transform);
            var runtime = new GameplayCueRuntime(this, data, request, cueObject, behaviour);
            liveVisualCues.Add(runtime);
            return runtime;
        }

        /// <summary>将新建的 Visual Runtime 计入 Active 查询列表。</summary>
        /// <param name="runtime">已成功创建的 Active 视觉句柄。</param>
        internal void RegisterActiveVisual(GameplayCueRuntime runtime) => activeVisualCues.Add(runtime);

        /// <summary>以数据库的 Source/Target/World 规则摆放视觉对象。</summary>
        /// <param name="data">视觉资产提供的默认空间配置。</param>
        /// <param name="request">本次请求的显式空间覆盖。</param>
        /// <param name="cueTransform">已从对象池取出的对象 Transform。</param>
        private void ApplyPlacement(VisualGameplayCueData data, GameplayCueRequest request, Transform cueTransform)
        {
            if (request.AttachTransform != null)
            {
                cueTransform.SetParent(request.AttachTransform, false);
                cueTransform.localPosition = data.LocalPosition;
                cueTransform.localRotation = data.LocalRotation;
                return;
            }

            if (request.HasExplicitPlacement)
            {
                cueTransform.SetParent(null, true);
                cueTransform.SetPositionAndRotation(
                    request.Position + data.LocalPosition,
                    request.Rotation * data.LocalRotation);
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

            cueTransform.SetParent(null, true);
            Vector3 position = anchor == null ? data.LocalPosition : anchor.TransformPoint(data.LocalPosition);
            Quaternion rotation = anchor == null ? data.LocalRotation : anchor.rotation * data.LocalRotation;
            cueTransform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>解析 Visual Cue 的 Source、Target 或 World 默认锚点。</summary>
        /// <param name="data">配置默认锚点与 Marker 的视觉数据。</param>
        /// <param name="request">提供 Source 与 Target ASC 的请求。</param>
        /// <returns>解析到的默认 Transform；World 或目标缺失时返回空。</returns>
        private static Transform ResolveDefaultAnchor(VisualGameplayCueData data, GameplayCueRequest request)
        {
            if (data.DefaultAnchor == GameplayCueAnchor.World) return null;

            GameplayAbilitySystemComponent anchorAsc = data.DefaultAnchor == GameplayCueAnchor.Source
                ? request.Source
                : request.Target;
            if (anchorAsc == null) return null;
            if (data.MarkerKey == null) return anchorAsc.Owner.RootTransform;

            IMarkerProvider provider = anchorAsc.Owner.MarkerProvider;
            if (provider != null && provider.TryGetMarker(data.MarkerKey, out Transform marker))
                return marker;

            Debug.LogWarning(
                $"[GameplayCueCtrl] Visual Cue '{data.name}' 无法解析 Marker '{data.MarkerKey.name}'，使用 Owner 根节点。",
                anchorAsc);
            return anchorAsc.Owner.RootTransform;
        }

        /// <summary>释放 Visual Runtime 并在需要时调用其对象池回调。</summary>
        /// <param name="runtime">当前 Controller 持有的 Visual Runtime。</param>
        /// <param name="invokeRemove">是否调用持续 Behaviour 的 OnRemove。</param>
        /// <returns>本次调用取得释放权并归还实例时返回 true。</returns>
        internal bool ReleaseRuntime(GameplayCueRuntime runtime, bool invokeRemove)
        {
            if (runtime == null || !runtime.TryBeginRelease()) return false;

            for (int index = activeRecords.Count - 1; index >= 0; index--)
                if (ReferenceEquals(activeRecords[index].State, runtime))
                {
                    activeRecords[index].TryBeginRelease();
                    activeRecords.RemoveAt(index);
                }

            activeVisualCues.Remove(runtime);
            liveVisualCues.Remove(runtime);
            if (invokeRemove && runtime.IsActive) runtime.Behaviour.InvokeRemove(runtime);
            runtime.Behaviour.InvokeCueRecycle(runtime);
            PoolManager.Instance.Recycle(runtime.CueObject);
            runtime.MarkReleased();
            Debug.Log($"[GameplayCueCtrl] ASC '{owner.name}' 已归还 Visual Cue '{runtime.CueTag}' 的对象池实例。", owner);
            return true;
        }

        /// <summary>响应运行时对象的主动 Release，并优先交还原 Active Handler 收尾。</summary>
        /// <param name="runtime">请求释放的 Visual Runtime。</param>
        /// <returns>对象已经释放或本次成功释放时返回 true。</returns>
        internal bool RequestRuntimeRelease(GameplayCueRuntime runtime)
        {
            if (runtime == null || !liveVisualCues.Contains(runtime)) return false;
            for (int index = activeRecords.Count - 1; index >= 0; index--)
            {
                GameplayCueActiveRecord record = activeRecords[index];
                if (!ReferenceEquals(record.State, runtime)) continue;
                ReleaseActiveRecord(record, true);
                return runtime.IsReleased;
            }

            return ReleaseRuntime(runtime, false);
        }

        #endregion
    }
}
