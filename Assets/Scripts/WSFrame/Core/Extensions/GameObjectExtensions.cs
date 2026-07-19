using UnityEngine;
using UnityEngine.Events;

namespace WS_Modules.Extensions
{
    public static class GameObjectExtensions
    {
        public static T GetOrAddComponent<T>(this UnityEngine.GameObject gameObject) where T : UnityEngine.Component
        {
            var component = gameObject.GetComponent<T>();
            if (component == null)
            {
                component = gameObject.AddComponent<T>();
            }
            return component;
        }

        public static GameObject OnDestroy(this UnityEngine.GameObject gameObject, UnityAction onDestroyAction)
        {
            var destroyListener = gameObject.GetOrAddComponent<GameObjectListener>();
            destroyListener.RegisterOnDestroyed(onDestroyAction);
            return gameObject;
        }
        
        public static void RemoveOnDestroy(this UnityEngine.GameObject gameObject, UnityAction onDestroyAction)
        {
            var destroyListener = gameObject.GetComponent<GameObjectListener>();
            if (destroyListener != null)
            {
                destroyListener.UnRegisterOnDestroyed(onDestroyAction);
            }
        }
        
        public static GameObject OnDisable(this UnityEngine.GameObject gameObject, UnityAction onDisableAction)
        {
            var disableListener = gameObject.GetOrAddComponent<GameObjectListener>();
            disableListener.RegisterOnDisabled(onDisableAction);
            return gameObject;
        }
        
        public static void RemoveOnDisable(this UnityEngine.GameObject gameObject, UnityAction onDisableAction)
        {
            var disableListener = gameObject.GetComponent<GameObjectListener>();
            if (disableListener != null)
            {
                disableListener.UnRegisterOnDisabled(onDisableAction);
            }
        }
        
        public static void MoveToActiveScene(this GameObject gameObject)
        {
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(gameObject, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
    }
    
    public class GameObjectListener : MonoBehaviour, IGameObjectEventListener
    {
        private UnityAction OnDestroyed;
        private UnityAction OnDisabled;

        public IGameObjectEventListener RegisterOnDestroyed(UnityAction onDestroyed)
        {
            OnDestroyed += onDestroyed;
            return this;
        }
        
        public void UnRegisterOnDestroyed(UnityAction onDestroyed)
        {
            OnDestroyed -= onDestroyed;
        }

        public IGameObjectEventListener RegisterOnDisabled(UnityAction onDisabled)
        {
            OnDisabled += onDisabled;
            return this;
        }
        
        public void UnRegisterOnDisabled(UnityAction onDisabled)
        {
            OnDisabled -= onDisabled;
        }
        
        private void OnDestroy()
        {
            OnDestroyed?.Invoke();
        }
    }

    /// <summary>
    /// 为了标记 GameObject 事件监听器组件，方便后续扩展和管理，同时实现链式调用
    /// </summary>
    public interface IGameObjectEventListener
    {
    }
}

