# Pooling 使用说明

本文档说明 `WSFrame` 对象池系统的使用方式、预热配置流程，以及当前设计中配置数据与运行时数据的职责划分。

## 总览

对象池系统由 `PoolManager` 作为统一入口，对外提供两类池：

- `GameObjectPoolModule`：管理预制体实例，使用资源 key 作为池标识。
- `ClassPoolModule`：管理普通 class 对象，使用 `Type` 作为池标识。

推荐业务代码只通过 `PoolManager.Instance` 使用对象池，不直接持有底层 Module。

## 初始化

`PoolManager.Initialize` 会创建 GameObject 池、class 池，并在初始化阶段应用全局预热配置。

```csharp
PoolManager.Instance.Initialize(poolingSetting);
```

如果没有显式传入资源加载器，会根据 `WSFrameSetting.PoolingSetting.ResLoadType` 创建默认加载器：

- `Resources`：使用 `ResourcesLoadMgrModule`
- `Addressable`：使用 `AddressablesLoadMgrModule`

初始化过程中会执行全局预热：

```text
PoolManager.Initialize
    -> 创建 PoolSystemRoot
    -> 创建 GameObjectPoolModule
    -> 创建 ClassPoolModule
    -> GlobalPoolPrewarmProcessor.SetConfig(...)
    -> GlobalPoolPrewarmProcessor.Apply(...)
```

## GameObject 池

GameObject 池通过资源 key 管理。key 通常是资源加载路径，例如 `Resources` 下的 `Cube` 或 `TestFolder/Cube1`。

### 预热

```csharp
PoolManager.Instance.Prewarm("TestFolder/Cube1", 5, 10);
```

参数含义：

- `key`：资源加载 key。
- `initCount`：预热后希望池中至少保留的可用对象数量，必须大于 0。
- `maxCapacity`：池最大容量，`-1` 表示不限容量。

如果 `initCount > maxCapacity` 且 `maxCapacity != -1`，预热会被判定为无效。

首次通过 `Get` 创建的池默认是无限容量。如果某个池需要固定容量，应先调用 `Prewarm`，再使用 `Get`。

`Prewarm` 是补足到目标数量，而不是每次追加 `initCount` 个对象。重复预热同一个池时，如果池中可用对象数量已经达到 `initCount`，会直接跳过；如果不足，只会创建缺少的数量。已有池的容量只会扩大，不会因为后续传入更小的 `maxCapacity` 而缩小。

当调用方已经持有可作为模板的 `GameObject`，并且不希望通过资源 key 加载时，也可以直接传入 `GameObject` 预热：

```csharp
PoolManager.Instance.Prewarm(prefabGameObject, 5, 10);
```

该重载会优先使用 `PoolObjectIdentity.PoolKey` 作为池 key，没有标记时使用 `prefabGameObject.name`。需要补充对象时，传入的 `prefabGameObject` 会被标记 `PoolObjectIdentity` 并作为第一个对象放入池中，剩余对象通过 `Instantiate(prefabGameObject, poolRootTransform, false)` 补足到 `initCount`。如果已有可用对象数量已经满足 `initCount`，不会重复把同一个 `GameObject` 放入池中。`initCount` 和 `maxCapacity` 的校验规则与按 key 预热一致。

### 异步预热

```csharp
await PoolManager.Instance.PrewarmAsync("TestFolder/Cube1", 5, 10);
```

也可以传入完成回调：

```csharp
await PoolManager.Instance.PrewarmAsync("TestFolder/Cube1", 5, 10, success =>
{
    Debug.Log(success);
});
```

### 获取

```csharp
GameObject obj = PoolManager.Instance.Get("TestFolder/Cube1");
```

也可以按类型获取，此时会使用类型名作为 key：

```csharp
GameObject obj = PoolManager.Instance.Get<MyPoolableView>();
```

该方式要求资源 key 与类型名一致。

批量获取：

```csharp
List<GameObject> objs = PoolManager.Instance.GetSome("TestFolder/Cube1", 10);
```

异步获取：

```csharp
GameObject obj = await PoolManager.Instance.GetAsync("TestFolder/Cube1");
```

回调式异步获取：

```csharp
PoolManager.Instance.GetAsync("TestFolder/Cube1", parent, obj =>
{
    if (obj == null) return;
});
```

### 回收

推荐直接回收对象实例：

```csharp
PoolManager.Instance.Recycle(obj);
```

池会通过 `PoolObjectIdentity` 找回对象所属的池 key。对象由池创建或预热时，会自动挂载并写入 `PoolObjectIdentity`。

也可以显式指定 key：

```csharp
PoolManager.Instance.Recycle("TestFolder/Cube1", obj);
```

批量回收：

```csharp
PoolManager.Instance.RecycleSome(objs);
```

## Class 池

Class 池用于普通 C# class，不适用于 `MonoBehaviour`。对象通过 `new()` 创建，因此使用的类型需要有无参构造。

### 预热

```csharp
PoolManager.Instance.PrewarmClass<MyData>(20, 100);
```

`maxCapacity = -1` 表示不限容量。

### 获取与回收

```csharp
MyData data = PoolManager.Instance.GetClass<MyData>();

PoolManager.Instance.RecycleClass(data);
```

如果对象实现了 `IPoolable`：

- 获取时会调用 `OnSpawn`
- 回收时会调用 `OnDespawn`
- 首次通过 `GetClass<T>` 创建池时，可以读取 `InitCount` 和 `MaxCount` 作为默认配置

