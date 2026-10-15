using System;
using UnityEngine;
#if UNITY_EDITOR
using WS_Modules.Utilities;
#endif

namespace WS_Modules.GAS.GameplayCue
{
    /// <summary>
    /// 使用对象池创建 Visual Gameplay Cue，并维持旧的 Behaviour 生命周期回调。
    /// </summary>
    [CreateAssetMenu(fileName = "VisualCueHandler", menuName = "WSFrame/GAS/Gameplay Cue Handler/Visual")]
    public sealed class VisualCueHandlerSO : GameplayCueHandlerSO
    {
        #region 编辑器调试绘制配置

#if UNITY_EDITOR
        [SerializeField, Tooltip("在 Visual Cue 的最终世界位置绘制坐标原点和三轴，帮助检查摆放结果。")]
        private bool drawPlacementInEditor = true;
        [SerializeField, Range(0f, 10f), Tooltip("WSFrame 调试图形在 Scene/Game 视图中保留的秒数。")]
        private float drawPlacementDuration = 1f;
#endif

        private const float PlacementMarkerRadius = 0.05f;
        private const float PlacementAxisLength = 0.5f;

        #endregion

        #region 类型声明

        /// <summary>获取此 Handler 支持的 Visual CueData 具体类型。</summary>
        public override Type CueDataType => typeof(VisualGameplayCueData);

        #endregion

        #region Cue 生命周期

        /// <inheritdoc />
        public override void Execute(GameplayCueData data, GameplayCueRequest request, GameplayCueCtrl controller)
        {
            VisualGameplayCueData visualData = RequireVisualData(data);
            GameplayCueRuntime runtime = controller.CreateVisualRuntime(visualData, request);
            if (runtime == null) return;

            DrawPlacement(runtime);
            runtime.Behaviour.InvokeCueSpawn(runtime);
            if (!runtime.IsReleased) runtime.Behaviour.InvokeExecute(runtime);
            if (!runtime.IsReleased && runtime.Behaviour.ReleaseAfterExecute)
                controller.ReleaseRuntime(runtime, false);
        }

        /// <inheritdoc />
        public override object Active(GameplayCueData data, GameplayCueRequest request, GameplayCueCtrl controller)
        {
            VisualGameplayCueData visualData = RequireVisualData(data);
            GameplayCueRuntime runtime = controller.CreateVisualRuntime(visualData, request);
            if (runtime == null) return null;

            DrawPlacement(runtime);
            runtime.IsActive = true;
            controller.RegisterActiveVisual(runtime);
            runtime.Behaviour.InvokeCueSpawn(runtime);
            if (!runtime.IsReleased) runtime.Behaviour.InvokeActive(runtime);
            return runtime;
        }

        /// <inheritdoc />
        public override void Remove(GameplayCueActiveRecord record)
        {
            if (record.State is GameplayCueRuntime runtime)
                record.Controller.ReleaseRuntime(runtime, true);
        }

        /// <inheritdoc />
        public override void Clear(GameplayCueActiveRecord record)
        {
            if (record.State is GameplayCueRuntime runtime)
                record.Controller.ReleaseRuntime(runtime, true);
        }

        #endregion

        #region 编辑器摆放绘制

        /// <summary>按对象池实例完成锚点、显式坐标和资源偏移后的最终 Transform 绘制世界坐标轴。</summary>
        /// <param name="runtime">已经由 GameplayCueCtrl 完成摆放的视觉运行时。</param>
        private void DrawPlacement(GameplayCueRuntime runtime)
        {
#if UNITY_EDITOR
            if (!drawPlacementInEditor) return;
            if (float.IsNaN(drawPlacementDuration) || float.IsInfinity(drawPlacementDuration) ||
                drawPlacementDuration < 0f)
            {
                Debug.LogError(
                    $"[VisualCueHandlerSO] '{name}' 的 Editor 绘制持续时间无效：{drawPlacementDuration}。",
                    this);
                return;
            }

            // 使用已经摆放完成的世界变换，令调试图形与实际 Cue 资源偏移及锚点结果一致。
            Transform cueTransform = runtime.CueObject.transform;
            Vector3 position = cueTransform.position;
            Quaternion rotation = cueTransform.rotation;
            float duration = drawPlacementDuration;
            DebugUtility.DrawSphere(position, PlacementMarkerRadius, Color.white, duration, false, 12);
            DebugUtility.DrawRay(position, rotation * Vector3.right * PlacementAxisLength, Color.red, duration, false);
            DebugUtility.DrawRay(position, rotation * Vector3.up * PlacementAxisLength, Color.green, duration, false);
            DebugUtility.DrawRay(position, rotation * Vector3.forward * PlacementAxisLength, Color.blue, duration, false);
#endif
        }

        #endregion

        #region 校验

        /// <summary>转换并验证共享数据库索引传入的 Visual CueData。</summary>
        /// <param name="data">数据库匹配出的 CueData。</param>
        /// <returns>类型正确的 Visual 数据。</returns>
        private static VisualGameplayCueData RequireVisualData(GameplayCueData data)
        {
            if (data is VisualGameplayCueData visualData) return visualData;
            throw new InvalidOperationException(
                $"VisualCueHandlerSO 收到不匹配的 CueData：{data?.GetType().Name ?? "null"}。");
        }

        #endregion
    }
}
