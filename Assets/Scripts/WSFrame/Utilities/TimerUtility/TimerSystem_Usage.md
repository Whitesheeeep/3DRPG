# Timer System 使用说明

`TimerManager` 是一个全局计时器系统，用于替代简单延迟、循环 Tick、UI 倒计时、技能冷却等场景。当前实现按 Timer 类型分两条调度路径：

- 无 `OnUpdate` 的 Timer 进入最小堆，只在到期时处理。
- 设置了 `OnUpdate` 的 Timer 进入逐帧列表，用于进度回调。

业务层不直接持有 `Timer`，而是持有 `TimerHandle`。`TimerHandle` 内部通过 `TimerManager + TimerId` 校验有效性，Timer 被回收后旧 Handle 不会误操作新 Timer。

## 快速开始

### 一次性延迟

```csharp
TimerManager.Register(2f, () =>
{
    Debug.Log("2 秒后执行");
});
```

### 循环指定次数

`SetLoop(count)` 表示完成回调总共触发 `count` 次。每次计时结束触发一次 `onComplete`，直到次数耗尽后自动回收。

```csharp
TimerManager.Register(1f, () => Debug.Log("Tick"))
    .SetLoop(3); // 第 1、2、3 秒各触发一次，然后结束
```

无限循环使用 `-1`：

```csharp
TimerManager.Register(1f, () => Debug.Log("Forever"))
    .SetLoop(-1);
```

### 进度回调

设置 `OnUpdate` 后 Timer 会进入逐帧更新列表，`progress` 范围是 `0 ~ 1`。

```csharp
TimerManager.Register(3f, () => Debug.Log("Done"))
    .OnUpdate(progress =>
    {
        progressBar.fillAmount = progress;
    });
```

### 不受 Time.timeScale 影响

UI 动画、加载提示、暂停菜单倒计时通常使用 unscaled time。

```csharp
TimerManager.Register(0.5f, OnAnimComplete)
    .SetUnscaledTime(true);
```

### 局部时间缩放

`SetTimeScale` 只影响当前 Timer，不修改 Unity 的 `Time.timeScale`。

```csharp
TimerManager.Register(5f, () => Debug.Log("Fast"))
    .SetTimeScale(2f); // 以 2 倍速度推进

TimerManager.Register(5f, () => Debug.Log("Slow"))
    .SetTimeScale(0.5f); // 以 0.5 倍速度推进
```

`SetTimeScale(0f)` 会冻结该 Timer 的推进，后续设回大于 0 的值会继续。

## TimerHandle

`Register` 返回 `TimerHandle`，可以链式配置，也可以保存起来做后续控制。

```csharp
private TimerHandle _cooldown;

private void StartCooldown()
{
    _cooldown = TimerManager.Register(5f, OnCooldownFinished)
        .SetTag(TimerManager.TimerTags.Test)
        .OnUpdate(progress => Debug.Log($"CD: {progress:P0}"));
}

private void PauseCooldown()
{
    if (_cooldown.IsValid)
    {
        _cooldown.Pause();
    }
}
```

常用属性：

- `IsValid`：Timer 是否仍由 Manager 管理。
- `Duration`：总时长。
- `TimeElapsed`：当前已推进时间。
- `TimeRemaining`：剩余时间。
- `Progress`：当前进度，范围 `0 ~ 1`。

注意：`Progress` 只是查询属性，不会让 Timer 主动逐帧通知外界。没有设置 `OnUpdate` 的 Timer 会走最小堆调度，只在到期时执行完成回调；如果要驱动进度条、冷却 UI 或其它实时显示，应注册时设置 `OnUpdate`。

常用控制：

- `Pause()` / `Resume()`
- `Cancel()`
- `ResetTime()` / `ResetTime(newDuration)`
- `SetTag(tag)`
- `SetLoop(count)`
- `SetUnscaledTime(true)`
- `SetTimeScale(scale)`
- `OnUpdate(callback)`

## Tag 批量控制

`TimerTags` 使用 `[Flags]`，一个 Timer 可以拥有多个标签。

```csharp
TimerManager.Register(10f, OnComplete)
    .SetTag(TimerManager.TimerTags.Test);
```

批量操作：

```csharp
TimerManager.PauseByTag(TimerManager.TimerTags.Test);
TimerManager.ResumeByTag(TimerManager.TimerTags.Test);
TimerManager.CancelByTag(TimerManager.TimerTags.Test);
TimerManager.SetTimeScaleByTag(TimerManager.TimerTags.Test, 0.5f);
```

