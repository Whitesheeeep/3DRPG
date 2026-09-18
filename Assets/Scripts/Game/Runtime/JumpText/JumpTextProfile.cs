using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;

namespace RPG.Game.JumpText
{
    /// <summary>
    /// 保存跳字字体、实例容量、动画曲线和各样式视觉参数的 ScriptableObject 配置。
    /// </summary>
    [CreateAssetMenu(fileName = "JumpTextProfile", menuName = "RPG/Jump Text/Profile")]
    public sealed class JumpTextProfile : ScriptableObject
    {
        #region 配置字段

        [SerializeField, Required, LabelText("数字字体 SDF")]
        private TMP_FontAsset fontAsset;

        [SerializeField, Required, LabelText("实例化 Shader")]
        private Shader instancedShader;

        [SerializeField, MinValue(1), LabelText("最大字符实例数")]
        private int maxGlyphCount = 4096;

        [SerializeField, MinValue(0.01f), LabelText("生命周期（秒）")]
        private float lifetimeSeconds = 0.95f;

        [SerializeField, MinValue(1f), LabelText("基础像素高度")]
        private float basePixelHeight = 40f;

        [SerializeField, LabelText("字符间距（像素）")]
        private float letterSpacingPixels = -1f;

        [SerializeField, MinValue(0f), LabelText("视口裁剪余量（像素）")]
        private float viewportMarginPixels = 128f;

        [SerializeField, MinValue(0f), LabelText("上浮距离（像素）")]
        private float verticalRisePixels = 34f;

        [SerializeField, MinValue(0f), LabelText("横向随机距离（像素）")]
        private float horizontalSpreadPixels = 8f;

        [SerializeField, LabelText("出生横向偏移范围")]
        private Vector2 initialHorizontalOffsetRange = new(-4f, 4f);

        [SerializeField, LabelText("缩放曲线")]
        private AnimationCurve scaleCurve = new AnimationCurve(
            new Keyframe(0f, 0.78f),
            new Keyframe(0.08f, 1.14f),
            new Keyframe(0.20f, 0.98f),
            new Keyframe(0.32f, 1f),
            new Keyframe(1f, 1f));

