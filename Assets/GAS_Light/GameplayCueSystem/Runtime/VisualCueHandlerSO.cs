using System;
using UnityEngine;

namespace WS_Modules.GAS.GameplayCue
{
    /// <summary>
    /// 使用对象池创建 Visual Gameplay Cue，并维持旧的 Behaviour 生命周期回调。
    /// </summary>
    [CreateAssetMenu(fileName = "VisualCueHandler", menuName = "WSFrame/GAS/Gameplay Cue Handler/Visual")]
    public sealed class VisualCueHandlerSO : GameplayCueHandlerSO
    {
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

            runtime.Behaviour.InvokeCueSpawn(runtime);
            if (!runtime.IsReleased) runtime.Behaviour.InvokeExecute(runtime);
            if (!runtime.IsReleased) controller.ReleaseRuntime(runtime, false);
        }

        /// <inheritdoc />
        public override object Active(GameplayCueData data, GameplayCueRequest request, GameplayCueCtrl controller)
        {
            VisualGameplayCueData visualData = RequireVisualData(data);
            GameplayCueRuntime runtime = controller.CreateVisualRuntime(visualData, request);
            if (runtime == null) return null;

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
