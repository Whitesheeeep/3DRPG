using System;
using System.Collections.Generic;

namespace WS_Modules.Pooling
{
    /// <summary>
    /// 代码侧全局对象池预热配置模块。
    /// 用户在 Collect 中集中写入固定预热项，Processor 会在 PoolManager 初始化时读取一次。
    /// </summary>
    public sealed class CodePoolPrewarmConfigModule
    {
        /// <summary>
        /// 收集代码配置的全局预热项。
        /// 这里只做配置声明，不直接访问 PoolManager，也不执行预热逻辑。
        /// </summary>
        public void Collect(CodePoolPrewarmConfigBuilder builder)
        {
            if (builder == null) return;

            // Example:
            // builder.GameObject("Cube", 10, 20);
            // builder.Class(typeof(MyPoolableClass), 20, 100);
        }
    }

    /// <summary>
    /// 代码预热配置构建器。
    /// GameObject 使用资源 key；class 使用 typeof(T) 和强类型预热委托，避免运行时字符串类型解析和泛型反射调用。
    /// </summary>
    public sealed class CodePoolPrewarmConfigBuilder
    {
        private readonly List<GameObjectPoolPrewarmRequest> _gameObjectItems = new();
        private readonly List<ClassPoolPrewarmRequest> _classItems = new();

        /// <summary>
        /// 已收集的 GameObject 预热配置项。
        /// </summary>
        internal IReadOnlyList<GameObjectPoolPrewarmRequest> GameObjectItems => _gameObjectItems;

        /// <summary>
        /// 已收集的 class 预热配置项。
        /// </summary>
        internal IReadOnlyList<ClassPoolPrewarmRequest> ClassItems => _classItems;

        /// <summary>
        /// 添加 GameObject 对象池预热项。
        /// </summary>
        public void GameObject(string key, int initCount, int maxCapacity)
        {
            if (string.IsNullOrEmpty(key)) return;

            _gameObjectItems.Add(new GameObjectPoolPrewarmRequest
            {
                Key = key,
                InitCount = initCount,
                MaxCapacity = maxCapacity,
            });
        }

        /// <summary>
        /// 添加 class 对象池预热项。Processor 会用 Type 指针从生成表中查找强类型预热委托。
        /// </summary>
        public void Class(Type type, int initCount, int maxCapacity)
        {
            if (type == null) return;

            _classItems.Add(new ClassPoolPrewarmRequest
            {
                Type = type,
                InitCount = initCount,
                MaxCapacity = maxCapacity,
            });
        }

        /// <summary>
        /// 添加 class 对象池预热项。该重载直接保存编译期强类型预热委托。
        /// </summary>
        public void Class<T>(int initCount, int maxCapacity) where T : class, IPoolable, new()
        {
            _classItems.Add(new ClassPoolPrewarmRequest
            {
                Type = typeof(T),
                InitCount = initCount,
                MaxCapacity = maxCapacity,
                Apply = (module, count, capacity) => module.Prewarm<T>(count, capacity),
            });
        }

        /// <summary>
        /// 释放构建器持有的临时配置和委托引用。
        /// </summary>
        internal void Release()
        {
            _gameObjectItems.Clear();
            _classItems.Clear();
        }
    }
}
