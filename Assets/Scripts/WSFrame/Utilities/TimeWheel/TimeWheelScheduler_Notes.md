# TimeWheel 使用与实现说明

这份文档只说明 `Assets/Scripts/WSFrame/Utilities/TimeWheel/` 下的时间轮工具，不涉及任何具体玩法时间系统。

`TimeWheel` 适合管理大量“延迟一段单位时间后触发”的任务，例如技能冷却、短期状态结束、UI 延迟关闭、批量倒计时等。它的核心价值是：任务数量多时，不需要每帧遍历所有任务，只需要推进当前 tick 对应的槽位。

## 文件职责

- `TimeWheelManager.cs`：Unity 场景中的默认入口，由 `Update()` 使用 `Time.deltaTime` 推动内部调度器，因此该实例的输入单位是真实秒。
- `TimeWheelScheduler.cs`：纯 C# 调度器，需要外部手动调用 `Tick(deltaUnits)`，输入单位由调用方定义。
- `TimeWheelConfig.cs`：时间轮配置，包括 tick 长度、各层槽位数量、单帧最大追帧 tick 数。
- `TimeWheelHandle.cs`：任务句柄，用于取消、暂停、恢复任务。
- `TimeWheelTask.cs`：被调度的任务对象，使用 `PoolManager` 的类对象池复用。
- `TimeWheelManagerConfigProvider.cs`：把配置注册给 `AutoConfigSingletonMonoBase` 使用。
- `TimeWheelManagerConfigProvider.asset`：默认配置资源。

## 什么时候使用 TimeWheel

适合使用：

- 同时存在大量延迟任务或重复任务。
- 任务只需要在“某个延迟时间之后”触发，不需要每帧连续更新。
- 可以接受 tick 粒度带来的误差。例如输入单位是真实秒且 `tickUnit = 0.1f` 时，触发精度约为 0.1 秒。
- 希望取消、暂停、恢复任务时成本稳定。

不适合使用：

- 每帧都要计算的逻辑，例如移动、插值、动画采样。
- 必须严格按物理步或帧同步推进的逻辑。
- 任务数量很少且逻辑非常简单的场景，直接协程或普通计时器可能更直观。
- 需要真实时间绝对精度的系统，例如音频节拍、网络同步判定。

## 推荐入口：TimeWheelManager

大多数 Unity 运行时逻辑直接使用 `TimeWheelManager.Instance`。它已经封装了一个内部 `TimeWheelScheduler`，并在 `Update()` 中自动推进。

```csharp
TimeWheelManager.Instance.Schedule(2f, () =>
{
    Debug.Log("2 秒后触发");
});
```

重复任务：

```csharp
TimeWheelHandle handle = TimeWheelManager.Instance.ScheduleRepeat(
    interval: 1f,
    callback: OnRepeatTick,
    repeatCount: 5);
```

取消任务：

```csharp
TimeWheelManager.Instance.Cancel(handle);
```

暂停与恢复：

```csharp
TimeWheelManager.Instance.Pause(handle);
TimeWheelManager.Instance.Resume(handle);
```

清空所有任务：

```csharp
TimeWheelManager.Instance.Clear();
```

## 手动驱动：TimeWheelScheduler

当你不希望依赖 Unity `Update()`，或者想在自己的模块中手动推进时间时，可以直接创建 `TimeWheelScheduler`。

```csharp
TimeWheelConfig config = new TimeWheelConfig(
    tickUnit: 0.1f,
    slotCounts: new List<int> { 256, 64, 64 },
    maxCatchUpTicksPerFrame: 100);

TimeWheelScheduler scheduler = new TimeWheelScheduler(config);

scheduler.Schedule(3f, () =>
{
    Debug.Log("3 秒后触发");
});

// 由外部循环推动。
scheduler.Tick(deltaUnits);
```

`TimeWheelScheduler` 本身不关心输入单位是什么。只要稳定调用 `Tick(deltaUnits)`，它就会按配置推进内部 tick。

典型用法：

