# 资源加载系统说明文档

## 1. 简介 (Introduction)
本系统提供了一套统一的资源加载接口 `IResLoad<TKey>`，旨在屏蔽底层加载方式（Resources / Addressables）的差异，同时无需修改业务逻辑代码即可切换加载策略。系统集成了引用计数管理、自动去重、异步等待（UniTask）等功能，极大简化了资源管理流程。

## 2. 核心接口 (Core Interface)

所有加载模块均实现 `IResLoad<TKey>` 接口：

```csharp
public interface IResLoad<TKey> where TKey : notnull
{
    // 同步加载
    public T Load<T>(TKey key) where T: UnityEngine.Object;
    
    // 异步加载 (Callback)
    public void LoadAsync<T>(TKey key, UnityAction<T> callback) where T : UnityEngine.Object;
    
    // 异步加载 (UniTask - 推荐)
    public UniTask<T> LoadAsync<T>(TKey key) where T : UnityEngine.Object;
    
    // 批量异步加载 (Callback)
    // 注意：Callback 会对加载到的每个资源调用一次，而不是一次性返回列表
    public void LoadAssetsAsync<T>(UnityAction<T> callback, params TKey[] keys) where T : UnityEngine.Object;

    // 批量异步加载 (UniTask)
    // 返回所有加载资源的列表
    public UniTask<IList<T>> LoadAssetsAsync<T>(params TKey[] keys) where T : UnityEngine.Object;
    
    // 卸载单个资源
    // deleteImmediately: 是否立即释放（默认 true）。若为 false，引用计数归零后不释放底层句柄，下次加载可复用。
    public void UnLoad<T>(string key, bool deleteImmediately = true) where T : UnityEngine.Object;

    // 异步卸载单个资源（带回调）
    public void UnLoadAsync<T>(string key, UnityAction<T> callback, bool deleteImmediately = true) where T : UnityEngine.Object;

    // 批量卸载资源
    public void UnLoadAssets<T>(bool deleteImmediately = true, params string[] keys) where T : UnityEngine.Object;
    
    // 批量卸载资源（带回调）
    public void UnLoadAssetsAsync<T>(UnityAction<T> callback, bool deleteImmediately = true, params string[] keys) where T : UnityEngine.Object;

    // 卸载所加载资源
    public void UnLoadAll();
}
```

## 3. 模块详解 (Modules)

### 3.1 ResourcesLoadMgrModule (传统加载)
基于 `UnityEngine.Resources` API 的封装。
- **Key 类型**: `string` (资源在 Resources 文件夹下的相对路径)。
- **特点**:
  - **引用计数**: 对同一路径、同一类型的资源进行引用计数管理。
  - **异步取消**: 支持异步加载资源的取消操作（销毁刚加载出的资源）。
  - **路径规范化**: 自动处理路径中的斜杠差异。

### 3.2 AddressablesLoadMgrModule (可寻址资源)
基于 Unity `Addressables` Package 的封装。
- **Key 类型**: `string` (Addressable Name / Label / Key)。
- **特点**:
  - **原生异步**: 更加适合现代异步资源流程。
  - **同步兼容**: 提供 `WaitForCompletion` 实现的同步加载（注意：可能会导致性能波动）。
  - **批量加载**: 
    - Callback 方式：对每个资源逐一回调。
    - UniTask 方式：等待所有资源加载完成后返回列表。
    - 支持通过 Label 加载一组资源，并支持 MergeMode（并集/交集）。
  - **健壮性**: 处理了加载失败重试、完成状态校验、并在卸载时自动清理挂起的回调。
  - **缓存保留**: 支持 `deleteImmediately = false`，允许在引用计数归零时保留底层 Handle，避免频繁创建销毁带来的开销。

## 4. 使用示例 (Usage Examples)

### 4.1 初始化
根据需求选择实例化对应的模块：

```csharp
IResLoad<string> resLoader;

// 方案 A: 使用 Addressables
resLoader = new AddressablesLoadMgrModule();

// 方案 B: 使用 Resources
resLoader = new ResourcesLoadMgrModule();
```

