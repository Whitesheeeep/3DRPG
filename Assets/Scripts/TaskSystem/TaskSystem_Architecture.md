# 任务系统架构与数据模型

> 文档状态：任务生命周期 v2 已实现；玩法目标适配器、正式调用方和任务 UI 仍待接入
> 文档范围：仅描述 TaskSystem 的配置、玩家任务数据、阶段监听、存档快照及对其他系统的任务侧接口

任务产品需求和后续能力见 [TaskSystem_Requirements.md](TaskSystem_Requirements.md)。红点通用实现见 [RedDotSystem_Architecture.md](../RedDotSystem/RedDotSystem_Architecture.md)。本文只记录任务系统自身已经落地的行为与边界。

## 1. 系统职责与数据流

任务系统由静态任务配置、玩家任务状态、当前阶段监听和任务存档快照组成。TaskManager 持有任务配置入口和玩家任务事实；TaskProgressSystem 编排接取、阶段推进、领奖及监听生命周期；TaskRuntime 只服务活动任务的当前阶段。

~~~mermaid
flowchart LR
    DB["TaskDatabase<br/>静态任务配置索引"] --> Def["TaskDefinition<br/>单任务静态配置"]
    Def --> Stage["TaskStageDefinition"]
    Stage --> Objective["TaskObjectiveDefinition"]
    Source["NPC / 对话 / 剧情等来源"] -->|TaskId| Progress["TaskProgressSystem"]
    Progress -->|查询与写入任务事实| Manager["TaskManager"]
    Manager --> Record["TaskRecord<br/>玩家单任务状态"]
    Progress -->|创建 / 停止当前阶段| Runtime["TaskRuntime"]
    Runtime -->|引用同一条记录| Record
    Runtime --> Handler["Objective Handler"]
    Handler -->|应用目标进度| Manager
    Manager -->|采集 / 恢复| Snapshot["TaskSaveSnapshot v2"]
~~~

一次目标事件的状态写入路径为：

~~~mermaid
sequenceDiagram
    participant Event as 玩法领域事件
    participant Runtime as TaskRuntime
    participant Handler as 目标 Handler
    participant Manager as TaskManager
    participant Progress as TaskProgressSystem

    Event->>Runtime: 当前阶段目标事件
    Runtime->>Handler: 分发到当前目标
    Handler->>Manager: AddProgress / SetProgress
    Manager->>Manager: 校验 TaskId、StageId、ObjectiveId 并更新 TaskRecord
    Manager-->>Progress: 进度变化事实回调
    Progress->>Progress: 排队检查当前阶段是否完成
~~~

## 2. 数据模型

### 2.1 四层数据表

| 层 | 主要对象和字段 | 持有者 | 生命周期与用途 | 是否写入任务快照 |
| --- | --- | --- | --- | --- |
| 静态配置 | TaskDatabase 的 TaskDefinition 引用；TaskDefinition 的 TaskId、CategoryId、标题、描述、条件、阶段、奖励 | TaskManager 持有已校验的 TaskDatabase 配置引用 | 由资产定义，整个业务会话内供查询和校验 | 否 |
| 玩家任务状态 | TaskRecord 的 TaskId、CurrentStageId、State、当前阶段 ObjectiveProgress；Manager 的 CompletedTaskIds、TrackedTaskId、UnreadTaskIds | TaskManager | 从接取成功持续到领奖完成；作为玩家任务事实 | 是 |
| 当前阶段监听 | TaskRuntime 对 TaskDefinition、TaskStageDefinition 和 TaskRecord 的引用；目标运行时和监听句柄 | TaskProgressSystem 为每个 InProgress 任务持有一个 TaskRuntime | 只在当前阶段执行期间存在；切阶段或进入 Claimable 时停止并移除 | 否 |
| 任务存档快照 | 活动记录快照、完成 ID、追踪 ID、未读 ID | TaskSaveModule 从 TaskManager 采集或恢复 | 保存或加载时转换为 DTO；恢复后由 TaskProgressSystem 重建监听 | 是，版本 v2 |

