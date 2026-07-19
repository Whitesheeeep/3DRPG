# WSFrame MVVM 使用指南

本目录提供 MVVM 的最小接口约束，用于统一 Unity Runtime UI、`WindowBase` 窗口、组件 View，以及 EditorWindow 工具中的 View/ViewModel/ViewData 组织方式。

核心数据流：

```text
View UI event
-> ViewModel intent method
-> Model/Manager mutation
-> Changed event
-> ViewModel rebuilds ViewData/state
-> View RefreshXXX
```

## 接口职责

### `IView<TViewModel>`

View 负责绑定 ViewModel、注册 UI 事件、转发用户输入、刷新视觉表现，并在销毁或重建时解除绑定。

- `Bind(TViewModel viewModel)`：保存 VM 引用，订阅 VM 事件，注册控件回调，执行首次刷新。
- `Unbind()`：取消 VM 事件订阅，释放 View 对 VM 的引用，避免重复绑定和内存泄漏。

View 不直接修改 Model/Manager 数据；如果存在 ViewModel，UI 输入应先转成 ViewModel 的用户意图方法。

### `IViewModel`

ViewModel 是 UI 状态边界，负责：

- 持有选择、筛选、排序、分页、当前编辑对象等 UI 状态。
- 暴露 View 可读取的只读属性或 `IReadOnlyList<TViewData>`。
- 暴露 View 可调用的用户意图方法。
- 订阅 Model/Manager 的数据变化事件，并把 Model 数据投影成 ViewData。
- 在 `Dispose()` 中释放事件订阅或其它生命周期资源。

ViewModel 不持有 `GameObject`、`VisualElement`、`Button`、`Image`、`ListView` 等 UI 控件引用。

### `IViewData`

ViewData 是只读显示投影，只服务 View 渲染。

适合放置：

- 显示文本、图标、数量、可用状态、选中状态。
- Editor 工具需要展示的轻量源对象引用。

不要把持久化业务数据命名或设计成 ViewData。ViewData 也不负责业务修改逻辑。

### `IViewModelProvider<TViewModel>`

Provider 用于多个 View 或 Window 必须共享同一份 UI 状态的场景。

典型场景：

- 快捷栏 View 与背包窗口共享槽位、选中、拖拽结果。
- 多个窗口观察同一个当前工作流状态。
- 全局浮层与详情窗口需要同步同一份 UI 状态。

不要因为多个 View 使用同一个 Manager 就默认共享 ViewModel。只有当它们需要共享同一份 UI 状态时才使用 Provider/Locator。

### `IViewModelResettable`

共享 VM 的 Provider/Locator 如果会缓存 VM，应实现重置能力，用于场景切换、Domain Reload、测试重置、Manager 替换等生命周期。

`ResetViewModel()` 应释放旧 VM，并清空缓存引用。下一次 `GetViewModel()` 再重新创建。

## ViewModel 范围选择

- **Shared ViewModel**：多个 View/Window 共享同一份业务 UI 状态。由 `XXXViewModelProvider` 或 `XXXViewModelLocator` 创建、缓存、释放。
- **Window/View Private ViewModel**：单个窗口或 View 独占状态。由窗口或组合根创建，关闭时释放。
- **Subview ViewModel**：复杂 View 内部某个局部区域的状态。由父 View 或父 VM 持有，不提升为全局共享。
- **Session/Workflow ViewModel**：短生命周期流程状态，如拖拽、向导、批量编辑、临时选择器。由流程发起者创建，结束后丢弃。

默认规则：

- 单 View/Window 私有状态：由该 View/Window 创建和释放 VM。
- 同一窗口内多个子 View 共享状态：由顶层窗口创建 VM 并传递给子 View。
- 跨多个窗口或独立 View 共享状态：使用 Provider/Locator。
- Manager 不持有 ViewModel，避免业务层依赖 UI 层。

## ViewModel 代码组织

生成或重构 VM 时，按 Model 侧和 View 侧职责使用 `#region` 分区。推荐顺序：

```csharp
#region Fields
#endregion

#region Events
#endregion

#region Properties
#endregion

#region LifeCycle
#endregion

#region Model
#endregion

#region View
#endregion
```

