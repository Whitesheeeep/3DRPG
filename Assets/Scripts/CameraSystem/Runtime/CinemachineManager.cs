using System;
using System.Collections.Generic;
using Cinemachine;
using UnityEngine;
using WS_Modules.Singleton;

namespace RPG.CameraSystem
{
    /// <summary>
    /// 管理 Gameplay Brain 最终输出上的 FOV、持续 Noise 与瞬时 Impulse。
    /// </summary>
    public sealed class CinemachineManager : SingletonMonoBase<CinemachineManager>,
        ICameraModifierService, ICameraShakeService, ICameraImpulseService
    {
        #region 配置与状态

        /// <summary>
        /// 负责 Gameplay 最终镜头输出的唯一 Cinemachine Brain。
        /// </summary>
        [SerializeField, Tooltip("负责 Gameplay 最终镜头输出的唯一 Cinemachine Brain。")]
        private CinemachineBrain brain;

        // 普通 Modifier 的字典负责定位、列表负责顺序；创建与释放时必须同步维护两者。
        /// <summary>
        /// 按 Handle ID 定位普通 Camera Modifier 请求的索引。
        /// </summary>
        private readonly Dictionary<int, CameraModifierRequest> requests = new();

        /// <summary>
        /// 按创建顺序保存普通 Camera Modifier 请求，供 Additive 与 Exclusive 混合使用。
        /// </summary>
        private readonly List<CameraModifierRequest> orderedRequests = new();

        // 持续 Shake 同样使用索引与有序列表双结构，不能只从其中一处移除请求。
        /// <summary>
        /// 按 Handle ID 定位持续 Noise Shake 请求的索引。
        /// </summary>
        private readonly Dictionary<int, CameraShakeRuntime> shakeRequests = new();

        /// <summary>
        /// 按创建顺序保存持续 Noise Shake 请求，并与普通 Modifier 共享竞争层级。
        /// </summary>
        private readonly List<CameraShakeRuntime> orderedShakeRequests = new();

        /// <summary>
        /// 按独立 Handle ID 定位由当前 Manager 发射且仍处于有效期内的 Impulse 事件。
        /// </summary>
        private readonly Dictionary<int, CameraImpulseRuntime> impulseRequests = new();

        /// <summary>
        /// 复用的过期 Shake ID 缓存，用于避免遍历字典期间直接删除元素。
        /// </summary>
        private readonly List<int> expiredShakeIds = new();

        /// <summary>
        /// 复用的过期 Impulse ID 缓存，用于避免遍历字典期间直接删除元素。
        /// </summary>
        private readonly List<int> expiredImpulseIds = new();

        /// <summary>
        /// 合并普通 Modifier 与持续 Noise Shake；Impulse 由 Cinemachine 自身负责混合。
        /// </summary>
        private readonly CameraModifierMixer mixer = new();

        /// <summary>
        /// 普通 Modifier 与持续 Noise Shake 共用的下一个 Handle ID。
        /// </summary>
        private int nextHandleId = 1;

        /// <summary>
        /// Impulse 请求独立使用的下一个 Handle ID。
        /// </summary>
        private int nextImpulseHandleId = 1;

        /// <summary>
        /// 普通 Modifier 与持续 Noise Shake 共用的创建序号，用于确定 Exclusive 竞争层级。
        /// </summary>
        private long nextSequence;

        #endregion

        #region Unity 生命周期

        /// <summary>
        /// 建立单例并订阅 Cinemachine 最终输出事件。
        /// </summary>
        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;
            if (brain == null) throw new InvalidOperationException("CinemachineManager 必须配置 CinemachineBrain。");
            CinemachineCore.CameraUpdatedEvent.AddListener(OnCameraUpdated);
        }

        /// <summary>
        /// 注销 Cinemachine 回调并释放当前 Manager 持有的全部请求引用。
        /// </summary>
        protected override void OnDestroy()
        {
            if (Instance == this)
            {
                CinemachineCore.CameraUpdatedEvent.RemoveListener(OnCameraUpdated);
                requests.Clear();
                orderedRequests.Clear();
                shakeRequests.Clear();
                orderedShakeRequests.Clear();
                CancelAllImpulses();
            }
            base.OnDestroy();
        }

        #endregion

        #region Modifier 请求

        /// <summary>
        /// 创建层级固定的新 Modifier 请求。
        /// </summary>
        /// <param name="debugName">用于诊断的请求名称。</param>
        /// <returns>新请求句柄。</returns>
        public CameraModifierHandle CreateModifier(string debugName)
        {
            int id = nextHandleId++;
            CameraModifierRequest request = new(++nextSequence, debugName ?? string.Empty);
            requests.Add(id, request);
            orderedRequests.Add(request);
            return new CameraModifierHandle(id);
        }

