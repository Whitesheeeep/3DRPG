using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.AudioSystem;
using WS_Modules.ConfigInstaller;
using WS_Modules.LogModule;
using WS_Modules.Pooling;
using WS_Modules.UIModule;

namespace WS_Modules
{
    [CreateAssetMenu(fileName = "FrameSetting", menuName = "WSFrame/FrameSetting", order = 0)]
    public partial class WSFrameSetting : ScriptableObject
    {
        [LabelText("Log 控制")]
        public LogSettings logSetting = new LogSettings();

        [LabelText("配置注册设置")]
        public ConfigRegisterSetting configRegisterSetting = new ConfigRegisterSetting();

        [LabelText("资源加载方式"), EnumToggleButtons]
        [InfoBox("请处理 Resources 文件夹", InfoMessageType.Warning, "@resLoadType == E_ResLoadType.Addressable")]
        public E_ResLoadType resLoadType = E_ResLoadType.Resources;

        [LabelText("音量系统设置")]
        public AudioSystemSetting audioSystemSetting = new AudioSystemSetting();

        [LabelText("UI 管理设置")]
        public UIManagerSetting uiManagerSetting = new UIManagerSetting();

        [SerializeField, LabelText("对象池设置")]
        private PoolingSetting poolingSetting = new PoolingSetting();

        public PoolingSetting PoolingSettings
        {
            get
            {
                poolingSetting ??= new PoolingSetting();
                poolingSetting.SetResLoadType(resLoadType);
                return poolingSetting;
            }
        }
    }
}