### 4.2 单个资源加载

#### 异步加载 (推荐使用 UniTask)
```csharp
// 加载 GameObject
GameObject prefab = await resLoader.LoadAsync<GameObject>("Prefabs/MyCube");

// 加载 AudioClip
AudioClip clip = await resLoader.LoadAsync<AudioClip>("Audio/BGM01");
```

#### 回调方式
```csharp
resLoader.LoadAsync<Texture2D>("Textures/Logo", (tex) => {
    if (tex != null) {
        // ... 使用资源
    }
});
```

#### 同步加载
```csharp
// 注意：Addressables 模式下同步加载可能会阻塞主线程
var obj = resLoader.Load<GameObject>("Prefabs/Hero");
```

### 4.3 批量资源加载
适用于需要同时通过 Key 数组或 Addressable Label 加载一组资源的场景。

#### UniTask (推荐)
```csharp
// 加载一组资源，返回列表
string[] keys = new string[] { "Enemy_A", "Enemy_B", "Enemy_C" };
IList<GameObject> enemies = await resLoader.LoadAssetsAsync<GameObject>(keys);
```

#### Callback
```csharp
// 逐个回调
resLoader.LoadAssetsAsync<GameObject>((enemy) => {
    Debug.Log($"Loaded enemy: {enemy.name}");
}, "Enemy_A", "Enemy_B");
```

### 4.4 资源卸载
资源卸载采用了**引用计数**机制。

#### 基本卸载
```csharp
// 引用计数 -1，若归零则立即释放内存
resLoader.UnLoad<GameObject>("Prefabs/MyCube");
```

#### 缓存保留 (Pool 模式常用)
```csharp
// 引用计数 -1，若归零，引用计数变为0但保留 Handle 不释放
// 下次 Load 时可直接复用，无需重新加载
resLoader.UnLoad<GameObject>("Prefabs/Bullet", deleteImmediately: false);

// ... 后续在对象池销毁或关卡切换时彻底释放
resLoader.UnLoad<GameObject>("Prefabs/Bullet", deleteImmediately: true);
```

**注意**: 即使是批量加载的资源，也可以单独卸载，或者使用对应的批量卸载方法（特定于实现类）。

## 5. 高级特性与注意事项

1.  **引用计数 (Ref Counting)**:
    - 每次调用 `Load` / `LoadAsync` 都会增加引用计数。
    - 每次调用 `UnLoad` 都会减少引用计数。
    - **务必成对调用**，否则会导致内存泄漏（资源无法释放）或空指针异常（资源过早释放）。

2.  **异步安全**:
    - `AddressablesLoadMgrModule` 处理了“加载过程中卸载”的边界情况（僵尸回调），确保存储状态的一致性。
    - `ResourcesLoadMgrModule` 处理了 `Cancellation`，如果异步加载被取消，资源加载完成后会被立即销毁。
    - **回调管理**：在资源卸载时，系统会自动断开所有未执行的回调，防止回调逻辑访问已销毁的对象。

3.  **缓存保留机制 (deleteImmediately)**:
    - 默认情况下 `deleteImmediately = true`，引用计数为 0 即释放。
    - 在对象池或高频复用场景下，可设置 `deleteImmediately = false`。
    - 这允许资源的引用计数降为 0，但底层资源（AssetBundle / Memory）仍在内存中。
    - **注意**：如果不设置 false，请确保在合适的时机（如 UnloadUnusedAssets 或场景切换）再次调用 `UnLoad(true)` 或 `Clear` 来彻底释放内存。

4.  **泛型区分**:
    - key 相同但类型不同的资源会被视为不同的缓存条目。
    - 例如 `Load<GameObject>("Cube")` 和 `Load<Texture>("Cube")` 是两个独立的引用计数。


## 6. Key (路径) 互通性说明

**重要：Resources 和 Addressables 的 Key 默认情况下是不互通的！**

