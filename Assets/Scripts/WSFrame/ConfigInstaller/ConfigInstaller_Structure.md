# Config Register

`ConfigRegister` 是 WSFrame 的启动配置注册模块。它不再使用独立的 Mono Installer，而是通过 `WSFrameRoot` 统一传入 `WSFrameSetting.configRegisterSetting`，再由 `ConfigRegisterSystem` 执行注册。

## Runtime Flow

```text
WSFrameRoot.Awake()
  -> WSLog.Init(...)
  -> ConfigRegisterSystem.Instance.Initialize(frameSetting.configRegisterSetting)
  -> ConfigRegisterSetting.rootNode.Register()
  -> 各模块 ConfigProvider.Register()
```

`ConfigRegisterSystem` 是模块外观入口，允许后续再次传入新的 `ConfigRegisterSetting` 执行注册。注册完成后，如果 `clearRootNodeAfterRegister` 为 true，只会清除 System 内部对 root node 的临时引用，不会删除 `FrameSetting` 上的配置引用，也不会删除 ScriptableObject 资产。

## Core Types

- `ConfigRegisterSetting`：模块 Setting，保存 `rootNode` 和注册后是否清除运行时引用。
- `ConfigRegisterSystem`：模块外观，负责执行注册、记录注册状态、清理临时引用。
- `IConfigRegisterNode`：最小注册行为接口。
- `ConfigRegisterNodeBase`：Unity 可序列化注册节点基类。
- `CompositeConfigRegisterNode`：组合节点，按 Inspector 中 `children` 顺序执行子节点。
- `FrameworkConfigRootNode`：配置树根节点资产类型。

## Module Providers

具体模块的 Provider 继承 `ConfigRegisterNodeBase`，并放在对应模块目录中。Provider 只负责把配置写入自己的目标注册表，不负责启动时机。

```csharp
public sealed class TimeWheelManagerConfigProvider : ConfigRegisterNodeBase
{
    public override void Register()
    {
        AutoSingletonConfigRegistry.Register<TimeWheelManager, TimeWheelConfig>(config.CreateRuntimeCopy());
    }
}
```

## Rules

- 场景中只需要 `WSFrameRoot`，不再挂独立 Installer。
- 启动入口由 `WSFrameRoot` 统一控制，具体注册逻辑留在各模块 Provider 中。
- `ConfigRegisterSystem.ClearRuntimeReferences()` 只清除运行时临时引用，不删除资产。
