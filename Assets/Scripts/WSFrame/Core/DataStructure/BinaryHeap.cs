using System;
using System.Collections.Generic;

namespace WS_Modules.DataStructure
{
    /// <summary>
    /// 通用二叉堆。比较结果越小，优先级越高，Pop 返回当前优先级最高的元素。
    /// </summary>
    public sealed class BinaryHeap<T>
    {
        private readonly List<T> items = new List<T>();
        private readonly IComparer<T> comparer;

        public BinaryHeap(IComparer<T> comparer)
        {
            this.comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
        }

        public BinaryHeap(Comparison<T> comparison)
            : this(Comparer<T>.Create(comparison ?? throw new ArgumentNullException(nameof(comparison))))
        {
        }

        public int Count => items.Count;

        public void Clear()
        {
            items.Clear();
        }

        public void Push(T item)
        {
            items.Add(item);
            SiftUp(items.Count - 1);
        }

        public T Pop()
        {
            T root = items[0];
            int lastIndex = items.Count - 1;
            items[0] = items[lastIndex];
            items.RemoveAt(lastIndex);

            if (items.Count > 0)
            {
                SiftDown(0);
            }

            return root;
        }

        private void SiftUp(int index)
        {
            while (index > 0)
            {
                int parentIndex = (index - 1) / 2;
                if (Compare(items[parentIndex], items[index]) <= 0)
                {
                    break;
                }

                Swap(parentIndex, index);
                index = parentIndex;
            }
        }

        private void SiftDown(int index)
        {
            while (true)
            {
                int leftIndex = index * 2 + 1;
                int rightIndex = leftIndex + 1;
                int smallestIndex = index;

                if (leftIndex < items.Count && Compare(items[leftIndex], items[smallestIndex]) < 0)
                {
                    smallestIndex = leftIndex;
                }

                if (rightIndex < items.Count && Compare(items[rightIndex], items[smallestIndex]) < 0)
                {
                    smallestIndex = rightIndex;
                }

                if (smallestIndex == index)
                {
                    break;
                }

                Swap(index, smallestIndex);
                index = smallestIndex;
            }
        }

        private void Swap(int a, int b) => (items[a], items[b]) = (items[b], items[a]);

        private int Compare(T a, T b) => comparer.Compare(a, b);
    }
}