TaskRecord 是 Manager 持有的权威玩家状态对象。TaskRuntime 会引用同一个 TaskRecord 来读取目标需求和提交进度，但不拥有记录，也不负责记录跨阶段和 Claimable 状态的保存。

### 2.2 静态配置结构

~~~mermaid
classDiagram
    class TaskDatabase {
        TaskDefinition[] definitions
        TaskId -> TaskDefinition index
    }
    class TaskDefinition {
        TaskId taskId
        TaskCategoryId categoryId
        string title
        string description
        TaskConditionDefinition[] unlockConditions
        TaskStageDefinition[] stages
        TaskRewardDefinition[] rewards
    }
    class TaskStageDefinition {
        TaskStageId stageId
        string title
        string description
        TaskObjectiveDefinition[] objectives
    }
    class TaskObjectiveDefinition {
        ObjectiveId objectiveId
        int required
    }
    class TaskManager {
        TaskDatabase database
        TaskRecord[] activeRecords
        TaskId[] completedTaskIds
        TaskId trackedTaskId
        TaskId[] unreadTaskIds
    }
    class TaskRecord {
        TaskId taskId
        TaskStageId currentStageId
        TaskLifecycleState state
        TaskObjectiveProgress[] objectiveProgress
    }
    class TaskObjectiveProgress {
        ObjectiveId objectiveId
        int required
        int current
    }
    TaskDatabase "1" o-- "*" TaskDefinition
    TaskDefinition "1" *-- "1..*" TaskStageDefinition
    TaskDefinition "1" *-- "0..*" TaskConditionDefinition
    TaskDefinition "1" *-- "1..*" TaskRewardDefinition
    TaskStageDefinition "1" *-- "1..*" TaskObjectiveDefinition
    TaskManager "1" o-- "0..*" TaskRecord
    TaskRecord "1" *-- "1..*" TaskObjectiveProgress
~~~

- 每个 TaskDefinition 是一个独立 ScriptableObject 任务资产；TaskDatabase 保存资产引用、校验唯一 TaskId 并建立索引。
- 阶段按资产列表顺序执行；一个阶段的目标按 AND 规则全部完成后才结束阶段。
- 接取条件为声明式配置并由条件 Handler 解释；当前实现前置任务已完成条件。
- 当前只接受货币奖励定义；未支持的奖励类型由配置校验拒绝。
- TaskCategoryId 仍以字符串 ID 存储；TaskCategoryCatalog 固定登记 main（主线）和 side（支线），Drawer 与运行时配置校验共用该表。

### 2.3 标识范围

| 标识 | 唯一范围 | 配置与存储约束 |
| --- | --- | --- |
| TaskId | 全局 | 资产、运行时 API 和快照共用的稳定 ID；快照中为字符串 |
| TaskCategoryId | 静态分类表 | 稳定字符串 ID；调整显示名不改变 ID |
| TaskStageId | 所属任务内 | 稳定字符串 ID；快照记录当前阶段 ID |
| ObjectiveId | 所属阶段内 | 稳定字符串 ID；进度记录和快照按此匹配目标 |
| TaskAcceptSource | 无 | Enum，只记录统一接取入口的调用来源，不参与任务身份或规则 |

配置字段使用字符串，运行时索引和 API 使用 TaskId、TaskStageId、ObjectiveId 等强类型值对象，序列化快照使用字符串值。重命名或删除已使用的稳定 ID 需要基于真实存档数据设计迁移。

### 2.4 玩家任务状态和生命周期

一个 TaskRecord 表示一项已接取且尚未领取完成奖励的任务：

- TaskId 指向静态任务定义。
- CurrentStageId 指向当前阶段。
- State 只取 InProgress 或 Claimable。
- ObjectiveProgress 只含当前阶段的目标进度；切换阶段会清除旧阶段进度并按新阶段目标初始化。

玩家任务全局状态由 TaskManager 持有：

- ActiveRecords 是按 TaskId 索引的活动 TaskRecord 集合。
- CompletedTaskIds 只记录已完成任务 ID，不保留活动记录。
- TrackedTaskId 最多指向一项活动任务；Claimable 任务仍可追踪。
- UnreadTaskIds 记录已接取但尚未确认查看的任务 ID。

