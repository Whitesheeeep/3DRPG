using System;
using System.Collections.Generic;
using WS_Modules.Pooling;

namespace WS_Modules.Utilities
{
    /// <summary>
    /// 独立的分层时间轮调度器，由外部传入单位增量推进。
    /// 输入单位由调用方定义，可以是真实秒、游戏分钟或其他逻辑时间单位。
    /// </summary>
    public class TimeWheelScheduler
    {
        private const float TickEpsilon = 0.000001f;

        // 入桶记录保存“任务以某个调度版本进入桶”的快照。
        // 取消、暂停、恢复都采用惰性移除，旧记录会暂时留在桶中；
        // 扫描桶时通过版本号判断它是不是过期记录。
        private readonly struct BucketEntry
        {
            // 指向真正的任务对象。任务对象由 PoolManager 复用，Entry 本身不拥有任务生命周期。
            internal readonly TimeWheelTask Task;

            // 任务入桶时的调度版本。只要任务重新入桶、暂停或回收，该版本就会变化。
            // 因此旧 Entry 即使还留在桶里，也会在 IsLiveEntry 中被识别为过期记录。
            internal readonly int ScheduleVersion;

            internal BucketEntry(TimeWheelTask task)
            {
                Task = task;
                ScheduleVersion = task.ScheduleVersion;
            }
        }

        // 标识调度器实例的唯一 ID，验证句柄归属。由于任务 ID 也使用 long，这里用 long 而不是 Guid 来避免性能开销。
        private readonly long _schedulerId = IdGenerator.Next();
        private readonly TimeWheelConfig _config;

        // _wheels[level][slot] 表示某一层某一格的桶。
        // 第 0 层是最细粒度层；层级越高，覆盖的时间范围越大。
        // 高层桶到期时，会把里面的任务重新分发到更低层。
        private readonly List<BucketEntry>[][] _wheels;

        // _levelSpans[level] 表示该层每一格等于多少个基础刻度。
        // 例如槽位数量为 { 4, 4 }：
        // 第 0 层每格跨度 = 1 个刻度，总覆盖 = 4 个刻度
        // 第 1 层每格跨度 = 4 个刻度，总覆盖 = 16 个刻度
        private readonly long[] _levelSpans;
        // Capacities 是从 0 Level 到 本 Level 的总跨度。例如槽位数量为 { 4, 4 }：
        // 第 0 层容量 = 4 个刻度
        // 第 1 层容量 = 16 个刻度
        private readonly long[] _levelCapacities;

        // 句柄查询表。由于移除是惰性的，已取消任务可能会在旧记录被扫描前暂留在这里。管理者所有的 Task。
        private readonly Dictionary<long, TimeWheelTask> _tasks = new Dictionary<long, TimeWheelTask>(256);

        private long _currentTick;
        private float _accumulatedUnits;

        // 业务可见的活跃任务数。即使旧记录仍留在桶中，已取消或已完成任务也不计入这里。
        private int _activeCount;

        public TimeWheelConfig Config => _config;
        public long CurrentTick => _currentTick;
        public int ActiveCount => _activeCount;

        public TimeWheelScheduler(TimeWheelConfig config = null)
        {
            _config = config ?? new TimeWheelConfig();
            _wheels = new List<BucketEntry>[_config.LevelCount][];
            _levelSpans = new long[_config.LevelCount];
            _levelCapacities = new long[_config.LevelCount];

            long span = 1;
            for (int level = 0; level < _config.LevelCount; level++)
            {
                int slotCount = _config.GetSlotCount(level);

                _levelSpans[level] = span;
                _levelCapacities[level] = SaturatingMultiply(span, slotCount);

                _wheels[level] = new List<BucketEntry>[slotCount];
                for (int slot = 0; slot < _wheels[level].Length; slot++)
                {
                    _wheels[level][slot] = new List<BucketEntry>();
                }

                span = _levelCapacities[level];
            }
        }

        public TimeWheelHandle Schedule(float delay, Action callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            long delayTicks = UnitsToTicks(delay);
            var task = PoolManager.Instance.GetClass<TimeWheelTask>();
            task.Init(delayTicks, _currentTick + delayTicks, callback);
            _tasks[task.Id] = task;
            _activeCount++;
            Insert(task);
            return new TimeWheelHandle(_schedulerId, task.Id, task.Version);
        }

        public TimeWheelHandle ScheduleRepeat(float interval, Action callback, int repeatCount = -1)
        {
            if (repeatCount == 0)
            {
                return default;
            }

            var handle = Schedule(interval, callback);
            if (_tasks.TryGetValue(handle.TaskId, out var task))
            {
                task.SetRepeat(UnitsToTicks(interval), repeatCount);
            }

            return handle;
        }

