using System;
using UnityEngine;
using WS_Modules.Pooling;

namespace WS_Modules.Utilities
{
    internal sealed class TimerFactory
    {
        private bool _poolFallbackWarned;

        internal Timer Create()
        {
            try
            {
                return PoolManager.Instance.GetClass<Timer>();
            }
            catch (Exception e)
            {
                LogPoolFallbackOnce(e);
                var timer = new Timer();
                timer.OnSpawn();
                return timer;
            }
        }

        internal void Recycle(Timer timer)
        {
            try
            {
                PoolManager.Instance.RecycleClass(timer);
            }
            catch (Exception e)
            {
                LogPoolFallbackOnce(e);
                timer.OnDespawn();
            }
        }

        private void LogPoolFallbackOnce(Exception e)
        {
            if (_poolFallbackWarned) return;

            _poolFallbackWarned = true;
            Debug.LogWarning($"TimerManager is using direct Timer allocation because PoolManager is not ready: {e.Message}");
        }
    }
}
