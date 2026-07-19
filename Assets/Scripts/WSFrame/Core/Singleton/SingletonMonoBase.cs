using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WS_Modules.Singleton
{
    /// <summary>
    /// 挂载式（必须挂载到场景上），继承 Mono Behaviour 的单例基类
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class SingletonMonoBase<T> : MonoBehaviour
        where T : MonoBehaviour
    {
        private static T _instance;
        public static T Instance => _instance;

        protected virtual void Awake()
        {
            if (_instance == null)
            {
                _instance = this as T;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }
}
