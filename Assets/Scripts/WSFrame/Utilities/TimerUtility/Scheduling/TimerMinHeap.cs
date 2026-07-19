using System.Collections.Generic;

namespace WS_Modules.Utilities
{
    internal sealed class TimerMinHeap
    {
        private readonly List<Timer> _items = new List<Timer>(100);

        public int Count => _items.Count;

        public Timer Peek() => _items[0];

        public void Add(Timer timer)
        {
            timer.heapIndex = _items.Count;
            _items.Add(timer);
            SiftUp(timer.heapIndex);
        }

        public Timer Pop()
        {
            var root = _items[0];
            Remove(root);
            return root;
        }

        public void Remove(Timer timer)
        {
            int index = timer.heapIndex;
            if (index < 0 || index >= _items.Count || _items[index] != timer)
            {
                timer.heapIndex = -1;
                return;
            }

            int lastIndex = _items.Count - 1;
            if (index < lastIndex)
            {
                Swap(index, lastIndex);
            }

            _items.RemoveAt(lastIndex);
            timer.heapIndex = -1;

            if (index < _items.Count)
            {
                SiftDown(index);
                SiftUp(index);
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                _items[i].heapIndex = -1;
            }

            _items.Clear();
        }

        private void SiftUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (_items[parent].nextDueTime <= _items[index].nextDueTime)
                {
                    break;
                }

                Swap(parent, index);
                index = parent;
            }
        }

        private void SiftDown(int index)
        {
            while (true)
            {
                int left = index * 2 + 1;
                int right = left + 1;
                int smallest = index;

                if (left < _items.Count && _items[left].nextDueTime < _items[smallest].nextDueTime)
                {
                    smallest = left;
                }

                if (right < _items.Count && _items[right].nextDueTime < _items[smallest].nextDueTime)
                {
                    smallest = right;
                }

                if (smallest == index)
                {
                    break;
                }

                Swap(index, smallest);
                index = smallest;
            }
        }

        private void Swap(int a, int b)
        {
            (_items[a], _items[b]) = (_items[b], _items[a]);
            _items[a].heapIndex = a;
            _items[b].heapIndex = b;
        }
    }
}
