using System;
using Sirenix.OdinInspector;

namespace WS_Modules.ConfigInstaller
{
    [Serializable]
    public sealed class ConfigRegisterSetting
    {
        [LabelText("配置注册根节点")]
        public ConfigRegisterNodeBase rootNode;

        [LabelText("注册完成后清除运行时引用")]
        [InfoBox("只会清除 ConfigRegisterSystem 内部的临时引用，不会删除 FrameSetting 上的配置引用，也不会删除 ScriptableObject 资产。")]
        public bool clearRootNodeAfterRegister = true;
    }
}
