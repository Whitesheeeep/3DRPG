using WS_Modules.Singleton;
using UnityEngine.Events;

namespace WS_Modules.MonoSystem
{
    public class PublicMono : AutoSingletonMonoBase<PublicMono>
    {
        private event UnityAction updateEvent;
        private event UnityAction fixedUpdateEvent;
        private event UnityAction lateUpdateEvent;
        private void Update()
        {
            updateEvent?.Invoke();
        }

        private void FixedUpdate()
        {
            fixedUpdateEvent?.Invoke();
        }

        private void LateUpdate()
        {
            lateUpdateEvent?.Invoke();
        }

        public void RegisterUpdate(UnityAction action)
        {
            updateEvent += action;
        }

        public void UnRegisterUpdate(UnityAction action)
        {
            updateEvent -= action;
        }

        public void RegisterFixedUpdate(UnityAction action)
        {
            fixedUpdateEvent += action;
        }

        public void UnRegisterFixedUpdate(UnityAction action)
        {
            fixedUpdateEvent -= action;
        }

        public void RegisterLateUpdate(UnityAction action)
        {
            lateUpdateEvent += action;
        }

        public void UnRegisterLateUpdate(UnityAction action)
        {
            lateUpdateEvent -= action;
        }
    }
}