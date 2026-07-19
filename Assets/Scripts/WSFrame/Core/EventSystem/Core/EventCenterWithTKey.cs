using System;
using System.Collections.Generic;

namespace WS_Modules.CustomEventSystem
{
    /// <summary>
    /// 事件信息类，用于存储事件的订阅者和触发事件
    /// </summary>
    /// <typeparam name="T">事件触发时传递的数据参数</typeparam>
    public class EasyEvent<T> : IEasyEvent
    {
        private Action<T> OnEvent;

        public EasyEvent()
        {
        }

        public EasyEvent(out IUnRegister unRegister, Action<T> onEvent)
        {
            OnEvent = onEvent;
            unRegister = new CustomUnRegister(() => Unregister(onEvent));
        }

        public IUnRegister Register(Action<T> handler)
        {
            OnEvent += handler;
            return new CustomUnRegister(() => Unregister(handler));
        }

        public void Unregister(Action<T> handler)
        {
            OnEvent -= handler;
        }

        public void Invoke(T eventData)
        {
            OnEvent?.Invoke(eventData);
        }

        public void Clear()
        {
            OnEvent = null;
        }
    }

    /// <summary>
    /// 值类型的事件信息类，用于存储事件的订阅者和触发事件
    /// </summary>
    /// <typeparam name="T">传递的数据类型必须为值类型</typeparam>
    public class StructEasyEvent<T> : IEasyEvent where T : struct
    {
        public event Action<T> OnEvent;

        public StructEasyEvent(Action<T> onEvent = null)
        {
            OnEvent = onEvent;
        }

        public void Register(Action<T> handler)
        {
            OnEvent += handler;
        }

        public void Unregister(Action<T> handler)
        {
            OnEvent -= handler;
        }

        public void Invoke(T eventData)
        {
            OnEvent?.Invoke(eventData);
        }

        public void Clear()
        {
            OnEvent = null;
        }
    }

    public class EventCenterModule<TKey> : IEventCenter<TKey> where TKey : notnull
    {
        private readonly Dictionary<TKey, IEasyEvent> eventDic = new();

        public IUnRegister Register<T>(TKey key, Action<T> handler)
        {
            IUnRegister unRegister;
            if (eventDic.TryGetValue(key, out var eventInfoBase))
            {
                var eventInfo = eventInfoBase as EasyEvent<T> ??
                                throw new InvalidOperationException(
                                    $"[EventCenter<{typeof(TKey).Name}>] Type mismatch when subscribing at key '{key}'.");
                unRegister = eventInfo.Register(handler);
            }
            else
            {
                eventDic[key] = new EasyEvent<T>(out unRegister, handler);
            }

            return unRegister;
        }

        public void UnRegister<T>(TKey key, Action<T> handler)
        {
            if (eventDic.TryGetValue(key, out var eventInfoBase))
            {
                var eventInfo = eventInfoBase as EasyEvent<T> ??
                                throw new InvalidOperationException(
                                    $"[EventCenter<{typeof(TKey).Name}>] Type mismatch when unsubscribing at key '{key}'.");
                eventInfo.Unregister(handler);
            }
        }

        public void EventTrigger<T>(TKey key, T @event)
        {
            if (eventDic.TryGetValue(key, out var eventInfoBase))
            {
                (eventInfoBase as EasyEvent<T>)?.Invoke(@event);
            }
        }

        public void Clear()
        {
            foreach (var eventInfo in eventDic.Values)
            {
                eventInfo.Clear();
            }

            eventDic.Clear();
        }

        public void ClearEvent(TKey key)
        {
            if (eventDic.TryGetValue(key, out var eventInfo))
            {
                eventInfo.Clear();
                eventDic.Remove(key);
            }
        }
    }

    /// <summary>
    /// 严格要求传输的事件参数为值类型的事件中心模块，适用于需要频繁触发且对性能有较高要求的场景，可以避免装箱和垃圾回收带来的性能损失
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    public class StructEventCenterModule<TKey> : IStructEventCenter<TKey> where TKey : notnull
    {
        private readonly Dictionary<TKey, IEasyEvent> eventDic = new();

        public void Register<T>(TKey key, Action<T> handler) where T : struct
        {
            if (eventDic.TryGetValue(key, out var eventInfoBase))
            {
                var eventInfo = eventInfoBase as StructEasyEvent<T> ??
                                throw new InvalidOperationException(
                                    $"[StructEventCenter<{typeof(TKey).Name}>] Type mismatch when subscribing at key '{key}'.");
                eventInfo.OnEvent += handler;
            }
            else
            {
                eventDic[key] = new StructEasyEvent<T>(handler);
            }
        }

        public void UnRegister<T>(TKey key, Action<T> handler) where T : struct
        {
            if (eventDic.TryGetValue(key, out var eventInfoBase))
            {
                var eventInfo = eventInfoBase as StructEasyEvent<T> ??
                                throw new InvalidOperationException(
                                    $"[StructEventCenter<{typeof(TKey).Name}>] Type mismatch when unsubscribing at key '{key}'.");
                eventInfo.OnEvent -= handler;
            }
        }

        public void EventTrigger<T>(TKey key, T @event) where T : struct
        {
            if (eventDic.TryGetValue(key, out var eventInfoBase))
            {
                (eventInfoBase as StructEasyEvent<T>)?.Invoke(@event);
            }
        }

        public void Clear()
        {
            foreach (var eventInfo in eventDic.Values)
            {
                eventInfo.Clear();
            }

            eventDic.Clear();
        }

        public void ClearEvent(TKey key)
        {
            if (eventDic.TryGetValue(key, out var eventInfo))
            {
                eventInfo.Clear();
                eventDic.Remove(key);
            }
        }
    }
}
