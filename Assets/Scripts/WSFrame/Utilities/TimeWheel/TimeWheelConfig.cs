using System;
using System.Collections.Generic;
using UnityEngine;

namespace WS_Modules.Utilities
{
    /// <summary>
    /// 单个时间轮调度器的配置。
    /// 配置只描述时间轮的粒度和容量，不保存任何运行中的任务状态。
    /// </summary>
    [Serializable]
    public sealed class TimeWheelConfig
    {
        // 默认三层时间轮：第 0 层负责精细触发，高层负责保存更远的任务。
        // 当调用方传入 Time.deltaTime，且 tickUnit = 0.1f 时，默认最大覆盖约 29.1 小时。
        private static readonly List<int> DefaultSlotCounts = new List<int> { 256, 64, 64 };

        // 推进一个基础 tick 需要累计多少输入单位。
        // 单位由调用方决定：可以是真实秒、游戏分钟，或者其他逻辑时间单位。
        // 值越小触发越精细，但 Tick 推进次数越多；值越大性能压力越小，但延迟误差越大。
        [SerializeField, Tooltip("推进一个基础 tick 需要累计多少输入单位。单位由调用方决定：可以是真实秒、游戏分钟，或者其他逻辑时间单位。值越小触发越精细，但 Tick 推进次数越多；值越大性能压力越小，但延迟误差越大。")]
        private float tickUnit = 0.1f;

        // 每一层时间轮的槽位数量。
        // 第 0 层每个槽覆盖 1 个基础 tick；后续每层的单槽跨度等于前面所有层槽位数的乘积。
        [SerializeField]
        private List<int> slotCounts = new List<int> { 256, 64, 64 };

        // 单次 Tick(deltaUnits) 最多补跑多少个基础 tick。
        // 用于限制卡顿后一次性补跑过多任务，避免某一帧被历史积压拖垮。
        [SerializeField]
        private int maxCatchUpTicksPerFrame = 100;

        // 对外暴露时始终返回经过校验的 tick 单位长度，避免运行时拿到非法配置。
        public float TickUnit => ValidateTickUnit(tickUnit);

        // 返回副本，避免调用方直接修改序列化字段中的 List。
        public List<int> SlotCounts => CopyAndValidateSlotCounts(slotCounts);

        // 至少允许补跑 1 个 tick，否则调度器将无法推进。
        public int MaxCatchUpTicksPerFrame => Math.Max(1, maxCatchUpTicksPerFrame);

        // 内部层数来自经过校验的 slotCounts。
        internal int LevelCount => SlotCounts.Count;

        public TimeWheelConfig(
            float tickUnit = 0.1f,
            List<int> slotCounts = null,
            int maxCatchUpTicksPerFrame = 100)
        {
            // 构造时立即校验并复制列表，保证后续调度器拿到的是稳定配置。
            this.tickUnit = ValidateTickUnit(tickUnit);
            this.slotCounts = CopyAndValidateSlotCounts(slotCounts ?? DefaultSlotCounts);
            this.maxCatchUpTicksPerFrame = Math.Max(1, maxCatchUpTicksPerFrame);
        }

        public TimeWheelConfig CreateRuntimeCopy()
        {
            // ConfigProvider 注册时使用运行时副本，避免运行中改动污染 Unity 资产文件。
            return new TimeWheelConfig(TickUnit, SlotCounts, MaxCatchUpTicksPerFrame);
        }

        internal int GetSlotCount(int level)
        {
            // 调度器内部按已校验层级访问；越界代表调用方层级计算有误。
            return slotCounts[level];
        }

        private static float ValidateTickUnit(float value)
        {
            if (value <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "tickUnit 必须大于 0。");
            }

            return value;
        }

        private static List<int> CopyAndValidateSlotCounts(List<int> slotCounts)
        {
            if (slotCounts == null || slotCounts.Count == 0)
            {
                throw new ArgumentException("时间轮至少需要一层。", nameof(slotCounts));
            }

            // 复制一份列表，避免外部继续持有原 List 并在调度器运行期间修改槽位结构。
            var copy = new List<int>(slotCounts.Count);
            for (int i = 0; i < slotCounts.Count; i++)
            {
                if (slotCounts[i] <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(slotCounts), "每一层时间轮都至少需要一个 slot。");
                }

                copy.Add(slotCounts[i]);
            }

            return copy;
        }
    }
}
