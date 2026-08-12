#if UNITY_EDITOR
using Sirenix.OdinInspector;
using UnityEngine;

namespace WS_Modules.Pooling
{
    /// <summary>
    /// 通过 Odin 按钮验证强制 GameObject 池身份、层级生命周期回调和复用顺序。
    /// </summary>
    public sealed class GameObjectPoolLifecycleOdinTester : MonoBehaviour
    {
        private const string TestPoolKey = "WSFrame.Pooling.LifecycleTest";

        #region Odin 操作

        /// <summary>执行首次预热、取出、回收和再次复用的完整生命周期测试。</summary>
        [Button("测试 GameObject Pool 生命周期", ButtonSizes.Large)]
        public void TestLifecycle()
        {
            PoolManager.Instance.ClearPool(TestPoolKey);
            GameObject source = CreatePoolSource();

            // 业务 Probe 本身就是池身份；预热先 Despawn，随后 Get 才发送第一次 Spawn。
            PoolManager.Instance.Prewarm(source, 1, 2);
            GameObject first = PoolManager.Instance.Get(source, transform);
            Expect("首次获取成功", first != null);
            if (first == null)
            {
                Destroy(source);
                return;
            }

            GameObjectPoolLifecycleProbe probe = first.GetComponent<GameObjectPoolLifecycleProbe>();
            Expect("业务组件同时提供池身份", probe != null);
            Expect("预热 Despawn 只执行一次", HaveCounts(probe, 1, 1));
            Expect("Spawn 前已恢复基础 Transform", HasExpectedSpawnTransform(probe));

            PoolManager.Instance.Recycle(first);
            Expect("首次回收 Despawn 累计两次", HaveCounts(probe, 1, 2));

            GameObject second = PoolManager.Instance.Get(source, transform);
            Expect("第二次获取复用同一实例", ReferenceEquals(first, second));
            Expect("第二次 Spawn 只增加一次", HaveCounts(probe, 2, 2));
            Expect("复用时再次恢复作者 Scale", HasExpectedSpawnTransform(probe));

            PoolManager.Instance.Recycle(second);
            PoolManager.Instance.ClearPool(TestPoolKey);
            Destroy(source);
        }

        /// <summary>验证缺少 IGameObjectPoolable 的 GameObject 会被严格拒绝。</summary>
        [Button("测试缺少 Pool Identity")]
        public void TestMissingIdentity()
        {
            GameObject invalid = new("Pool Lifecycle Invalid Source");
            GameObject result = PoolManager.Instance.Get(invalid);
            Expect("缺少 IGameObjectPoolable 时 Get 返回 null", result == null);
            Destroy(invalid);
        }

        #endregion

        #region 测试辅助

        /// <summary>创建由业务 Probe 直接承担池身份且带作者 Scale 的临时源对象。</summary>
        /// <returns>用于本次测试的临时源对象。</returns>
        private static GameObject CreatePoolSource()
        {
            GameObject root = new("Pool Lifecycle Source");
            root.transform.localScale = new Vector3(0.5f, 1.5f, 2f);
            root.AddComponent<GameObjectPoolLifecycleProbe>().ConfigureKey(TestPoolKey);
            return root;
        }

        /// <summary>检查池化业务探针的累计回调次数。</summary>
        /// <param name="probe">待检查的池化业务探针。</param>
        /// <param name="spawnCount">期望 Spawn 次数。</param>
        /// <param name="despawnCount">期望 Despawn 次数。</param>
        /// <returns>探针次数符合预期时返回 true。</returns>
        private static bool HaveCounts(
            GameObjectPoolLifecycleProbe probe,
            int spawnCount,
            int despawnCount)
        {
            return probe != null &&
                   probe.SpawnCount == spawnCount &&
                   probe.DespawnCount == despawnCount;
        }

        /// <summary>检查 Spawn 回调观察到的位置、旋转和作者 Scale 已恢复。</summary>
        /// <param name="probe">待检查的池化业务探针。</param>
        /// <returns>基础 Transform 与作者配置一致时返回 true。</returns>
        private static bool HasExpectedSpawnTransform(GameObjectPoolLifecycleProbe probe)
        {
            return probe != null &&
                   probe.LastSpawnLocalPosition == Vector3.zero &&
                   probe.LastSpawnLocalRotation == Quaternion.identity &&
                   probe.LastSpawnLocalScale == new Vector3(0.5f, 1.5f, 2f);
        }

        /// <summary>输出一项对象池手动测试断言。</summary>
        /// <param name="label">断言名称。</param>
        /// <param name="condition">断言是否通过。</param>
        private static void Expect(string label, bool condition)
        {
            if (condition)
                Debug.Log($"[GameObjectPoolTest][PASS] {label}");
            else
                Debug.LogError($"[GameObjectPoolTest][FAIL] {label}");
        }

        #endregion
    }
}
#endif
