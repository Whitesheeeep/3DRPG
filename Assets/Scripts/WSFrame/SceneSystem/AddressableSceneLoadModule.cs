using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using WS_Modules.LogModule;

namespace WS_Modules.SceneModule
{
    /// <summary>以 Addressables 加载、切换活动场景并按操作句柄对称卸载场景。</summary>
    public sealed class AddressableSceneLoadModule
    {
        #region 场景句柄状态

        // 状态：每项对应一次成功加载；Additive 场景允许相同地址有多个句柄。
        // 每一项对应一次成功的 Addressables 场景加载；Additive 允许同一地址出现多个句柄。
        private readonly List<AddressableSceneHandleEntry> loadedAddressableScenes = new();

        #endregion

        #region 场景加载卸载

        /// <summary>判断指定逻辑场景 ID 是否持有传入的 Unity 场景实例。</summary>
        /// <param name="sceneId">场景配置的稳定 ID。</param>
        /// <param name="scene">需要核对的 Unity 场景实例。</param>
        /// <returns>该实例仍由此模块持有时返回 true。</returns>
        public bool OwnsScene(string sceneId, Scene scene)
        {
            for (int index = loadedAddressableScenes.Count - 1; index >= 0; index--)
            {
                AddressableSceneHandleEntry entry = loadedAddressableScenes[index];
                if (entry.Handle.IsValid() &&
                    string.Equals(entry.SceneId, sceneId, StringComparison.Ordinal) &&
                    entry.Handle.Result.Scene == scene)
                    return true;
            }
            return false;
        }

        // 加载：激活并校验 Scene 后登记场景句柄所有权。
        /// <summary>加载配置中的目标场景并在激活后清理 Single 模式的旧场景句柄。</summary>
        /// <param name="config">包含 Addressable Scene、SceneName 与模式的配置。</param>
        /// <param name="makeActive">是否将加载结果设为活动场景。</param>
        /// <param name="cancellationToken">协作式取消令牌；Unity 场景操作启动后仍等到底层操作结束。</param>
        /// <returns>激活并通过 SceneName 校验的 Scene。</returns>
        public async UniTask<Scene> LoadAsync(
            SceneLoadConfig config,
            bool makeActive,
            CancellationToken cancellationToken)
        {
            return await LoadAsync(config, makeActive, cancellationToken, null);
        }

        /// <summary>加载场景并把 Addressables 操作进度映射给调用方。</summary>
        /// <param name="config">包含 Addressable Scene、SceneName 与模式的配置。</param>
        /// <param name="makeActive">是否将加载结果设为活动场景。</param>
        /// <param name="cancellationToken">协作式取消令牌；场景操作启动后仍观察至完成。</param>
        /// <param name="progressReporter">可选的单调进度接收器。</param>
        /// <returns>激活并通过 SceneName 校验的 Scene。</returns>
        public async UniTask<Scene> LoadAsync(
            SceneLoadConfig config,
            bool makeActive,
            CancellationToken cancellationToken,
            Action<float> progressReporter)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WSLog.Log($"[AddressableSceneLoadModule] 开始加载 Addressables 场景，sceneId={config.SceneId}，mode={config.LoadMode}。");
            AsyncOperationHandle<SceneInstance> handle = Addressables.LoadSceneAsync(
                config.SceneReference,
                config.LoadMode,
                activateOnLoad: true);

            // Addressables 的 Unity Scene 操作不可由调用令牌安全终止，必须观察到完成后再处理结果。
            while (!handle.IsDone)
            {
                progressReporter?.Invoke(handle.PercentComplete);
                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Exception operationException = handle.OperationException;
                if (handle.IsValid()) Addressables.Release(handle);
                WSLog.LogError($"[AddressableSceneLoadModule] 场景加载操作失败，sceneId={config.SceneId}，exception={operationException}");
                throw operationException ?? new InvalidOperationException(
                    $"[AddressableSceneLoadModule] 加载场景失败：{config.SceneId}。");
            }

            Scene scene = handle.Result.Scene;
            if (!scene.IsValid() || !string.Equals(scene.name, config.SceneName, StringComparison.Ordinal))
            {
                await UnloadOperationAsync(handle);
                throw new InvalidOperationException(
                    $"[AddressableSceneLoadModule] 场景名称校验失败，sceneId={config.SceneId}，expected={config.SceneName}，actual={scene.name}。");
            }

            if (makeActive && !SceneManager.SetActiveScene(scene))
            {
                await UnloadOperationAsync(handle);
                throw new InvalidOperationException(
                    $"[AddressableSceneLoadModule] 无法将加载场景设为活动场景：{scene.name}。");
            }

            if (config.LoadMode == LoadSceneMode.Single)
                ReleaseUnloadedSingleSceneHandles();

