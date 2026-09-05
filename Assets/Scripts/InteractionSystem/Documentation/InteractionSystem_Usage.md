# 交互系统使用文档

返回：[交互系统入口](../ReadMe.md) · [技术文档](InteractionSystem_Technical.md) · [扩展指南](InteractionSystem_ExtensionGuide.md)

本文面向场景设计、策划和测试人员，说明如何在场景中配置交互检测、对话、物品拾取和交互选项窗口。

## 1. 玩家节点配置

保留稳定的 Player 与移动的 CharacterRoot 分层。`InteractionDetector` 和 `PlayerInteractor` 挂在 CharacterRoot，检测形状和 MaxDistance 随角色移动；`PlayerInteractor` 通过 `GetComponentInParent<PlayerController>(true)` 从自身或父级获取稳定的玩家对象。旧的所有组件同节点布局也可解析，但不要把不移动的 Player 作为新布局的检测位置。

```mermaid
flowchart TD
    Player[Player 根节点]
    Player --> Controller[PlayerController]
    Player --> CharacterRoot[CharacterRoot 移动节点]
    CharacterRoot --> Detector[InteractionDetector]
    CharacterRoot --> Interactor[PlayerInteractor]
    Player --> Receiver[可选 IItemPickupReceiver]
    Detector --> Interactor
```

配置检查：

- `PlayerController`：提供稳定玩家对象、移动和角色能力输入编排。
- `InteractionDetector`：提供交互区域和 Provider 候选扫描。
- `PlayerInteractor`：收集、筛选、排序和执行当前玩家的 Option。
- `PlayerInteractor.detector`：引用同一 CharacterRoot 上的 `InteractionDetector`；为空时从同节点获取。
- `PlayerInteractor.viewCamera`：可配置主摄像机。当前版本只把它传给查询上下文，不执行 Viewport、遮挡或镜头评分筛选。
- 物品拾取场景还需要玩家根节点上的背包或接收组件实现 `IItemPickupReceiver`。

`PlayerInteractor` 只保留 `Instance` 引用及变化事件，不对 CharacterRoot 单独执行 `DontDestroyOnLoad`，由父级 Player 管理整个层级的跨场景保留。替换玩家时先销毁旧实例，再创建新实例，窗口通过 `InstanceChanged` 重新绑定；重复实例会明确报错，不会删除 CharacterRoot。缺少 PlayerController 时会在 Awake 报错，请先修复玩家初始化问题。

交互查询和执行传入的是 PlayerController 所在的 Player 对象，距离使用 CharacterRoot 的世界位置，因此玩家侧对话参与者、物品接收器仍放在 Player 上。

## 2. 配置 InteractionDetector

### 2.1 检测形状

在 `InteractionDetector` 的 `Detection Shape` 中选择形状。当前支持：

| 类型 | 适用场景 | 主要参数 |
| --- | --- | --- |
| Box | 走廊、房间或矩形工作台 | `Size` |
| Sphere | 角色周围的通用交互半径 | `Radius` |
| Capsule | 纵向角色空间或窄长区域 | `Radius`、`Height` |
| Sector | 扇形视野或朝向区域 | `OuterRadius`、`InnerRadius`、`Height`、`Angle` |

None 和 Ray 不能用于 Detector。形状尺寸必须是有限且有效的正数；Capsule 的高度至少为两个半径，Sector 的内半径不能大于外半径。

常用配置示例：

```text
Type          = Sphere
Local Position= (0, 0.8, 0)
Radius        = 8
Draw Gizmos   = true
Scan Interval = 0.1
```

### 2.2 LayerMask、Trigger 和扫描

`Detection Mask` 决定哪些 Collider 能成为候选。物理查询当前使用 `QueryTriggerInteraction.Collide`，因此目标 Trigger Collider 也会参与检测。目标 Collider 可以在 Provider 的子节点上，Detector 会沿父级收集所有 `IInteractable`。

`Initial Buffer Size` 是 NonAlloc 查询的起始容量。命中数量达到容量时，Detector 会扩容并立即重查，不会静默截断候选。扫描间隔必须是有限正数，默认 `0.1s`。

### 2.3 Gizmo 与 Scene Handle

`PhysicsShapeData` 的 `Can Draw Gizmos` 开启后，即使未选中玩家，也可以在 Scene View 看到交互区域。选中 Detector 后使用 Inspector 的“开始编辑”按钮进入形状编辑，再使用 Scene View 的 Move、Rotate、Scale Handle 调整局部位置、旋转和尺寸。修改会记录 Unity Undo/Redo，并能作为 Prefab Override 保存。

