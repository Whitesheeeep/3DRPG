namespace WS_Modules.Pooling
{
    public interface IPoolable
    {
        int MaxCount { get;}
        int InitCount { get;}
        void OnSpawn();
        void OnDespawn();
    }
}