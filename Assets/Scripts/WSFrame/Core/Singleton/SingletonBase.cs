using System;
using System.Reflection;
using UnityEngine;

namespace WS_Modules.Singleton
{
    /// <summary>
    /// 该类没有继承 MonoBehaviour
    /// 要求继承对象要有一个私有的构造函数，可以通过调用私有构造函数进行初始化。
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class SingletonBase<T>
        where T : class
    {
        private static T _instance;

        // 加锁
        private static readonly object _lock = new object();
        public static T Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            ConstructorInfo info = typeof(T).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                                null, Type.EmptyTypes, null);
                            if (info == null)
                            {
                                throw new Exception("SingletonBase: " + typeof(T).Name + " must have a private constructor.");
                            }
                            _instance = info.Invoke(null) as T;
                            Debug.Log($"SingletonBase: Created instance of {typeof(T).Name}");
                        }
                    }
                }
                return _instance;
            }
        }
    }
}