using System;
using UnityEngine;

namespace RPG.Game.JumpText
{
    /// <summary>
    /// 跳字使用的视觉样式类别；具体颜色、字号和描边由 <see cref="JumpTextProfile"/> 配置。
    /// </summary>
    public enum JumpTextStyle
    {
        /// <summary>普通伤害样式。</summary>
        NormalDamage,

        /// <summary>暴击伤害样式。</summary>
        CriticalDamage,

        /// <summary>治疗或回复样式。</summary>
        Healing
    }

    /// <summary>
    /// 描述一次跳字请求；请求只携带最终要展示的数值和出生世界坐标，不参与伤害结算。
    /// </summary>
    public readonly struct JumpTextRequest
    {
        /// <summary>要展示的有符号整数值。</summary>
        public int Value { get; }

        /// <summary>数字出生时记录的世界坐标。</summary>
        public Vector3 WorldPosition { get; }

        /// <summary>请求使用的视觉样式。</summary>
        public JumpTextStyle Style { get; }

        /// <summary>正数是否强制显示加号；负数始终保留减号。</summary>
        public bool ShowPositiveSign { get; }

        /// <summary>可选的可复现随机种子；为空时由 Manager 分配递增种子。</summary>
        public int? RandomSeed { get; }

        /// <summary>
        /// 创建跳字请求快照。
        /// </summary>
        /// <param name="value">要显示的有符号整数值。</param>
        /// <param name="worldPosition">跳字出生世界坐标。</param>
        /// <param name="style">视觉样式。</param>
        /// <param name="showPositiveSign">正数是否显示加号。</param>
        /// <param name="randomSeed">可选的轨迹随机种子。</param>
        public JumpTextRequest(int value, Vector3 worldPosition,
            JumpTextStyle style = JumpTextStyle.NormalDamage,
            bool showPositiveSign = false, int? randomSeed = null)
        {
            Value = value;
            WorldPosition = worldPosition;
            Style = style;
            ShowPositiveSign = showPositiveSign;
            RandomSeed = randomSeed;
        }
    }

    /// <summary>
    /// 保存一条跳字在其生命周期内不变的数值、锚点和随机状态；实例本身不对应任何 Unity 对象。
    /// </summary>
    internal sealed class JumpTextRuntimeState
    {
        #region 不变请求数据

        /// <summary>数值绝对值，使用 long 保存以覆盖 int.MinValue。</summary>
        internal long Magnitude;

        /// <summary>原始有符号数值是否为负。</summary>
        internal bool IsNegative;

        /// <summary>出生世界坐标快照。</summary>
        internal Vector3 WorldPosition;

        /// <summary>当前视觉样式。</summary>
        internal JumpTextStyle Style;

        /// <summary>正数是否显示加号。</summary>
        internal bool ShowPositiveSign;

        /// <summary>本条跳字使用的随机种子。</summary>
        internal int RandomSeed;

        /// <summary>用于容量淘汰的单调出生序号。</summary>
        internal long Sequence;

        /// <summary>按符号规则展开后的字符数量。</summary>
        internal int CharacterCount;

        /// <summary>按字体度量计算出的未播放动画文字宽度（像素）。</summary>
        internal float BaseTextWidthPixels;

        #endregion

        #region 可变生命周期数据

        /// <summary>已经播放的缩放秒数。</summary>
        internal float ElapsedSeconds;

        #endregion

        #region 生命周期辅助

        /// <summary>
        /// 从请求快照初始化一条可复用运行时记录。
        /// </summary>
        /// <param name="request">待显示的跳字请求。</param>
        /// <param name="randomSeed">已解析的实际随机种子。</param>
        /// <param name="sequence">用于最老记录淘汰的出生序号。</param>
        /// <param name="characterCount">该请求展开后的字符数量。</param>
        internal void Initialize(in JumpTextRequest request, int randomSeed,
            long sequence, int characterCount)
        {
            Magnitude = Math.Abs((long)request.Value);
            IsNegative = request.Value < 0;
            WorldPosition = request.WorldPosition;
            Style = request.Style;
            ShowPositiveSign = request.ShowPositiveSign;
            RandomSeed = randomSeed;
            Sequence = sequence;
            CharacterCount = characterCount;
            BaseTextWidthPixels = 0f;
            ElapsedSeconds = 0f;
        }

        /// <summary>
        /// 清除运行时记录，使其可以在下一次 Show 调用中复用。
        /// </summary>
        internal void Reset()
        {
            Magnitude = 0L;
            IsNegative = false;
            WorldPosition = default;
            Style = JumpTextStyle.NormalDamage;
            ShowPositiveSign = false;
            RandomSeed = 0;
            Sequence = 0L;
            CharacterCount = 0;
            BaseTextWidthPixels = 0f;
            ElapsedSeconds = 0f;
        }

        #endregion
    }

    /// <summary>
    /// 保存一次实例化渲染后端的统计结果，供 Manager 和 Odin 测试器读取。
    /// </summary>
    internal readonly struct JumpTextRenderResult
    {
        /// <summary>本帧通过相机裁剪后实际展开的字符数量。</summary>
        internal int VisibleGlyphCount { get; }

        /// <summary>本帧提交给 Graphics 的 Instanced Draw Call 数量。</summary>
        internal int DrawCallCount { get; }

        /// <summary>
        /// 创建渲染统计快照。
        /// </summary>
        /// <param name="visibleGlyphCount">可见字符数量。</param>
        /// <param name="drawCallCount">绘制批次数量。</param>
        internal JumpTextRenderResult(int visibleGlyphCount, int drawCallCount)
        {
            VisibleGlyphCount = visibleGlyphCount;
            DrawCallCount = drawCallCount;
        }
    }
}