        /// <summary>
        /// 更新请求状态；普通更新不会改变请求创建层级。
        /// </summary>
        /// <param name="handle">目标请求句柄。</param>
        /// <param name="state">已经由调用方求值完成的状态。</param>
        public void UpdateModifier(CameraModifierHandle handle, CameraModifierState state)
        {
            CameraModifierRequest request = GetRequest(handle);
            request.State = state;
            request.Active = state.AffectedChannels != CameraModifierChannel.None;
        }

        /// <summary>
        /// 停用请求但保留其创建层级。
        /// </summary>
        /// <param name="handle">目标请求句柄。</param>
        public void DeactivateModifier(CameraModifierHandle handle) => GetRequest(handle).Active = false;

        /// <summary>
        /// 永久释放请求并禁止句柄再次使用。
        /// </summary>
        /// <param name="handle">目标请求句柄。</param>
        public void ReleaseModifier(CameraModifierHandle handle)
        {
            CameraModifierRequest request = GetRequest(handle);
            requests.Remove(handle.Id);
            orderedRequests.Remove(request);
        }

        /// <summary>
        /// 取得有效请求；失效 Handle 立即暴露调用契约错误。
        /// </summary>
        /// <param name="handle">待解析句柄。</param>
        /// <returns>Manager 内部请求记录。</returns>
        /// <exception cref="InvalidOperationException">句柄无效或已释放。</exception>
        private CameraModifierRequest GetRequest(CameraModifierHandle handle)
        {
            if (!requests.TryGetValue(handle.Id, out CameraModifierRequest request))
                throw new InvalidOperationException($"CameraModifierHandle {handle.Id} 已失效或不属于当前 Manager。");
            return request;
        }

        #endregion

        #region 持续 Shake

        /// <summary>
        /// 使用 Profile 创建一个以缩放游戏时间驱动的持续 Noise 请求。
        /// </summary>
        /// <param name="profile">Shake 预设。</param>
        /// <param name="strength">本次播放强度倍率。</param>
        /// <param name="seed">稳定噪声相位种子。</param>
        /// <returns>用于调整和停止请求的句柄。</returns>
        /// <exception cref="ArgumentNullException">Profile 为空。</exception>
        /// <exception cref="InvalidOperationException">Profile 未配置 NoiseSettings。</exception>
        public CameraShakeHandle PlayShake(CameraShakeProfile profile,
            float strength = 1f, int seed = 0)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (profile.NoiseSettings == null)
                throw new InvalidOperationException($"Shake Profile {profile.name} 未配置 NoiseSettings。");

