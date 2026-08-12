#if UNITY_EDITOR
using UnityEngine;

namespace WS_Modules.Pooling
{
    /// <summary>
    /// 记录 GameObject 对象池生命周期次数与 Spawn 时 Transform 状态的手动测试探针。
    /// </summary>
    public sealed class GameObjectPoolLifecycleProbe : PoolObjectIdentity
    {
        /// <summary>获取累计 Spawn 回调次数。</summary>
        public int SpawnCount { get; private set; }
        /// <summary>获取累计 Despawn 回调次数。</summary>
        public int DespawnCount { get; private set; }
        /// <summary>获取最近一次 Spawn 回调观察到的局部位置。</summary>
        public Vector3 LastSpawnLocalPosition { get; private set; }
        /// <summary>获取最近一次 Spawn 回调观察到的局部旋转。</summary>
        public Quaternion LastSpawnLocalRotation { get; private set; }
        /// <summary>获取最近一次 Spawn 回调观察到的局部缩放。</summary>
        public Vector3 LastSpawnLocalScale { get; private set; }

        /// <summary>记录对象完成 Parent 与 Transform 准备后的生成状态。</summary>
        protected override void OnSpawn()
        {
            SpawnCount++;
            LastSpawnLocalPosition = transform.localPosition;
            LastSpawnLocalRotation = transform.localRotation;
            LastSpawnLocalScale = transform.localScale;
        }

        /// <summary>记录对象归还池前收到的清理通知。</summary>
        protected override void OnDespawn() => DespawnCount++;
    }
}
#endif