显式调用 `PrewarmClass<T>` 时，传入的 `maxCapacity` 优先级高于 `IPoolable.MaxCount`。

## 全局预热

全局预热用于“项目启动时固定需要准备”的对象池。它由两部分来源合并：

- `PoolPrewarmConfig`：ScriptableObject 配置资源，只保存数据。
- `CodePoolPrewarmConfigModule`：代码侧固定配置入口。

实际预热逻辑由 `GlobalPoolPrewarmProcessor` 执行，执行时机由 `PoolManager.Initialize` 管理。

### ScriptableObject 配置

`PoolPrewarmConfig` 只作为数据资源，不执行预热逻辑。

GameObject 配置项包含：

- `enable`：是否启用
- `key`：资源加载 key
- `initCount`：预热数量
- `maxCapacity`：最大容量，`-1` 表示不限容量

Class 配置项包含：

- `enable`：是否启用
- `classId`：由生成表提供的稳定类型 ID
- `displayName`：仅用于 Inspector 显示
- `initCount`
- `maxCapacity`

`WSFrameSetting.PoolingSetting` 中只保存全局 `PoolPrewarmConfig` 的引用，不直接保存预热列表，也不执行预热逻辑。

### 代码配置

固定代码配置写在 `CodePoolPrewarmConfigModule.Collect` 中：

```csharp
public sealed class CodePoolPrewarmConfigModule
{
    public void Collect(CodePoolPrewarmConfigBuilder builder)
    {
        if (builder == null) return;

        builder.GameObject("Cube", 10, 20);
        builder.Class(typeof(MyData), 20, 100);
        builder.Class<MyOtherData>(5, -1);
    }
}
```

代码配置会在 `PoolManager.Initialize` 期间被读取一次。初始化结束后，Processor 会释放临时配置引用。

运行过程中临时预热请使用：

```csharp
PoolManager.Instance.Prewarm("Cube", 10, 20);
PoolManager.Instance.PrewarmClass<MyData>(20, 100);
```

不要在运行中继续向全局配置模块追加数据。

## 合并规则

当 SO 配置和代码配置中出现相同池时，Processor 会合并配置。

GameObject 池按 `key` 合并：

- `initCount` 取更大值
- `maxCapacity` 取更大值
- 任意一方 `maxCapacity = -1` 时，合并结果为 `-1`

Class 池按 `Type` 合并：

- `initCount` 取更大值
- `maxCapacity` 取更大值
- 任意一方 `maxCapacity = -1` 时，合并结果为 `-1`
- `Apply` 委托优先保留已有值，缺失时从 Registry 或代码配置补齐

## 配置数据与运行时数据

配置类只表达“用户配置了什么”，不作为运行时合并结果使用。

- `GameObjectPoolPrewarmItem`：SO 中的 GameObject 配置数据。
- `ClassPoolPrewarmItem`：SO 中的 class 配置数据。
- `GameObjectPoolPrewarmRequest`：运行时 GameObject 预热请求。
- `ClassPoolPrewarmRequest`：运行时 class 预热请求。

这样可以避免配置层数据侵入运行时逻辑。Processor 会把 SO 配置和代码配置都转换成运行时 Request，再进行合并和应用。

## Class Registry

Class 的 SO 配置不直接保存字符串类型名，也不在运行时使用 `Type.GetType`。

Class 下拉选项来自生成的 `ClassPoolPrewarmRegistry`：

```text
Tools/WSFrame/Pooling/Generate Class Pool Prewarm Registry
```

生成表会提供：

- `ClassPoolPrewarmId`
- `Type`
- Inspector 显示名
- 强类型预热委托

当新增、删除、移动或重命名可预热 class 后，需要重新生成 Registry。

运行时 Apply 不做字符串类型解析，也不通过 `GetMethod`、`MakeGenericMethod`、`Invoke` 调用泛型方法。

## 文件职责

Pooling 文件夹按职责划分：

- `Core`：对象池核心能力，包括 `PoolManager`、`GameObjectPoolModule`、`ClassPoolModule` 和池数据结构。
- `Config`：SO 配置数据和代码侧配置入口。
- `Runtime`：全局预热 Processor 和运行时预热 Request。
- `Editor`：Registry 生成工具。
- `Generated`：生成的 class 预热 Registry。

## 清理

清理指定 GameObject 池：

```csharp
PoolManager.Instance.ClearPool("Cube");
```

清理指定 class 池：

```csharp
PoolManager.Instance.ClearClassPool<MyData>();
```

清理全部池：

```csharp
PoolManager.Instance.ClearAll();
```

## 使用建议

- 高频使用的对象建议在初始化阶段全局预热。
- 只在某个玩法或界面中使用的对象，建议进入对应流程时调用运行时 `Prewarm`。
- 需要容量限制的 GameObject 池必须先预热，否则首次 `Get` 创建的池默认无限容量。
- 已经持有模板对象且不需要资源加载时，可以使用 `PoolManager.Instance.Prewarm(GameObject, initCount, maxCapacity)`；否则优先使用资源 key 预热。
- GameObject 回收优先使用 `Recycle(GameObject)`，让 `PoolObjectIdentity` 自动定位池。
- Class 池对象如果有状态，建议实现 `IPoolable` 并在 `OnSpawn` / `OnDespawn` 中重置。