            int id = nextHandleId++;
            CameraShakeRuntime runtime = new(profile, ++nextSequence,
                Time.timeAsDouble, strength, seed);
            shakeRequests.Add(id, runtime);
            orderedShakeRequests.Add(runtime);
            return new CameraShakeHandle(id);
        }

        /// <summary>
        /// 尝试修改尚未结束的 Shake 请求强度。
        /// </summary>
        /// <param name="handle">目标请求句柄。</param>
        /// <param name="strength">新的非负倍率。</param>
        /// <returns>请求仍有效时返回 true。</returns>
        public bool TrySetShakeStrength(CameraShakeHandle handle, float strength)
        {
            PruneExpiredRequests(Time.timeAsDouble);
            if (!shakeRequests.TryGetValue(handle.Id, out CameraShakeRuntime runtime)) return false;
            runtime.SetStrength(strength);
            return true;
        }

        /// <summary>
        /// 尝试停止 Shake；普通停止进入预设淡出，立即停止则移除请求。
        /// </summary>
        /// <param name="handle">目标请求句柄。</param>
        /// <param name="immediate">是否跳过淡出。</param>
        /// <returns>请求仍有效时返回 true。</returns>
        public bool TryStopShake(CameraShakeHandle handle, bool immediate = false)
        {
            double time = Time.timeAsDouble;
            PruneExpiredRequests(time);
            if (!shakeRequests.TryGetValue(handle.Id, out CameraShakeRuntime runtime)) return false;
            runtime.Stop(time, immediate);
            if (immediate) RemoveShake(handle.Id, runtime);
            return true;
        }

        #endregion

        #region Impulse

        /// <summary>
        /// 使用 Profile 默认方向发射一次 Uniform Impulse。
        /// </summary>
        /// <param name="profile">Impulse 预设。</param>
        /// <param name="amplitude">本次强度倍率。</param>
        /// <returns>用于独立取消本次事件的句柄。</returns>
        public CameraImpulseHandle EmitImpulse(CameraImpulseProfile profile, float amplitude = 1f) =>
            EmitImpulse(profile, profile != null ? profile.DefaultDirection : Vector3.down, amplitude);

        /// <summary>
        /// 使用调用方方向发射一次 Uniform Impulse。
        /// </summary>
        /// <param name="profile">Impulse 预设。</param>
        /// <param name="direction">冲击方向。</param>
        /// <param name="amplitude">本次强度倍率。</param>
        /// <returns>用于独立取消本次事件的句柄。</returns>
        /// <exception cref="ArgumentNullException">Profile 为空。</exception>
        /// <exception cref="ArgumentException">方向为零向量。</exception>
        /// <exception cref="InvalidOperationException">Cinemachine 拒绝创建事件。</exception>
        public CameraImpulseHandle EmitImpulse(CameraImpulseProfile profile,
            Vector3 direction, float amplitude = 1f)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (direction.sqrMagnitude < 0.000001f)
                throw new ArgumentException("Impulse 方向不能为零向量。", nameof(direction));

            CinemachineImpulseDefinition definition = profile.CreateDefinition();
            Vector3 velocity = direction.normalized * profile.DefaultAmplitude * Mathf.Max(0f, amplitude);
            CinemachineImpulseManager.ImpulseEvent impulseEvent =
                definition.CreateAndReturnEvent(Vector3.zero, velocity);
            if (impulseEvent == null)
                throw new InvalidOperationException($"Impulse Profile {profile.name} 无法创建有效事件。");

            int id = nextImpulseHandleId++;
            impulseRequests.Add(id, new CameraImpulseRuntime(
                impulseEvent, Time.timeAsDouble + profile.Duration));
            return new CameraImpulseHandle(id);
        }

        /// <summary>
        /// 尝试取消当前 Manager 发射且尚未到期的单次 Impulse。
        /// </summary>
        /// <param name="handle">目标事件句柄。</param>
        /// <param name="immediate">是否立即截断事件。</param>
        /// <returns>事件仍有效且成功取消时返回 true。</returns>
        public bool TryCancelImpulse(CameraImpulseHandle handle, bool immediate = true)
        {
            PruneExpiredRequests(Time.timeAsDouble);
            if (!impulseRequests.TryGetValue(handle.Id, out CameraImpulseRuntime runtime)) return false;

            // Cinemachine 使用自己的当前时间计算事件包络，不能混入 Time.timeAsDouble。
            runtime.Event.Cancel(CinemachineCore.CurrentTime, immediate);
            impulseRequests.Remove(handle.Id);
            return true;
        }

        #endregion

        #region Cinemachine 输出

        /// <summary>
        /// 在 Brain 写入输出后，以原始 CameraState 为基准应用一次非累积修饰。
        /// </summary>
        /// <param name="updatedBrain">本次完成更新的 Brain。</param>
        private void OnCameraUpdated(CinemachineBrain updatedBrain)
        {
            if (updatedBrain != brain || brain.OutputCamera == null) return;
            double time = Time.timeAsDouble;
            PruneExpiredRequests(time);
            CameraModifierState state = mixer.Mix(orderedRequests, orderedShakeRequests, time);
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

        #endregion

        #region 请求回收

        /// <summary>
        /// 清理已经完成淡出或超出 Impulse 安全持有期的请求。
        /// </summary>
        /// <param name="time">当前缩放游戏时间。</param>
        private void PruneExpiredRequests(double time)
        {
            expiredShakeIds.Clear();
            foreach (KeyValuePair<int, CameraShakeRuntime> pair in shakeRequests)
            {
                if (pair.Value.IsExpired(time)) expiredShakeIds.Add(pair.Key);
            }
            foreach (int id in expiredShakeIds)
                RemoveShake(id, shakeRequests[id]);

            // ImpulseEvent 会被 Cinemachine 内部对象池复用，超期后必须立即放弃引用。
            expiredImpulseIds.Clear();
            foreach (KeyValuePair<int, CameraImpulseRuntime> pair in impulseRequests)
            {
                if (time >= pair.Value.ExpireTime) expiredImpulseIds.Add(pair.Key);
            }
            foreach (int id in expiredImpulseIds)
                impulseRequests.Remove(id);
        }

        /// <summary>
        /// 同时从句柄索引和有序混合列表移除一条 Shake 请求。
        /// </summary>
        /// <param name="id">Shake 句柄编号。</param>
        /// <param name="runtime">对应运行时请求。</param>
        private void RemoveShake(int id, CameraShakeRuntime runtime)
        {
            shakeRequests.Remove(id);
            orderedShakeRequests.Remove(runtime);
        }

        /// <summary>
        /// 取消当前 Manager 发射且仍被 Cinemachine 持有的全部 Impulse 事件。
        /// </summary>
        private void CancelAllImpulses()
        {
            // 只取消本 Manager 保存的事件，不能清空其他系统写入的 Cinemachine 全局事件队列。
            foreach (CameraImpulseRuntime runtime in impulseRequests.Values)
                runtime.Event.Cancel(CinemachineCore.CurrentTime, true);
            impulseRequests.Clear();
        }

        #endregion
    }
}
