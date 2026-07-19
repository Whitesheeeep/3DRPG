namespace WS_Modules.Singleton
{
    /// <summary>
    /// 带预配置能力的 AutoSingleton 基类。
    /// Init 时从 AutoConfigRegistry 读取配置，未注册时回退到默认配置。
    /// </summary>
    public abstract class AutoConfigSingletonMonoBase<TSingleton, TConfig> : AutoSingletonMonoBase<TSingleton>
        where TSingleton : AutoConfigSingletonMonoBase<TSingleton, TConfig>
    {
        public sealed override void Init()
        {
            if (!AutoSingletonConfigRegistry.TryGet<TSingleton, TConfig>(out var config))
            {
                config = CreateDefaultConfig();
            }

            InitWithConfig(config);
        }

        protected abstract TConfig CreateDefaultConfig();
        protected abstract void InitWithConfig(TConfig config);
    }
}
