using System;
using System.Collections.Generic;
using UnityEngine;

namespace WS_Modules.Singleton
{
    /// <summary>
    /// AutoSingleton 的预配置注册表。
    /// </summary>
    public static class AutoSingletonConfigRegistry
    {
        private static readonly Dictionary<Type, object> ConfigMap = new Dictionary<Type, object>(32);

        public static bool Register<TSingleton, TConfig>(TConfig config)
            where TSingleton : AutoSingletonMonoBase<TSingleton>
        {
            if (AutoSingletonMonoBase<TSingleton>.IsCreated)
            {
                Debug.LogWarning($"{typeof(TSingleton).Name} 已创建，配置注册被拒绝。");
                return false;
            }

            ConfigMap[typeof(TSingleton)] = config;
            return true;
        }

        public static bool TryGet<TSingleton, TConfig>(out TConfig config)
            where TSingleton : AutoSingletonMonoBase<TSingleton>
        {
            if (ConfigMap.TryGetValue(typeof(TSingleton), out var value) && value is TConfig typed)
            {
                config = typed;
                return true;
            }

            config = default;
            return false;
        }

        public static bool Clear<TSingleton>()
            where TSingleton : AutoSingletonMonoBase<TSingleton>
        {
            if (AutoSingletonMonoBase<TSingleton>.IsCreated)
            {
                Debug.LogWarning($"{typeof(TSingleton).Name} 已创建，配置清除被拒绝。");
                return false;
            }

            return ConfigMap.Remove(typeof(TSingleton));
        }

        public static void ClearAll()
        {
            ConfigMap.Clear();
        }
    }
}
