using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.LogModule;
using WS_Modules.Singleton;

namespace RPG.Game.JumpText
{
    /// <summary>
    /// 管理跳字请求的生命周期，并驱动可替换的 GPU Instancing 渲染后端。
    /// </summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖 JumpTextProfile 和场景中的 MainCamera；数字使用世界坐标出生快照，不创建 Canvas 或 TMP GameObject。")]
    public sealed class JumpTextManager : SingletonMonoBase<JumpTextManager>
    {
        #region 配置与依赖字段

        [SerializeField, Required, Tooltip("跳字字体、容量、动画曲线和样式配置。")]
        private JumpTextProfile profile;

        // 后端持有共享 Mesh、Material 和批处理数组；Manager 只负责请求与生命周期。
        private IJumpTextRenderBackend renderBackend;

        #endregion

        #region 运行时状态

        // 活跃列表保存完整跳字记录；淘汰时整条移除，避免容量不足显示残缺数字。
        private readonly List<JumpTextRuntimeState> activeTexts = new();
        private readonly Stack<JumpTextRuntimeState> recycledTexts = new();
        private int activeGlyphCount;
        private int nextSeed = 1;
        private long nextSequence;
        private bool initialized;
        private bool cameraWarningIssued;
        private bool capacityWarningIssued;

        #endregion

        #region 诊断属性

        /// <summary>获取当前活跃跳字条数。</summary>
        public int ActiveTextCount => activeTexts.Count;

        /// <summary>获取当前活跃跳字展开后的字符实例总数。</summary>
        public int ActiveGlyphCount => activeGlyphCount;

        /// <summary>获取最近一次 LateUpdate 提交的 Instanced Draw Call 数量。</summary>
        public int LastDrawCallCount { get; private set; }

        /// <summary>获取当前使用的跳字 Profile。</summary>
        public JumpTextProfile Profile => profile;

        #endregion

        #region Unity 生命周期

        /// <summary>
        /// 注册单例并创建字体缓存、共享 Quad 和实例材质。
        /// </summary>
        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;

            renderBackend = new InstancedJumpTextRenderBackend(profile);
            initialized = true;
            WSLog.Log($"[JumpTextManager] 初始化完成，最大字符实例数={profile.MaxGlyphCount}。");
        }

        /// <summary>
        /// 销毁时释放后端 Unity 资源、清空活动记录并解除单例引用。
        /// </summary>
        protected override void OnDestroy()
        {
            if (Instance == this)
            {
                renderBackend?.Dispose();
                renderBackend = null;
                activeTexts.Clear();
                recycledTexts.Clear();
                activeGlyphCount = 0;
                initialized = false;
                WSLog.Log("[JumpTextManager] 已释放渲染后端和活跃跳字记录。");
            }

            base.OnDestroy();
        }

        /// <summary>
        /// 使用缩放时间推进所有活跃跳字；生命周期结束的记录回收到轻量运行时池。
        /// </summary>
        private void Update()
        {
            if (!initialized || activeTexts.Count == 0) return;

            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;

            for (int index = activeTexts.Count - 1; index >= 0; index--)
            {
                JumpTextRuntimeState state = activeTexts[index];
                state.ElapsedSeconds += deltaTime;
                if (state.ElapsedSeconds < profile.LifetimeSeconds) continue;
                RemoveAt(index);
            }
        }

        /// <summary>
        /// 在所有普通帧更新后按当前 MainCamera 投影并提交跳字 Instancing 批次。
        /// </summary>
        private void LateUpdate()
        {
            LastDrawCallCount = 0;
            if (!initialized || activeTexts.Count == 0) return;

            Camera camera = Camera.main;
            if (camera == null)
            {
                if (!cameraWarningIssued)
                {
                    WSLog.LogWarning("[JumpTextManager] 存在活跃跳字但未找到 MainCamera，暂时跳过绘制。");
                    cameraWarningIssued = true;
                }

                return;
            }

            cameraWarningIssued = false;
            JumpTextRenderResult result = renderBackend.Render(camera, activeTexts, profile);
            LastDrawCallCount = result.DrawCallCount;
        }

        #endregion

        #region 公开操作

        /// <summary>
        /// 发布一条世界坐标出生快照跳字；调用方应传入已经结算完成的最终展示数值。
        /// </summary>
        /// <param name="request">跳字请求快照。</param>
        /// <exception cref="InvalidOperationException">Manager 尚未完成初始化，或请求字符数超过 Profile 容量时抛出。</exception>
        /// <exception cref="ArgumentException">世界坐标包含非有限数值时抛出。</exception>
        public void Show(in JumpTextRequest request)
        {
            EnsureInitialized();
            ValidateRequest(request);

            int characterCount = InstancedJumpTextRenderBackend.GetCharacterCount(
                request.Value, request.ShowPositiveSign);
            if (characterCount > profile.MaxGlyphCount)
                throw new InvalidOperationException(
                    $"[JumpTextManager] 请求字符数 {characterCount} 超过 Profile 容量 {profile.MaxGlyphCount}。");

            int evictedTextCount = 0;
            while (activeGlyphCount + characterCount > profile.MaxGlyphCount)
            {
                int oldestIndex = FindOldestIndex();
                RemoveAt(oldestIndex);
                evictedTextCount++;
            }

            if (evictedTextCount > 0)
            {
                // 连续爆发期间只记录一次容量压力，避免 Show 高频调用产生日志洪水。
                if (!capacityWarningIssued)
                {
                    WSLog.LogWarning(
                        $"[JumpTextManager] 容量不足，淘汰最老完整跳字 {evictedTextCount} 条后继续显示。");
                    capacityWarningIssued = true;
                }
            }
            else
            {
                capacityWarningIssued = false;
            }

            JumpTextRuntimeState state = recycledTexts.Count > 0
                ? recycledTexts.Pop()
                : new JumpTextRuntimeState();
            int seed = request.RandomSeed ?? AllocateSeed();
            state.Initialize(request, seed, ++nextSequence, characterCount);
            state.BaseTextWidthPixels = renderBackend.GetBaseTextWidth(state);
            activeTexts.Add(state);
            activeGlyphCount += characterCount;
        }

