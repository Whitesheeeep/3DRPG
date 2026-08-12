#if UNITY_EDITOR
using System;
using UnityEngine;

namespace WS_Modules.GAS.GameplayCue
{
    /// <summary>
    /// 用于集成测试的可视化 Cue 行为。
    /// 通过材质颜色和局部缩放区分 Execute、Active 与 Remove 状态，并在对象回收前恢复实例状态。
    /// </summary>
    public sealed class GameplayCueVisualProbeBehaviour : GameplayCueBehaviour
    {
        #region 测试配置与运行时状态

        [SerializeField]
        private Color spawnColor = Color.white;

        [SerializeField]
        private Color executeColor = Color.cyan;

        [SerializeField]
        private Color activeColor = Color.green;

        [SerializeField]
        private Color removeColor = Color.gray;

        [SerializeField]
        private Vector3 executeScale = Vector3.one;

        [SerializeField]
        private Vector3 activeScale = Vector3.one * 1.25f;

        private Renderer[] renderers;
        private MaterialPropertyBlock propertyBlock;
        private Vector3 initialScale;

        #endregion

        #region 测试事件与计数

        /// <summary>通知集成 Tester 某个可视化 Cue 已收到生命周期回调。</summary>
        public static event Action<GameplayCueRuntime, GameplayCueEventType> CueObserved;

        /// <summary>当前对象收到 Execute 回调的次数。</summary>
        public int ExecuteCount { get; private set; }

        /// <summary>当前对象收到 Active 回调的次数。</summary>
        public int ActiveCount { get; private set; }

        /// <summary>当前对象收到 Remove 回调的次数。</summary>
        public int RemoveCount { get; private set; }

        #endregion

        #region Unity 生命周期

        /// <summary>缓存 Renderer、材质属性块和对象池初始缩放。</summary>
        protected override void Awake()
        {
            base.Awake();
            renderers = GetComponentsInChildren<Renderer>(true);
            propertyBlock = new MaterialPropertyBlock();
            initialScale = transform.localScale;
        }

        #endregion

        #region Cue 生命周期

        /// <summary>设置对象刚从对象池取出时的基础外观。</summary>
        /// <param name="runtime">当前 Cue 运行时句柄。</param>
        protected override void OnCueSpawn(GameplayCueRuntime runtime)
        {
            ApplyVisual(spawnColor, Vector3.one);
        }

        /// <summary>记录一次性 Cue，通知测试器后主动释放对象池实例。</summary>
        /// <param name="runtime">当前 Cue 运行时句柄。</param>
        protected override void OnExecute(GameplayCueRuntime runtime)
        {
            ExecuteCount++;
            ApplyVisual(executeColor, executeScale);
            CueObserved?.Invoke(runtime, GameplayCueEventType.Execute);
            runtime.Release();
        }

        /// <summary>将持续 Cue 设置为激活颜色和放大状态。</summary>
        /// <param name="runtime">当前 Cue 运行时句柄。</param>
        protected override void OnActive(GameplayCueRuntime runtime)
        {
            ActiveCount++;
            ApplyVisual(activeColor, activeScale);
            CueObserved?.Invoke(runtime, GameplayCueEventType.Active);
        }

        /// <summary>记录持续 Cue 的移除回调，并切换到移除颜色。</summary>
        /// <param name="runtime">当前 Cue 运行时句柄。</param>
        protected override void OnRemove(GameplayCueRuntime runtime)
        {
            RemoveCount++;
            ApplyVisual(removeColor, Vector3.one);
            CueObserved?.Invoke(runtime, GameplayCueEventType.Remove);
        }

        /// <summary>清理对象池实例的材质、缩放和计数，避免下一次测试继承旧状态。</summary>
        /// <param name="runtime">当前 Cue 运行时句柄。</param>
        protected override void OnCueRecycle(GameplayCueRuntime runtime)
        {
            ApplyVisual(spawnColor, Vector3.one);
            transform.localScale = initialScale;
            ExecuteCount = 0;
            ActiveCount = 0;
            RemoveCount = 0;
        }

        #endregion

        #region 内部辅助

        /// <summary>通过 MaterialPropertyBlock 修改实例颜色，避免污染共享材质。</summary>
        /// <param name="color">要显示的颜色。</param>
        /// <param name="scale">要设置的局部缩放。</param>
        private void ApplyVisual(Color color, Vector3 scale)
        {
            transform.localScale = Vector3.Scale(initialScale, scale);
            if (renderers == null || propertyBlock == null) return;

            propertyBlock.SetColor("_BaseColor", color);
            propertyBlock.SetColor("_Color", color);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].SetPropertyBlock(propertyBlock);
        }

        #endregion
    }
}
#endif
