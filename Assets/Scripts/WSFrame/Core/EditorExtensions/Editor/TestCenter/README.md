# TestCenter 使用说明

`TestCenter` 是一个 Unity Editor 手动测试集中管理窗口，用来扫描项目里的 `MonoBehaviour` 测试组件，并把选中的测试组件加载到当前场景中的 `Test` 物体上。

它的目标不是替代 Unity Test Runner，而是把项目中常见的 Inspector/Odin Button 手动测试脚本集中起来，方便查找、挂载、定位和执行。

## 打开方式

菜单路径：

```text
Tools/WSFrame/总测试窗口
```

快捷键：

```text
Ctrl + Shift + T
```

窗口打开后会自动刷新 Tester 列表，也可以点击刷新按钮重新扫描。

## Test 是怎么被捕获的

捕获逻辑主要在 `TestCenterViewModel.Refresh()` 中完成，分为两步：

1. 扫描脚本资源：通过 `AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets/Scripts" })` 在 `Assets/Scripts` 下查找脚本。
2. 扫描场景实例：通过 `Resources.FindObjectsOfTypeAll<MonoBehaviour>()` 查找当前已加载场景中的测试组件实例。

最终列表会合并脚本和场景实例：

- 如果脚本存在且场景里已经挂了同类型组件，列表显示为已加载。
- 如果脚本存在但场景里没有实例，列表显示为未加载，可以点击加载。
- 如果场景里存在符合规则的 Tester 实例，即使脚本没有被脚本扫描阶段捕获，也会显示在列表中。

## 能捕获哪些 Test

TestCenter 捕获的是 `MonoBehaviour` 测试组件，并且类型必须满足：

- 类型不为空。
- 不是抽象类。
- 不是泛型类型。
- 继承自 `MonoBehaviour`。
- 符合下面任意一种测试命名或路径规则。

### 脚本捕获规则

脚本资源必须位于 `Assets/Scripts` 下，并且脚本路径满足以下任一条件：

- 路径中包含 `/Test/` 或 `\Test\` 目录。
- 文件名以 `Tester.cs` 结尾。
- 文件名以 `OdinTester.cs` 结尾。

通过脚本路径初筛后，脚本对应的类型还需要是有效的 `MonoBehaviour` 测试类型。常见示例：

```text
Assets/Scripts/Inventory/Test/InventoryOdinTester.cs
Assets/Scripts/WSFrame/UISystem/Test/UIManagerOdinTester.cs
Assets/Scripts/WSFrame/Utilities/TMProKit/TMProTypeWriter_Tester.cs
```

推荐写法：

```csharp
#if UNITY_EDITOR
using Sirenix.OdinInspector;
using UnityEngine;

public sealed class InventoryOdinTester : MonoBehaviour
{
    [Button("测试添加物品")]
    public void TestAddItem()
    {
        Debug.Log("Run test logic here.");
    }
}
#endif
```

### 场景实例捕获规则

场景中已经存在的 `MonoBehaviour` 也会被扫描。实例必须满足：

- 不是持久化资源，也就是不能是 Project 面板里的 prefab/asset。
- 所在 `GameObject` 有有效场景。
- 类型满足 Tester 类型规则。

场景实例额外支持按类型名捕获：

- 类型名以 `Tester` 结尾。
- 类型名以 `OdinTester` 结尾。
- 类型已经在脚本扫描阶段被识别为 Tester。

这意味着如果一个测试组件已经挂在场景里，只要类名符合 `Tester` 或 `OdinTester` 后缀，即使脚本路径不在 `/Test/` 目录下，也仍然可能出现在 TestCenter 列表中。

## 捕获不到哪些 Test

下面这些不会被 TestCenter 当作 Tester：

- 不继承 `MonoBehaviour` 的类。
- `ScriptableObject`、普通 C# 类、EditorWindow、静态工具类。
- 抽象类或泛型类。
- Unity Test Framework / NUnit 的 `[Test]`、`[UnityTest]` 方法。
- 不在 `Assets/Scripts` 下，并且场景中也没有符合命名规则实例的脚本。
- Project 面板中的 prefab/asset 上的组件实例。
- 文件路径不包含 `Test` 目录，文件名也不以 `Tester.cs` 或 `OdinTester.cs` 结尾的未加载脚本。

如果某个脚本没有出现在列表里，优先检查：

1. 脚本是否在 `Assets/Scripts` 下。
2. 文件是否放在 `Test` 目录中，或文件名是否以 `Tester.cs` / `OdinTester.cs` 结尾。
3. 类是否继承 `MonoBehaviour`。
4. 类是否不是抽象类、不是泛型类。
5. Unity 是否已经编译通过，`MonoScript.GetClass()` 能否拿到类型。

## 加载和管理流程

TestCenter 使用当前场景中的 `Test` 物体作为测试组件容器。

- 点击创建 Test 物体：如果场景中已经存在名为 `Test` 的物体，会直接选中并 Ping；否则创建一个新的 `GameObject("Test")`。
- 点击加载 Tester：把当前选中的测试组件类型通过 `Undo.AddComponent` 挂到 `Test` 物体上。
- 点击删除 Tester：通过 `Undo.DestroyObjectImmediate` 删除当前选中的测试组件实例。
- 点击定位脚本：选中并 Ping 对应的 `MonoScript`。
- 点击定位实例：选中测试组件实例，并 Ping 所在的 `GameObject`。

加载、创建和删除操作都会接入 Unity Undo，并标记当前场景为 dirty。

## 列表和搜索规则

列表排序规则：

- 已加载的 Tester 排在未加载 Tester 前面。
- 同一状态下按类型名升序排列。

搜索框会匹配：

- 类型名。
- 命名空间。
- 脚本路径。

搜索只过滤当前已捕获的 Tester，不会改变捕获规则本身。

## 和 Odin Tester 的关系

TestCenter 本身只负责捕获、挂载和展示 `MonoBehaviour`。如果测试组件使用 Odin Inspector 的 `[Button]`、`[Title]`、`[InfoBox]` 等特性，加载后右侧 Inspector 区域会绘制该组件的 Inspector，开发者可以直接点击 Odin 按钮执行手动测试。

因此推荐复杂手动测试脚本使用 `OdinTester` 后缀，例如：

```text
InventoryOdinTester
UIManagerOdinTester
```

普通 Inspector 测试脚本也可以使用 `Tester` 后缀。

## 建议约定

- 测试脚本优先放在被测试模块附近的 `Test` 目录中。
- 手动测试组件命名为 `模块名Tester` 或 `模块名OdinTester`。
- 测试脚本只用于 Editor 或开发场景时，建议包在 `#if UNITY_EDITOR` 中。
- 测试逻辑尽量走真实 Manager/API 路径，不直接改内部字段。
- 测试组件不要作为运行时依赖，不要被正式流程引用。