检测区域只是 Provider 粗筛范围。`InteractionOption.MaxDistance` 可以进一步缩短某个 Option 的有效距离，但不能让它超出 Detector 命中范围。

## 3. 配置可交互对象

### 3.1 对话

在 NPC 的交互 Collider 节点或其父节点添加 `DialogueInteractable`：

1. 配置 `Dialogue Asset`。
2. 配置 `Participant Root`，通常指向 NPC 的角色根节点。
3. 确认 NPC 根节点或父级存在 `DialogueParticipant`。
4. 确认玩家根节点存在玩家 `DialogueParticipant`。
5. 确认 DialogueSystem 已由项目架构启动。
6. 运行时进入 Detector 形状，HUD 中出现“对话” Option。

对话 Option 的可用性由资源、玩家参与者、NPC 参与者和 DialogueSystem 共同决定。启动失败时 Option 不会被执行成功确认。

### 3.2 物品拾取

在物品的 Collider 节点或其父节点添加 `ItemInteractable`：

1. 配置 `Item Definition`。
2. 配置 `Quantity`、`Option Display Name`、`Priority` 和可选的 `Max Distance`。
3. 确认玩家根节点存在实现 `IItemPickupReceiver` 的组件。
4. 进入检测区域后，只有 `CanReceive` 返回 `true` 时才显示拾取 Option。
5. 执行时会再次调用 `TryReceive`；接收成功后物品 `GameObject` 被停用，失败则保持激活。

场景物品默认使用稳定 ActionId `Pickup`。当前约定是停用而不是销毁，便于未来对象池或重新激活流程接入。

### 3.3 多选项对象

一个对象可以同时挂载多个 Provider，也可以由一个 Provider 贡献多个 Option。例如 NPC 可以同时拥有对话、商店和任务 Provider。最终顺序由 `Priority` 降序、`InteractionOptionId` 升序决定，不要依赖组件在 Inspector 中的顺序或物理命中顺序。

## 4. 交互输入与 ChoiceWindow

### 4.1 默认操作

当前输入资产中的 ChoiceWindow UI 导航和执行如下：

| 操作 | Unity UI Action | 默认绑定 |
| --- | --- | --- |
| 上一项 | `Navigate` | 键盘 `W`/`Up Arrow`；手柄左摇杆或方向键 |
| 下一项 | `Navigate` | 键盘 `S`/`Down Arrow`；手柄左摇杆或方向键 |
| 执行 | `Submit` | 通用 Submit 绑定；额外支持键盘 `G` |

`InteractionPrevious`、`InteractionNext` 和 `Interact` 仍保留在 PlayerInputType 与输入资产中，供
兼容代码或未来非 UI 交互使用，但不会自动写入 PlayerInteractor 的选择状态。

ChoiceWindow 不再依赖 PlayerInteractor 自动消费 Blackboard Intent。上下键、手柄方向键和其他 UI Navigate 输入
由 Unity EventSystem 移动当前 Button Selection；OptionChoice 的 `ISelectHandler` 将 Selection 同步到
`PlayerInteractor.Select`。鼠标点击和 UI Submit 也先同步 Selection，再调用 `SubmitSelected`。因此鼠标、键盘和
手柄共用一条 UI 交互链，鼠标 Hover 不会单独改变持久高亮。

### 4.2 窗口行为

`GameWindowPreloadService` 会在跨场景保留的 WSFrameRoot 上依次预加载 HUD、Choice 和 Dialogue 窗口，并等待 ChoiceWindow 首次创建三个 `OptionChoice` 行。全部依赖准备完成后，HUD 显式显示；Choice 和 Dialogue 保持隐藏，直到各自业务流程打开。

```mermaid
stateDiagram-v2
    [*] --> Hidden: 预加载完成
    Hidden --> Visible: 出现至少一个有效 Option
    Visible --> Visible: Previous / Next 或列表刷新
    Visible --> Hidden: Option 列表变空
    Hidden --> Visible: 新扫描发现有效 Option
    Visible --> Executed: 点击或 Execute
    Executed --> Visible: 执行成功且仍有 Option
    Executed --> Hidden: 执行后列表为空
```

ChoiceWindow 只展示 Option 名称和选中高亮。EventSystem Selection 变化时，Controller 按行索引调用
`PlayerInteractor.Select(optionId)`；鼠标点击或 UI Submit 时，选择成功后再调用 `SubmitSelected()`。
无效索引或已消失的 Option 不会提交其他行。Option 的 Icon 字段当前不投影到该窗口。

