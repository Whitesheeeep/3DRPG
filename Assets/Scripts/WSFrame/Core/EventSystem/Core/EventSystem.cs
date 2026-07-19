using System;
using UnityEngine;

namespace WS_Modules.CustomEventSystem
{
    public static class EventSystem
    {
        // 这里我们使用了三个不同类型的事件中心来管理不同类型的事件，分别是字符串类型、类型类型和整数类型
        // 整数类型使用枚举来管理，字符串类型使用字符串来管理，类型类型使用 Type 来管理
        private static readonly IEventCenter<string> _stringEventCenter = new EventCenterModule<string>();
        private static readonly IEventCenter<Type> _typeEventCenter = new EventCenterModule<Type>();
        private static readonly IEventCenter<int> _intEventCenter = new EventCenterModule<int>();

        public static IUnRegister Register_String<T>(string key, Action<T> handler)
        {
            return _stringEventCenter.Register(key, handler);
        }

        public static IUnRegister Register_Type<T>(Type key, Action<T> handler)
        {
            return _typeEventCenter.Register(key, handler);
        }

        public static IUnRegister Register_Int<T>(int key, Action<T> handler)
        {
            return _intEventCenter.Register(key, handler);
        }

        public static void UnRegister<T>(string key, Action<T> handler)
        {
            _stringEventCenter.UnRegister(key, handler);
        }

        // 新增：Type 类型的 UnRegister
        public static void UnRegister_Type<T>(Type key, Action<T> handler)
        {
            _typeEventCenter.UnRegister(key, handler);
        }

        // 新增：int 类型的 UnRegister
        public static void UnRegister_Int<T>(int key, Action<T> handler)
        {
            _intEventCenter.UnRegister(key, handler);
        }

        public static void EventTrigger_String<T>(string key, T @event)
        {
            _stringEventCenter.EventTrigger(key, @event);
        }

        // 新增：Type 类型的 EventTrigger
        public static void EventTrigger_Type<T>(Type key, T @event)
        {
            _typeEventCenter.EventTrigger(key, @event);
        }

        // 新增：int 类型的 EventTrigger
        public static void EventTrigger_Int<T>(int key, T @event)
        {
            _intEventCenter.EventTrigger(key, @event);
        }

        public static void Clear()
        {
            _stringEventCenter.Clear();
            _typeEventCenter.Clear();
            _intEventCenter.Clear();
        }

        public static void ClearEvent_String(string key)
        {
            _stringEventCenter.ClearEvent(key);
        }

        public static void ClearEvent_Type(Type key)
        {
            _typeEventCenter.ClearEvent(key);
        }

        public static void ClearEvent_Int(int key)
        {
            _intEventCenter.ClearEvent(key);
        }
    }
}