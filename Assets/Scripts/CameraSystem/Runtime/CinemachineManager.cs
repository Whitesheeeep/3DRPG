using System;
using System.Collections.Generic;
using Cinemachine;
using UnityEngine;
using WS_Modules.Singleton;

namespace RPG.CameraSystem
{
    /// <summary>
    /// 管理 Gameplay Brain 最终输出上的技能 FOV 与局部 Shake 修饰。
    /// </summary>
    public sealed class CinemachineManager : SingletonMonoBase<CinemachineManager>, ICameraModifierService
    {
        [SerializeField] private CinemachineBrain brain;

        private readonly Dictionary<int, CameraModifierRequest> requests = new();
        private readonly List<CameraModifierRequest> orderedRequests = new();
        private readonly CameraModifierMixer mixer = new();
        private int nextHandleId = 1;
        private long nextSequence;

        /// <summary>建立单例并订阅 Cinemachine 最终输出事件。</summary>
        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;
            if (brain == null) throw new InvalidOperationException("CinemachineManager 必须配置 CinemachineBrain。");
            CinemachineCore.CameraUpdatedEvent.AddListener(OnCameraUpdated);
        }

        /// <summary>注销 Cinemachine 回调，避免场景切换后保留失效 Manager。</summary>
        protected override void OnDestroy()
        {
            if (Instance == this) CinemachineCore.CameraUpdatedEvent.RemoveListener(OnCameraUpdated);
            base.OnDestroy();
        }

        /// <summary>创建层级固定的新请求。</summary>
        public CameraModifierHandle CreateModifier(string debugName)
        {
            int id = nextHandleId++;
            CameraModifierRequest request = new(++nextSequence, debugName ?? string.Empty);
            requests.Add(id, request);
            orderedRequests.Add(request);
            return new CameraModifierHandle(id);
        }

        /// <summary>更新请求状态；普通更新不会改变请求创建层级。</summary>
        public void UpdateModifier(CameraModifierHandle handle, CameraModifierState state)
        {
            CameraModifierRequest request = GetRequest(handle);
            request.State = state;
            request.Active = state.AffectedChannels != CameraModifierChannel.None;
        }

        /// <summary>停用请求但保留其创建层级。</summary>
        public void DeactivateModifier(CameraModifierHandle handle) => GetRequest(handle).Active = false;

        /// <summary>永久释放请求并禁止句柄再次使用。</summary>
        public void ReleaseModifier(CameraModifierHandle handle)
        {
            CameraModifierRequest request = GetRequest(handle);
            requests.Remove(handle.Id);
            orderedRequests.Remove(request);
        }

        /// <summary>取得有效请求；失效 Handle 立即暴露调用契约错误。</summary>
        private CameraModifierRequest GetRequest(CameraModifierHandle handle)
        {
            if (!requests.TryGetValue(handle.Id, out CameraModifierRequest request))
                throw new InvalidOperationException($"CameraModifierHandle {handle.Id} 已失效或不属于当前 Manager。");
            return request;
        }

        /// <summary>在 Brain 写入输出后，以原始 CameraState 为基准应用一次非累积修饰。</summary>
        private void OnCameraUpdated(CinemachineBrain updatedBrain)
        {
            if (updatedBrain != brain || brain.OutputCamera == null) return;
            CameraModifierState state = mixer.Mix(orderedRequests);
            CameraState baseState = brain.CurrentCameraState;
            Transform output = brain.OutputCamera.transform;

            // 每帧从 Brain 原始状态重建最终姿态，避免上帧 Shake 被再次累加。
            output.SetPositionAndRotation(
                baseState.FinalPosition + baseState.FinalOrientation * state.LocalPositionOffset,
                baseState.FinalOrientation * Quaternion.Euler(state.LocalRotationOffset));
            if ((state.AffectedChannels & CameraModifierChannel.Lens) == 0) return;
            if (brain.OutputCamera.orthographic)
                brain.OutputCamera.orthographicSize = baseState.Lens.OrthographicSize * state.FovScale;
            else
                brain.OutputCamera.fieldOfView = baseState.Lens.FieldOfView * state.FovScale;
        }
    }
}