~~~mermaid
stateDiagram-v2
    [*] --> InProgress: 接取成功
    InProgress --> InProgress: 阶段目标完成且存在下一阶段
    InProgress --> Claimable: 最后阶段目标完成
    Claimable --> Completed: 手动领奖成功
    Completed --> [*]: 活动记录移除，仅保留 TaskId
~~~

Locked 和 Available 是未接取任务的查询结果，不属于需要保存的生命周期状态。Completed 作为状态转换事实存在，完成后通过 CompletedTaskIds 表达。

## 3. 临时阶段运行时

### 3.1 TaskProgressSystem

TaskProgressSystem 是流程协调者，依赖 TaskManager、条件与目标 Handler 注册表、货币奖励 Handler，并在架构生命周期内注册 TaskSaveModule。主要入口为：

- GetAvailability(TaskId)：返回 NotFound、Locked、Available、Active 或 Completed，以及结构化条件原因。
- TryAcceptTask(TaskId, TaskAcceptSource)：查询资格、验证 Handler、创建记录、启动首阶段监听，成功后提交接取事实。
- TryClaimReward(TaskId)：预检并发放货币奖励，成功后将任务记入完成集合。
- RebuildRuntimes()：读档成功后根据活动记录重建 InProgress 任务当前阶段的监听。

TaskProgressSystem 还维护按 TaskId 索引的 TaskRuntime、待处理阶段检查队列和防止重复领奖的任务集合。

### 3.2 TaskRuntime 与 TaskRecord

每项 InProgress 任务在 TaskProgressSystem 中对应一个 TaskRuntime。TaskRuntime 持有当前 TaskDefinition、TaskStageDefinition、TaskRecord 的引用、目标 Handler 注册表及已创建目标运行时列表。它启动和停止目标订阅，并将目标进度变更提交给 TaskManager。

TaskRuntime 的生命周期属于当前阶段：

- 接取时创建并启动首阶段 Runtime。
- 阶段完成时停止并移除旧 Runtime；TaskRecord 保留在 Manager 中并切到下一阶段，然后为新阶段创建 Runtime。
- 最后阶段完成后停止并移除 Runtime，TaskRecord 转为 Claimable。
- 领奖成功后活动 TaskRecord 从 Manager 移除并记录完成 ID。
- 读档时从 Manager 的 TaskRecord 恢复 InProgress Runtime；Claimable 记录不启动监听。

目标上下文将 TaskId、TaskStageId 和 ObjectiveId 一并交给 Manager 校验。阶段 ID 同时充当进度写入令牌，使旧阶段迟到回调在阶段已变更后被忽略。

## 4. 生命周期命令与事件时机

### 4.1 接取任务

所有来源只提交 TaskId 和 TaskAcceptSource 到 TaskProgressSystem.TryAcceptTask。来源不直接创建记录。

~~~mermaid
sequenceDiagram
    participant Source as 任务来源
    participant Progress as TaskProgressSystem
    participant Manager as TaskManager
    participant Runtime as TaskRuntime

    Source->>Progress: TryAcceptTask(TaskId, Source)
    Progress->>Progress: 查询资格并验证目标 Handler
    Progress->>Manager: 创建未提交的 TaskRecord
    Progress->>Runtime: 创建并启动首阶段监听
    Runtime-->>Progress: 监听建立成功
    Progress->>Manager: 提交接取事实并加入未读
    Manager-->>Source: 发布 TaskAcceptedEventArgs
~~~

若首阶段 Runtime 创建失败，ProgressSystem 停止已建立的监听并回滚未提交记录。业务拒绝返回结构化结果；缺少 Handler 等配置错误明确抛出。

### 4.2 进度与阶段推进

目标 Handler 通过 RuntimeContext 调用 Manager 的进度写入入口。Manager 检查任务仍活动、阶段 ID 仍匹配、目标属于当前阶段，再写入 TaskRecord 并发布目标进度事实。ProgressSystem 收到进度变化后排队检查阶段完成，避免目标初始化期间同步回调重入阶段切换。

阶段切换顺序：

