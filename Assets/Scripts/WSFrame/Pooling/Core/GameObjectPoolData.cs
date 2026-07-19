using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using WS_Modules.Extensions;
using WS_Modules.LogModule;

namespace WS_Modules.Pooling
{
    /// <summary>
    /// 对象池数据基础，用于存储对象池相关的数据和逻辑，支持容量限制和抽屉式管理
    /// </summary>
    public class GameObjectPoolData
    {
        private Transform _root;
        // -1 代表不限制容量
        private int _maxCapacity;
        public int MaxCapacity => _maxCapacity;
        // 存储相关数据的栈
        private Queue<GameObject> _poolStack;
        
        // 是否开启抽屉
        public static bool IsOpenLayout { get; set; } = true;
        public int Count => _poolStack?.Count ?? 0;

        public GameObjectPoolData(GameObject poolRootGo, int maxCapacity = -1, string name = "PoolRoot")
        {
            InitPool(poolRootGo, maxCapacity, name);
        }

        public GameObjectPoolData(Transform poolRootTrans, int maxCapacity = -1, string name = "PoolRoot")
        {
            InitPool(poolRootTrans, maxCapacity, name);
        }

        public void InitPool(GameObject poolRootGo, int maxCapacity = -1, string name = "PoolRoot") =>
            InitPool(poolRootGo.transform, maxCapacity, name);

        public void InitPool(Transform poolRootTrans, int maxCapacity = -1, string name = "PoolRoot")
        {
            this._maxCapacity = maxCapacity;
            this._poolStack = maxCapacity < 0 ? new Queue<GameObject>() : new Queue<GameObject>(maxCapacity);

            if (IsOpenLayout)
            {
                _root ??= new GameObject(name).transform;
                _root.SetParent(poolRootTrans);
            }
            else
            {
                // 如果不开启抽屉，直接使用池根对象作为父对象
                _root = poolRootTrans;
            }
        }

        public void EnsureMaxCapacity(int maxCapacity)
        {
            if (_maxCapacity == -1) return;

            if (maxCapacity == -1 || maxCapacity > _maxCapacity)
            {
                _maxCapacity = maxCapacity;
            }
        }

        public void PushObj(GameObject go)
        {
            if (go is null) return;

            // 不能超出容量
            if (_maxCapacity > 0 && _poolStack.Count >= _maxCapacity)
            {
                ClearEditorSelectionIfNeeded(go);
                GameObject.Destroy(go);
            }
            else
            {
                ClearEditorSelectionIfNeeded(go);
                _poolStack.Enqueue(go);
                go.SetActive(false);
                if(_root) go.transform.SetParent(_root);
            }
        }

        public void PushObjs([DisallowNull] GameObject[] gos)
        {
            foreach (var go in gos)
            {
                PushObj(go);
            }
        }
        
        public void PushObjs([DisallowNull] List<GameObject> gos)
        {
            foreach (var go in gos)
            {
                PushObj(go);
            }
        }

        public bool TryGet(out GameObject go, Transform parent = null)
        {
            if (_poolStack.Count > 0)
            {
                go = _poolStack.Dequeue();
                if (go)
                {
                    go.transform.SetParent(parent);
                    go.SetActive(true);
                    if (parent is null) go.MoveToActiveScene();
                    return true;
                }
            }

            WSLog.LogWarning("对象池已空，无法获取对象，请检查是否预热对象或者增加池容量");
            go = null;

            return false;
        }

        public bool TryGetSome(int count, out List<GameObject> gos, Transform parent = null)
        {
            if (count <= 0)
            {
                WSLog.LogWarning("请求的对象数量必须大于0");
                gos = new List<GameObject>();
                return false;
            }
            
            gos = new List<GameObject>(count);
            if (_poolStack.Count >= count)
            {
                for (int i = 0; i < count; i++)
                {
                    var go = _poolStack.Dequeue();
                    if (go)
                    {
                        go.SetActive(true);
                        go.transform.SetParent(parent);
                        if (parent is null) go.MoveToActiveScene();
                        gos.Add(go);
                    }
                }
                return true;
            }
            else
            {
                WSLog.LogWarning("对象池中可用对象数量不足，无法获取请求的对象数量，请检查是否预热对象或者增加池容量");
                return false;
            }
        }
        
        private static void ClearEditorSelectionIfNeeded(GameObject root)
        {
        }

        public void ClearPool()
        {
            if (_poolStack == null) return;
            while (_poolStack.Count > 0)
            {
                var go = _poolStack.Dequeue();
                if (go != null)
                    ClearEditorSelectionIfNeeded(go);
                GameObject.Destroy(go);
            }

            if (IsOpenLayout && _root != null)
            {
                ClearEditorSelectionIfNeeded(_root.gameObject);
                GameObject.Destroy(_root.gameObject);
            }

            _root = null;
            _poolStack = new Queue<GameObject>();
        }
    }
}


