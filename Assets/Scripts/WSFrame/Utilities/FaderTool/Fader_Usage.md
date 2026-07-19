# AsyncFader vs ValueFader 使用指南

本文档旨在说明 `AsyncFader`（静态工具）与 `ValueFader`（状态对象）的区别、使用场景以及性能考量。

---

## 1. AsyncFader (静态工具类)

`AsyncFader` 是一个轻量级的静态工具类，提供单纯的**异步渐变函数**。它不保存任何状态，只负责在一段时间内根据曲线插值并回调。

### 特点
- **无状态**：不需要实例化，直接调用静态方法。
- **Fire-and-Forget**：适合一次性的渐变任务。
- **手动管理生命周期**：如果需要中途取消，必须由调用者自己传递并管理 `CancellationToken`。

### 代码示例
```csharp
// 简单的透明度渐变，无需保存状态
private async void FadeOut()
{
    await AsyncFader.FadeFloatAsync(
        startValue: 1f, 
        endValue: 0f, 
        duration: 0.5f, 
        onUpdate: (val) => canvasGroup.alpha = val
    );
}

// 如果需要手动取消，比较麻烦
private CancellationTokenSource _cts;
private async void FadeWithCancel()
{
    _cts?.Cancel();
    _cts = new CancellationTokenSource();
    
    try 
    {
        await AsyncFader.FadeFloatAsync(..., token: _cts.Token);
    }
    catch (OperationCanceledException) { }
}
```

---

## 2. ValueFader (状态型渐变器)

`ValueFader` 是一个**类（Class）**，设计用于**长期持有**并**管理**某个数值的渐变状态。它内部封装了 `currentValue` 和 `CancellationTokenSource`。

### 特点
- **自带状态**：记住当前的数值（`Value`），下次渐变可以自动从当前值开始（无需手动传 startValue）。
- **自动打断**：当调用新的 `.To()` 方法时，会自动取消上一次正在进行的渐变，**非常适合防止动画冲突**。
- **配置化**：可以序列化 `AnimationCurve`，方便在 Inspector 中调整曲线。

### 代码示例
```csharp
public class UIWindow : MonoBehaviour
{
    // 实例化一个渐变器，专门管理 Alpha 值
    private ValueFader _alphaFader = new ValueFader(0f); 

    public void Show()
    {
        // 自动打断之前的 Hide 动画，从当前 alpha 渐变到 1
        _alphaFader.To(1f, 0.5f, val => canvasGroup.alpha = val).Forget();
    }

    public void Hide()
    {
        // 自动打断之前的 Show 动画，从当前 alpha 渐变到 0
        _alphaFader.To(0f, 0.5f, val => canvasGroup.alpha = val).Forget();
    }
}
```

---

## 3. 核心对比

| 特性 | AsyncFader (静态) | ValueFader (实例) |
| :--- | :--- | :--- |
| **定位** | 通用工具函数 | 专用数值管理器 |
| **内存开销** | 极低 (仅 `UniTask` 结构体和闭包) | 低 (每个实例占用少量堆内存) |
| **状态管理** | 无 (无记忆) | 有 (记忆当前值) |
| **并发处理** | 并发调用会产生冲突 (多个 tween 同时改一个值) | **自动互斥** (新任务自动 Cancel 旧任务) |
| **中断方式** | 需手动管理 `CancellationToken` | 内部自动管理，调用即打断 |
| **使用便利性** | 适合一次性、非频繁打断的任务 | 适合频繁交互、需要平滑过渡的 UI/数值 |

---

## 4. 性能与选型建议

### 性能考量
1.  **GC (垃圾回收)**：
    *   **AsyncFader**：极优。主要开销在于 `UniTask` 的状态机和 lambda 闭包（如果有捕获变量）。
    *   **ValueFader**：作为类对象，实例化时会分配堆内存。但通常作为成员变量**复用**，因此运行时 GC 开销极低。内部创建 `CancellationTokenSource` 会产生少量 GC，但已做优化（复用检查）。

2.  **CPU 开销**：
    *   二者核心都是 `UniTask.Yield` 和简单的数学计算 (`Mathf.Lerp` / `Curve.Evaluate`)，CPU 消耗差异可忽略不计。

### 什么时候用哪个？

*   👉 **请使用 `ValueFader`，如果...**
    *   你需要**频繁打断**动画（例如：玩家快速反复开关背包，UI 需要在 0~1 之间平滑折返，不能突变）。
    *   你需要**从当前值**开始渐变，而不是每次都硬编码 `startValue`。
    *   你需要管理 UI 组件的属性（Color, Alpha, Scale, Position）。

*   👉 **请使用 `AsyncFader`，如果...**
    *   这是一次性的逻辑（例如：过关后延迟 2 秒淡出转场，之后场景就销毁了）。
    *   你不需要打断它，或者你有一套很复杂的外部状态机来管理 Token。
    *   你在写编辑器工具或非 MonoBehaviour 逻辑，只需要一个简单的 `Lerp`。

### 总结
**在 UI 开发中，90% 的情况建议使用 `ValueFader`**。因为它完美解决了“动画未播放完又收到反向指令”导致的闪烁或数值跳变问题。

