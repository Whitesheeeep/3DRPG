using System;
using System.Collections.Generic;

namespace WS_Modules.CustomEventSystem
{
    public class OrEvent : IUnRegisterList, IRegister
    {
        private Action _onAnyEventInvoke = delegate { };
        public List<IUnRegister> UnRegisterList { get; }

        public OrEvent(params IUnRegister[] unRegisters)
        {
            UnRegisterList = new List<IUnRegister>(unRegisters);
        }


        public IUnRegister Register(Action onAnyEventInvoke)
        {
            _onAnyEventInvoke += onAnyEventInvoke;
            return new CustomUnRegister(() => UnRegister(onAnyEventInvoke));
        }

        public IUnRegister RegisterWithACall(Action onAnyEventInvoke)
        {
            onAnyEventInvoke?.Invoke();
            return Register(onAnyEventInvoke);
        }

        public void UnRegister(Action onAnyEventInvoke)
        {
            // 让自身的解开
            _onAnyEventInvoke -= onAnyEventInvoke;
            // 取消其他的 register 和 Invoke 方法的链接
            this.UnRegisterAll();
        }

        public OrEvent Or(IRegister orTarget)
        {
            orTarget.Register(this.Invoke).AddToUnregisterList(this);
            return this;
        }

        private void Invoke() => _onAnyEventInvoke?.Invoke();
    }
}