- `TimeWheelManager` 传入 `Time.deltaTime`，所以输入单位是真实秒。
- 游戏时间系统可以在每个游戏分钟到来时传入 `1f`，所以输入单位是游戏分钟。

## 配置说明

默认配置：

```csharp
tickUnit = 0.1f;
slotCounts = new List<int> { 256, 64, 64 };
maxCatchUpTicksPerFrame = 100;
```

### tickUnit

推进一个基础 tick 需要累计多少输入单位。输入单位由调用方决定，可以是真实秒、游戏分钟，或其他逻辑时间单位。

- 值越小，触发越精细，但推进频率越高。
- 值越大，性能压力越小，但延迟误差越大。

例如：

- `0.05f`：适合较精细的短期倒计时。如果输入单位是真实秒，则表示 0.05 秒。
- `0.1f`：通用默认值。
- `1f`：适合粗粒度后台任务，或表示“每 1 个游戏分钟推进 1 tick”这类逻辑时间。

所有延迟时间都会向上取整为 tick：

```text
delayTicks = Ceiling(delayUnits / tickUnit)
```

因此 `delay <= 0` 的任务也会被安排到下一个 tick，而不是在当前调用栈立即执行。

### slotCounts

每一层时间轮的槽位数量。默认 `{ 256, 64, 64 }` 表示三层：

- `level 0`：256 个槽。
- `level 1`：64 个槽。
- `level 2`：64 个槽。

层数越多、槽位越多，能覆盖的最大延迟越长，但结构也更大。

### maxCatchUpTicksPerFrame

当某次输入的 `deltaUnits` 很大时，调度器会尝试补跑多个 tick。这个参数限制单次 `Tick(deltaUnits)` 最多补跑多少个 tick，避免一次性执行过多任务。

如果积压超过限制，当前实现会丢弃过多积压，只保留不足一个 tick 的余量。也就是说，它优先保护帧稳定性，而不是无限追赶历史时间。

## 层级时间轮模型

多级时间轮不是把时间切成：

```text
level 0 管前一段时间
level 1 管后一段时间
level 2 管更后一段时间
```

当前实现使用的是“同一条绝对 tick 时间轴的不同粒度映射”：

- `level 0` 用最细粒度观察时间。
- `level 1` 用更粗粒度观察同一条时间轴。
- `level 2` 再用更粗粒度观察同一条时间轴。

高层不是低层后面的时间段，而是低层时间轴的粗粒度覆盖。

## slotCount、span、capacity

### slotCount

某一层有多少个槽位。

```text
slotCounts = [256, 64, 64]
```

表示：

- `level 0` 有 256 个槽位。
- `level 1` 有 64 个槽位。
- `level 2` 有 64 个槽位。

### span

某一层的每个槽位覆盖多少个基础 tick。

```text
span[0] = 1
span[level] = span[level - 1] * slotCount[level - 1]
```

当 `slotCounts = [256, 64, 64]`：

```text
span[0] = 1
span[1] = 256
span[2] = 256 * 64 = 16384
```

含义：

- `level 0` 每格 1 tick。
- `level 1` 每格 256 tick。
- `level 2` 每格 16384 tick。

### capacity

某一层转一整圈能覆盖多少个基础 tick。

```text
capacity[level] = span[level] * slotCount[level]
```

当 `slotCounts = [256, 64, 64]`：

```text
capacity[0] = 1 * 256 = 256
capacity[1] = 256 * 64 = 16384
capacity[2] = 16384 * 64 = 1048576
```

如果输入单位是真实秒，且 `tickUnit = 0.1f`，默认配置最大一圈约为：

```text
1048576 * 0.1 秒 = 104857.6 秒，约 29.1 小时
```

## 调度流程

一次普通延迟任务的生命周期：

