using System.Collections.Generic;
using WS_Modules.LogModule;

namespace WS_Modules.Pooling
{
    /// <summary>
    /// 类对象池数据接口，用于 Module 统一管理
    /// </summary>
    public interface IClassPoolData
    {
        void Clear();
        int Count { get; }
        int MaxCapacity { get; }
    }

    /// <summary>
    /// 普通类对象池数据，支持泛型 T
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ClassPoolData<T> : IClassPoolData where T : new()
    {
        private int _maxCapacity;
        public int MaxCapacity => _maxCapacity;
        // 使用 Stack 提高缓存命中率 (LIFO)
        private Stack<T> _poolStack;
        
        public int Count => _poolStack?.Count ?? 0;

        public ClassPoolData(int maxCapacity = -1)
        {
            this._maxCapacity = maxCapacity;
            this._poolStack = maxCapacity < 0 ? new Stack<T>() : new Stack<T>(maxCapacity);
        }

        public void Push(T obj)
        {
            if (obj == null) return;

            // 检查容量限制
            if (_maxCapacity > 0 && _poolStack.Count >= _maxCapacity)
            {
                // 超出容量，直接丢弃供 GC 回收
                return;
            }
            
            _poolStack.Push(obj);
        }

        public bool TryGet(out T obj)
        {
            if (_poolStack.Count > 0)
            {
                obj = _poolStack.Pop();
                return true;
            }
            
            obj = default;
            return false;
        }

        public void Clear()
        {
            _poolStack.Clear();
        }
    }
}

