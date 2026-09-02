# 交互系统技术文档

返回：[交互系统入口](../ReadMe.md) · [扩展指南](InteractionSystem_ExtensionGuide.md) · [使用文档](InteractionSystem_Usage.md)

## 1. 系统定位

交互系统将“发现附近对象”和“执行一个具体动作”拆开：Detector 只负责空间候选发现，Provider 负责贡献 Option，PlayerInteractor 负责最终筛选、排序和选择，Option 负责业务校验与执行。

```mermaid
flowchart LR
    Shape[PhysicsShapeData<br/>可编辑检测形状] --> Detector[InteractionDetector]
    Detector --> Providers[IInteractable Provider 集合]
    Providers --> Collect[CollectInteractionOptions]
    Collect --> Options[InteractionOption 列表]
    Options --> Filter[有效对象 / MaxDistance / CanExecute]
    Filter --> Sort[Priority 降序<br/>InteractionOptionId 升序]
    Sort --> Interactor[PlayerInteractor]
    Interactor --> DomainEvents[OptionsChanged / SelectionChanged]
    DomainEvents --> Controller[InteractionUIController]
    Controller --> View[ChoiceWindowView]
    UI[EventSystem Navigate / Submit] --> View
    View -->|SelectionRequested / ChoiceRequested| Controller
    Controller -->|Select / SubmitSelected| Interactor
    Interactor --> Execute[InteractionOption.TryExecute]
```

当前版本是本地单玩家系统。它不负责网络同步、多人抢占、服务端校验、失败原因展示或置灰选项。

## 2. 核心职责与契约

| 类型 | 职责 | 不负责的内容 |
| --- | --- | --- |
| `InteractionDetector` | 使用 `PhysicsShapeData` 查询 Collider、收集 Provider、去重并周期通知 | 不创建 Option，不执行 Action，不决定最终 UI 顺序 |
| `IInteractable` | 提供交互对象、交互中心，并向调用方集合贡献一个或多个 Option | 不直接提供统一的 `Interact` 命令 |
| `InteractableObject` | 为 MonoBehaviour Provider 提供自身 `GameObject` 和 `Transform` 默认实现 | 不承载具体业务执行逻辑 |
| `InteractionOption` | 保存展示数据、空间约束、业务校验和执行回调 | 不管理 UI 行实例，不保存玩家选择 |
| `InteractionOptionId` | 由 Provider Unity 运行时实例 ID 与稳定 `ActionId` 组成，用于去重、排序和保留选择 | 不作为存档 ID，不保证跨运行稳定 |
| `InteractionQueryContext` | 向 Provider 传递稳定 Player 对象、移动 CharacterRoot Transform 和查询摄像机 | 不代表最终筛选结果；两个身份不要求是同一 GameObject |
| `PlayerInteractor` | 收集、硬筛选、排序、选择、执行并发布领域事件 | 不负责窗口资源加载和 UI 行创建 |
| `InteractionUIController` | 将领域 Option 投影为字符串和 ID，并协调 ChoiceWindow 显隐 | 不执行距离、遮挡或业务判断 |

### 2.1 Provider 与 Option 的边界

一个 Provider 可以贡献多个 Option；同一个 GameObject 也可以挂载多个 Provider。Detector 从命中 Collider 的父级链收集全部 `IInteractable`，不限定为 `InteractableObject`。

Provider 应长期缓存不会变化的 Option 对象。周期扫描只把缓存引用追加到调用方列表，避免热路径持续创建命令对象。Option 内部的 `CanExecute` 和 `TryExecute` 可以读取实时业务状态。

### 2.2 Option 执行契约

`PlayerInteractor` 刷新列表时调用 `CanExecute`。用户点击或输入执行时，`InteractionOption.TryExecute` 会再次调用 `CanExecute`，通过后才调用执行回调：

```text
扫描刷新 → CanExecute
用户选择 → 保存 SelectedOption
用户执行 → CanExecute 再校验 → Execute
成功     → 由业务入口确认对应 Intent
失败     → 不确认输入消费
```

因此，刷新时可用不代表执行时一定成功；执行入口必须保持幂等和实时校验。

## 3. Detector 扫描流程

### 3.1 形状与查询

`InteractionDetector` 直接持有 `PhysicsShapeData`，支持 Box、Sphere、Capsule 和 Sector。形状的局部位置、旋转、尺寸以及 Gizmo 开关由 `PhysicsShapeData` 和现有 Inspector/Scene Handle 工具管理。