1. 调用 `Schedule(delay, callback)`。
2. 把 `delay` 转为 `delayTicks`。
3. 计算绝对到期时间 `dueTick = currentTick + delayTicks`。
4. 根据 `dueTick - currentTick` 选择合适层级。
5. 根据 `dueTick` 和层级粒度计算槽位。
6. 随着 `Tick(deltaUnits)` 推进，任务从高层级逐步 cascade 到低层级。
7. 到达 `level 0` 的对应槽位后执行回调。
8. 一次性任务执行后回收到对象池；重复任务则重新计算下一次到期时间。

## currentTick 与 slot 的区别

`_currentTick` 是从调度器启动到现在已经走过的基础 tick 数，它是绝对时间坐标，应该单调递增。

`slot` 是某一层环形数组里的槽位下标，会通过取模回绕。

例如 `level 0` 有 256 个槽位：

```csharp
slot = currentTick % 256;
```

当：

```text
currentTick = 255 -> slot = 255
currentTick = 256 -> slot = 0
```

回绕的是槽位，不是 `_currentTick`。

## GetSlot 的含义

当前槽位计算：

```csharp
return (int)((tick / _levelSpans[level]) % _config.GetSlotCount(level));
```

它分两步：

1. `tick / span`：把绝对 tick 换算成当前层级下的逻辑格号。
2. `% slotCount`：把逻辑格号映射回环形数组下标。

`GetSlot(level, tick)` 不负责选择层级。它只回答一个问题：已知任务要放到这一层，那么它应该落在哪个槽位。

层级选择由插入逻辑根据剩余 tick 数决定。

## Cascade 推进

高层任务不会每个基础 tick 都被扫描。只有当当前 tick 走到某一层的边界时，该层才会被推进。

判断方式：

```csharp
_currentTick % _levelSpans[level] == 0
```

含义是：当前绝对 tick 正好落在这一层的大格边界上，可以把该层当前槽位中的任务重新分发到更低层或继续留在高层。

这和 `GetSlot()` 中的取模不是同一件事：

- `% _levelSpans[level]`：判断某一层是否到达推进边界。
- `% slotCount`：计算环形数组下标。

## 取消、暂停与恢复

`TimeWheelHandle` 是值类型句柄，包含：

- `SchedulerId`
- `TaskId`
- `Version`

调度器通过这些字段确认句柄是否属于当前调度器，以及是否仍然匹配当前任务版本。

### Cancel

`Cancel(handle)` 会把任务标记为取消，并减少活跃任务数。任务所在槽位不会立即被遍历删除，而是在之后处理槽位时被跳过并回收。

这种做法避免了取消任务时在桶链表里做额外查找。

### Pause

`Pause(handle)` 会记录剩余 tick：

```text
remainingTicks = dueTick - currentTick
```

然后让旧槽位中的任务版本失效。之后旧条目即使被扫描到，也不会被当作有效任务执行。

### Resume

`Resume(handle)` 会用保存的 `remainingTicks` 重新计算：

```text
dueTick = currentTick + remainingTicks
```

然后重新插入时间轮。

### Handle.IsValid

`Handle.IsValid` 只表示句柄字段不是无效默认值，不代表任务一定仍然存活。任务是否真的可操作，以 `Cancel`、`Pause`、`Resume` 的返回结果为准。

## 重复任务语义

`ScheduleRepeat(interval, callback, repeatCount)` 用于注册重复任务。

- `repeatCount = -1`：无限重复。
- `repeatCount = 0`：不创建任务，返回默认无效句柄。
- `repeatCount > 0`：执行指定次数。

重复任务在每次回调执行后，会基于当前执行 tick 加上 interval 重新安排下一次触发：

```text
nextDueTick = currentTick + intervalTicks
```

这意味着如果某一帧卡顿导致任务晚执行，下一次触发会从实际执行时刻重新计算，而不是强行追补到原始时间表。

## 回调执行注意事项

回调会在 `TimeWheelScheduler.Tick()` 内执行。对于 `TimeWheelManager` 来说，也就是在 Unity `Update()` 推进调度器时执行。

建议：

