using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.LogModule;
using WS_Modules.Pooling;
using WS_Modules.ResLoadModule;
using WS_Modules.Singleton;
using WS_Modules.AudioSystem;
using WS_Modules.ConfigInstaller;
using WS_Modules.UIModule;

namespace WS_Modules
{
    /// <summary>
    /// 框架根类，用于初始化框架的核心组件和设置，例如日志系统、资源管理器、事件中心等
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)] // 确保这个组件在其他组件之前执行
    public class WSFrameRoot : SingletonMonoBase<WSFrameRoot>
    {
        [SerializeField]
        private WSFrameSetting frameSetting;
        public WSFrameSetting FrameSetting => frameSetting;

        #region 音频系统管理
        [BoxGroup("AudioSystem"), LabelText("全局音量"), PropertyRange(0, 1)]
        [ShowInInspector]
        public float GlobalVolume
        {
            get => AudioManager.Instance.GlobalVolume;
            set => AudioManager.Instance.GlobalVolume = value;
        }

        [BoxGroup("AudioSystem"), LabelText("背景音量"), PropertyRange(0, 1)]
        [ShowInInspector]
        public float BGVolume
        {
            get => AudioManager.Instance.BGVolume;
            set => AudioManager.Instance.BGVolume = value;
        }

        [BoxGroup("AudioSystem"), LabelText("特效音量"), PropertyRange(0, 1)]
        [ShowInInspector]
        public float EffectVolume
        {
            get => AudioManager.Instance.EffectVolume;
            set => AudioManager.Instance.EffectVolume = value;
        }

        [BoxGroup("AudioSystem"), LabelText("是否静音")]
        [ShowInInspector]
        public bool IsMute
        {
            get => AudioManager.Instance.IsMute;
            set => AudioManager.Instance.IsMute = value;
        }

        [BoxGroup("AudioSystem"), LabelText("是否循环(仅背景音乐)")]
        [ShowInInspector]
        public bool IsLoop
        {
            get => AudioManager.Instance.IsLoop;
            set => AudioManager.Instance.IsLoop = value;
        }

        [BoxGroup("AudioSystem"), LabelText("是否暂停")]
        [ShowInInspector]
        public bool IsPause
        {
            get => AudioManager.Instance.IsPause;
            set => AudioManager.Instance.IsPause = value;
        }
        #endregion

        private IResLoad<string> _resLoader;
        private bool applicationQuitting;

        /// <summary>
        /// 注册 WSFrame 单例并初始化框架核心系统。
        /// </summary>
        protected override void Awake()
        {
            base.Awake();

            InitWSFrameRoot();
        }

        /// <summary>
        /// 按框架依赖顺序初始化日志、配置、资源、对象池、音频和 UI 系统。
        /// </summary>
        private void InitWSFrameRoot()
        {
            GetResLoader();

            WSLog.Init(frameSetting.logSetting);
            ConfigRegisterSystem.Instance.Initialize(frameSetting.configRegisterSetting);
            ResSystem.Instance.Initialize(_resLoader);
            PoolManager.Instance.Initialize(frameSetting.PoolingSettings, _resLoader, transform);
            AudioManager.Instance.Initialize(frameSetting.audioSystemSetting, this.transform, _resLoader);
            UIManager.Instance.Initialize(frameSetting.uiManagerSetting);
        }

        /// <summary>
        /// 根据 FrameSetting 选择并创建资源加载实现。
        /// </summary>
        private void GetResLoader()
        {
            switch (frameSetting.resLoadType)
            {
                case E_ResLoadType.Resources:
                    _resLoader = new ResourcesLoadMgrModule();
                    break;
                case E_ResLoadType.Addressable:
                    _resLoader = new AddressablesLoadMgrModule();
                    break;
            }
        }

        /// <summary>
        /// 在 Unity 开始退出前先释放 UI 窗口逻辑，确保纯 C# Window 收到 OnDestroy。
        /// </summary>
        protected virtual void OnApplicationQuit()
        {
            if (Instance != this)
            {
                return;
            }

            applicationQuitting = true;
            UIManager.Instance.Shutdown();
        }

        /// <summary>
        /// 在根节点被非正常销毁时兜底关闭 UI，并最后清理框架单例引用。
        /// </summary>
        protected override void OnDestroy()
        {
            bool isCurrentInstance = Instance == this;
            if (isCurrentInstance && !applicationQuitting)
            {
                UIManager.Instance.Shutdown();
            }

            base.OnDestroy();
        }
    }
}