            loadedAddressableScenes.Add(new AddressableSceneHandleEntry(config.SceneId, handle));
            progressReporter?.Invoke(1f);
            WSLog.Log($"[AddressableSceneLoadModule] Addressables 场景已加载并激活，sceneId={config.SceneId}，mode={config.LoadMode}。");
            cancellationToken.ThrowIfCancellationRequested();
            return scene;
        }

        // 卸载：只处理当前模块登记的 Scene 实例。
        /// <summary>卸载由当前模块加载并登记的指定 Scene 实例。</summary>
        /// <param name="scene">需要卸载的已登记场景实例。</param>
        /// <param name="cancellationToken">调用方协作式取消令牌。</param>
        /// <returns>Unity 卸载操作结束的异步任务。</returns>
        public async UniTask UnloadAsync(Scene scene, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int entryIndex = FindSceneEntry(scene);
            if (entryIndex < 0)
                throw new InvalidOperationException($"[AddressableSceneLoadModule] 场景未由此模块持有，不能卸载：{scene.name}。");

            AddressableSceneHandleEntry entry = loadedAddressableScenes[entryIndex];
            WSLog.Log($"[AddressableSceneLoadModule] 开始卸载 Addressables 场景，sceneId={entry.SceneId}，scene={scene.name}。");
            try
            {
                await UnloadOperationAsync(entry.Handle);
            }
            catch (Exception exception)
            {
                WSLog.LogError($"[AddressableSceneLoadModule] 场景卸载失败，sceneId={entry.SceneId}，scene={scene.name}，exception={exception}");
                throw;
            }
            loadedAddressableScenes.RemoveAt(entryIndex);
            WSLog.Log($"[AddressableSceneLoadModule] Addressables 场景已卸载并释放句柄，sceneId={entry.SceneId}，scene={scene.name}。");
        }

        #endregion

        #region 句柄生命周期

        /// <summary>按 Unity 场景实例定位其唯一 Addressables 所有权记录。</summary>
        /// <param name="scene">待查找的场景。</param>
        /// <returns>记录索引；无记录时返回负一。</returns>
        private int FindSceneEntry(Scene scene)
        {
            for (int index = loadedAddressableScenes.Count - 1; index >= 0; index--)
            {
                if (loadedAddressableScenes[index].Handle.IsValid() &&
                    loadedAddressableScenes[index].Handle.Result.Scene == scene)
                    return index;
            }
            return -1;
        }

        /// <summary>释放 Single 切换后已被 Unity 自动卸载的旧 Addressables 场景句柄。</summary>
        private void ReleaseUnloadedSingleSceneHandles()
        {
            for (int index = loadedAddressableScenes.Count - 1; index >= 0; index--)
            {
                AsyncOperationHandle<SceneInstance> oldHandle = loadedAddressableScenes[index].Handle;
                if (oldHandle.IsValid()) Addressables.Release(oldHandle);
            }
            loadedAddressableScenes.Clear();
        }

        /// <summary>完成场景卸载后释放 Unity 对应的 Addressables 句柄。</summary>
        /// <param name="loadHandle">完成过的场景加载句柄。</param>
        /// <returns>卸载完成后的异步任务。</returns>
        private static async UniTask UnloadOperationAsync(AsyncOperationHandle<SceneInstance> loadHandle)
        {
            if (!loadHandle.IsValid()) return;
            AsyncOperationHandle<SceneInstance> unloadHandle = Addressables.UnloadSceneAsync(
                loadHandle,
                autoReleaseHandle: false);
            while (!unloadHandle.IsDone)
                await UniTask.Yield(PlayerLoopTiming.Update);
            try
            {
                if (unloadHandle.Status != AsyncOperationStatus.Succeeded)
                    throw unloadHandle.OperationException ?? new InvalidOperationException(
                        "[AddressableSceneLoadModule] Unity 场景卸载失败。");
            }
            finally
            {
                if (unloadHandle.IsValid()) Addressables.Release(unloadHandle);
            }
        }

        /// <summary>描述一个场景 ID 与其一次 Addressables 场景加载句柄的所有权关系。</summary>
        private readonly struct AddressableSceneHandleEntry
        {
            /// <summary>获取逻辑场景 ID。</summary>
            public string SceneId { get; }
            /// <summary>获取绑定场景实例的 Addressables 句柄。</summary>
            public AsyncOperationHandle<SceneInstance> Handle { get; }

            /// <summary>创建场景句柄所有权记录。</summary>
            /// <param name="sceneId">逻辑场景 ID。</param>
            /// <param name="handle">加载得到的场景句柄。</param>
            public AddressableSceneHandleEntry(string sceneId, AsyncOperationHandle<SceneInstance> handle)
            {
                SceneId = sceneId;
                Handle = handle;
            }
        }

        #endregion
    }
}