```mermaid
sequenceDiagram
    participant D as InteractionDetector
    participant P as PhysicsUtility
    participant C as Collider[] NonAlloc 缓冲区
    participant S as Provider Set
    participant E as PlayerInteractor

    D->>P: OverlapNonAlloc(transform, detectionShape, buffer)
    P-->>C: 返回命中 Collider 数量
    alt 缓冲区已满
        D->>C: 扩容
        D->>P: 立即重查
    end
    D->>D: 沿 Collider 父级收集全部 IInteractable
    D->>S: nextProviderSet 与 providerSet 比较
    D-->>E: ProvidersChanged（集合变化时）
    D-->>E: ScanCompleted（每次扫描）
    E->>E: 收集、筛选、排序并维护选择
```

NonAlloc 缓冲区满时会扩容并立即重查，不能把“返回数量等于容量”当作完整结果。Provider 使用当前 Set 与下一轮 Set 对比，多个 Collider 命中同一个 Provider 时只保留一份。

### 3.2 两类扫描事件

- `ProvidersChanged`：只有 Provider 集合实际发生变化时发送，适合观察进入、离开范围。
- `ScanCompleted`：每次扫描完成都发送，即使 Provider 集合没有变化；`PlayerInteractor` 依靠它刷新动态 `CanExecute` 和 `MaxDistance`。

扫描间隔当前默认为 `0.1s`。`StartDetect()` 会立即扫描，`PauseDetect()` 会停止扫描、清空 Provider，并发送一次空状态刷新。`startDetectOnEnable` 是序列化启动配置，运行时暂停状态单独保存在 `isDetecting`，暂停不会改写 Inspector 配置。

## 4. PlayerInteractor 刷新、筛选与选择

### 4.1 刷新时机

`PlayerInteractor.Update()` 只消费当前帧的输入 Intent，不再逐帧调用 `RefreshOptions()`。Option 列表由 Detector 的 `ScanCompleted` 驱动，因此 Provider 集合不变时，业务状态仍会在下一次扫描时重新筛选。

### 4.2 硬筛选顺序

当前最终列表只保留同时满足以下条件的 Option：

1. `InteractionObject` 和 `InteractionOrigin` 有效。
2. 玩家到 `InteractionOrigin` 的距离没有超过 `MaxDistance`；零表示不额外收紧 Detector 范围。
3. `CanExecute(player)` 返回 `true`。

当前版本不启用 Viewport、遮挡和镜头关注度筛选。Detector 的形状负责 Provider 粗筛，Option 的 `MaxDistance` 负责进一步收紧范围。

### 4.3 稳定排序与选择保持

排序顺序固定为：

1. `Priority` 降序。
2. `InteractionOptionId` 升序。

刷新前保存当前 Option ID。刷新后若 ID 仍存在则保留选择；原选择消失时选新的第一项；首次出现列表时选择第一项。`SelectPrevious()` 和 `SelectNext()` 首尾循环。

只有最终 Option ID 顺序发生变化时才发送 `OptionsChanged`；只有选中 ID 发生变化时才发送 `SelectionChanged`。这两个事件都不会因为重复刷新而无条件发送。

## 5. 输入与 ChoiceWindow

场景交互选项由 `ChoiceWindow` 通过 Unity EventSystem 直接接收 UI `Navigate` 和 `Submit`。上下键、手柄方向键
和 UI 导航动作由 EventSystem 根据每个 `OptionChoice.Button.navigation` 的显式链移动 Selection；
`ChoiceWindowView` 在收到 `ISelectHandler` 后通过 `SelectionRequested` 同步 `PlayerInteractor.Select`。
点击或 Submit 则走同一条路径：先选择稳定 `InteractionOptionId`，再调用 `SubmitSelected()`。

`PlayerInteractor` 不再在 `Update` 中自动读取 `PlayerStateBlackboard` 的交互 Intent，也不负责消费
`InteractionPrevious`、`InteractionNext` 或 `Interaction.Execute`。保留 `SelectPrevious`、`SelectNext`、
`SubmitSelected` 等公开方法，供业务代码和兼容测试直接调用。这样 UI 选择不会和角色输入仲裁重复消费，
鼠标和键盘/手柄最终都归一到同一个 EventSystem Selection。

```mermaid
sequenceDiagram
    participant I as UI Navigate / Submit
    participant E as EventSystem
    participant V as ChoiceWindowView
    participant C as InteractionUIController
    participant P as PlayerInteractor

    I->>E: 移动 Navigation 或提交 Button
    E->>V: Select / Click
    V->>C: SelectionRequested / ChoiceRequested
    C->>P: Select(optionId)
    C->>P: SubmitSelected()
```

