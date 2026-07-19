using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using WS_Modules.Pooling;

namespace WS_Modules
{
    internal sealed class PoolStatsService : IPoolStatsService
    {
        public void CollectSnapshot(out List<PoolItemData> gameObjectPools, out List<PoolItemData> classPools)
        {
            gameObjectPools = new List<PoolItemData>();
            classPools = new List<PoolItemData>();

            var poolManager = PoolManager.Instance;

            var gameObjectPool = poolManager.GetType().GetField("_gameObjectPoolModule",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(poolManager);
            var classPool = poolManager.GetType().GetField("_classPoolModule",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(poolManager);

            if ((gameObjectPool == null || classPool == null) && WSFrameRoot.Instance != null &&
                WSFrameRoot.Instance.FrameSetting != null)
            {
                try
                {
                    PoolManager.Instance.Initialize(WSFrameRoot.Instance.FrameSetting.PoolingSettings);
                    gameObjectPool = poolManager.GetType().GetField("_gameObjectPoolModule",
                            BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.GetValue(poolManager);
                    classPool = poolManager.GetType().GetField("_classPoolModule",
                            BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.GetValue(poolManager);
                    Debug.Log("[FrameSetting] Auto-initialized PoolManager from WSFrameRoot.");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FrameSetting] Auto-initialize PoolManager failed: {ex.Message}");
                }
            }

            if (gameObjectPool == null || classPool == null)
            {
                return;
            }

            Dictionary<string, GameObjectPoolData> gameObjectPoolDic = null;
            Dictionary<Type, IClassPoolData> classPoolDic = null;

            var f = FindDictionaryField(gameObjectPool.GetType(), typeof(string), typeof(GameObjectPoolData));
            if (f != null)
            {
                var val = f.GetValue(gameObjectPool);
                gameObjectPoolDic = val as Dictionary<string, GameObjectPoolData>;
            }

            if (gameObjectPoolDic == null)
            {
                var obj = FindDictionaryInstanceByAssignable(gameObjectPool, typeof(string), typeof(GameObjectPoolData));
                if (obj != null)
                    gameObjectPoolDic = obj as Dictionary<string, GameObjectPoolData>;
            }

            if (gameObjectPoolDic == null)
            {
                Debug.LogWarning(
                    $"[FrameSetting] Could not find Dictionary<string,GameObjectPoolData> in {gameObjectPool.GetType().FullName}.\nFields: {string.Join(", ", gameObjectPool.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public).Select(x => x.Name))}\nProperties: {string.Join(", ", gameObjectPool.GetType().GetProperties(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public).Select(x => x.Name))}");
            }

            var f2 = FindDictionaryField(classPool.GetType(), typeof(Type), typeof(IClassPoolData));
            if (f2 != null)
            {
                var val2 = f2.GetValue(classPool);
                classPoolDic = val2 as Dictionary<Type, IClassPoolData>;
            }

            if (classPoolDic == null)
            {
                var obj2 = FindDictionaryInstanceByAssignable(classPool, typeof(Type), typeof(IClassPoolData));
                if (obj2 != null) classPoolDic = obj2 as Dictionary<Type, IClassPoolData>;
            }

            if (classPoolDic == null)
            {
                Debug.LogWarning(
                    $"[FrameSetting] Could not find Dictionary<Type,IClassPoolData> in {classPool.GetType().FullName}.\nFields: {string.Join(", ", classPool.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public).Select(x => x.Name))}\nProperties: {string.Join(", ", classPool.GetType().GetProperties(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public).Select(x => x.Name))}");
            }

            if (gameObjectPoolDic != null)
            {
                gameObjectPools = gameObjectPoolDic.Select(kvp => new PoolItemData
                {
                    Name = kvp.Key,
                    Count = kvp.Value.Count,
                    MaxCapacity = kvp.Value.MaxCapacity,
                }).ToList();
            }

            if (classPoolDic != null)
            {
                classPools = classPoolDic.Select(kvp => new PoolItemData
                {
                    Name = kvp.Key.Name,
                    Count = kvp.Value.Count,
                    MaxCapacity = kvp.Value.MaxCapacity,
                }).ToList();
            }
        }

        private FieldInfo FindDictionaryField(Type containerType, Type keyType, Type valueType)
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public;
            foreach (var f in containerType.GetFields(flags))
            {
                var ft = f.FieldType;
                if (!ft.IsGenericType) continue;
                var def = ft.GetGenericTypeDefinition();
                if (def != typeof(Dictionary<,>)) continue;
                var args = ft.GetGenericArguments();
                if (args.Length != 2) continue;
                if (args[0] == keyType && args[1] == valueType) return f;
            }

            return null;
        }

        private object FindDictionaryInstanceByAssignable(object containerInstance, Type keyType, Type valueType)
        {
            var containerType = containerInstance.GetType();
            var flags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public;

            foreach (var f in containerType.GetFields(flags))
            {
                var ft = f.FieldType;
                if (!ft.IsGenericType) continue;
                var def = ft.GetGenericTypeDefinition();
                if (def != typeof(Dictionary<,>)) continue;
                var args = ft.GetGenericArguments();
                if (args.Length != 2) continue;
                if (IsAssignableGenericArg(args[0], keyType) && IsAssignableGenericArg(args[1], valueType))
                {
                    return f.GetValue(containerInstance);
                }
            }

            foreach (var p in containerType.GetProperties(flags))
            {
                var pt = p.PropertyType;
                if (!pt.IsGenericType) continue;
                var def = pt.GetGenericTypeDefinition();
                if (def != typeof(Dictionary<,>)) continue;
                var args = pt.GetGenericArguments();
                if (args.Length != 2) continue;
                if (IsAssignableGenericArg(args[0], keyType) && IsAssignableGenericArg(args[1], valueType))
                {
                    try
                    {
                        return p.GetValue(containerInstance);
                    }
                    catch
                    {
                        /* ignore getter exceptions */
                    }
                }
            }

            return null;
        }

        private bool IsAssignableGenericArg(Type actualArg, Type desired)
        {
            if (actualArg == desired) return true;
            if (desired.IsAssignableFrom(actualArg)) return true;
            if (actualArg.IsAssignableFrom(desired)) return true;
            return false;
        }
    }
}