当前项目内置标签：

```csharp
[Flags]
public enum TimerTags
{
    None = 0,
    Test = 1 << 0,
    All = ~0
}
```

需要更多业务标签时，可以继续扩展 `TimerTags`，例如 `UI`、`Battle`、`Buff`、`Cooldown`。

## 调度模式

内部 `TimerScheduleMode` 用于描述 Timer 当前在哪条调度路径上：

- `Pending`：刚注册后暂存一帧，等待链式配置完成。
- `UpdateList`：设置了 `OnUpdate`，逐帧推进。
- `HeapScaled`：无 `OnUpdate`，使用 `Time.time` 的最小堆。
- `HeapUnscaled`：无 `OnUpdate`，使用 `Time.unscaledTime` 的最小堆。
- `Paused`：暂停，不在调度容器中。
- `Executing`：正在执行完成回调。
- `Detached`：容器迁移中的临时状态。
- `Recycled`：已回收。

## 适用场景

`TimerManager` 适合处理“到时间再执行”或“低频逻辑 Tick”的任务：

- 延迟执行：例如 0.5 秒后关闭提示、3 秒后销毁临时特效、1 秒后恢复按钮点击。
- 冷却和倒计时：例如技能 CD、商店刷新倒计时、制作剩余时间。
- 低频轮询：例如每 0.2 秒检查附近交互物、每 1 秒刷新状态提示、每 5 秒自动保存。
- 周期性规则结算：例如中毒每 1 秒掉血、回血光环每 0.5 秒结算、农作物定时成长。
- 临时状态到期：例如 Buff 持续时间、无敌帧、交互锁定、提示高亮自动消失。

如果只需要到期回调，不要设置 `OnUpdate`，让 Timer 走最小堆调度即可。只有需要实时进度显示时，才使用 `OnUpdate`。

## 不适用场景

以下场景通常不建议用 `TimerManager` 替代 `Update`、Tween、动画系统或物理系统：

- 跟手输入：例如拖拽物体跟随鼠标、镜头跟随鼠标、摇杆持续移动。
- 连续视觉运动：例如平滑滚动、拖拽边缘滚动、UI 跟随、镜头平滑插值。
- 物理相关逻辑：例如刚体移动、力、碰撞相关判断，应使用 Unity 物理生命周期。
- 每帧精确输入状态：例如鼠标按住拖动、按键持续输入、悬停实时反馈。

这类逻辑追求连续反馈和低延迟，使用低频 Timer 会产生阶梯感或响应延迟。

## 低频 Timer 与 DeltaTime

低频 Timer 的回调仍然是在 Unity 某一帧中执行的，因此 `Time.deltaTime` / `Time.unscaledDeltaTime` 表示的是“当前帧间隔”，不是“距离上次 Timer 回调的间隔”。

例如每 `0.1f` 秒执行一次滚动：

```csharp
TimerManager.Register(0.1f, OnTick)
    .SetLoop(-1)
    .SetUnscaledTime(true);
```

如果 `OnTick` 中使用 `Time.unscaledDeltaTime`：

```csharp
scrollY += speed * Time.unscaledDeltaTime;
```

在 60 FPS 下，每次 Tick 只会乘以约 `0.016f`，而不是 `0.1f`，实际速度会明显变慢。低频 Tick 应使用已知间隔或自行记录上次触发时间：

```csharp
const float interval = 0.1f;

TimerManager.Register(interval, () =>
{
    scrollY += speed * interval;
})
    .SetLoop(-1)
    .SetUnscaledTime(true);
```

如果目标是平滑、跟手、连续的视觉运动，优先使用 `Update` 或 Tween，而不是低频 Timer。

## 注意事项

1. `OnUpdate` 会让 Timer 进入逐帧列表，因此只在确实需要进度时使用。
2. 回调中可以 `Cancel`、`ResetTime`、`Pause`，系统会根据当前调度状态处理。
3. 场景切换时 `TimerManager` 通过 `SceneSystem.RegisterLoadStarted(...)` 监听加载开始并调用 `CancelAll`；内部保存 `IUnRegister` 并在销毁时注销，避免旧场景回调继续执行。
4. 如果业务对象销毁时仍持有 `TimerHandle`，建议主动调用 `Cancel()`。
5. `TimerHandle` 是值类型，可以保存；但不要假设旧 Handle 永远有效，操作前可检查 `IsValid`。