## 5. 运行时控制

可在需要时从玩家侧调用：

| API | 用途 |
| --- | --- |
| `StartDetect()` | 开启检测并立即扫描，恢复场景或重新启用组件时使用 |
| `PauseDetect()` | 停止扫描、清空 Provider 和 Option，并刷新为空状态 |
| `ScanNow()` | 不等待扫描间隔，立即按当前形状重新查询，适合测试和业务状态变化后的主动刷新 |

Detector 的 `StartDetect()` 与 `PlayerInteractor.StartDetect()` 都会立即扫描。正常情况下优先调用玩家侧 API，使 Detector 和最终 Option 状态一起恢复。

## 6. Odin Tester

将 `InteractionSystemOdinTester` 挂到测试场景中的任意活动 GameObject，确保它位于 `Tests` 目录对应的运行时程序集可见范围内，然后在 Inspector 中使用 Odin Button。测试器用于手动观察：

- 当前 Detector 形状、检测状态和 Provider 数量。
- 胶囊/体积扫描命中与 NonAlloc 扩容结果。
- 多 Collider 去重、父级 Provider 和多 Provider 收集。
- 多 Option 的排序、选择保持、首尾循环和执行结果。
- Pause/Resume 后的清空与立即恢复。
- ItemPickupReceiver 的允许、拒绝和成功停用行为。

测试前先确认场景存在玩家根节点、Detector 形状有效、目标 Collider 位于 `Detection Mask`，并且 `GameplayTagDatabaseConfigProvider` 已在 ConfigInstaller 中引用正确的 GameplayTagDatabase。

## 7. 常见问题

| 现象 | 检查项 |
| --- | --- |
| 没有显示 Option | Detector 是否在扫描；形状是否有效；LayerMask 是否包含目标；Collider 父级是否存在 Provider；`CanExecute`/`CanReceive` 是否返回 `true` |
| 上下键无法切换 | EventSystem 是否存在并有当前选中 Button；`ChoiceWindowView` 是否已初始化行；OptionChoice Button 的 Navigation 是否为 Explicit；不可用项是否被正确置灰；Console 是否有 UI 初始化错误 |
| Option 顺序异常 | 检查 `Priority`；同优先级由运行时 `InteractionOptionId` 排序，不要依赖场景组件顺序 |
| 物品无法拾取 | `Item Definition` 是否配置；玩家根节点是否有 `IItemPickupReceiver`；`CanReceive` 是否允许；容量不足时 `TryReceive` 是否返回 `false` |
| 窗口没有显示 | 是否已创建并初始化 UIManager；GameWindowPreloadService 是否运行；是否仍有有效 Option；零 Option 时窗口会被 Hide 而不是销毁 |
| 停止运行时报 UI 错误 | 确认使用当前 WSFrameRoot 的统一 `UIManager.Shutdown()` 流程；不要在外部提前销毁 ChoiceWindow 或 OptionChoice 行 |

## 8. 场景交付检查清单

- Player 包含 `PlayerController`；移动的 CharacterRoot 包含 `InteractionDetector` 和 `PlayerInteractor`，父级控制器已完成初始化。
- Detector 形状、尺寸、LayerMask、扫描间隔和 Gizmo 已配置并在 Scene View 验证。
- 每个 Provider 的交互 Collider 可被 Detector 命中，Provider 位于 Collider 父级链上。
- 对话资源和参与者、物品定义和接收器均已配置。
- 多 Option 的 Priority、显示名称和稳定 ActionId 已确认。
- 键盘/手柄 UI Navigate、Submit 和鼠标点击已在 Play Mode 测试。
- 角色能力输入仍可通过 `PlayerInputController` 与 `GameplayInputIntentArbiterManager` 处理；ChoiceWindow 不再把交互导航写入玩家黑板。
- ChoiceWindow 预加载后创建三个初始行，零 Option 时隐藏，重新出现 Option 时复用同一窗口。
- 测试了 Option 列表动态变化、UI Navigate/Submit、鼠标点击、执行失败和停止运行释放流程。

相关文档：[WSFrame UI 文档](../../WSFrame/UISystem/Core/UISystem_Documentation.md)、[玩家输入预处理文档](../../Input/PlayerInputPreprocessing.md)、[对话系统需求文档](../../DialogueSystem/DialogueSystem_Requirements.md)、[ConfigInstaller 使用文档](../../WSFrame/ConfigInstaller/ConfigInstaller_Usage.md)。