- 回调保持轻量，不做大量同步计算。
- 需要加载资源、创建大量对象、触发复杂流程时，考虑在回调中派发事件或提交请求。
- 回调中可以取消当前或其他任务，调度器会通过任务版本和取消标记处理。
- `callback` 不能为 `null`，否则注册时会抛出异常。

## 对象池依赖

`TimeWheelTask` 通过：

```csharp
PoolManager.Instance.GetClass<TimeWheelTask>()
```

获取任务对象，并在任务结束、取消后回收到对象池。

因此使用 `TimeWheel` 前，需要确保 WSFrame 的 `PoolManager` 已经完成初始化。

## ConfigProvider 与注册

`TimeWheelManager` 继承自 `AutoConfigSingletonMonoBase<TimeWheelManager, TimeWheelConfig>`，配置来源由 `TimeWheelManagerConfigProvider` 提供。

Provider 会把资源中的配置复制为运行时配置后注册：

```csharp
AutoSingletonConfigRegistry.Register<TimeWheelManager, TimeWheelConfig>(
    config.CreateRuntimeCopy());
```

这里使用运行时副本，是为了避免运行中修改配置时污染项目资产。

如果运行时已有活跃任务，再调用 `TimeWheelManager.Initialize(config)`，当前实现会忽略这次初始化并输出警告，避免重建调度器导致任务丢失。

## 边界行为

- `delay <= 0`：安排到下一个 tick 触发，不会立即同步执行。
- `interval <= 0`：重复任务仍会被转换为至少 1 tick 间隔。
- `repeatCount = 0`：不创建任务。
- `Clear()`：清空所有桶、释放任务、重置 `_currentTick` 和累计时间。
- `Cancel()`、`Pause()`、`Resume()`：返回 `false` 表示句柄无效、任务不存在、版本不匹配或状态不允许。
- 超过最高层 capacity 的长延迟任务不会丢失，会放入最高层，并在 cascade 时重新插入，直到足够接近后下沉到低层。
- `ActiveTaskCount` 表示当前认为仍活跃的任务数量，不等同于桶中节点数量，因为取消和暂停采用延迟清理。

## 推荐配置

通用默认：

```csharp
tickUnit = 0.1f;
slotCounts = new List<int> { 256, 64, 64 };
maxCatchUpTicksPerFrame = 100;
```

适合大多数短期和中期延迟任务。如果输入单位是真实秒，覆盖约 29.1 小时。

更精细的短倒计时：

```csharp
tickUnit = 0.05f;
slotCounts = new List<int> { 256, 64, 64 };
```

触发更细，但 Tick 推进次数更多。

粗粒度后台任务：

```csharp
tickUnit = 1f;
slotCounts = new List<int> { 256, 64, 64 };
```

适合粗粒度任务。如果输入单位是真实秒，则是秒级；如果输入单位是游戏分钟，则是分钟级。

配置选择原则：

- 先确定允许的触发误差，再决定 `tickUnit`。
- 先估算最大延迟，再决定 `slotCounts` 和层数。
- 不要为了极少数超长任务把默认时间轮做得过大；超长任务可以用更粗粒度的独立 scheduler。

## 常见误区

### 高层是低层后面的时间段

不是。高层和低层覆盖的是同一条绝对时间轴，只是粒度不同。

### currentTick 应该每转一圈清零

不是。`currentTick` 是绝对 tick 坐标，清零会破坏 `dueTick` 比较、层级边界判断和暂停恢复语义。

### GetSlot 同时负责选层

不是。选层由插入逻辑根据剩余 tick 决定。`GetSlot` 只负责在已选层级内计算槽位。

### tick = 300 在 level 1 应该进 slot 0

如果 `slotCounts = [256, 64, 64]`，则 `level 1` 每格覆盖 256 tick。`tick = 300` 属于 `[256, 511]`，所以在 `level 1` 的逻辑格 1，也就是 `slot 1`。

### TimeWheel 是精确到帧的更新器

不是。它是离散 tick 调度器。触发精度由 `tickUnit` 决定，不应该用它替代每帧更新逻辑。
