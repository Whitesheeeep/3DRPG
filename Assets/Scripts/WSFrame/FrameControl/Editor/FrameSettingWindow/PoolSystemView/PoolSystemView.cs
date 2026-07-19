using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.Pooling;

namespace WS_Modules
{
    internal sealed class PoolSystemView
    {
        private const string CachedTooltip = "对象池内当前未被取出，可直接复用的空闲对象数量。";
        public const string CachedStateTooltip = "池子使用状态：OK 表示池子健康，余量多；Open 表示无限池；High 表示池子快满了；Full 表示池子已满，无法再放入更多对象。";

        private readonly Func<VisualElement, WSFrameRoot> _getFrameRoot;
        private readonly IPoolStatsService _poolStatsService = new PoolStatsService();

        private PoolSystemViewModel _viewModel;
        private VisualElement _viewRoot;
        private VisualElement _gameObjectSection;
        private VisualElement _classSection;
        private MultiColumnListView _gameObjectListView;
        private MultiColumnListView _classListView;
        private Label _summaryLabel;
        private Label _lastRefreshLabel;
        private Toggle _autoRefreshToggle;
        private TextField _searchField;

        public PoolSystemView(Func<VisualElement, WSFrameRoot> getFrameRoot)
        {
            _getFrameRoot = getFrameRoot;
        }

        public void Draw(VisualElement container, VisualTreeAsset template)
        {
            var wsFrameRoot = _getFrameRoot(container);
            if (wsFrameRoot == null || wsFrameRoot.FrameSetting == null) return;

            template ??= LoadVisualTreeAsset();
            if (template == null)
            {
                container.Add(new HelpBox(
                    "PoolSystemView.uxml not found. Create or assign the template to render Pool System.",
                    HelpBoxMessageType.Error));
                return;
            }

            _viewModel = new PoolSystemViewModel();
            _viewModel.Initialize(_poolStatsService);

            _viewRoot = template.Instantiate();
            _viewRoot.AddToClassList("pool-system-view");

            container.Add(_viewRoot);
            BindView(wsFrameRoot);
            Refresh();

            _viewRoot.RegisterCallback<DetachFromPanelEvent>(_ => DisposeViewModel());
            _viewRoot.schedule.Execute(() =>
            {
                if (_viewModel == null || !_viewModel.AutoRefresh) return;
                Refresh();
            }).Every(1000).Resume();
        }

        private void BindView(WSFrameRoot wsFrameRoot)
        {
            var initButton = _viewRoot.Q<Button>("InitButton");
            var refreshButton = _viewRoot.Q<Button>("RefreshButton");
            var clearSearchButton = _viewRoot.Q<Button>("ClearSearchButton");
            _autoRefreshToggle = _viewRoot.Q<Toggle>("AutoRefreshToggle");
            _searchField = _viewRoot.Q<TextField>("SearchField");
            _summaryLabel = _viewRoot.Q<Label>("SummaryLabel");
            _lastRefreshLabel = _viewRoot.Q<Label>("LastRefreshLabel");
            _gameObjectSection = _viewRoot.Q<VisualElement>("GameObjectSection");
            _classSection = _viewRoot.Q<VisualElement>("ClassSection");

            initButton?.RegisterCallback<ClickEvent>(_ =>
            {
                _viewModel.InitializePoolManager(wsFrameRoot.FrameSetting.PoolingSettings);
                Refresh();
            });
            refreshButton?.RegisterCallback<ClickEvent>(_ => Refresh());
            _autoRefreshToggle?.RegisterValueChangedCallback(evt =>
            {
                _viewModel.AutoRefresh = evt.newValue;
                SyncBoundFields();
            });
            _searchField?.RegisterValueChangedCallback(evt =>
            {
                _viewModel.SetSearchKeyword(evt.newValue);
                SyncBoundFields();
                DrawSection(_gameObjectSection, _viewModel.GameObjectSection);
                DrawSection(_classSection, _viewModel.ClassSection);
            });
            clearSearchButton?.RegisterCallback<ClickEvent>(_ =>
            {
                _searchField?.SetValueWithoutNotify(string.Empty);
                _viewModel.SetSearchKeyword(string.Empty);
                SyncBoundFields();
                DrawSection(_gameObjectSection, _viewModel.GameObjectSection);
                DrawSection(_classSection, _viewModel.ClassSection);
            });
        }