        /// <summary>
        /// 使用便捷参数发布一条跳字请求。
        /// </summary>
        /// <param name="value">要显示的有符号整数值。</param>
        /// <param name="worldPosition">跳字出生世界坐标。</param>
        /// <param name="style">视觉样式。</param>
        /// <param name="showPositiveSign">正数是否显示加号。</param>
        public void Show(int value, Vector3 worldPosition,
            JumpTextStyle style = JumpTextStyle.NormalDamage,
            bool showPositiveSign = false)
        {
            Show(new JumpTextRequest(value, worldPosition, style, showPositiveSign));
        }

        /// <summary>
        /// 清空所有活跃跳字并回收运行时记录；不会销毁共享字体材质和 Mesh。
        /// </summary>
        public void Clear()
        {
            EnsureInitialized();
            int clearedCount = activeTexts.Count;
            for (int index = 0; index < activeTexts.Count; index++)
            {
                JumpTextRuntimeState state = activeTexts[index];
                state.Reset();
                recycledTexts.Push(state);
            }

            activeTexts.Clear();
            activeGlyphCount = 0;
            LastDrawCallCount = 0;
            capacityWarningIssued = false;
            if (clearedCount > 0)
                WSLog.Log($"[JumpTextManager] 清空活跃跳字 {clearedCount} 条。");
        }

        #endregion

        #region 状态刷新与淘汰

        /// <summary>确保公开操作只在后端和 Profile 均完成初始化后执行。</summary>
        /// <exception cref="InvalidOperationException">Manager 尚未初始化时抛出。</exception>
        private void EnsureInitialized()
        {
            if (!initialized || renderBackend == null)
                throw new InvalidOperationException("[JumpTextManager] 尚未完成初始化。");
        }

        /// <summary>校验请求坐标和样式枚举等外部输入边界。</summary>
        /// <param name="request">待校验请求。</param>
        /// <exception cref="ArgumentException">请求坐标不是有限数值时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">样式枚举值无效时抛出。</exception>
        private static void ValidateRequest(in JumpTextRequest request)
        {
            Vector3 position = request.WorldPosition;
            if (float.IsNaN(position.x) || float.IsInfinity(position.x) ||
                float.IsNaN(position.y) || float.IsInfinity(position.y) ||
                float.IsNaN(position.z) || float.IsInfinity(position.z))
                throw new ArgumentException("跳字 WorldPosition 必须由有限数值组成。", nameof(request));
            // 使用无装箱的 switch 校验高频 Show 请求，避免枚举输入检查制造托管分配。
            switch (request.Style)
            {
                case JumpTextStyle.NormalDamage:
                case JumpTextStyle.CriticalDamage:
                case JumpTextStyle.Healing:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request.Style), request.Style,
                        "跳字样式枚举值无效。");
            }
        }

        /// <summary>分配不与零冲突的递增随机种子。</summary>
        /// <returns>本条跳字使用的随机种子。</returns>
        private int AllocateSeed()
        {
            int seed = nextSeed;
            nextSeed = nextSeed == int.MaxValue ? 1 : nextSeed + 1;
            return seed;
        }

        /// <summary>查找出生序号最小的完整跳字记录。</summary>
        /// <returns>最老记录在活跃列表中的索引。</returns>
        /// <exception cref="InvalidOperationException">活跃列表为空时抛出。</exception>
        private int FindOldestIndex()
        {
            if (activeTexts.Count == 0)
                throw new InvalidOperationException("[JumpTextManager] 无法从空列表中淘汰跳字。");

            int oldestIndex = 0;
            long oldestSequence = activeTexts[0].Sequence;
            for (int index = 1; index < activeTexts.Count; index++)
            {
                if (activeTexts[index].Sequence >= oldestSequence) continue;
                oldestIndex = index;
                oldestSequence = activeTexts[index].Sequence;
            }

            return oldestIndex;
        }

        /// <summary>移除指定记录、同步字符计数并回收状态对象。</summary>
        /// <param name="index">活跃列表索引。</param>
        private void RemoveAt(int index)
        {
            JumpTextRuntimeState state = activeTexts[index];
            activeTexts.RemoveAt(index);
            activeGlyphCount -= state.CharacterCount;
            state.Reset();
            recycledTexts.Push(state);
        }

        #endregion
    }
}