        public bool Cancel(TimeWheelHandle handle)
        {
            if (!TryGetTask(handle, out var task))
            {
                return false;
            }

            task.State = TimeWheelTaskState.Cancelled;
            Deactivate(task);
            return true;
        }

        public bool Pause(TimeWheelHandle handle)
        {
            if (!TryGetTask(handle, out var task) || task.State != TimeWheelTaskState.Scheduled)
            {
                return false;
            }

            task.RemainingTicks = Math.Max(1, task.DueTick - _currentTick);
            task.State = TimeWheelTaskState.Paused;
            task.ScheduleVersion++;
            return true;
        }

        public bool Resume(TimeWheelHandle handle)
        {
            if (!TryGetTask(handle, out var task) || task.State != TimeWheelTaskState.Paused)
            {
                return false;
            }

            task.DueTick = _currentTick + Math.Max(1, task.RemainingTicks);
            task.State = TimeWheelTaskState.Scheduled;
            Insert(task);
            return true;
        }

        public void Tick(float deltaUnits)
        {
            if (deltaUnits <= 0f)
            {
                return;
            }

            _accumulatedUnits += deltaUnits;

            int ticks = 0;
            // 把外部输入单位转换成固定刻度。误差值用来抵消浮点累加误差，
            // 例如 0.29f + 0.01f 可能略小于 0.3f。
            while (_accumulatedUnits + TickEpsilon >= _config.TickUnit && ticks < _config.MaxCatchUpTicksPerFrame)
            {
                _accumulatedUnits -= _config.TickUnit;
                if (_accumulatedUnits is < 0f and > -TickEpsilon)
                {
                    _accumulatedUnits = 0f;
                }

                AdvanceOneTick();
                ticks++;
            }

            // 如果某一帧卡顿很久，避免在单帧内无限补刻度。
            // 剩余累计时间会被限制在一个上限内，让后续帧逐步追赶。
            if (ticks == _config.MaxCatchUpTicksPerFrame && _accumulatedUnits + TickEpsilon >= _config.TickUnit)
            {
                float maxRemainder = _config.TickUnit * _config.MaxCatchUpTicksPerFrame;
                if (_accumulatedUnits > maxRemainder)
                {
                    _accumulatedUnits = maxRemainder;
                }
            }
        }

        public void Clear()
        {
            foreach (var pair in _tasks)
            {
                ReleaseTask(pair.Value);
            }

            _tasks.Clear();
            _activeCount = 0;
            for (int level = 0; level < _wheels.Length; level++)
            {
                for (int slot = 0; slot < _wheels[level].Length; slot++)
                {
                    _wheels[level][slot].Clear();
                }
            }

            _currentTick = 0;
            _accumulatedUnits = 0f;
        }

        private void AdvanceOneTick()
        {
            _currentTick++;

            // 先把到期的高层桶下放，再执行第 0 层当前刻度。
            // 这样从高层下放到第 0 层、且已经到期的任务可以在同一个刻度触发。
            CascadeDueLevels();
            ProcessCurrentSlot();
        }

        private void CascadeDueLevels()
        {
            for (int level = 1; level < _wheels.Length; level++)
            {
                // 高层只有在当前基础刻度对齐该层跨度时才会推进。
                // 如果这一层没有推进，更高层也不可能推进。
                if (_currentTick % _levelSpans[level] != 0)
                {
                    break;
                }

                int slot = GetSlot(level, _currentTick);
                var bucket = _wheels[level][slot];
                if (bucket.Count == 0)
                {
                    continue;
                }

                var entries = new List<BucketEntry>(bucket);
                bucket.Clear();

                for (int i = 0; i < entries.Count; i++)
                {
                    var task = entries[i].Task;
                    if (!IsLiveEntry(entries[i]))
                    {
                        // 过期记录来自惰性的取消、暂停、恢复或重新入桶。
                        TryRecycleInactive(task);
                        continue;
                    }

                    // 按任务的绝对到期刻度重新入桶。它可能进入更低层，
                    // 如果距离仍然很远，也可能继续留在当前层或高层。
                    Insert(task);
                }
            }
        }

        private void ProcessCurrentSlot()
        {
            int slot = GetSlot(0, _currentTick);
            var bucket = _wheels[0][slot];
            if (bucket.Count == 0)
            {
                return;
            }

            var entries = new List<BucketEntry>(bucket);
            bucket.Clear();

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var task = entry.Task;

                if (!IsLiveEntry(entry))
                {
                    TryRecycleInactive(task);
                    continue;
                }

                if (task.DueTick > _currentTick)
                {
                    // 落在同一个槽位不代表一定到期。槽位环形复用时，可能提前扫到
                    // 到期刻度更晚的任务，这时重新入桶等待下一轮。
                    Insert(task);
                    continue;
                }

                Execute(task);
            }
        }