        private void Refresh()
        {
            if (_viewModel == null) return;

            _viewModel.Refresh();
            SyncBoundFields();
            DrawSection(_gameObjectSection, _viewModel.GameObjectSection);
            DrawSection(_classSection, _viewModel.ClassSection);
        }

        private void SyncBoundFields()
        {
            // Label text binding is inconsistent across Unity 2022 patch versions, so keep explicit sync.
            if (_summaryLabel != null) _summaryLabel.text = _viewModel.SummaryText;
            if (_lastRefreshLabel != null) _lastRefreshLabel.text = _viewModel.LastRefreshText;
            _autoRefreshToggle?.SetValueWithoutNotify(_viewModel.AutoRefresh);
            _searchField?.SetValueWithoutNotify(_viewModel.SearchKeyword);
        }

        private void DrawSection(VisualElement sectionRoot, PoolSectionViewData sectionData)
        {
            if (sectionRoot == null || sectionData == null) return;

            var titleLabel = sectionRoot.Q<Label>("SectionTitle");
            var countLabel = sectionRoot.Q<Label>("SectionCount");
            var tableContainer = sectionRoot.Q<VisualElement>("TableContainer");

            if (titleLabel != null) titleLabel.text = sectionData.Title;
            if (countLabel != null) countLabel.text = sectionData.CountText;
            if (tableContainer == null) return;

            if (sectionData.Rows.Count == 0)
            {
                tableContainer.Clear();
                tableContainer.Add(new HelpBox(sectionData.EmptyStateText, HelpBoxMessageType.Info));
                AssignListView(sectionRoot, null);
                return;
            }

            var listView = GetListView(sectionRoot);
            if (listView == null)
            {
                tableContainer.Clear();
                listView = CreateListView(sectionData.Rows);
                AssignListView(sectionRoot, listView);
                tableContainer.Add(listView);
            }
            else
            {
                listView.itemsSource = sectionData.Rows;
                listView.style.height = Mathf.Clamp(34 + sectionData.Rows.Count * 28, 92, 260);
                listView.Rebuild();
            }
        }

        private static MultiColumnListView CreateListView(List<PoolRowViewData> rows)
        {
            var listView = new MultiColumnListView
            {
                itemsSource = rows,
                sortingEnabled = true,
                fixedItemHeight = 26,
            };
            listView.AddToClassList("pool-table");
            listView.style.height = Mathf.Clamp(34 + rows.Count * 28, 92, 260);

            listView.columns.Add(new Column { title = "Pool Name", width = 190, stretchable = true });
            listView.columns.Add(new Column { title = "Cached", width = 82 });
            listView.columns.Add(new Column { title = "Capacity", width = 78 });
            listView.columns.Add(new Column { title = "Usage", width = 160 });
            listView.columns.Add(new Column { title = "State", width = 78 });

            List<PoolRowViewData> GetRows() => listView.itemsSource as List<PoolRowViewData>;
            BindNameColumn(listView.columns[0], GetRows);
            BindCachedColumn(listView.columns[1], GetRows);
            BindCapacityColumn(listView.columns[2], GetRows);
            BindUsageColumn(listView.columns[3], GetRows);
            BindStateColumn(listView.columns[4], GetRows);
            return listView;
        }

        private static void BindNameColumn(Column column, Func<IList<PoolRowViewData>> getRows)
        {
            column.makeCell = () =>
            {
                var label = new Label();
                label.AddToClassList("pool-cell-name");
                return label;
            };
            column.bindCell = (ve, idx) =>
            {
                if (ve is not Label label || !TryGetRow(getRows(), idx, out var row)) return;
                label.text = row.Name;
                label.tooltip = row.Name;
            };
        }

        private static void BindCachedColumn(Column column, Func<IList<PoolRowViewData>> getRows)
        {
            column.makeHeader = () =>
            {
                var label = new Label("Cached");
                label.tooltip = CachedTooltip;
                label.AddToClassList("pool-header-tooltip");
                return label;
            };
            column.makeCell = () =>
            {
                var label = new Label { tooltip = CachedTooltip };
                label.AddToClassList("pool-cell-center");
                return label;
            };
            column.bindCell = (ve, idx) =>
            {
                if (ve is Label label && TryGetRow(getRows(), idx, out var row))
                {
                    label.text = row.Cached.ToString();
                }
            };
        }

