using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.Extensions;

namespace WS_Modules.CustomEventSystem
{
    public interface IRegister
    {
        IUnRegister Register(Action onAnyEventInvoke);
    }

    #region IUnRegister 相关：提供一种统一的接口来管理事件的取消注册，特别适用于在特定条件下（如 GameObject 销毁或禁用时）自动取消注册事件。
    public interface IUnRegister 
    {
        void UnRegister();
    }

    public static class IUnRegisterExtensions
    {
        public static void UnRegisterWhenGameObjectDestroyed(this IUnRegister self, GameObject gameObject)
        {
            gameObject.GetOrAddComponent<UnRegisterOnDestroyTrigger>().AddUnRegister(self);
        }

        public static void UnRegisterWhenGameObjectDisabled(this IUnRegister self, GameObject gameObject)
        {
            gameObject.GetOrAddComponent<UnRegisterOnDisableTrigger>().AddUnRegister(self);
        }

        public static void AddToUnregisterList(this IUnRegister self, IUnRegisterList unRegisterList)
        {
            unRegisterList.UnRegisterList.Add(self);
        }
    }

    public interface IUnRegisterList
    {
        List<IUnRegister> UnRegisterList { get; }
    }

    public static class IUnRegisterListExtensions
    {
        public static void UnRegisterAll(this IUnRegisterList self)
        {
            foreach (var unRegister in self.UnRegisterList)
            {
                unRegister.UnRegister();
            }
            self.UnRegisterList.Clear();
        }
    }

    // 命令模式
    public class CustomUnRegister : IUnRegister
    {
        private Action _onUnRegister;

        public CustomUnRegister(Action onUnRegister) => _onUnRegister = onUnRegister;
        
        public void UnRegister()
        {
            _onUnRegister?.Invoke();
            _onUnRegister = null;
        }
    }


    // 用于 MonoBehaviour 的生命周期触发取消注册事件的组件，提供了在 GameObject 销毁或禁用时自动取消注册事件的功能。
    public class UnRegisterTrigger : MonoBehaviour
    {
        private readonly HashSet<IUnRegister> UnRegisterSet = new();

        public IUnRegister AddUnRegister(IUnRegister unRegister)
        {
            UnRegisterSet.Add(unRegister);
            return unRegister;
        }

        public void RemoveUnRegister(IUnRegister unRegister) => UnRegisterSet.Remove(unRegister);

        public void UnRegister()
        {
            foreach (var unRegister in UnRegisterSet)
            {
                unRegister.UnRegister();
            }
            UnRegisterSet.Clear();
        }
    }
    
    public class UnRegisterOnDestroyTrigger : UnRegisterTrigger
    {
        private void OnDestroy()
        {
            UnRegister();
        }
    }

    public class UnRegisterOnDisableTrigger : UnRegisterTrigger
    {
        private void OnDisable()
        {
            UnRegister();
        }
    }
    #endregion

    /*public class UnRegisterCurrentSceneUnloadedTrigger : UnRegisterTrigger
    {
        private static UnRegisterCurrentSceneUnloadedTrigger mDefault;

        public static UnRegisterCurrentSceneUnloadedTrigger Get
        {
            get
            {
                if (!mDefault)
                {
                    mDefault = new GameObject("UnRegisterCurrentSceneUnloadedTrigger")
                        .AddComponent<UnRegisterCurrentSceneUnloadedTrigger>();
                }

                return mDefault;
            }
        }

        private void Awake()
        {
            DontDestroyOnLoad(this);
            hideFlags = HideFlags.HideInHierarchy;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private void OnDestroy() => SceneManager.sceneUnloaded -= OnSceneUnloaded;
        void OnSceneUnloaded(Scene scene) => UnRegister();
    }*/

    public interface IEasyEvent
    {
        void Clear();
    }
    // ---------------------------- 新增：基于泛型键 TKey 的版本 ----------------------------
    public interface IEventCenter<TKey> where TKey : notnull
    {
        IUnRegister Register<T>(TKey key, Action<T> handler);
        void UnRegister<T>(TKey key, Action<T> handler);
        void EventTrigger<T>(TKey key, T @event);
        void Clear();
        void ClearEvent(TKey key);
    }

    public interface IStructEventCenter<TKey> where TKey : notnull
    {
        void Register<T>(TKey key, Action<T> handler) where T : struct;
        void UnRegister<T>(TKey key, Action<T> handler) where T : struct;
        void EventTrigger<T>(TKey key, T @event) where T : struct;
        void Clear();
        void ClearEvent(TKey key);
    }
}