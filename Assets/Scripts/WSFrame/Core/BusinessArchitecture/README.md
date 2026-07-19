# WSFrame 业务架构说明

这是一个面向 WSFrame 业务层的 QFramework 风格架构模块。

## 边界

- WSFrame 继续负责运行时基础设施，例如 `UIManager`、`PoolManager`、`ResSystem`、`AudioManager`、`SceneSystem`、`ConfigInstaller` 和全局 `EventSystem`。
- 业务架构负责 `IOC`、`Manager`、`System`、`Command`、`Query` 和 `Utility` 的注册与访问。
- 界面层仍然采用 MVVM。`ViewModel` 不需要实现 Controller 接口。

## 命名

- `Data`、`State`、`Record` 是纯数据结构。
- `Manager` 负责业务数据、修改 API、校验逻辑和数据变化事件。
- `System` 负责协调业务流程，尤其是跨多个 `Manager` 的流程。
- `Command` 表示一次有顺序的写操作。
- `Query` 表示一次只读的业务查询。
- `ViewData` 是只用于界面渲染的展示数据。

## 流程

```text
界面事件
-> ViewModel 意图方法
-> Command
-> System
-> Manager 修改数据
-> WSFrame 事件 / Manager 数据变化事件
-> ViewModel 重建 ViewData
-> 界面刷新
```

事件适合做通知和频繁的状态变化同步。不要把核心的有序业务流程拆散到多个事件监听器里。

## 能力接口与扩展方法

`ICanGetManager`、`ICanSendCommand`、`ICanSendQuery` 等接口表示一个业务对象拥有的能力。

扩展方法负责提供接近 QFramework 的调用手感：

```csharp
this.GetManager<IInventoryManager>();
this.SendCommand(new BuyItemCommand(itemId));
this.SendQuery(new CanBuyItemQuery(itemId));
```

这样可以让 API 保持收窄。比如 `Query` 可以读取 `Manager` 和 `System`，但不会因为继承了某个大基类而获得无关能力。每个 `Manager`、`System`、`Command`、`Query` 只声明自己需要的能力。

## IOC 容器范围

IOC 容器只保存长期存在的业务模块实例：

- `Manager`
- `System`
- `Utility`

这些注册对象在一个 `Architecture` 生命周期内等价于单例实例。`Command` 和 `Query` 不注册进 IOC；它们是操作对象，在调用处创建，然后传给 `SendCommand` 或 `SendQuery`。

普通运行时对象应通过 `Factory` 或 `Utility` 创建，不直接注册进 IOC。第一版 IOC 保持为实例注册表，不承担短生命周期对象工厂职责。

## 命令与事件

`Command` 用于有顺序的核心业务流程：

```text
购买物品
-> 校验商店物品
-> 校验货币
-> 校验背包空间
-> 扣除货币
-> 添加物品
-> 标记商店状态
-> 发送购买完成事件
```

`Event` 用于已经发生的事实、通知和频繁状态变化。界面刷新、音效、红点、成就、日志和引导响应都适合作为事件监听者。

不要通过拆分事件监听器来构建核心有序流程。`Command` 或 `System` 应先完成必要的业务修改，再发布事件。