        private static void BindCapacityColumn(Column column, Func<IList<PoolRowViewData>> getRows)
        {
            column.makeCell = () =>
            {
                var label = new Label();
                label.AddToClassList("pool-cell-center");
                return label;
            };
            column.bindCell = (ve, idx) =>
            {
                if (ve is Label label && TryGetRow(getRows(), idx, out var row))
                {
                    label.text = row.CapacityText;
                }
            };
        }

        private static void BindUsageColumn(Column column, Func<IList<PoolRowViewData>> getRows)
        {
            column.makeCell = CreateUsageCell;
            column.bindCell = (ve, idx) =>
            {
                if (TryGetRow(getRows(), idx, out var row))
                {
                    BindUsageCell(ve, row);
                }
            };
        }

        private static void BindStateColumn(Column column, Func<IList<PoolRowViewData>> getRows)
        {
            column.makeHeader = () =>
            {
                var label = new Label("State");
                label.tooltip = CachedStateTooltip;
                label.AddToClassList("pool-header-state");
                return label;
            };
            column.makeCell = () =>
            {
                var label = new Label();
                label.tooltip = CachedStateTooltip;
                label.AddToClassList("pool-state");
                return label;
            };
            column.bindCell = (ve, idx) =>
            {
                if (ve is not Label label || !TryGetRow(getRows(), idx, out var row)) return;

                label.text = row.StateText;
                label.RemoveFromClassList("pool-state-open");
                label.RemoveFromClassList("pool-state-ok");
                label.RemoveFromClassList("pool-state-high");
                label.RemoveFromClassList("pool-state-full");
                label.AddToClassList($"pool-state-{row.StateLevel.ToString().ToLowerInvariant()}");
            };
        }

        private static VisualElement CreateUsageCell()
        {
            var root = new VisualElement();
            root.AddToClassList("pool-usage-cell");

            var barBack = new VisualElement { name = "UsageBarBack" };
            barBack.AddToClassList("pool-usage-bar-back");

            var barFill = new VisualElement { name = "UsageBarFill" };
            barFill.AddToClassList("pool-usage-bar-fill");
            barBack.Add(barFill);

            var label = new Label { name = "UsageText" };
            label.AddToClassList("pool-usage-text");

            root.Add(barBack);
            root.Add(label);
            return root;
        }

        private static void BindUsageCell(VisualElement root, PoolRowViewData row)
        {
            var fill = root.Q<VisualElement>("UsageBarFill");
            var label = root.Q<Label>("UsageText");

            if (fill != null)
            {
                fill.style.width = Length.Percent(Mathf.Clamp01(row.UsagePercent) * 100f);
                fill.RemoveFromClassList("pool-usage-open");
                fill.RemoveFromClassList("pool-usage-ok");
                fill.RemoveFromClassList("pool-usage-high");
                fill.RemoveFromClassList("pool-usage-full");
                fill.AddToClassList($"pool-usage-{row.StateLevel.ToString().ToLowerInvariant()}");
            }

            if (label != null)
            {
                label.text = row.UsageText;
            }
        }

        private static bool TryGetRow(IList<PoolRowViewData> rows, int index, out PoolRowViewData row)
        {
            row = null;
            if (rows == null || index < 0 || index >= rows.Count) return false;
            row = rows[index];
            return true;
        }

        private MultiColumnListView GetListView(VisualElement sectionRoot)
        {
            if (sectionRoot == _gameObjectSection) return _gameObjectListView;
            if (sectionRoot == _classSection) return _classListView;
            return null;
        }

        private void AssignListView(VisualElement sectionRoot, MultiColumnListView listView)
        {
            if (sectionRoot == _gameObjectSection)
            {
                _gameObjectListView = listView;
            }
            else if (sectionRoot == _classSection)
            {
                _classListView = listView;
            }
        }

        private static VisualTreeAsset LoadVisualTreeAsset()
        {
            var guids = AssetDatabase.FindAssets("PoolSystemView t:VisualTreeAsset");
            if (guids.Length == 0) return null;

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
        }

        private void DisposeViewModel()
        {
            if (_viewModel == null) return;

            _viewModel = null;
            _gameObjectListView = null;
            _classListView = null;
        }
    }
}
