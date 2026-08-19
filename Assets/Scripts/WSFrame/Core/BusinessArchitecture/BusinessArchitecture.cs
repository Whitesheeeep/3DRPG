using System;
using System.Collections.Generic;
using System.Linq;
using WS_Modules.CustomEventSystem;
using WSEventSystem = WS_Modules.CustomEventSystem.EventSystem;

namespace WS_Modules.BusinessArchitecture
{
    #region Architecture

    // 业务层入口。UIManager、PoolManager、ResSystem、SceneSystem 等
    // WSFrame 运行时基础设施仍然由 WSFrame 自己负责。
    public interface IArchitecture
    {
        void RegisterManager<T>(T manager) where T : IManager;
        void RegisterSystem<T>(T system) where T : ISystem;
        void RegisterUtility<T>(T utility) where T : IUtility;

        T GetManager<T>() where T : class, IManager;
        T GetSystem<T>() where T : class, ISystem;
        T GetUtility<T>() where T : class, IUtility;

        void SendCommand<T>(T command) where T : ICommand;
        TResult SendCommand<TResult>(ICommand<TResult> command);
        TResult SendQuery<TResult>(IQuery<TResult> query);

        void SendEvent<T>() where T : new();
        void SendEvent<T>(T e);
        IUnRegister RegisterEvent<T>(Action<T> onEvent);
        void UnRegisterEvent<T>(Action<T> onEvent);

        void Deinit();
    }

    public abstract class Architecture<T> : IArchitecture where T : Architecture<T>, new()
    {
        private readonly IOCContainer container = new IOCContainer();
        private bool inited;

        protected static T architecture;
        public static Action<T> OnRegisterPatch = _ => { };

        public static IArchitecture Interface
        {
            get
            {
                if (architecture == null)
                {
                    InitArchitecture();
                }

                return architecture;
            }
        }

        public static void InitArchitecture()
        {
            if (architecture != null)
            {
                return;
            }

            architecture = new T();
            architecture.Init();

            // 保持 QFramework 风格的两阶段启动：
            // 先在 Init 中注册模块，再初始化持有数据的 Manager，最后初始化业务流程 System。
            OnRegisterPatch?.Invoke(architecture);

            foreach (var manager in architecture.container.GetInstancesByType<IManager>().Where(manager => !manager.Initialized))
            {
                manager.Init();
                manager.Initialized = true;
            }

            foreach (var system in architecture.container.GetInstancesByType<ISystem>().Where(system => !system.Initialized))
            {
                system.Init();
                system.Initialized = true;
            }

            architecture.inited = true;
        }

        protected abstract void Init();

        public void Deinit()
        {
            OnDeinit();

            foreach (var system in container.GetInstancesByType<ISystem>().Where(system => system.Initialized))
            {
                system.Deinit();
                system.Initialized = false;
            }

            foreach (var manager in container.GetInstancesByType<IManager>().Where(manager => manager.Initialized))
            {
                manager.Deinit();
                manager.Initialized = false;
            }

            container.Clear();
            inited = false;
            architecture = null;
        }

        protected virtual void OnDeinit()
        {
        }

        public void RegisterManager<TManager>(TManager manager) where TManager : IManager
        {
            manager.SetArchitecture(this);
            container.Register(manager);

            if (!inited)
            {
                return;
            }

            manager.Init();
            manager.Initialized = true;
        }

        public void RegisterSystem<TSystem>(TSystem system) where TSystem : ISystem
        {
            system.SetArchitecture(this);
            container.Register(system);

            if (!inited)
            {
                return;
            }

            system.Init();
            system.Initialized = true;
        }

        public void RegisterUtility<TUtility>(TUtility utility) where TUtility : IUtility
        {
            container.Register(utility);
        }

        public TManager GetManager<TManager>() where TManager : class, IManager => container.Get<TManager>();
        public TSystem GetSystem<TSystem>() where TSystem : class, ISystem => container.Get<TSystem>();
        public TUtility GetUtility<TUtility>() where TUtility : class, IUtility => container.Get<TUtility>();

        public void SendCommand<TCommand>(TCommand command) where TCommand : ICommand => ExecuteCommand(command);
        public TResult SendCommand<TResult>(ICommand<TResult> command) => ExecuteCommand(command);
        public TResult SendQuery<TResult>(IQuery<TResult> query) => DoQuery(query);

