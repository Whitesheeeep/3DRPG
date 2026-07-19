using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.LogModule;
using WS_Modules.Singleton;

namespace WS_Modules.Pooling
{
    /// <summary>
    /// 全局对象池预热处理器。
    /// 负责在 PoolManager 初始化阶段读取 SO 配置和代码配置模块，并将合并结果应用到具体对象池模块。
    /// Apply 完成后会释放临时配置引用，后续运行时预热应直接使用 PoolManager.Prewarm 系列 API。
    /// </summary>
    public class GlobalPoolPrewarmProcessor : SingletonBase<GlobalPoolPrewarmProcessor>
    {
        private GlobalPoolPrewarmProcessor()
        {
        }

        /// <summary>
        /// 当前等待应用的 SO 配置。Apply 后置空，避免长期持有配置资源引用。
        /// </summary>
        private PoolPrewarmConfig _config;

        /// <summary>
        /// 标记全局预热是否已经应用。该处理器按一次性初始化流程设计。
        /// </summary>
        private bool _hasApplied;

        /// <summary>
        /// 设置全局预热 SO 配置。应在 PoolManager.Initialize 调用 Apply 前执行。
        /// </summary>
        public void SetConfig(PoolPrewarmConfig config)
        {
            if (_hasApplied)
            {
                WSLog.LogWarning("Global pool prewarm has already been applied. SetConfig was ignored.");
                return;
            }

            _config = config;
        }

        /// <summary>
        /// 应用所有全局预热配置。该方法只允许执行一次，执行后会释放临时配置数据。
        /// </summary>
        public void Apply(GameObjectPoolModule gameObjectModule, ClassPoolModule classModule)
        {
            if (_hasApplied)
            {
                WSLog.LogWarning("Global pool prewarm has already been applied. Apply was ignored.");
                return;
            }

            CodePoolPrewarmConfigBuilder codeConfigBuilder = null;
            try
            {
                codeConfigBuilder = BuildCodePrewarmConfig();
                ApplyGameObjectPrewarm(gameObjectModule, codeConfigBuilder);
                ApplyClassPrewarm(classModule, codeConfigBuilder);
            }
            finally
            {
                ReleaseTransientData(codeConfigBuilder);
                _hasApplied = true;
            }
        }

        /// <summary>
        /// 从代码配置模块中收集固定预热项。
        /// </summary>
        private static CodePoolPrewarmConfigBuilder BuildCodePrewarmConfig()
        {
            var builder = new CodePoolPrewarmConfigBuilder();
            var module = new CodePoolPrewarmConfigModule();
            module.Collect(builder);
            return builder;
        }

        /// <summary>
        /// 释放 Apply 前临时收集的数据和委托引用，降低初始化后常驻内存。
        /// </summary>
        private void ReleaseTransientData(CodePoolPrewarmConfigBuilder codeConfigBuilder)
        {
            _config = null;
            codeConfigBuilder?.Release();
        }

        /// <summary>
        /// 合并并应用 GameObject 预热配置。
        /// </summary>
        private void ApplyGameObjectPrewarm(GameObjectPoolModule module, CodePoolPrewarmConfigBuilder codeConfigBuilder)
        {
            if (module == null) return;

            var mergedItems = BuildMergedGameObjectItems(codeConfigBuilder);
            foreach (var item in mergedItems.Values)
            {
                module.Prewarm(item.Key, item.InitCount, item.MaxCapacity);
            }
        }

        /// <summary>
        /// 合并并应用 class 预热配置。
        /// SO 配置项的预热委托来自生成的 ClassPoolPrewarmRegistry。
        /// </summary>
        private void ApplyClassPrewarm(ClassPoolModule module, CodePoolPrewarmConfigBuilder codeConfigBuilder)
        {
            if (module == null) return;

            var mergedItems = BuildMergedClassItems(codeConfigBuilder);
            foreach (var pair in mergedItems)
            {
                if (pair.Value.Apply != null)
                {
                    pair.Value.Apply(module, pair.Value.InitCount, pair.Value.MaxCapacity);
                    continue;
                }

                WSLog.LogWarning($"Class pool prewarm type {pair.Key.FullName} has no registered prewarm action.");
            }
        }

        /// <summary>
        /// 构建最终 GameObject 预热表，SO 配置和代码配置模块按 key 合并。
        /// </summary>
        private Dictionary<string, GameObjectPoolPrewarmRequest> BuildMergedGameObjectItems(
            CodePoolPrewarmConfigBuilder codeConfigBuilder)
        {
            var mergedItems = new Dictionary<string, GameObjectPoolPrewarmRequest>();

            if (_config?.gameObjectPrewarmItems != null)
            {
                foreach (var item in _config.gameObjectPrewarmItems)
                {
                    MergeGameObjectItem(mergedItems, item);
                }
            }

            if (codeConfigBuilder != null)
            {
                foreach (var item in codeConfigBuilder.GameObjectItems)
                {
                    MergeGameObjectItem(mergedItems, item);
                }
            }

            return mergedItems;
        }