在切换加载模块时，如果直接替换 `IResLoad` 的实现类，可能会因为 Key 格式不匹配导致加载失败。

### 6.1 Resources 模式的 Key
- **规则**：必须是相对于 `Assets/Resources/` 文件夹的路径。
- **后缀**：**不能包含**文件扩展名（如 `.prefab`, `.png`）。
- **示例**：
  - 文件物理路径：`Assets/Resources/Characters/Hero.prefab`
  - 加载 Key：`"Characters/Hero"`

### 6.2 Addressables 模式的 Key
- **规则**：可以是 Addressable Name、Label、GUID 或完整路径。
- **默认行为**：当你在 Inspector 中勾选 "Addressable" 时，Unity 默认将其 Addressable Name 设置为 `Assets/...` 开头的完整项目路径。
- **后缀**：通常**包含**文件扩展名（如果是使用路径作为 Key）。
- **示例**：
  - 文件物理路径：`Assets/GameData/Prefabs/Hero.prefab`
  - 默认 Addressable Key：`"Assets/GameData/Prefabs/Hero.prefab"`
  - 自定义 Addressable Name：你可以手动将其改为 `"Hero"` 或 `"Characters/Hero"`。

### 6.3 如何实现互通？
如果希望一套代码能在两种模式下无缝切换，建议遵循以下策略之一：

1.  **手动对齐 (推荐)**：
    - 将资源从 `Resources` 文件夹移出（为了避免打包冗余）。
    - 将资源的 Addressable Name 手动修改为与原 Resources 路径一致的字符串（去掉后缀）。
    - 例如：将 `Hero.prefab` 的 Addressable Name 设为 `"Characters/Hero"`。

2.  **代码适配层**：
    - 在调用 `Load` 之前，根据当前的加载模式对传入的 Key 进行字符串处理。
    - *注意：Addressables 很难反向兼容 Resources 的无后缀路径，除非你手动配置好了 Name。*

### 6.4 为什么不要保留在 Resources 文件夹？
- 如果一个资源在 `Resources` 文件夹内，同时又被标记为 Addressable：
  - Unity 打包时会将该资源打入包体自带的 Resources 索引中（不仅增大包体，启动还慢）。
  - 同时 Addressables 也会将其打入 AssetBundle。
  - **结果**：包体里会有两份一模一样的资源，造成严重的冗余。
- **最佳实践**：决定使用 Addressables 后，请将资源移出 `Resources` 文件夹。

## 7. 统一入口 ResSystem (Unified Entry Point)
虽然您可以直接使用具体的加载模块 (`ResourcesLoadMgrModule` 或 `AddressablesLoadMgrModule`)，但推荐使用 `ResSystem` 单例作为统一入口。

### 7.1 初始化
在游戏启动流程中（例如入口脚本 `GameManager` 或 `Bootstrapper`），根据配置注入具体的加载模块：

```csharp
// 初始化 ResSystem (注入 Resources 模块)
ResSystem.Instance.Init(new ResourcesLoadMgrModule());

// 或者初始化 ResSystem (注入 Addressables 模块)
// ResSystem.Instance.Init(new AddressablesLoadMgrModule());
```

### 7.2 常用 API
`ResSystem` 实现了 `IResLoad<string>` 接口的所有方法，并额外提供了便捷的实例化方法：

```csharp
// 加载并实例化 GameObject (同步)
GameObject go = ResSystem.Instance.Instantiate("Prefabs/Hero");

// 加载并实例化 GameObject (异步 UniTask)
GameObject goAsync = await ResSystem.Instance.InstantiateAsync("Prefabs/Hero");
```

### 7.3 为什么使用 ResSystem？
1. **全局访问**：通过 `ResSystem.Instance` 在任何地方访问资源加载功能。
2. **解耦**：业务逻辑只依赖 `ResSystem` 和 `IResLoad` 接口，不直接依赖 `Resources` 或 `Addressables` API。
3. **便捷扩展**：`ResSystem` 提供了额外的工具方法（如 `Instantiate`），简化了常用操作的代码量。
