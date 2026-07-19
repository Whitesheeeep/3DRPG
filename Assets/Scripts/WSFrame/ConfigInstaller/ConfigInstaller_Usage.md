# Config Register Usage

本文用 TimeWheel 作为示例，说明如何通过 `ConfigRegisterSystem + ConfigRegisterSetting` 在启动时注册模块配置。

## Setup

1. 创建或使用现有 `FrameworkConfigRootNode.asset`。
2. 创建具体模块 Provider，例如 `TimeWheelManagerConfigProvider`。
3. 将 Provider 添加到根节点或组合节点的 `children` 中。
4. 在 `FrameSetting.configRegisterSetting.rootNode` 指向该根节点。
5. 场景中保留 `WSFrameRoot`，启动时 Root 会调用 `ConfigRegisterSystem`。

## Startup Flow

```text
WSFrameRoot.Awake()
  -> ConfigRegisterSystem.Instance.Initialize(frameSetting.configRegisterSetting)
  -> rootNode.Register()
  -> AutoSingletonConfigRegistryModule.Register()
  -> TimeWheelManagerConfigProvider.Register()
```

如果 `clearRootNodeAfterRegister` 为 true，注册完成后 `ConfigRegisterSystem` 会清理内部 root node 引用。`FrameSetting` 上的配置引用和 ScriptableObject 资产不会被删除。

## Provider Example

```csharp
using UnityEngine;
using WS_Modules.ConfigInstaller;

public sealed class MyManagerConfigProvider : ConfigRegisterNodeBase
{
    [SerializeField] private MyManagerConfig config;

    public override void Register()
    {
        MyRegistry.Register(config.CreateRuntimeCopy());
    }
}
```

## Manual Register

编辑器面板中的 `Register All` 会执行当前 `FrameSetting.configRegisterSetting`：

```csharp
ConfigRegisterSystem.Instance.Register(wsFrameRoot.FrameSetting.configRegisterSetting);
```

这适合手动验证配置树，不需要场景中存在额外 Installer 对象。