        protected virtual void ExecuteCommand(ICommand command)
        {
            command.SetArchitecture(this);
            command.Execute();
        }

        protected virtual TResult ExecuteCommand<TResult>(ICommand<TResult> command)
        {
            command.SetArchitecture(this);
            return command.Execute();
        }

        protected virtual TResult DoQuery<TResult>(IQuery<TResult> query)
        {
            query.SetArchitecture(this);
            return query.Do();
        }

        // 业务架构的事件底层桥接到 WSFrame 的 Type 事件中心。
        public void SendEvent<TEvent>() where TEvent : new()
        {
            WSEventSystem.EventTrigger_Type(typeof(TEvent), new TEvent());
        }

        public void SendEvent<TEvent>(TEvent e)
        {
            WSEventSystem.EventTrigger_Type(typeof(TEvent), e);
        }

        // 复用 WSFrame 的注销句柄，方便调用方接入现有生命周期。
        public IUnRegister RegisterEvent<TEvent>(Action<TEvent> onEvent)
        {
            return WSEventSystem.Register_Type(typeof(TEvent), onEvent);
        }

        public void UnRegisterEvent<TEvent>(Action<TEvent> onEvent)
        {
            WSEventSystem.UnRegister_Type(typeof(TEvent), onEvent);
        }
    }

    #endregion

    #region Manager

    // Manager 承接 QFramework 中 Model 常见的状态模块位置，
    // 但项目内的纯数据结构仍然命名为 Data、State 或 Record。
    public interface IManager : IBelongToArchitecture, ICanSetArchitecture, ICanGetUtility, ICanSendEvent, ICanInit
    {
    }

    public abstract class AbstractManager : IManager
    {
        private IArchitecture architecture;

        public bool Initialized { get; set; }

        IArchitecture IBelongToArchitecture.GetArchitecture() => architecture;
        void ICanSetArchitecture.SetArchitecture(IArchitecture architecture) => this.architecture = architecture;
        void ICanInit.Init() => OnInit();
        void ICanInit.Deinit() => OnDeinit();

        protected abstract void OnInit();

        protected virtual void OnDeinit()
        {
        }
    }

    #endregion

    #region System

    public interface ISystem : IBelongToArchitecture, ICanSetArchitecture, ICanGetManager, ICanGetSystem,
        ICanGetUtility, ICanRegisterEvent, ICanSendEvent, ICanInit
    {
    }

    public abstract class AbstractSystem : ISystem
    {
        private IArchitecture architecture;

        public bool Initialized { get; set; }

        IArchitecture IBelongToArchitecture.GetArchitecture() => architecture;
        void ICanSetArchitecture.SetArchitecture(IArchitecture architecture) => this.architecture = architecture;
        void ICanInit.Init() => OnInit();
        void ICanInit.Deinit() => OnDeinit();

        protected abstract void OnInit();

        protected virtual void OnDeinit()
        {
        }
    }

    #endregion

    #region Utility

    public interface IUtility
    {
    }

    #endregion

    #region Command

    public interface ICommand : IBelongToArchitecture, ICanSetArchitecture, ICanGetManager, ICanGetSystem,
        ICanGetUtility, ICanSendEvent, ICanSendCommand, ICanSendQuery
    {
        void Execute();
    }

    public interface ICommand<TResult> : IBelongToArchitecture, ICanSetArchitecture, ICanGetManager, ICanGetSystem,
        ICanGetUtility, ICanSendEvent, ICanSendCommand, ICanSendQuery
    {
        TResult Execute();
    }

    public abstract class AbstractCommand : ICommand
    {
        private IArchitecture architecture;

        IArchitecture IBelongToArchitecture.GetArchitecture() => architecture;
        void ICanSetArchitecture.SetArchitecture(IArchitecture architecture) => this.architecture = architecture;
        void ICommand.Execute() => OnExecute();

        protected abstract void OnExecute();
    }

    public abstract class AbstractCommand<TResult> : ICommand<TResult>
    {
        private IArchitecture architecture;

        IArchitecture IBelongToArchitecture.GetArchitecture() => architecture;
        void ICanSetArchitecture.SetArchitecture(IArchitecture architecture) => this.architecture = architecture;
        TResult ICommand<TResult>.Execute() => OnExecute();

        protected abstract TResult OnExecute();
    }

    #endregion

    #region Query

