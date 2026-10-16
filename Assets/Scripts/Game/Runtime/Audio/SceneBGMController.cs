using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.SceneManagement;
using WS_Modules;
using WS_Modules.AudioSystem;
using WS_Modules.LogModule;
using WS_Modules.ResLoadModule;

namespace RPG.Game.Audio
{
    /// <summary>
    /// 在所属场景成为 Active Scene 时播放背景音乐，离场时仅停止自身仍持有的播放。
    /// 场景按 Addressable Key 加载音乐，音源、Mixer 和淡入继续由 WSFrame 管理。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneBGMController : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField, Required, LabelText("场景背景音乐 Key"), WSAddressableKey("BGM")]
        private string bgmKey;

        #endregion

        #region 配置与运行状态

        // 配置
        [SerializeField, LabelText("循环播放")]
        private bool loop = true;

        [SerializeField, MinValue(0f), LabelText("淡入时长（秒）")]
        private float fadeInDuration = 1f;

        // key：场景句柄；value：该场景已启用的唯一 BGM 组件。
        private static readonly Dictionary<int, SceneBGMController> controllerBySceneHandleMap = new();
        private static SceneBGMController playbackOwner;
        private bool hasStarted;
        private bool isRegistered;
        private int registeredSceneHandle;
        private PlaybackRequest currentRequest;

        #endregion

        #region 生命周期