`OptionChoice` 会在 `Awake` 保存原始 ColorBlock，并把 Highlighted 颜色复用为普通颜色，因此鼠标 Hover 不会形成独立的
持续高亮；`Selected` 颜色只由 EventSystem Selection 对应的 View 状态写入。不可用行使用 Button 的
`interactable = false` 并从显式上下导航链排除。旧的 `InteractionInputIntentArbiter` 与输入 Action 保留用于兼容
和未来非 UI 场景，但不再由 `GameplayInputIntentArbiterManager.RegisterDefaultArbiters` 自动注册，也不会驱动
`PlayerInteractor`。

## 6. ChoiceWindow MVC 与预加载

`ChoiceWindow` 是 Window 层的 MVC 组合根：它创建 View 和 Controller，并在销毁时释放二者。`ChoiceWindowView` 负责异步加载 `OptionChoice` prefab，第一次创建三行，后续扩容并复用；零选项时隐藏窗口但不销毁窗口和行。

```mermaid
sequenceDiagram
    participant W as ChoiceWindow
    participant V as ChoiceWindowView
    participant C as InteractionUIController
    participant P as PlayerInteractor
    participant U as UIManager

    W->>V: 创建并初始化 View
    W->>C: 注入 Window 与 View
    C->>P: 绑定 InstanceChanged 和领域事件
    P-->>C: OptionsChanged / SelectionChanged
    C->>V: RefreshOptions(string[], selectedIndex)
    alt 有 Option
        C->>U: PopUpWindowAsync<ChoiceWindow>()
    else 零 Option
        C->>U: HideWindow<ChoiceWindow>()
    end
    V-->>C: SelectionRequested(index)
    C->>P: Select(optionId)
    V-->>C: ChoiceRequested(index)
    C->>P: Select(optionId)
    C->>P: SubmitSelected()
```

当前 ChoiceWindow 不读取 `InteractionOption.Icon`。领域层保留图标字段供未来 View 使用，ChoiceWindow 只投影 Option 名称和选中索引；`OptionChoice` prefab 中的 ChatIcon 是固定装饰。

`GameWindowPreloadService` 通过 `IWindowPreloadService : IScenePreloadTask` 依次预加载 HUD、Choice 和 Dialogue，并等待 ChoiceWindow 的三行 View 初始化完成。预加载只初始化，不自动显示窗口。

停止运行时，`WSFrameRoot` 先调用 `UIManager.Shutdown()`，窗口统一释放 Controller、View、行实例和资源，避免玩家销毁事件刷新已经被 Unity 销毁的 UI 行。

## 7. 依赖、性能与边界

Player 是稳定的业务身份，CharacterRoot 是移动空间基准。交互组件与 Detector 保持在 CharacterRoot；查询上下文的
`Interactor`、`CanExecute` 和 `TryExecute` 参数统一使用 PlayerController 所在对象，MaxDistance 继续使用交互组件的世界位置。

`PlayerInteractor` 是普通 MonoBehaviour，提供本地单玩家 `Instance` 与 `InstanceChanged`，不继承会对自身调用 `DontDestroyOnLoad` 的单例基类。父级 Player 负责跨场景保留；重复交互实例或父级控制器缺失会在 Awake 明确报错。只有依赖就绪才发布实例并允许 OnEnable 订阅扫描；销毁时清空当前实例并通知 UI 解绑。

```mermaid
flowchart TD
    Root[WSFrameRoot] --> Config[ConfigInstaller]
    Config --> Tags[GameplayTagManager]
    Root --> UI[UIManager]
    UI --> Preload[GameWindowPreloadService]
    Player --> Interactor[PlayerInteractor]
    Interactor --> Controller[InteractionUIController]
```

- 物理查询和 Provider 列表尽量复用缓冲区；缓冲区扩容只在容量不足时发生。
- Provider 集合、Option ID 集合和最终 Option 列表均由玩家侧长期复用。
- UI 文本和 ID 投影由 Controller 复用 `List<string>` 与 `List<InteractionOptionId>`。
- `InteractionOption` 不能作为跨运行存档对象；其 ID 包含 Unity 运行时实例 ID。
- 当前系统只支持本地单玩家，一个 `PlayerInteractor` 对应一个 UI 绑定实例。

相关框架说明：[WSFrame UI 文档](../../WSFrame/UISystem/Core/UISystem_Documentation.md)、[输入预处理文档](../../Input/PlayerInputPreprocessing.md)、[ConfigInstaller 使用文档](../../WSFrame/ConfigInstaller/ConfigInstaller_Usage.md)。
