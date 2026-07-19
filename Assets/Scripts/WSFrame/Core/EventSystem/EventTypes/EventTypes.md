# 事件类型

## 设计目标

事件类型拆分的目标不是单纯把文件分散，而是让事件系统在后续持续扩展时仍然保持：
- 定义位置清晰
- 命名语义明确
- int 事件 key 不冲突
- 发布者 / 订阅者更容易搜索和定位
- 核心机制层不被业务事件定义污染

当前推荐结构是：
- `Core/`：事件系统机制、接口、事件中心实现
- `EventTypes/`：事件类型定义、号段定义、说明文档

---

## 字符串事件类型

字符串事件类型请在 `StringEventDefinitions.cs` 文件中定义。

当前示例：
- `StringEvent.TestEvent`

建议约定：
- 字符串事件统一集中在 `StringEventDefinitions.cs` 中维护
- 事件名尽量按模块或语义命名，避免无语义的临时字符串扩散
- 不要在业务代码中直接散写裸字符串 key
- 后续如果字符串事件继续膨胀，可以进一步按模块拆分为多个定义文件

推荐命名方式：
- `UI_OpenPanel`
- `Audio_BgmChanged`
- `Scene_LoadCompleted`

如果后续字符串事件继续增加，建议按模块拆分：
- `InputStringEventDefinitions.cs`
- `UIStringEventDefinitions.cs`
- `AudioStringEventDefinitions.cs`
- `SceneStringEventDefinitions.cs`

---

## 枚举事件类型 / int 事件类型

枚举事件类型 / int 事件类型请在 `IntEventDefinitions.cs` 文件中定义。

### int 事件号段管理

int 事件的起止范围统一在 `EventIdRanges.cs` 中登记，而不是直接在各个枚举中写裸数字。

当前规则：
- 每个模块在 `EventIdRanges.cs` 中维护自己的 `Start` / `End`
- 枚举中的 `start` 统一引用对应模块的 `Start`
- 事件成员从 `start + 1` 开始递增
- `end` 仅作为当前模块的结束标记使用
- `EventIdRanges` 不反向依赖具体事件枚举的 `end`

示例：

```csharp
public enum EventIdRange
{
    TestStart = 0,
    TestEnd = TestStart + 100,

    InputStart = TestEnd,
    InputEnd = InputStart + 100,
}

public enum E_InputEvent
{
    start = EventIdRange.InputStart,
    OnKeyDown = start + 1,
    OnKeyUp,
    end,
}
```

### 为什么这样做

这样做的好处：
- 避免不同模块的 int 事件 key 重复
- 统一管理号段，后续扩展时更清晰
- 模块枚举不直接互相依赖，降低耦合
- 搜索发布者 / 订阅者时，更容易按模块定位事件来源
- 修改某个模块的事件时，不需要回头翻查其他模块定义文件

### 后续新增事件时的约定

新增 int 事件时请按以下顺序处理：
1. 先在 `EventIdRanges.cs` 中登记新模块的 `Start` / `End`
2. 再在 `IntEventDefinitions.cs` 或对应模块事件定义文件中新增枚举
3. 不要在枚举 `start` 上直接写裸数字
4. 不要让 `EventIdRanges` 反向依赖具体枚举的 `end`
5. 如果某个模块事件过多，优先拆分独立模块定义文件，而不是继续把所有枚举堆在同一文件里

---

## 推荐扩展方式

当事件量继续增长时，建议采用“按模块拆分”的方式维护，而不是继续扩展一个总表。

推荐形式：
- `InputIntEventDefinitions.cs`
- `UIIntEventDefinitions.cs`
- `AudioIntEventDefinitions.cs`
- `SceneIntEventDefinitions.cs`

字符串事件同理：
- `InputStringEventDefinitions.cs`
- `UIStringEventDefinitions.cs`
- `AudioStringEventDefinitions.cs`
- `SceneStringEventDefinitions.cs`

拆分原则：
- 同一模块内的事件定义放在一起
- 定义文件只负责声明，不混入事件业务逻辑
- `EventSystem.cs` 只保留事件机制，不回收这些定义

---

## 发布者 / 订阅者搜索约定

为了让 `EventSystemView` 后续更容易搜索事件调用，建议保持统一调用入口，不要在外部再包过多层自定义转发。

当前搜索应优先围绕以下入口：

### 字符串事件
- 注册：`Register_String<T>(...)`
- 注销：`UnRegister<T>(...)`
- 触发：`EventTrigger_String<T>(...)`

### int 事件
- 注册：`Register_Int<T>(...)`
- 注销：`UnRegister_Int<T>(...)`
- 触发：`EventTrigger_Int<T>(...)`

### Type 事件
- 注册：`Register_Type<T>(...)`
- 注销：`UnRegister_Type<T>(...)`
- 触发：`EventTrigger_Type<T>(...)`

推荐搜索策略：
- 搜发布者：优先搜 `EventTrigger_*`
- 搜订阅者：优先搜 `Register_*`
- 搜具体事件：优先搜事件常量名 / 枚举名，而不是只搜字符串字面量

例如：
- 搜 `StringEvent.TestEvent`
- 搜 `E_InputEvent.OnKeyDown`
- 再结合 `Register_*` / `EventTrigger_*` 判断是订阅还是发布

这样做的好处：
- 降低误报
- 更容易从事件定义反查使用方
- 后续给 `EventSystemView` 做静态分析时规则更统一

---

## 维护注意事项

- 不要把事件定义重新塞回 `EventSystem.cs`
- 不要在业务代码里直接散写大量裸字符串事件名
- 不要在多个文件里重复定义相同语义的事件 key
- 不要让 int 事件枚举直接互相引用 `end` 来串联模块范围
- 不要让 `EventIdRanges` 去读取具体枚举的 `end`
- 如果某类事件已经明显属于某个模块，就直接放入该模块的定义文件，不要先堆到通用文件里

---

## 当前状态总结

当前已经完成：
- `StringEvent` 已拆到 `StringEventDefinitions.cs`
- `E_TestEvent` / `E_InputEvent` 已拆到 `IntEventDefinitions.cs`
- int 事件号段已集中到 `EventIdRanges.cs`
- `EventSystem` 核心机制与事件定义已开始解耦

当前推荐方向：
- 继续维持 `Core` 与 `EventTypes` 分层
- 事件增加时优先按模块拆分
- 搜索发布者 / 订阅者时统一围绕 `Register_*` / `EventTrigger_*` 入口

> [!tip]
> 当前推荐方向是：事件系统机制放在 `Core`，事件定义放在 `EventTypes`，后续若事件继续增加，可以按模块继续拆分定义文件，而不是把所有事件堆回一个总表。