        /// <summary>每次进入 Play Mode 清空静态记录，兼容禁用 Domain Reload。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            controllerBySceneHandleMap.Clear();
            playbackOwner = null;
        }

        /// <summary>注册唯一场景入口和场景切换事件；首次播放留给 Start。</summary>
        /// <exception cref="InvalidOperationException">同一场景启用了多个 BGM 组件。</exception>
        private void OnEnable()
        {
            registeredSceneHandle = gameObject.scene.handle;
            if (controllerBySceneHandleMap.TryGetValue(registeredSceneHandle, out SceneBGMController existing) &&
                existing != this)
            {
                string message = $"[SceneBGM] 场景 {gameObject.scene.name} 重复配置 BGM：{existing.name}、{name}。";
                // Unity 不会因 OnEnable 抛异常而自动停用组件，必须阻止重复入口随后执行 Start。
                enabled = false;
                Debug.LogError(message, this);
                throw new InvalidOperationException(message);
            }

            controllerBySceneHandleMap.Add(registeredSceneHandle, this);
            isRegistered = true;
            SceneManager.activeSceneChanged += HandleActiveSceneChanged;
            WSLog.Log($"[SceneBGM] 注册场景音乐入口，scene={gameObject.scene.name}。");
            if (hasStarted)
                RefreshPlayback();
        }

        /// <summary>等待框架 Awake 和场景 Bootstrap Start 完成后首次播放。</summary>
        private void Start()
        {
            hasStarted = true;
            RefreshPlayback();
        }

        /// <summary>停用时注销事件和场景入口，释放仍属于自身的音乐。</summary>
        private void OnDisable()
        {
            if (!isRegistered)
                return;

            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
            controllerBySceneHandleMap.Remove(registeredSceneHandle);
            isRegistered = false;
            StopOwnedPlayback();
            WSLog.Log($"[SceneBGM] 注销场景音乐入口，scene={gameObject.scene.name}。");
        }

        /// <summary>销毁时幂等释放播放权；已启用组件的事件由 OnDisable 注销。</summary>
        private void OnDestroy()
        {
            StopOwnedPlayback();
        }

        #endregion

        #region 场景事件与播放协调

        /// <summary>Active Scene 改变时重新判断归属，附加加载本身不会抢占音乐。</summary>
        /// <param name="previousScene">切换前的场景。</param>
        /// <param name="nextScene">切换后的场景。</param>
        private void HandleActiveSceneChanged(Scene previousScene, Scene nextScene)
        {
            if (hasStarted)
                RefreshPlayback();
        }

        /// <summary>仅当前场景首次取得播放权时加载音乐；同次激活失败后不自动重试。</summary>
        /// <exception cref="InvalidOperationException">缺少音乐配置或 WSFrame 尚未初始化。</exception>
        private void RefreshPlayback()
        {
            if (gameObject.scene != SceneManager.GetActiveScene())
            {
                StopOwnedPlayback();
                return;
            }

            if (currentRequest != null)
                return;

            if (string.IsNullOrWhiteSpace(bgmKey) || WSFrameRoot.Instance == null)
            {
                string message = $"[SceneBGM] 无法播放场景 {gameObject.scene.name} 的音乐：" +
                    (string.IsNullOrWhiteSpace(bgmKey) ? "未配置背景音乐 Key。" : "WSFrameRoot 尚未初始化，请配置场景 Bootstrap。");
                Debug.LogError(message, this);
                throw new InvalidOperationException(message);
            }
            if (float.IsNaN(fadeInDuration) || float.IsInfinity(fadeInDuration) || fadeInDuration < 0f)
                throw new InvalidOperationException($"[SceneBGM] 场景 {gameObject.scene.name} 的淡入时间必须是非负有限值。");

            // 取得加载时的具体资源模块，后续即使门面重新初始化也向原模块归还自己的引用。
            IResLoad<string> resourceLoader = ResSystem.Instance.ResLoadModule;
            if (resourceLoader == null)
                throw new InvalidOperationException($"[SceneBGM] 场景 {gameObject.scene.name} 的资源加载模块尚未初始化。");

            // 加载前即取得所有权；旧加载结果只认各自请求身份，不能抢占后续场景。
            if (playbackOwner != null)
                playbackOwner.StopOwnedPlayback();
            currentRequest = new PlaybackRequest(resourceLoader, bgmKey, loop, fadeInDuration, gameObject.scene.name);
            playbackOwner = this;
            LoadAndPlayAsync(currentRequest).Forget();
        }

        /// <summary>异步加载冻结配置，仅仍有效的请求可播放；所有失效或失败路径对称释放资源。</summary>
        /// <param name="request">本次激活的唯一请求，包含资源加载器和配置快照。</param>
        /// <returns>加载与播放提交任务，不等待音乐自然结束。</returns>
        /// <exception cref="InvalidOperationException">资源 Key 未加载出有效 AudioClip。</exception>
        private async UniTask LoadAndPlayAsync(PlaybackRequest request)
        {
            WSLog.Log($"[SceneBGM] 开始加载，scene={request.SceneName}，key={request.Key}。");
            try
            {
                AudioClip clip = await request.ResourceLoader.LoadAsync<AudioClip>(request.Key);
                request.LoadCompleted = true;
                if (clip == null)
                    throw new InvalidOperationException($"[SceneBGM] 场景 {request.SceneName} 的音乐加载失败，key={request.Key}。");

                // 不提前取消底层共享加载；失效请求在完成后释放自己的那份引用，不访问已销毁的 Unity 对象。
                if (this == null || !ReferenceEquals(currentRequest, request) ||
                    !ReferenceEquals(playbackOwner, this) || !isActiveAndEnabled ||
                    gameObject.scene != SceneManager.GetActiveScene())
                {
                    WSLog.Log($"[SceneBGM] 加载结果已失效，不播放，scene={request.SceneName}，key={request.Key}。");
                    return;
                }

                AudioManager.Instance.PlayBGM(clip, volume: -1f, loop: request.Loop, fadeInDuration: request.FadeInDuration);
                request.IsPlaying = true;
                WSLog.Log($"[SceneBGM] 加载完成并提交播放，scene={request.SceneName}，key={request.Key}，fadeIn={request.FadeInDuration:0.###}。");
            }
            finally
            {
                // 加载异常也完成本次等待；未进入播放的引用立即释放，异常继续由 UniTask 上报。
                request.LoadCompleted = true;
                if (!request.IsPlaying)
                    request.ReleaseResource();
            }
        }

        /// <summary>仅播放所有者停止全局 BGM，清理可安全重复调用。</summary>
        private void StopOwnedPlayback()
        {
            PlaybackRequest request = currentRequest;
            currentRequest = null;
            if (request == null)
                return;

            // 音源清除 Clip 后才能释放加载引用，旧场景无权停止新曲。
            if (ReferenceEquals(playbackOwner, this))
            {
                playbackOwner = null;
                AudioManager.Instance.StopBGM();
            }
            request.IsPlaying = false;
            if (request.LoadCompleted)
                request.ReleaseResource();
            WSLog.Log($"[SceneBGM] 结束播放请求，scene={request.SceneName}，key={request.Key}，waitingForLoad={!request.LoadCompleted}。");
        }

        #endregion

        #region 加载请求快照

        /// <summary>保存一次激活的配置和引用所有权，避免异步加载与后续场景切换串用资源。</summary>
        private sealed class PlaybackRequest
        {
            #region 依赖字段

            public readonly IResLoad<string> ResourceLoader;

            #endregion

            #region 配置与引用状态

            // 配置快照
            public readonly string Key;
            public readonly bool Loop;
            public readonly float FadeInDuration;
            public readonly string SceneName;

            // 引用状态
            public bool LoadCompleted;
            public bool IsPlaying;
            private bool resourceReleased;

            #endregion

            /// <summary>冻结当前激活的配置；每个请求仅拥有一次加载获得的资源引用。</summary>
            /// <param name="resourceLoader">本次加载及释放使用的同一个模块。</param>
            /// <param name="key">音乐资源 Key。</param>
            /// <param name="loop">是否循环。</param>
            /// <param name="fadeInDuration">淡入秒数。</param>
            /// <param name="sceneName">用于异步完成后记录日志的场景名称。</param>
            public PlaybackRequest(IResLoad<string> resourceLoader, string key, bool loop,
                float fadeInDuration, string sceneName)
            {
                ResourceLoader = resourceLoader;
                Key = key;
                Loop = loop;
                FadeInDuration = fadeInDuration;
                SceneName = sceneName;
            }

            /// <summary>加载完成后幂等释放本次引用，不提前中断仍被其他请求等待的共享加载。</summary>
            public void ReleaseResource()
            {
                if (resourceReleased)
                    return;
                resourceReleased = true;
                ResourceLoader.UnLoad<AudioClip>(Key);
                WSLog.Log($"[SceneBGM] 释放音乐资源引用，scene={SceneName}，key={Key}。");
            }
        }

        #endregion
    }
}
