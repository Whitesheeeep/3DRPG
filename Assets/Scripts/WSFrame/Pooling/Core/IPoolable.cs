namespace WS_Modules.Pooling
{
    /// <summary>定义普通 C# 类池对象的容量配置与生成、回收回调。</summary>
    public interface IPoolable
    {
        /// <summary>获取当前类型池的最大容量。</summary>
        int MaxCount { get;}
        /// <summary>获取当前类型池的初始预热数量。</summary>
        int InitCount { get;}
        /// <summary>对象从类池取出后执行准备。</summary>
        void OnSpawn();
        /// <summary>对象归还类池前执行清理。</summary>
        void OnDespawn();
    }

    /// <summary>
    /// 定义池化 GameObject 根节点的稳定身份与激活、回收生命周期入口。
    /// </summary>
    public interface IGameObjectPoolable
    {
        /// <summary>获取对象所属对象池的稳定 Key。</summary>
        string Key { get; }

        /// <summary>在 Parent 与 Transform 准备完成后激活对象并发送池化生成通知。</summary>
        void Spawn();

        /// <summary>在对象归还池根节点前发送池化回收通知并禁用对象。</summary>
        void Despawn();
    }

}
