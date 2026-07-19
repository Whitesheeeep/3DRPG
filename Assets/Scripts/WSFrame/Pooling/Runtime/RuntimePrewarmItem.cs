using System;

namespace WS_Modules.Pooling
{
    /// <summary>
    /// 代码侧 GameObject 预热配置项。
    /// </summary>
    internal sealed class GameObjectPoolPrewarmRequest
    {
        public string Key;
        public int InitCount;
        public int MaxCapacity;
    }

    /// <summary>
    /// class 预热请求。
    /// Apply 是强类型预热委托，可由代码配置或生成 Registry 提供。
    /// </summary>
    internal sealed class ClassPoolPrewarmRequest
    {
        public Type Type;
        public int InitCount;
        public int MaxCapacity;
        public Action<ClassPoolModule, int, int> Apply;
    }
}