        [SerializeField, LabelText("上浮曲线")]
        private AnimationCurve verticalOffsetCurve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.16f, 0.45f),
            new Keyframe(0.48f, 0.80f),
            new Keyframe(1f, 1f));

        [SerializeField, LabelText("横向曲线")]
        private AnimationCurve horizontalOffsetCurve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.45f, 0.70f),
            new Keyframe(1f, 1f));

        [SerializeField, LabelText("透明度曲线")]
        private AnimationCurve alphaCurve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.64f, 1f),
            new Keyframe(0.82f, 0.80f),
            new Keyframe(1f, 0f));

        [SerializeField, LabelText("样式配置")]
        private JumpTextStyleSettings[] styleSettings =
        {
            new(JumpTextStyle.NormalDamage,
                new Color(1f, 0.92f, 0.72f, 1f),
                new Color(0.18f, 0.06f, 0.01f, 1f), 1f, 0.065f),
            new(JumpTextStyle.CriticalDamage,
                new Color(1f, 0.42f, 0.16f, 1f),
                new Color(0.24f, 0.015f, 0f, 1f), 1.14f, 0.08f),
            new(JumpTextStyle.Healing,
                new Color(0.36f, 1f, 0.55f, 1f),
                new Color(0.01f, 0.18f, 0.04f, 1f), 1f, 0.065f)
        };

        #endregion

        #region 配置属性

        /// <summary>获取用于解析字符度量和 SDF Atlas 的字体资产。</summary>
        public TMP_FontAsset FontAsset => fontAsset;

        /// <summary>获取带有 GPU Instancing 变体的跳字 Shader。</summary>
        public Shader InstancedShader => instancedShader;

        /// <summary>获取活跃字符实例总容量。</summary>
        public int MaxGlyphCount => maxGlyphCount;

        /// <summary>获取使用缩放时间计算的跳字生命周期。</summary>
        public float LifetimeSeconds => lifetimeSeconds;

        /// <summary>获取字符基准像素高度。</summary>
        public float BasePixelHeight => basePixelHeight;

        /// <summary>获取相邻字符之间附加的像素间距。</summary>
        public float LetterSpacingPixels => letterSpacingPixels;

        /// <summary>获取相机视口裁剪时使用的像素余量。</summary>
        public float ViewportMarginPixels => viewportMarginPixels;

        /// <summary>获取生命周期内的最大上浮像素距离。</summary>
        public float VerticalRisePixels => verticalRisePixels;

        /// <summary>获取随机横向轨迹的最大像素距离。</summary>
        public float HorizontalSpreadPixels => horizontalSpreadPixels;

        /// <summary>获取出生时随机横向偏移的最小值和最大值。</summary>
        public Vector2 InitialHorizontalOffsetRange => initialHorizontalOffsetRange;

        /// <summary>获取缩放曲线；输入为 0 到 1 的归一化生命周期。</summary>
        public AnimationCurve ScaleCurve => scaleCurve;

        /// <summary>获取上浮曲线；输入为 0 到 1 的归一化生命周期。</summary>
        public AnimationCurve VerticalOffsetCurve => verticalOffsetCurve;

        /// <summary>获取横向扰动曲线；输入为 0 到 1 的归一化生命周期。</summary>
        public AnimationCurve HorizontalOffsetCurve => horizontalOffsetCurve;

        /// <summary>获取透明度曲线；输入为 0 到 1 的归一化生命周期。</summary>
        public AnimationCurve AlphaCurve => alphaCurve;

        #endregion

        #region 配置查询

        /// <summary>
        /// 按样式取得不可变的运行时视觉参数。
        /// </summary>
        /// <param name="style">待查询的样式。</param>
        /// <returns>对应样式的文字颜色、描边和字号倍率。</returns>
        /// <exception cref="InvalidOperationException">配置中不存在指定样式时抛出。</exception>
        internal JumpTextStyleSettings GetStyleSettings(JumpTextStyle style)
        {
            for (int index = 0; index < styleSettings.Length; index++)
            {
                JumpTextStyleSettings settings = styleSettings[index];
                if (settings.Style == style)
                    return settings;
            }

            throw new InvalidOperationException($"JumpTextProfile '{name}' 缺少样式 '{style}'。");
        }

        #endregion

        #region 配置校验

        /// <summary>
        /// 在运行时边界校验字体、Shader、容量、动画曲线和全部样式。
        /// </summary>
        /// <exception cref="InvalidOperationException">配置缺失、数值非法或字体 Atlas 不满足单页约束时抛出。</exception>
        public void Validate()
        {
            if (fontAsset == null)
                throw new InvalidOperationException($"JumpTextProfile '{name}' 未配置 TMP_FontAsset。");
            if (instancedShader == null)
                throw new InvalidOperationException($"JumpTextProfile '{name}' 未配置实例化 Shader。");
            if (fontAsset.atlasTextures == null || fontAsset.atlasTextures.Length != 1 ||
                fontAsset.atlasTextures[0] == null)
                throw new InvalidOperationException(
                    $"JumpTextProfile '{name}' 要求字体包含且只包含一个有效 Atlas Texture。");
            if (fontAsset.faceInfo.pointSize <= 0 || float.IsNaN(fontAsset.faceInfo.scale) ||
                float.IsInfinity(fontAsset.faceInfo.scale) || fontAsset.faceInfo.scale <= 0f)
                throw new InvalidOperationException($"JumpTextProfile '{name}' 的字体 FaceInfo 无效。");
            if (maxGlyphCount <= 0)
                throw new InvalidOperationException($"JumpTextProfile '{name}' 的最大字符实例数必须大于零。");
            ValidateFinitePositive(lifetimeSeconds, "LifetimeSeconds");
            ValidateFinitePositive(basePixelHeight, "BasePixelHeight");
            ValidateFinite(letterSpacingPixels, "LetterSpacingPixels");
            ValidateFiniteNonNegative(viewportMarginPixels, "ViewportMarginPixels");
            ValidateFiniteNonNegative(verticalRisePixels, "VerticalRisePixels");
            ValidateFiniteNonNegative(horizontalSpreadPixels, "HorizontalSpreadPixels");
            if (float.IsNaN(initialHorizontalOffsetRange.x) ||
                float.IsInfinity(initialHorizontalOffsetRange.x) ||
                float.IsNaN(initialHorizontalOffsetRange.y) ||
                float.IsInfinity(initialHorizontalOffsetRange.y) ||
                initialHorizontalOffsetRange.x > initialHorizontalOffsetRange.y)
                throw new InvalidOperationException(
                    $"JumpTextProfile '{name}' 的出生横向偏移范围无效。");

            ValidateCurve(scaleCurve, "ScaleCurve");
            ValidateCurve(verticalOffsetCurve, "VerticalOffsetCurve");
            ValidateCurve(horizontalOffsetCurve, "HorizontalOffsetCurve");
            ValidateCurve(alphaCurve, "AlphaCurve");

            int styleCount = Enum.GetValues(typeof(JumpTextStyle)).Length;
            if (styleSettings == null || styleSettings.Length != styleCount)
                throw new InvalidOperationException(
                    $"JumpTextProfile '{name}' 必须为每个 JumpTextStyle 配置一项样式。");

            var styles = new HashSet<JumpTextStyle>();
            for (int index = 0; index < styleSettings.Length; index++)
            {
                JumpTextStyleSettings settings = styleSettings[index];
                settings.Validate($"JumpTextProfile '{name}' 的 StyleSettings[{index}]");
                if (!styles.Add(settings.Style))
                    throw new InvalidOperationException(
                        $"JumpTextProfile '{name}' 的样式 '{settings.Style}' 重复配置。");
            }
        }

        /// <summary>校验一个必须为正的有限浮点配置。</summary>
        /// <param name="value">待校验值。</param>
        /// <param name="fieldName">字段名称。</param>
        private static void ValidateFinitePositive(float value, string fieldName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
                throw new InvalidOperationException($"JumpTextProfile 的 {fieldName} 必须是正的有限数值。");
        }

        /// <summary>校验一个允许零值但必须有限的浮点配置。</summary>
        /// <param name="value">待校验值。</param>
        /// <param name="fieldName">字段名称。</param>
        private static void ValidateFinite(float value, string fieldName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidOperationException($"JumpTextProfile 的 {fieldName} 必须是有限数值。");
        }

        /// <summary>校验一个必须为非负有限值的浮点配置。</summary>
        /// <param name="value">待校验值。</param>
        /// <param name="fieldName">字段名称。</param>
        private static void ValidateFiniteNonNegative(float value, string fieldName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new InvalidOperationException(
                    $"JumpTextProfile 的 {fieldName} 必须是非负有限数值。");
        }

        /// <summary>校验动画曲线至少包含两个有限关键帧。</summary>
        /// <param name="curve">待校验曲线。</param>
        /// <param name="fieldName">曲线字段名称。</param>
        private static void ValidateCurve(AnimationCurve curve, string fieldName)
        {
            if (curve == null || curve.length < 2)
                throw new InvalidOperationException($"JumpTextProfile 的 {fieldName} 至少需要两个关键帧。");
            Keyframe[] keys = curve.keys;
            for (int index = 0; index < keys.Length; index++)
            {
                Keyframe key = keys[index];
                if (float.IsNaN(key.time) || float.IsInfinity(key.time) ||
                    float.IsNaN(key.value) || float.IsInfinity(key.value))
                    throw new InvalidOperationException(
                        $"JumpTextProfile 的 {fieldName}[{index}] 必须使用有限数值。");
            }
        }

        #endregion
    }

    /// <summary>
    /// 描述一个跳字样式的填充色、描边色、字号倍率和描边宽度。
    /// </summary>
    [Serializable]
    public struct JumpTextStyleSettings
    {
        #region 序列化字段

        [SerializeField, LabelText("样式")]
        private JumpTextStyle style;
        [SerializeField, LabelText("文字颜色")]
        private Color textColor;
        [SerializeField, LabelText("描边颜色")]
        private Color outlineColor;
        [SerializeField, MinValue(0.01f), LabelText("字号倍率")]
        private float scaleMultiplier;
        [SerializeField, MinValue(0f), MaxValue(0.49f), LabelText("描边宽度")]
        private float outlineWidth;

        #endregion

        #region 属性

        /// <summary>获取样式枚举值。</summary>
        public JumpTextStyle Style => style;

        /// <summary>获取 SDF 填充颜色。</summary>
        public Color TextColor => textColor;

        /// <summary>获取 SDF 描边颜色。</summary>
        public Color OutlineColor => outlineColor;

        /// <summary>获取相对于 Profile 基础像素高度的倍率。</summary>
        public float ScaleMultiplier => scaleMultiplier;

        /// <summary>获取以 SDF 阈值表示的描边宽度。</summary>
        public float OutlineWidth => outlineWidth;

        #endregion

        #region 生命周期

        /// <summary>
        /// 创建可序列化的样式配置。
        /// </summary>
        /// <param name="style">样式枚举值。</param>
        /// <param name="textColor">文字填充颜色。</param>
        /// <param name="outlineColor">描边颜色。</param>
        /// <param name="scaleMultiplier">字号倍率。</param>
        /// <param name="outlineWidth">SDF 描边宽度。</param>
        public JumpTextStyleSettings(JumpTextStyle style, Color textColor,
            Color outlineColor, float scaleMultiplier, float outlineWidth)
        {
            this.style = style;
            this.textColor = textColor;
            this.outlineColor = outlineColor;
            this.scaleMultiplier = scaleMultiplier;
            this.outlineWidth = outlineWidth;
        }

        /// <summary>校验样式颜色、字号倍率和描边范围。</summary>
        /// <param name="context">用于异常信息的配置路径。</param>
        internal void Validate(string context)
        {
            if (!Enum.IsDefined(typeof(JumpTextStyle), style))
                throw new InvalidOperationException($"{context} 的 Style 无效。");
            ValidateColor(textColor, $"{context} 的 TextColor");
            ValidateColor(outlineColor, $"{context} 的 OutlineColor");
            if (float.IsNaN(scaleMultiplier) || float.IsInfinity(scaleMultiplier) || scaleMultiplier <= 0f)
                throw new InvalidOperationException($"{context} 的 ScaleMultiplier 必须是正的有限数值。");
            if (float.IsNaN(outlineWidth) || float.IsInfinity(outlineWidth) ||
                outlineWidth < 0f || outlineWidth >= 0.5f)
                throw new InvalidOperationException($"{context} 的 OutlineWidth 必须位于 [0, 0.5) 范围。");
        }

        /// <summary>校验颜色四个分量均为有限数值。</summary>
        /// <param name="color">待校验颜色。</param>
        /// <param name="context">用于异常信息的字段路径。</param>
        private static void ValidateColor(Color color, string context)
        {
            if (float.IsNaN(color.r) || float.IsInfinity(color.r) ||
                float.IsNaN(color.g) || float.IsInfinity(color.g) ||
                float.IsNaN(color.b) || float.IsInfinity(color.b) ||
                float.IsNaN(color.a) || float.IsInfinity(color.a))
                throw new InvalidOperationException($"{context} 必须使用有限数值。");
        }

        #endregion
    }
}