1. 停止并移除旧阶段 TaskRuntime。
2. Manager 更新同一 TaskRecord 的 CurrentStageId，并初始化新阶段目标进度。
3. ProgressSystem 创建并启动新阶段 TaskRuntime。
4. 新监听建立成功后，由 TaskProgressSystem 发布 TaskStageChangedEventArgs。

最后阶段完成时，ProgressSystem 移除监听，Manager 将 TaskRecord 状态改为 Claimable，并发布状态及可领奖事实。追踪切换仅修改 TrackedTaskId，不改变任何目标进度或监听。

### 4.3 事件分工

WSFrame EventSystem 使用事件类型路由任务事实事件：

- TaskManager 在任务事实写入成功后发布接取、目标进度、状态、可领奖、完成、追踪和确认查看事件。
- TaskProgressSystem 在阶段数据切换且新阶段监听建立成功后发布阶段切换事件，因为发布时机依赖流程编排。
- 恢复快照不重放接取、目标累计、领奖或阶段切换等正常业务事件。

事件载荷使用稳定 ID；目标进度事件包含 TaskId、TaskStageId、ObjectiveId、旧值与新值。

## 5. 奖励领取

当前只支持手动领取货币奖励：

1. TaskProgressSystem 确认活动记录处于 Claimable。
2. 奖励 Handler 汇总同种货币并调用无副作用的 CanAddCurrencies 预检。
3. 预检失败时不发放，任务保留 Claimable，调用方可修正状态后重试。
4. 预检通过后只调用一次现有批量 AddCurrencies API。
5. 发放成功后停止残留 Runtime（正常 Claimable 状态没有监听），Manager 移除活动记录、清除对应追踪和未读状态、写入 CompletedTaskIds。

其他奖励类型在 TaskDefinition 校验阶段拒绝。自动发奖及需要跨系统回滚的奖励，待存在对应事务方案后再扩展。

## 6. 任务存档模型与恢复

任务存档由 TaskSaveModule 适配 SaveSystem。模块 ID 为 task，模块版本为 v2，TaskManager 提供快照采集与恢复。存档保存玩家任务事实，不复制静态配置，也不保存 Handler 实例或事件句柄。

~~~mermaid
classDiagram
    class TaskSaveSnapshot {
        ActiveTasks: TaskRecordSnapshot[]
        CompletedTaskIds: string[]
        TrackedTaskId: string
        UnreadTaskIds: string[]
    }
    class TaskRecordSnapshot {
        TaskId: string
        CurrentStageId: string
        State: InProgress | Claimable
        ObjectiveProgress: TaskObjectiveProgressSnapshot[]
    }
    class TaskObjectiveProgressSnapshot {
        ObjectiveId: string
        Current: int
        Required: int
    }
    TaskSaveSnapshot "1" *-- "0..*" TaskRecordSnapshot
    TaskRecordSnapshot "1" *-- "1..*" TaskObjectiveProgressSnapshot
~~~

- 活动快照保存 TaskId、CurrentStageId、State 和当前阶段目标的 ObjectiveId、Current、Required。
- 全局快照保存 CompletedTaskIds、TrackedTaskId 和 UnreadTaskIds。
- TaskDefinition、Handler、Unity 对象、事件句柄和阶段以外的派生结果不进入快照。
- 恢复前会按当前配置验证任务、阶段、目标、状态及进度范围；无效快照不替换当前任务状态。
- 恢复不会调用正常接取、进度写入、领奖或事实事件入口。
- 加载成功后，TaskProgressSystem 对 InProgress 记录重建当前阶段监听；Claimable 记录保持无监听状态。
- 当前任务模块版本为 v2；由于目前没有实际任务存档槽位，未提供 v1 迁移代码。

## 7. 与其他系统的任务侧接口

### 7.1 货币系统

TaskCurrencyRewardHandler 通过 ICurrencyWallet 的无副作用 CanAddCurrencies 和原子 AddCurrencies 批量接口发放奖励。任务模块不直接修改货币内部状态。

### 7.2 存档系统

