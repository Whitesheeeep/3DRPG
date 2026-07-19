using System;
using System.Collections.Generic;
using WS_Modules.LogModule;

namespace WS_Modules.Pooling
{
    /// <summary>
    /// 普通 class 对象池模块。
    /// </summary>
    public class ClassPoolModule
    {
        private readonly Dictionary<Type, IClassPoolData> _poolDic = new();

        /// <summary>
        /// 显式预热对象池。显式传入的 maxCapacity 优先于 IPoolable 默认配置。
        /// </summary>
        public void Prewarm<T>(int count, int maxCapacity) where T : class, new()
        {
            if (count <= 0) return;

            if (maxCapacity != -1 && count > maxCapacity)
            {
                WSLog.LogWarning($"Prewarm class {typeof(T).Name}: count {count} exceeds maxCapacity {maxCapacity}");
                count = maxCapacity;
            }

            var data = GetPoolData<T>(maxCapacity, false);
            int needed = count - data.Count;
            PrewarmObjects(data, needed, default);
        }

        /// <summary>
        /// 获取对象。首次创建池时会读取 IPoolable 的 MaxCount / InitCount 作为默认配置。
        /// </summary>
        public T Get<T>() where T : class, new()
        {
            var data = GetPoolData<T>(-1, true);

            if (!data.TryGet(out T obj))
            {
                obj = new T();
            }

            if (obj is IPoolable poolable)
            {
                poolable.OnSpawn();
            }

            return obj;
        }

        /// <summary>
        /// 回收对象。 如果池不存在，只创建空池，不触发 IPoolable 默认预热。
        /// </summary>
        public void Recycle<T>(T obj) where T : class, new()
        {
            if (obj == null) return;

            if (obj is IPoolable poolable)
            {
                poolable.OnDespawn();
            }

            var data = GetPoolData<T>(-1, false);
            data.Push(obj);
        }

        /// <summary>
        /// 清理指定类型的池。
        /// </summary>
        public void Clear<T>()
        {
            var type = typeof(T);
            if (_poolDic.TryGetValue(type, out var data))
            {
                data.Clear();
                _poolDic.Remove(type);
            }
        }

        /// <summary>
        /// 清理所有池。
        /// </summary>
        public void ClearAll()
        {
            foreach (var data in _poolDic.Values)
            {
                data.Clear();
            }

            _poolDic.Clear();
        }

        /// <summary>
        /// 获取或创建指定类型的池数据。首次创建时可选择是否使用 IPoolable 默认配置。
        /// </summary>
        /// <param name="maxCapacity">用于指定在没有池子时创建多大的池子</param>
        /// <param name="usePoolableDefaults">在没有池子且 T 为 IPoolable 派生类，使用 IPoolable 中的值</param>
        /// <returns></returns>
        private ClassPoolData<T> GetPoolData<T>(int maxCapacity, bool usePoolableDefaults) where T : class, new()
        {
            var type = typeof(T);
            if (_poolDic.TryGetValue(type, out var data))
            {
                return (ClassPoolData<T>)data;
            }

            T defaultInstance = default;
            int resolvedMaxCapacity = maxCapacity;
            int initCount = 0;

            if (usePoolableDefaults && maxCapacity == -1)
            {
                defaultInstance = new T();
                if (defaultInstance is IPoolable poolable)
                {
                    resolvedMaxCapacity = NormalizeMaxCapacity(poolable.MaxCount);
                    initCount = NormalizeInitCount(type, poolable.InitCount, resolvedMaxCapacity);
                }
                else
                {
                    defaultInstance = default;
                }
            }

            var newData = new ClassPoolData<T>(resolvedMaxCapacity);
            _poolDic.Add(type, newData);
            PrewarmObjects(newData, initCount, defaultInstance);
            return newData;
        }

        private void PrewarmObjects<T>(ClassPoolData<T> data, int count, T firstInstance) where T : class, new()
        {
            if (data == null || count <= 0) return;

            for (int i = 0; i < count; i++)
            {
                T obj = i == 0 && firstInstance != null ? firstInstance : new T();
                if (obj is IPoolable poolable)
                {
                    poolable.OnDespawn();
                }

                data.Push(obj);
            }
        }

        private int NormalizeMaxCapacity(int maxCount)
        {
            return maxCount <= 0 ? -1 : maxCount;
        }

        private int NormalizeInitCount(Type type, int initCount, int maxCapacity)
        {
            if (initCount <= 0)
            {
                return 0;
            }

            if (maxCapacity > 0 && initCount > maxCapacity)
            {
                WSLog.LogWarning(
                    $"IPoolable default initCount {initCount} exceeds maxCount {maxCapacity} for class {type.Name}. Init count has been clamped.");
                return maxCapacity;
            }

            return initCount;
        }
    }
}
