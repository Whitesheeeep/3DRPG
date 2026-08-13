using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace WS_Modules.Pooling
{
    /// <summary>
    /// 作为池化 GameObject 根节点的标准身份，并统一执行基础 Transform、激活状态和业务生命周期。
    /// </summary>
    [DisallowMultipleComponent]
    public class PoolObjectIdentity : MonoBehaviour, IGameObjectPoolable
    {
        #region 作者配置与状态

        [FormerlySerializedAs("PoolKey")]
        [SerializeField, Tooltip("对象所属对象池的稳定 Key，必须与 Get/Prewarm 使用的 Key 一致。")]
        private string key;

        private bool scaleInitialized;
        private bool spawned;
        private Vector3 initialLocalScale;

        /// <summary>获取对象所属对象池的稳定 Key。</summary>
        public string Key => key;
        /// <summary>获取对象当前是否已经从池中取出。</summary>
        public bool IsSpawned => spawned;

        #endregion

        #region Unity 生命周期

        /// <summary>缓存 Prefab 作者配置的局部缩放，供每次生成和回收恢复。</summary>
        protected virtual void Awake()
        {
            initialLocalScale = transform.localScale;
            scaleInitialized = true;
        }

        private void OnValidate()
        {
            name = key;
        }
        #endregion

        #region 池生命周期

        /// <summary>为运行时动态创建的池化对象设置稳定 Key。</summary>
        /// <param name="poolKey">后续 Get、Recycle 使用的对象池 Key。</param>
        public void ConfigureKey(string poolKey) => key = poolKey;

        /// <summary>恢复基础 Transform、激活对象并执行子类生成准备。</summary>
        public void Spawn()
        {
            if (spawned) return;

            // 非激活 Prefab 的 Awake 可能尚未执行，首次 Spawn 仍需从作者 Transform 捕获缩放。
            EnsureInitialScale();
            // Parent 已由池数据设置；此处统一恢复相对于新 Parent 的基础局部 Transform。
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = initialLocalScale;
            gameObject.SetActive(true);

            OnSpawn();
            spawned = true;
        }

        /// <summary>执行子类回收清理、恢复基础 Transform 并禁用对象。</summary>
        public void Despawn()
        {
            if (!spawned) return;

            EnsureInitialScale();
            // 业务先清除本轮运行状态，再恢复基础 Transform 并禁用对象。
            OnDespawn();
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = initialLocalScale;
            gameObject.SetActive(false);

            spawned = false;
        }

        /// <summary>对象激活且基础 Transform 恢复后，由业务子类执行无上下文准备。</summary>
        protected virtual void OnSpawn() { }

        /// <summary>对象禁用前，由业务子类清除上一轮运行状态。</summary>
        protected virtual void OnDespawn() { }

        #endregion

        #region 内部辅助

        /// <summary>确保首次池生命周期发生前已经保存作者配置的局部缩放。</summary>
        private void EnsureInitialScale()
        {
            if (scaleInitialized) return;
            initialLocalScale = transform.localScale;
            scaleInitialized = true;
        }

        #endregion
    }
}