    public interface IQuery<TResult> : IBelongToArchitecture, ICanSetArchitecture, ICanGetManager, ICanGetSystem,
        ICanSendQuery
    {
        TResult Do();
    }

    public abstract class AbstractQuery<TResult> : IQuery<TResult>
    {
        private IArchitecture architecture;

        IArchitecture IBelongToArchitecture.GetArchitecture() => architecture;
        void ICanSetArchitecture.SetArchitecture(IArchitecture architecture) => this.architecture = architecture;
        TResult IQuery<TResult>.Do() => OnDo();

        protected abstract TResult OnDo();
    }

    #endregion

    #region Rule

    public interface IBelongToArchitecture
    {
        IArchitecture GetArchitecture();
    }

    public interface ICanSetArchitecture
    {
        void SetArchitecture(IArchitecture architecture);
    }

    // 能力接口只声明对象可以访问什么；扩展方法负责提供顺手的 API。
    // 这样可以避免大基类，也避免把不需要的能力泄漏给业务对象。
    public interface ICanGetManager : IBelongToArchitecture
    {
    }

    public static class CanGetManagerExtension
    {
        public static T GetManager<T>(this ICanGetManager self) where T : class, IManager =>
            self.GetArchitecture().GetManager<T>();
    }

    public interface ICanGetSystem : IBelongToArchitecture
    {
    }

    public static class CanGetSystemExtension
    {
        public static T GetSystem<T>(this ICanGetSystem self) where T : class, ISystem =>
            self.GetArchitecture().GetSystem<T>();
    }

    public interface ICanGetUtility : IBelongToArchitecture
    {
    }

    public static class CanGetUtilityExtension
    {
        public static T GetUtility<T>(this ICanGetUtility self) where T : class, IUtility =>
            self.GetArchitecture().GetUtility<T>();
    }

    public interface ICanRegisterEvent : IBelongToArchitecture
    {
    }

    public static class CanRegisterEventExtension
    {
        public static IUnRegister RegisterEvent<T>(this ICanRegisterEvent self, Action<T> onEvent) =>
            self.GetArchitecture().RegisterEvent(onEvent);

        public static void UnRegisterEvent<T>(this ICanRegisterEvent self, Action<T> onEvent) =>
            self.GetArchitecture().UnRegisterEvent(onEvent);
    }

    public interface ICanSendCommand : IBelongToArchitecture
    {
    }

    public static class CanSendCommandExtension
    {
        public static void SendCommand<T>(this ICanSendCommand self) where T : ICommand, new() =>
            self.GetArchitecture().SendCommand(new T());

        public static void SendCommand<T>(this ICanSendCommand self, T command) where T : ICommand =>
            self.GetArchitecture().SendCommand(command);

        public static TResult SendCommand<TResult>(this ICanSendCommand self, ICommand<TResult> command) =>
            self.GetArchitecture().SendCommand(command);
    }

    public interface ICanSendEvent : IBelongToArchitecture
    {
    }

    public static class CanSendEventExtension
    {
        public static void SendEvent<T>(this ICanSendEvent self) where T : new() =>
            self.GetArchitecture().SendEvent<T>();

        public static void SendEvent<T>(this ICanSendEvent self, T e) =>
            self.GetArchitecture().SendEvent(e);
    }

    public interface ICanSendQuery : IBelongToArchitecture
    {
    }

    public static class CanSendQueryExtension
    {
        public static TResult SendQuery<TResult>(this ICanSendQuery self, IQuery<TResult> query) =>
            self.GetArchitecture().SendQuery(query);
    }

    public interface ICanInit
    {
        bool Initialized { get; set; }
        void Init();
        void Deinit();
    }

    #endregion

    #region IOC

    // 长期业务模块实例注册表。它不负责创建短生命周期对象；
    // 普通运行时对象应交给 Factory 或 Utility 模块创建。
    public sealed class IOCContainer
    {
        private readonly Dictionary<Type, object> instances = new Dictionary<Type, object>();

        public void Register<T>(T instance)
        {
            instances[typeof(T)] = instance;
        }

        public T Get<T>() where T : class
        {
            return instances.TryGetValue(typeof(T), out var instance) ? instance as T : null;
        }

        public IEnumerable<T> GetInstancesByType<T>()
        {
            var type = typeof(T);
            return instances.Values.Where(type.IsInstanceOfType).Cast<T>();
        }

        public void Clear()
        {
            instances.Clear();
        }
    }

    #endregion
}








