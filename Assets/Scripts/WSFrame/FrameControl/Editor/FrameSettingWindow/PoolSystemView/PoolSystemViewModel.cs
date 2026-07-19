using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WS_Modules.Pooling;

namespace WS_Modules
{
    internal enum PoolStateLevel
    {
        Open,
        OK,
        High,
        Full,
    }

    [Serializable]
    internal sealed class PoolRowViewData
    {
        public string Name;
        public int Cached;
        public string CapacityText;
        public float UsagePercent;
        public string UsageText;
        public string StateText;
        public PoolStateLevel StateLevel;
    }

    [Serializable]
    internal sealed class PoolSectionViewData
    {
        public string Title;
        public string CountText;
        public string EmptyStateText;
        public List<PoolRowViewData> Rows = new List<PoolRowViewData>();
    }

    internal sealed class PoolSystemViewModel
    {
        private const string EmptyStateText = "No pool data collected yet.";

        [SerializeField] private bool autoRefresh = true;
        [SerializeField] private string searchKeyword = string.Empty;
        [SerializeField] private string summaryText = "Pools: 0 | Cached: 0 | Capacity: 0";
        [SerializeField] private string lastRefreshText = "Last refresh: -";
        [SerializeField] private PoolSectionViewData gameObjectSection = new PoolSectionViewData();
        [SerializeField] private PoolSectionViewData classSection = new PoolSectionViewData();

        private IPoolStatsService _poolStatsService;
        private List<PoolRowViewData> _allGameObjectRows = new List<PoolRowViewData>();
        private List<PoolRowViewData> _allClassRows = new List<PoolRowViewData>();

        public bool AutoRefresh
        {
            get => autoRefresh;
            set => autoRefresh = value;
        }

        public string SummaryText => summaryText;
        public string LastRefreshText => lastRefreshText;
        public PoolSectionViewData GameObjectSection => gameObjectSection;
        public PoolSectionViewData ClassSection => classSection;
        public string SearchKeyword => searchKeyword;

        public void Initialize(IPoolStatsService poolStatsService)
        {
            _poolStatsService = poolStatsService;
            gameObjectSection = CreateEmptySection("GameObject Pools");
            classSection = CreateEmptySection("Class Pools");
        }

        public void InitializePoolManager(PoolingSetting poolingSettings)
        {
            try
            {
                PoolManager.Instance.Initialize(poolingSettings);
                Debug.Log("[FrameSetting] PoolManager initialized from editor.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FrameSetting] Failed to initialize PoolManager: {ex.Message}");
            }
        }

        public void Refresh()
        {
            if (_poolStatsService == null)
            {
                return;
            }

            _poolStatsService.CollectSnapshot(out var gameObjectPools, out var classPools);
            _allGameObjectRows = BuildRows(gameObjectPools);
            _allClassRows = BuildRows(classPools);
            ApplySearchFilter();
            lastRefreshText = $"Last refresh: {DateTime.Now:HH:mm:ss}";
        }

        public void SetSearchKeyword(string keyword)
        {
            searchKeyword = keyword?.Trim() ?? string.Empty;
            ApplySearchFilter();
        }

        private void ApplySearchFilter()
        {
            var gameObjectRows = FilterRows(_allGameObjectRows, searchKeyword);
            var classRows = FilterRows(_allClassRows, searchKeyword);

            gameObjectSection = BuildSection("GameObject Pools", gameObjectRows, _allGameObjectRows.Count, searchKeyword);
            classSection = BuildSection("Class Pools", classRows, _allClassRows.Count, searchKeyword);
            UpdateSummary();
        }

        private static List<PoolRowViewData> BuildRows(IReadOnlyList<PoolItemData> source)
        {
            return (source ?? Array.Empty<PoolItemData>())
                .Select(CreateRow)
                .OrderByDescending(row => row.UsagePercent)
                .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static PoolSectionViewData BuildSection(string title, List<PoolRowViewData> rows, int totalCount,
            string keyword)
        {
            return new PoolSectionViewData
            {
                Title = title,
                CountText = totalCount == rows.Count
                    ? $"{rows.Count} pools / {rows.Sum(row => row.Cached)} cached"
                    : $"{rows.Count}/{totalCount} pools / {rows.Sum(row => row.Cached)} cached",
                EmptyStateText = totalCount > 0 && !string.IsNullOrEmpty(keyword)
                    ? $"No pools match \"{keyword}\"."
                    : EmptyStateText,
                Rows = rows,
            };
        }

        private static List<PoolRowViewData> FilterRows(IEnumerable<PoolRowViewData> rows, string keyword)
        {
            if (string.IsNullOrEmpty(keyword))
            {
                return rows.ToList();
            }

            return rows
                .Where(row => row.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }

        private static PoolSectionViewData CreateEmptySection(string title)
        {
            return new PoolSectionViewData
            {
                Title = title,
                CountText = "0 pools / 0 cached",
                EmptyStateText = EmptyStateText,
            };
        }

        private static PoolRowViewData CreateRow(PoolItemData item)
        {
            int cached = item?.Count ?? 0;
            int maxCapacity = item?.MaxCapacity ?? 0;
            float usage = maxCapacity > 0 ? cached / (float)maxCapacity : 0f;
            var stateLevel = GetStateLevel(cached, maxCapacity, usage);

            return new PoolRowViewData
            {
                Name = string.IsNullOrEmpty(item?.Name) ? "(Unnamed Pool)" : item.Name,
                Cached = cached,
                CapacityText = maxCapacity <= 0 ? "None" : maxCapacity.ToString(),
                UsagePercent = maxCapacity > 0 ? Mathf.Clamp01(usage) : 0f,
                UsageText = maxCapacity > 0 ? $"{Mathf.RoundToInt(Mathf.Clamp01(usage) * 100f)}%" : "-",
                StateText = GetStateText(stateLevel),
                StateLevel = stateLevel,
            };
        }

        private void UpdateSummary()
        {
            var allRows = gameObjectSection.Rows.Concat(classSection.Rows).ToList();
            int totalCached = allRows.Sum(row => row.Cached);
            int totalCapacity = allRows.Sum(ParseCapacity);
            int fullCount = allRows.Count(row => row.StateLevel == PoolStateLevel.Full);
            int highCount = allRows.Count(row => row.StateLevel == PoolStateLevel.High);

            summaryText =
                string.IsNullOrEmpty(searchKeyword)
                    ? $"Pools: {allRows.Count} | Cached: {totalCached} | Capacity: {totalCapacity} | High: {highCount} | Full: {fullCount}"
                    : $"Search: \"{searchKeyword}\" | Pools: {allRows.Count}/{_allGameObjectRows.Count + _allClassRows.Count} | Cached: {totalCached} | Capacity: {totalCapacity} | High: {highCount} | Full: {fullCount}";
        }

        private static int ParseCapacity(PoolRowViewData row)
        {
            return int.TryParse(row.CapacityText, out var capacity) ? capacity : 0;
        }

        private static PoolStateLevel GetStateLevel(int cached, int maxCapacity, float usage)
        {
            if (maxCapacity <= 0) return PoolStateLevel.Open;
            if (cached >= maxCapacity) return PoolStateLevel.Full;
            if (usage >= 0.8f) return PoolStateLevel.High;
            return PoolStateLevel.OK;
        }

        private static string GetStateText(PoolStateLevel stateLevel)
        {
            return stateLevel switch
            {
                PoolStateLevel.Full => "Full",
                PoolStateLevel.High => "High",
                PoolStateLevel.OK => "OK",
                _ => "Open",
            };
        }
    }
}

