# PropertyDrawer 说明

该目录用于存放 WSFrame 编辑器扩展中的 Unity `PropertyDrawer`。当前已提供 `WSFolderPathDrawer`，用于配合 `[WSFolderPath]` 属性在 Inspector 中编辑项目文件夹路径。

## WSFolderPathDrawer

`WSFolderPathDrawer` 会为标记了 `[WSFolderPath]` 的字段绘制一个普通文本输入框，并在右侧追加 `...` 按钮。点击按钮后会打开 Unity 文件夹选择面板，选择完成后自动把路径写回字段。

对应 Attribute：

```csharp
using UnityEngine;

namespace WS_Modules
{
    public sealed class WSFolderPathAttribute : PropertyAttribute
    {
    }
}
```

## 支持功能

- 支持 `string` 字段：显示文本框和文件夹选择按钮。
- 支持 `string[]` 字段：显示可折叠数组、数组 Size，以及每个元素的文件夹选择按钮。
- 选择项目内文件夹时，保存为项目相对路径，例如 `Assets/Scripts/Generated`。
- 选择项目外文件夹时，保存为绝对路径。
- 打开文件夹面板时，如果当前字段已有路径，会优先以当前路径作为起始目录。
- 当前字段为空时，文件夹面板默认从 `Assets` 目录开始。
- 标记到非 `string`、非 `string[]` 字段时，会退回 Unity 默认绘制方式，不提供文件夹选择按钮。

## 使用方式

在需要编辑项目文件夹路径的字段上添加 `[WSFolderPath]`：

```csharp
using UnityEngine;
using WS_Modules;

public class ExampleSetting : ScriptableObject
{
    [Tooltip("生成脚本的目标目录。")]
    [WSFolderPath]
    public string GeneratorPath = "Assets/Scripts/Generated";

    [Tooltip("窗口预制体搜索目录。")]
    [WSFolderPath]
    public string[] WindowPrefabFolderPathArr;
}
```

在 Inspector 中：

1. 直接输入路径，或点击右侧 `...`。
2. 在系统文件夹选择面板中选择目标目录。
3. 如果选择的是当前 Unity 项目内目录，字段会保存为相对项目根目录的路径。

## 路径规则

项目根目录通过 `Application.dataPath` 的父目录计算。假设项目路径是：

```text
D:/Unity/DefaultNewProjData/MTWY
```

选择目录：

```text
D:/Unity/DefaultNewProjData/MTWY/Assets/Scripts/Generated
```

字段保存结果：

```text
Assets/Scripts/Generated
```

如果选择的是项目外目录，例如：

```text
D:/Shared/Generated
```

字段会保存原始绝对路径。

## 当前使用位置

当前框架中主要用于 `WSFrameSetting.UIManagerSetting`：

- `BindComponentGeneratorPath`：组件绑定脚本生成路径。
- `WindowGeneratorPath`：窗口交互脚本生成路径。
- `ItemScriptsGeneratorPath`：Item 脚本生成路径。
- `WindowPrefabFolderPathArr`：窗口预制体存放路径数组。
- `UsingNameSpaceArr`：额外命名空间数组。该字段目前也使用 `[WSFolderPath]`，因此 Inspector 会按文件夹路径数组绘制；如果语义上是命名空间列表，建议后续改用普通字符串数组或专用 Drawer。

## 注意事项

- `[WSFolderPath]` 只负责编辑器绘制和路径回填，不会自动创建目录。
- Drawer 不校验目录是否存在，运行时代码读取路径前如有需要应自行校验。
- 数组支持只针对 `string[]`。如果数组为空，Drawer 会按字符串数组处理；新增元素后每个元素都会带文件夹选择按钮。
- 保存的项目相对路径使用 `/` 作为分隔符，便于 Unity 和资源加载代码处理。
- 该 Drawer 位于 Editor 目录下，只在 Unity 编辑器中生效，不会进入运行时构建。
