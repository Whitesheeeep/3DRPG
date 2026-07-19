using System.Collections.Generic;

namespace WS_Modules
{
    internal interface IPoolStatsService
    {
        void CollectSnapshot(out List<PoolItemData> gameObjectPools, out List<PoolItemData> classPools);
    }
}

