using System;
using WS_Modules.Pooling;

namespace WS_Modules.Utilities
{
    // Task 的内部生命周期状态。外部只通过 TimeWheelHandle 操作任务，不直接读写这些状态。
    internal enum TimeWheelTaskState
    {
        // 已经进入时间轮，等待到期触发。
        Scheduled,
        // 被暂停，旧桶记录会通过 ScheduleVersion 自动失效。
        Paused,
        // 被取消，等待后续扫描或回收流程清理。
        Cancelled,
        // 正在执行回调。回调期间仍允许取消当前任务。
        Executing,
        // 已完成或已回收，不再参与调度。
        Completed
    }

    // 时间轮中的实际任务对象。
    // 任务对象由 PoolManager 复用，因此所有运行时字段都必须在 Init / Recycle 中完整重置。
    internal sealed class TimeWheelTask : IPoolable
    {
        // 任务唯一 ID，用于 TimeWheelHandle 定位任务。回收后会重置为 InvalidId。
        internal long Id = IdGenerator.InvalidId;

        // 任务生命周期版本。任务复用后版本递增，使旧 Handle 自动失效。
        internal int Version;

        // 调度版本。每次重新入桶都会递增，使桶中的旧 Entry 自动失效。
        internal int ScheduleVersion;

        // 当前任务状态，用于判断能否执行、暂停、恢复或回收。
        internal TimeWheelTaskState State = TimeWheelTaskState.Completed;

        // 绝对到期 tick。它基于 scheduler 的 currentTick，而不是桶下标。
        internal long DueTick;

        // 暂停时保存的剩余 tick，恢复时用它重新计算 DueTick。
        internal long RemainingTicks;

        // 重复任务的间隔 tick。一次性任务为 0。
        internal long IntervalTicks;

        // 剩余重复次数。-1 表示无限重复，0 表示不再重复。
        internal int RemainingRepeatCount;

        // 是否为重复任务。
        internal bool IsRepeating;

        // 是否已经计入 scheduler 的 ActiveCount，避免多条清理路径重复扣减。
        internal bool IsActiveCounted;

        // 到期时执行的回调。回收时必须清空，避免对象池持有外部引用。
        internal Action Callback;

        public int MaxCount => 1000;
        public int InitCount => 0;

        public void OnSpawn()
        {
        }

        public void OnDespawn()
        {
            Recycle();
        }

        internal void Init(long delayTicks, long dueTick, Action callback)
        {
            // 从对象池取出后重新初始化。Version 递增是为了让上一次生命周期的 Handle 失效。
            Id = IdGenerator.Next();
            Version++;
            ScheduleVersion = 0;
            State = TimeWheelTaskState.Scheduled;
            DueTick = dueTick;
            RemainingTicks = delayTicks;
            IntervalTicks = 0;
            RemainingRepeatCount = 0;
            IsRepeating = false;
            IsActiveCounted = true;
            Callback = callback;
        }

        internal void SetRepeat(long intervalTicks, int repeatCount)
        {
            // repeatCount = -1 表示无限重复；正数表示总执行次数。
            IsRepeating = true;
            IntervalTicks = intervalTicks;
            RemainingRepeatCount = repeatCount;
        }

        internal void Recycle()
        {
            // 清理所有运行时引用和状态，确保下次从对象池取出时没有残留数据。
            Id = IdGenerator.InvalidId;
            Version++;
            ScheduleVersion++;
            State = TimeWheelTaskState.Completed;
            DueTick = 0;
            RemainingTicks = 0;
            IntervalTicks = 0;
            RemainingRepeatCount = 0;
            IsRepeating = false;
            IsActiveCounted = false;
            Callback = null;
        }
    }
}