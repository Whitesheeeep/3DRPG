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

        /// <summary>
        /// 注册首个挂载实例并使其跨场景保留；重复对象会立即销毁。
        /// </summary>
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

        /// <summary>
        /// 当前挂载实例销毁时清空静态引用，避免场景退出后保留 Unity 伪空对象。
        /// </summary>
        protected virtual void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