TaskSaveModule 只负责将 TaskManager 状态映射为 v2 快照并校验快照结构。槽位、容器、序列化、完整性和迁移框架由 SaveSystem 管理。

### 7.3 红点系统

任务红点的业务事实是 UnreadTaskIds。确认查看通过 TaskManager.AcknowledgeTask(TaskId) 清除单个任务的未读事实；领奖完成也清除该任务的未读状态。阶段完成或进入 Claimable 不增加任务未读。

通用红点树及其运行时机制由 RedDotSystem 管理。任务侧适配器尚未接入正式红点节点；接入时从 UnreadTaskIds 计算数量，不把任务规则放入红点核心。

## 8. 校验与失败边界

- 重复或无效 TaskId、无效分类、重复阶段或目标 ID、空阶段、非法目标需求和未支持奖励类型由配置校验拒绝。
- TaskDatabase 在初始化时验证全部任务资产并建立唯一 TaskId 索引。
- 业务资格拒绝、重复接取、已完成和不可领奖通过结构化结果返回。
- 缺少条件或目标 Handler 属于配置或集成错误，明确报错，不转换为普通玩家拒绝。
- 目标进度写入使用当前 StageId 检查迟到事件；目标 ID 不属于当前阶段时暴露不变量错误。
- 领奖预检失败保持 Claimable；不得先写完成 ID 再发放货币。

## 9. 当前实现状态与后续能力

| 能力 | 状态 |
| --- | --- |
| 独立 TaskDefinition 资产、TaskDatabase 索引与配置校验 | 已实现 |
| 固定分类表及 Inspector 分类选择器 | 已实现 |
| 前置任务条件、统一资格查询与统一接取 | 已实现 |
| 线性阶段、当前阶段目标进度和监听重建 | 已实现 |
| 追踪、未读、完成事实及 v2 快照 | 已实现 |
| 货币预检、原子批量发放和手动领奖 | 已实现 |
| 战斗、背包、对话等真实玩法目标适配器 | 待对应玩法接入 |
| 任务链和章节、导航目标、资源占用 | 后续任务能力 |
| 正式任务 Query、NPC/对话调用方、UI 与任务红点适配器 | 待接入 |
| 自动及非货币奖励 | 待事务方案和 Handler 扩展 |

现有 Odin 生命周期手动测试组件位于 Assets/Scripts/TaskSystem/Test/TaskSystemOdinTester.cs，覆盖资格、统一接取、多阶段进度、追踪、未读、领奖及快照恢复。测试组件用于手动验证，不替代 Unity 中正式场景调用方的验证。

## 10. 代码位置

~~~
Assets/Scripts/TaskSystem/
├─ Runtime/Config/       TaskDatabase、TaskDefinition、阶段与静态分类
├─ Runtime/Conditions/
│  ├─ Definitions/       条件基类与具体条件定义
│  ├─ Interfaces/        条件 Handler 契约
│  ├─ Handlers/          条件评估实现
│  └─ Registries/        条件 Handler 显式注册表
├─ Runtime/Data/         强类型 ID、TaskRecord 与命令结果
├─ Runtime/Core/         TaskManager、TaskProgressSystem、TaskRuntime
├─ Runtime/Objectives/
│  ├─ Definitions/       目标定义基类
│  ├─ Interfaces/        Handler、运行时与上下文契约
│  ├─ Handlers/          泛型 Handler 适配基类
│  └─ Registries/        目标 Handler 显式注册表
├─ Runtime/Rewards/      货币奖励定义与适配
├─ Runtime/Events/       任务事实事件
├─ Runtime/Save/         TaskSaveModule 与任务快照
└─ Test/                 Odin 生命周期手动测试
~~~

新增条件 Handler 时，在 `Runtime/Conditions/Handlers/` 创建实现文件，并在 `Runtime/Conditions/Registries/TaskConditionHandlerRegistry.cs` 的 `RegisterDefault()` 中显式登记；新增目标 Handler 时，对应修改 `Runtime/Objectives/Registries/TaskObjectiveHandlerRegistry.cs`。Odin 测试专用 Handler 继续由测试入口直接注册；`GameArchitecture` 只调用两个注册表的默认初始化入口。