        private void Execute(TimeWheelTask task)
        {
            task.State = TimeWheelTaskState.Executing;

            try
            {
                task.Callback?.Invoke();
            }
            finally
            {
                // 回调里可能取消当前任务；这种情况下不能再把它重新入桶。
                if (task.State == TimeWheelTaskState.Cancelled || task.Id == IdGenerator.InvalidId)
                {
                    RecycleTask(task);
                }
                else if (ShouldRepeat(task))
                {
                    // 周期任务按原始间隔重新计时，不按“原计划到期刻度”追赶。
                    // 这样语义简单，也避免卡顿后一次性爆发执行。
                    task.DueTick = _currentTick + task.IntervalTicks;
                    task.RemainingTicks = task.IntervalTicks;
                    task.State = TimeWheelTaskState.Scheduled;
                    Insert(task);
                }
                else
                {
                    Deactivate(task);
                    _tasks.Remove(task.Id);
                    RecycleTask(task);
                }
            }
        }

        private bool ShouldRepeat(TimeWheelTask task)
        {
            if (!task.IsRepeating)
            {
                return false;
            }

            if (task.RemainingRepeatCount == -1)
            {
                return true;
            }

            task.RemainingRepeatCount--;
            return task.RemainingRepeatCount > 0;
        }

        private void Insert(TimeWheelTask task)
        {
            // 每次入桶都递增调度版本，使该任务的旧记录自动失效。
            task.ScheduleVersion++;
            long delayTicks = Math.Max(1, task.DueTick - _currentTick);
            int level = ChooseLevel(delayTicks);
            int slot = GetSlot(level, task.DueTick);
            _wheels[level][slot].Add(new BucketEntry(task));
        }

        private int ChooseLevel(long delayTicks)
        {
            // 选择能覆盖剩余延迟的最低层级。
            for (int level = 0; level < _levelCapacities.Length; level++)
            {
                if (delayTicks <= _levelCapacities[level])
                {
                    return level;
                }
            }

            return _levelCapacities.Length - 1;
        }

        private int GetSlot(int level, long dueTick)
        {
            // 将绝对刻度映射到指定层级的槽位下标。
            return (int)((dueTick / _levelSpans[level]) % _config.GetSlotCount(level));
        }

        private long UnitsToTicks(float units)
        {
            if (units <= 0f)
            {
                return 1;
            }

            return Math.Max(1, (long)Math.Ceiling(units / _config.TickUnit));
        }

        private bool TryGetTask(TimeWheelHandle handle, out TimeWheelTask task)
        {
            task = null;
            return handle.BelongsTo(_schedulerId) &&
                   _tasks.TryGetValue(handle.TaskId, out task) &&
                   task.Version == handle.Version &&
                   task.State != TimeWheelTaskState.Cancelled &&
                   task.State != TimeWheelTaskState.Completed;
        }

        private bool IsLiveEntry(BucketEntry entry)
        {
            var task = entry.Task;
            return task != null &&
                   task.Id != IdGenerator.InvalidId &&
                   task.State == TimeWheelTaskState.Scheduled &&
                   task.ScheduleVersion == entry.ScheduleVersion &&
                   _tasks.TryGetValue(task.Id, out var activeTask) &&
                   ReferenceEquals(activeTask, task);
        }

        private void TryRecycleInactive(TimeWheelTask task)
        {
            if (task == null || task.Id == IdGenerator.InvalidId)
            {
                return;
            }

            if (task.State is TimeWheelTaskState.Cancelled or TimeWheelTaskState.Completed)
            {
                RecycleTask(task);
            }
        }

        private void RecycleTask(TimeWheelTask task)
        {
            if (task == null || task.Id == IdGenerator.InvalidId)
            {
                return;
            }

            Deactivate(task);
            _tasks.Remove(task.Id);
            ReleaseTask(task);
        }

        private void Deactivate(TimeWheelTask task)
        {
            // 活跃数量表示业务可见的活跃任务，而不是内部桶记录数量。
            // 一个任务可能走多条清理路径，所以这里只允许扣减一次。
            if (task == null || !task.IsActiveCounted)
            {
                return;
            }

            task.IsActiveCounted = false;
            _activeCount = Math.Max(0, _activeCount - 1);
        }

        private void ReleaseTask(TimeWheelTask task) => PoolManager.Instance.RecycleClass(task);

        // 饱和乘法：避免 a * b > long.MaxValue，超过最大值时，直接使用最大值
        private static long SaturatingMultiply(long a, int b)
        {
            if (a > long.MaxValue / b)
            {
                return long.MaxValue;
            }

            return a * b;
        }
    }
}
