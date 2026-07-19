using System;
using System.Collections.Generic;

namespace WS_Modules.DataStructure
{
    /// <summary>
    /// 通用的最近最少使用缓存容器。
    /// </summary>
    /// <remarks>
    /// 该类型负责保存 key/value、维护访问顺序，并在新增数据导致数量超过容量时触发事件。
    /// 它不会自动删除超出的数据；订阅方可以在 <see cref="CapacityExceeded"/> 中决定是否裁剪。
    /// </remarks>
    /// <typeparam name="TKey">缓存键类型。</typeparam>
    /// <typeparam name="TValue">缓存值类型。</typeparam>
    public sealed class LruCache<TKey, TValue>
    {
        /// <summary>
        /// 容量超限事件。参数是触发事件的缓存实例，等价于强类型 sender。
        /// </summary>
        public event Action<LruCache<TKey, TValue>> CapacityExceeded;

        private readonly LinkedList<TKey> list = new LinkedList<TKey>();
        private readonly Dictionary<TKey, LinkedListNode<TKey>> nodes = new Dictionary<TKey, LinkedListNode<TKey>>();
        private readonly Dictionary<TKey, TValue> values = new Dictionary<TKey, TValue>();

        /// <summary>
        /// 创建一个指定容量的 LRU 缓存。
        /// </summary>
        /// <param name="capacity">最大缓存数量。小于等于 0 时不会触发容量超限事件。</param>
        public LruCache(int capacity)
        {
            Capacity = capacity;
        }

        /// <summary>
        /// 最大缓存数量。小于等于 0 时不会触发容量超限事件。
        /// </summary>
        public int Capacity { get; set; }

        /// <summary>
        /// 当前缓存项数量。
        /// </summary>
        public int Count => values.Count;

        /// <summary>
        /// 设置或更新缓存项。
        /// </summary>
        /// <param name="key">缓存键。</param>
        /// <param name="value">缓存值。</param>
        /// <remarks>
        /// 已存在的 key 会更新 value 并刷新为最近使用，不触发容量事件。
        /// 新 key 会插入为最近使用，并在超过容量时触发 <see cref="CapacityExceeded"/>。
        /// </remarks>
        public void Set(TKey key, TValue value)
        {
            if (nodes.TryGetValue(key, out LinkedListNode<TKey> node))
            {
                values[key] = value;
                MoveToMostRecentlyUsed(node);
                return;
            }

            values.Add(key, value);
            nodes.Add(key, list.AddFirst(key));

            if (Capacity > 0 && Count > Capacity)
            {
                CapacityExceeded?.Invoke(this);
            }
        }

        /// <summary>
        /// 获取缓存值，并将命中的 key 刷新为最近使用。
        /// </summary>
        public bool TryGet(TKey key, out TValue value)
        {
            if (!nodes.TryGetValue(key, out LinkedListNode<TKey> node))
            {
                value = default;
                return false;
            }

            MoveToMostRecentlyUsed(node);
            value = values[key];
            return true;
        }

        /// <summary>
        /// 获取缓存值，但不刷新访问顺序。
        /// </summary>
        public bool TryPeek(TKey key, out TValue value)
        {
            return values.TryGetValue(key, out value);
        }

        /// <summary>
        /// 判断 key 是否存在于缓存中。
        /// </summary>
        public bool Contains(TKey key)
        {
            return values.ContainsKey(key);
        }

        /// <summary>
        /// 移除指定 key。
        /// </summary>
        /// <returns>成功移除返回 true；key 不存在返回 false。</returns>
        public bool Remove(TKey key)
        {
            if (!nodes.TryGetValue(key, out LinkedListNode<TKey> node))
            {
                return false;
            }

            list.Remove(node);
            nodes.Remove(key);
            values.Remove(key);
            return true;
        }

        /// <summary>
        /// 移除并返回最近最少使用的缓存项。
        /// </summary>
        public bool TryRemoveLeastRecentlyUsed(out TKey key, out TValue value)
        {
            LinkedListNode<TKey> node = list.Last;
            if (node == null)
            {
                key = default;
                value = default;
                return false;
            }

            key = node.Value;
            value = values[key];
            Remove(key);
            return true;
        }

        /// <summary>
        /// 清空全部缓存项。
        /// </summary>
        public void Clear()
        {
            list.Clear();
            nodes.Clear();
            values.Clear();
        }

#if UNITY_EDITOR
        #region Editor Debug

        /// <summary>
        /// 从最近使用到最久未使用枚举缓存内容，不刷新访问顺序。
        /// </summary>
        public IEnumerable<KeyValuePair<TKey, TValue>> EnumerateMostRecentlyUsed()
        {
            LinkedListNode<TKey> node = list.First;
            while (node != null)
            {
                TKey key = node.Value;
                yield return new KeyValuePair<TKey, TValue>(key, values[key]);
                node = node.Next;
            }
        }

        #endregion
#endif

        private void MoveToMostRecentlyUsed(LinkedListNode<TKey> node)
        {
            list.Remove(node);
            list.AddFirst(node);
        }
    }
}