        /// <summary>
        /// 构建最终 class 预热表，SO 配置和代码配置模块按 Type 合并。
        /// </summary>
        private Dictionary<Type, ClassPoolPrewarmRequest> BuildMergedClassItems(
            CodePoolPrewarmConfigBuilder codeConfigBuilder)
        {
            var mergedItems = new Dictionary<Type, ClassPoolPrewarmRequest>();

            if (_config?.classPrewarmItems != null)
            {
                foreach (var item in _config.classPrewarmItems)
                {
                    MergeClassItem(mergedItems, item);
                }
            }

            if (codeConfigBuilder != null)
            {
                foreach (var item in codeConfigBuilder.ClassItems)
                {
                    MergeClassItem(mergedItems, item);
                }
            }

            return mergedItems;
        }

        /// <summary>
        /// 合并 SO GameObject 配置项。重复 key 的 initCount 和 maxCapacity 均取更大值。
        /// </summary>
        private static void MergeGameObjectItem(Dictionary<string, GameObjectPoolPrewarmRequest> items,
            GameObjectPoolPrewarmItem item)
        {
            if (!IsValid(item)) return;

            MergeGameObjectItem(items, item.key, item.initCount, item.maxCapacity);
        }

        /// <summary>
        /// 合并代码 GameObject 配置项。重复 key 的 initCount 和 maxCapacity 均取更大值。
        /// </summary>
        private static void MergeGameObjectItem(Dictionary<string, GameObjectPoolPrewarmRequest> items,
            GameObjectPoolPrewarmRequest item)
        {
            if (item == null || string.IsNullOrEmpty(item.Key)) return;

            MergeGameObjectItem(items, item.Key, item.InitCount, item.MaxCapacity);
        }

        private static void MergeGameObjectItem(Dictionary<string, GameObjectPoolPrewarmRequest> items,
            string key, int initCount, int maxCapacity)
        {
            if (!items.TryGetValue(key, out var existing))
            {
                items[key] = new GameObjectPoolPrewarmRequest
                {
                    Key = key,
                    InitCount = initCount,
                    MaxCapacity = maxCapacity,
                };
                return;
            }

            existing.InitCount = MaxInitCount(existing.InitCount, initCount);
            existing.MaxCapacity = MaxCapacity(existing.MaxCapacity, maxCapacity);
        }

        /// <summary>
        /// 合并 SO class 配置项。classId 会通过生成表映射到 Type 和强类型预热委托。
        /// </summary>
        private static void MergeClassItem(Dictionary<Type, ClassPoolPrewarmRequest> items,
            ClassPoolPrewarmItem item)
        {
            if (!IsValid(item)) return;

            if (!ClassPoolPrewarmRegistry.TryGetEntry(item.classId, out var entry))
            {
                WSLog.LogWarning($"Invalid class pool prewarm id: {item.classId} ({item.displayName})");
                return;
            }

            MergeClassItem(items, entry.Type, item.initCount, item.maxCapacity, entry.Apply);
        }

        /// <summary>
        /// 合并代码 class 配置项。重复 Type 的 initCount 和 maxCapacity 均取更大值。
        /// </summary>
        private static void MergeClassItem(Dictionary<Type, ClassPoolPrewarmRequest> items,
            ClassPoolPrewarmRequest item)
        {
            if (item == null) return;

            var apply = item.Apply ?? FindRegistryApply(item.Type);
            MergeClassItem(items, item.Type, item.InitCount, item.MaxCapacity, apply);
        }

        /// <summary>
        /// 使用 Type 指针从生成表中查找预热委托，不进行字符串解析或泛型反射调用。
        /// </summary>
        private static Action<ClassPoolModule, int, int> FindRegistryApply(Type type)
        {
            if (type == null) return null;

            foreach (var entry in ClassPoolPrewarmRegistry.Entries)
            {
                if (entry.Type == type)
                {
                    return entry.Apply;
                }
            }

            return null;
        }

        private static void MergeClassItem(Dictionary<Type, ClassPoolPrewarmRequest> items, Type type,
            int initCount, int maxCapacity, Action<ClassPoolModule, int, int> apply)
        {
            if (type == null) return;

            if (!items.TryGetValue(type, out var existing))
            {
                items[type] = new ClassPoolPrewarmRequest
                {
                    Type = type,
                    InitCount = initCount,
                    MaxCapacity = maxCapacity,
                    Apply = apply,
                };
                return;
            }

            existing.InitCount = MaxInitCount(existing.InitCount, initCount);
            existing.MaxCapacity = MaxCapacity(existing.MaxCapacity, maxCapacity);
            existing.Apply ??= apply;
        }

        private static bool IsValid(GameObjectPoolPrewarmItem item)
        {
            return item is { enable: true } && !string.IsNullOrEmpty(item.key);
        }

        private static bool IsValid(ClassPoolPrewarmItem item)
        {
            return item is { enable: true } && item.classId != ClassPoolPrewarmId.None;
        }

        private static int MaxInitCount(int current, int next)
        {
            return Math.Max(current, next);
        }

        private static int MaxCapacity(int current, int next)
        {
            if (current == -1 || next == -1) return -1;
            return Math.Max(current, next);
        }
    }
}