- `Fields`：Manager/Model 依赖、ViewData 缓存、注销句柄、内部 UI 状态。
- `Events`：给 View 订阅的变化事件。
- `Properties`：给 View 读取的只读状态和 ViewData。
- `LifeCycle`：构造、初始化、`Dispose`、订阅/解绑 Model 事件。
- `Model`：`OnModelXXXChanged`、`RefreshXXXFromModel`、`CreateViewData` 等从数据层进入 VM 的逻辑。
- `View`：`SelectXXX`、`MoveXXX`、`SetSearchKeyword`、`Save` 等由 View 调用的用户意图入口。

很小的 VM 可以省略空 region；一旦同时包含 Model 同步逻辑和 View 意图方法，至少要区分 `Model` / `View`。

## 命名规则

使用能表达数据流方向的名称：

- `XXXView`：控件引用、UI 输入转发、视觉刷新。
- `XXXViewModel`：UI 状态、ViewData、用户意图入口。
- `XXXViewData`：显示投影。
- `XXXModel`：业务或持久化数据。
- `XXXCommand`：需要撤销、队列、验证、复用或跨系统派发的显式操作对象。

ViewModel 事件使用 `XXXChanged`，例如 `SlotsChanged`、`SelectionChanged`。View 刷新方法使用 `RefreshXXX`。View 输入处理方法可使用 `OnXXXClicked` / `OnXXXChanged`，但 ViewModel 方法应表达业务意图，不要写成 UI 回调名。

避免把 `Update` 当通用动词；它无法说明是在写数据、刷新 UI、重算状态还是帧更新。

## 基础示例

```csharp
public sealed class ExampleViewModel : IViewModel
{
    #region Fields
    private readonly ExampleManager manager;
    private readonly List<ExampleItemViewData> items = new List<ExampleItemViewData>();
    private int selectedIndex = -1;
    #endregion

    #region Events
    public event Action ItemsChanged;
    public event Action SelectionChanged;
    #endregion

    #region Properties
    public IReadOnlyList<ExampleItemViewData> Items => items;
    public int SelectedIndex => selectedIndex;
    #endregion

    #region LifeCycle
    public ExampleViewModel(ExampleManager manager)
    {
        this.manager = manager;
        this.manager.ItemsChanged += OnModelItemsChanged;
        RefreshItemsFromModel();
    }

    public void Dispose()
    {
        manager.ItemsChanged -= OnModelItemsChanged;
    }
    #endregion

    #region Model
    private void OnModelItemsChanged()
    {
        RefreshItemsFromModel();
    }

    private void RefreshItemsFromModel()
    {
        items.Clear();
        foreach (ExampleItemModel model in manager.Items)
        {
            items.Add(new ExampleItemViewData(model.Id, model.DisplayName));
        }

        ItemsChanged?.Invoke();
    }
    #endregion

    #region View
    public void SelectItem(int index)
    {
        if (selectedIndex == index) return;

        selectedIndex = index;
        SelectionChanged?.Invoke();
    }
    #endregion
}
```

```csharp
public sealed class ExampleView : IView<ExampleViewModel>
{
    private ExampleViewModel viewModel;

    public void Bind(ExampleViewModel viewModel)
    {
        this.viewModel = viewModel;
        this.viewModel.ItemsChanged += RefreshItems;
        this.viewModel.SelectionChanged += RefreshSelection;
        RefreshItems();
        RefreshSelection();
    }

    public void Unbind()
    {
        if (viewModel == null) return;

        viewModel.ItemsChanged -= RefreshItems;
        viewModel.SelectionChanged -= RefreshSelection;
        viewModel = null;
    }

    private void OnItemClicked(int index)
    {
        viewModel?.SelectItem(index);
    }

    private void RefreshItems()
    {
    }

    private void RefreshSelection()
    {
    }
}
```

## 完成检查

- ViewModel 范围明确：共享、私有、子 View、流程。
- ViewModel 持有者与生命周期匹配。
- Manager 不持有 ViewModel 或 View。
- ViewModel 不持有 UI 控件。
- ViewData 只用于显示，不替代业务数据。
- ViewModel 使用 region 区分 Model 同步逻辑和 View 意图入口。
- View 订阅了 VM 事件，也能正确解绑。
- 事件和刷新方法粒度能支持局部刷新。
