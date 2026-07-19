using System;

namespace WS_Modules.Pooling
{
    [Serializable]
    public class PoolingSetting
    {
        public PoolPrewarmConfig GlobalPrewarmConfig;

        public E_ResLoadType ResLoadType { get; private set; }

        public void SetResLoadType(E_ResLoadType loadType)
        {
            ResLoadType = loadType;
        }
    }
}
