using System;
using WS_Modules.Pooling;

namespace WS_Modules.Utilities
{
    /// <summary>
    /// TimerManager 内部使用的计时器数据对象。
    /// 业务层不应直接持有 Timer，而应使用 TimerHandle 作为控制句柄。
    /// </summary>
    public class Timer : IPoolable
    {
        /// <summary>
        /// 当前 Timer 的运行 ID。
        /// Id 为 -1 表示该 Timer 已回收，不应再继续配置或使用。
        /// </summary>
        public long Id { get; private set; }

        // --- 配置参数 ---
        internal TimerManager.TimerTags tag = TimerManager.TimerTags.None;
        internal float duration;
        internal bool isLoop;
        internal int loopCount; // -1 表示无限循环
        internal bool useUnscaledTime;
        internal float timeScale = 1f;
        internal Action onComplete;
        internal Action<float> onUpdate;

        // --- 运行时状态 ---
        internal float timeElapsed;
        internal bool isPaused;
        internal bool isCompleted;

        // --- 调度状态 ---
        internal TimerManager.TimerScheduleMode scheduleMode = TimerManager.TimerScheduleMode.Recycled;

        // 三个 Index 都是为了让 TimerManager 能 O(1) 定位 Timer 在容器中的位置，再配合 swap-remove 或堆调整完成快速移除。
        /// activeIndex: Timer 在 TimerManager 全局活跃列表中的位置，用于 Tag 批量操作和最终回收时移出活跃集合。
        internal int activeIndex = -1;
        /// collectionIndex: Timer 在 Pending 列表或 UpdateList 逐帧列表中的位置，用于从线性列表中 O(1) 移除。
        internal int collectionIndex = -1;
        /// heapIndex: Timer 在 scaled/unscaled 最小堆中的位置，用于 Pause/Cancel/Reset/SetTimeScale 时 O(logn) 删除或重排。
        internal int heapIndex = -1;
        internal float lastClockTime;
        internal float nextDueTime;

        internal bool IsRecycled => Id == IdGenerator.InvalidId;

        public Timer()
        {
            Id = IdGenerator.InvalidId;
        }

        // --- IPoolable Implementation ---
        public int MaxCount => 100;
        public int InitCount => 20;

        public void OnSpawn()
        {
            Id = IdGenerator.Next();
        }

        public void OnDespawn()
        {
            Recycle();
        }

        internal void Init(float duration, Action onComplete)
        {
            tag = TimerManager.TimerTags.None;
            this.duration = duration;
            this.onComplete = onComplete;

            timeScale = 1f;
            timeElapsed = 0f;
            isLoop = false;
            loopCount = 0;
            useUnscaledTime = false;
            onUpdate = null;
            isPaused = false;
            isCompleted = false;

            activeIndex = -1;
            collectionIndex = -1;
            heapIndex = -1;
            lastClockTime = 0f;
            nextDueTime = 0f;
            scheduleMode = TimerManager.TimerScheduleMode.Detached;
        }

        internal void Recycle()
        {
            Id = IdGenerator.InvalidId;
            tag = TimerManager.TimerTags.None;
            onComplete = null;
            onUpdate = null;
            timeScale = 1f;
            timeElapsed = 0f;
            isPaused = false;
            isCompleted = true;
            activeIndex = -1;
            collectionIndex = -1;
            heapIndex = -1;
            lastClockTime = 0f;
            nextDueTime = 0f;
            scheduleMode = TimerManager.TimerScheduleMode.Recycled;
        }
    }
}
