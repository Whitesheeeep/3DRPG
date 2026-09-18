using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TextCore;

namespace RPG.Game.JumpText
{
    /// <summary>
    /// 跳字渲染后端契约；将字符布局和 Graphics 提交从 Manager 生命周期中隔离，便于未来替换为 Indirect。
    /// </summary>
    internal interface IJumpTextRenderBackend : IDisposable
    {
        /// <summary>按字体度量计算一条运行时跳字的基础像素宽度。</summary>
        /// <param name="state">已经完成数值初始化的跳字运行时记录。</param>
        /// <returns>未应用动画缩放和样式倍率的文字宽度。</returns>
        float GetBaseTextWidth(JumpTextRuntimeState state);

        /// <summary>将当前可见跳字展开并提交为 GPU Instancing Draw Call。</summary>
        /// <param name="camera">用于世界坐标投影和绘制过滤的 Gameplay 相机。</param>
        /// <param name="activeTexts">当前仍处于生命周期内的跳字记录。</param>
        /// <param name="profile">本轮渲染使用的配置快照。</param>
        /// <returns>本帧可见字符数和绘制批次数。</returns>
        JumpTextRenderResult Render(Camera camera, List<JumpTextRuntimeState> activeTexts,
            JumpTextProfile profile);
    }

    /// <summary>
    /// 使用共享 Quad、MaterialPropertyBlock 和 Graphics.DrawMeshInstanced 绘制跳字字符。
    /// </summary>
    internal sealed class InstancedJumpTextRenderBackend : IJumpTextRenderBackend
    {
        #region 常量与 Shader 属性

        /// <summary>Unity Graphics.DrawMeshInstanced 单次允许提交的实例上限。</summary>
        private const int MaxInstancesPerDraw = 1023;

        /// <summary>首版允许的 ASCII 字符集合。</summary>
        private const string SupportedCharacters = "0123456789+-";

        private static readonly int AtlasTextureId = Shader.PropertyToID("_JumpTextAtlas");
        private static readonly int ScreenRectId = Shader.PropertyToID("_JumpTextScreenRect");
        private static readonly int UvRectId = Shader.PropertyToID("_JumpTextUVRect");
        private static readonly int ColorId = Shader.PropertyToID("_JumpTextColor");
        private static readonly int OutlineId = Shader.PropertyToID("_JumpTextOutline");

        #endregion

        #region 依赖与缓存字段

        // 字体和 GPU 资源在后端生命周期内保持不变；Manager 销毁时统一 Dispose。
        private readonly Material material;
        private readonly Mesh quadMesh;
        private readonly MaterialPropertyBlock propertyBlock;

        // key：ASCII 字符码；value：已换算到基础像素尺寸的字形度量和 Atlas UV。
        private readonly GlyphInfo[] glyphByAscii = new GlyphInfo[128];
        private readonly float fontScale;
        private readonly float letterSpacingPixels;

        #endregion

        #region 批处理缓存

        // 这些数组固定为 Unity 单次 Instancing 上限，Render 循环只覆写当前批次前缀。
        private readonly Matrix4x4[] matrices = new Matrix4x4[MaxInstancesPerDraw];
        private readonly Vector4[] screenRects = new Vector4[MaxInstancesPerDraw];
        private readonly Vector4[] uvRects = new Vector4[MaxInstancesPerDraw];
        private readonly Vector4[] colors = new Vector4[MaxInstancesPerDraw];
        private readonly Vector4[] outlines = new Vector4[MaxInstancesPerDraw];

        #endregion

        #region 生命周期

        /// <summary>
        /// 校验 Profile、缓存字体字形并创建共享 Quad 与实例材质。
        /// </summary>
        /// <param name="profile">跳字渲染配置。</param>
        /// <exception cref="ArgumentNullException">Profile 为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">字体缺少所需字符或 GPU 资源创建前置条件不满足时抛出。</exception>
        internal InstancedJumpTextRenderBackend(JumpTextProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            profile.Validate();

            TMP_FontAsset fontAsset = profile.FontAsset;
            Texture2D atlasTexture = fontAsset.atlasTextures[0];
            fontScale = profile.BasePixelHeight / fontAsset.faceInfo.pointSize * fontAsset.faceInfo.scale;
            letterSpacingPixels = profile.LetterSpacingPixels;
            CacheGlyphs(fontAsset, atlasTexture);

            material = new Material(profile.InstancedShader)
            {
                name = $"[JumpText] {profile.name} Material",
                enableInstancing = true,
                hideFlags = HideFlags.HideAndDontSave
            };
            material.SetTexture(AtlasTextureId, atlasTexture);

            quadMesh = CreateQuadMesh();
            propertyBlock = new MaterialPropertyBlock();
        }

        /// <summary>
        /// 释放实例材质、Quad Mesh 和批处理属性块持有的 Unity 资源。
        /// </summary>
        public void Dispose()
        {
            if (material != null) UnityEngine.Object.Destroy(material);
            if (quadMesh != null) UnityEngine.Object.Destroy(quadMesh);
            propertyBlock?.Clear();
        }

        #endregion

        #region 字体度量

        /// <summary>
        /// 按当前跳字的符号和数字位数求出未应用动画的总宽度。
        /// </summary>
        /// <param name="state">已初始化的跳字运行时记录。</param>
        /// <returns>基础像素宽度。</returns>
        public float GetBaseTextWidth(JumpTextRuntimeState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            float width = 0f;
            for (int index = 0; index < state.CharacterCount; index++)
            {
                GlyphInfo glyph = GetGlyph(GetUnicodeAt(state, index));
                width += glyph.AdvancePixels;
                if (index + 1 < state.CharacterCount)
                    width += letterSpacingPixels;
            }

            return width;
        }

        /// <summary>
        /// 从 TMP_FontAsset 缓存首版支持的数字和符号字形。
        /// </summary>
        /// <param name="fontAsset">字体资产。</param>
        /// <param name="atlasTexture">字体唯一 Atlas Texture。</param>
        /// <exception cref="InvalidOperationException">字符不存在、字形为空或 Atlas 尺寸无效时抛出。</exception>
        private void CacheGlyphs(TMP_FontAsset fontAsset, Texture2D atlasTexture)
        {
            if (atlasTexture.width <= 0 || atlasTexture.height <= 0)
                throw new InvalidOperationException("JumpText 字体 Atlas 尺寸必须大于零。");

            // key：Unicode 码点；value：字体资产中对应的字符记录。
            Dictionary<uint, TMP_Character> characterByUnicodeMap = fontAsset.characterLookupTable;
            for (int index = 0; index < SupportedCharacters.Length; index++)
            {
                uint unicode = SupportedCharacters[index];
                if (!characterByUnicodeMap.TryGetValue(unicode, out TMP_Character character) ||
                    character == null || character.glyph == null)
                    throw new InvalidOperationException(
                        $"JumpText 字体 '{fontAsset.name}' 缺少字符 '{SupportedCharacters[index]}'。");

                Glyph glyph = character.glyph;
                GlyphMetrics metrics = glyph.metrics;
                GlyphRect rect = glyph.glyphRect;
                float glyphScale = fontScale * glyph.scale;
                glyphByAscii[(int)unicode] = new GlyphInfo(
                    metrics.width * glyphScale,
                    metrics.height * glyphScale,
                    metrics.horizontalBearingX * glyphScale,
                    metrics.horizontalBearingY * glyphScale,
                    metrics.horizontalAdvance * glyphScale,
                    new Vector4(
                        (float)rect.x / atlasTexture.width,
                        (float)rect.y / atlasTexture.height,
                        (float)rect.width / atlasTexture.width,
                        (float)rect.height / atlasTexture.height));
            }
        }

        /// <summary>按 ASCII 字符码取得已经缓存的字形度量。</summary>
        /// <param name="unicode">ASCII 字符码。</param>
        /// <returns>对应字形缓存。</returns>
        /// <exception cref="InvalidOperationException">传入了不支持的字符码时抛出。</exception>
        private GlyphInfo GetGlyph(uint unicode)
        {
            if (unicode >= glyphByAscii.Length || glyphByAscii[(int)unicode].IsEmpty)
                throw new InvalidOperationException($"JumpText 缓存中不存在字符码 {unicode}。");
            return glyphByAscii[(int)unicode];
        }

        #endregion

        #region Instancing 渲染

        /// <summary>
        /// 过滤不可见锚点、求值动画曲线并按 1023 字符批次提交 Graphics Draw Call。
        /// </summary>
        /// <param name="camera">当前 Gameplay 相机。</param>
        /// <param name="activeTexts">活跃跳字记录。</param>
        /// <param name="profile">渲染配置。</param>
        /// <returns>本帧可见字符数和绘制批次数。</returns>
        public JumpTextRenderResult Render(Camera camera, List<JumpTextRuntimeState> activeTexts,
            JumpTextProfile profile)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (activeTexts == null) throw new ArgumentNullException(nameof(activeTexts));
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            int visibleGlyphCount = 0;
            int drawCallCount = 0;
            int batchCount = 0;
            Rect pixelRect = camera.pixelRect;

            for (int textIndex = 0; textIndex < activeTexts.Count; textIndex++)
            {
                JumpTextRuntimeState state = activeTexts[textIndex];
                if (!IsAnchorVisible(camera, pixelRect, state.WorldPosition, profile.ViewportMarginPixels))
                    continue;

                float normalizedTime = Mathf.Clamp01(state.ElapsedSeconds / profile.LifetimeSeconds);
                JumpTextStyleSettings style = profile.GetStyleSettings(state.Style);
                float animationScale = Mathf.Max(0f, profile.ScaleCurve.Evaluate(normalizedTime)) *
                                       style.ScaleMultiplier;
                float alpha = Mathf.Clamp01(profile.AlphaCurve.Evaluate(normalizedTime));
                float verticalOffset = profile.VerticalRisePixels *
                                       profile.VerticalOffsetCurve.Evaluate(normalizedTime);
                float horizontalOffset = EvaluateHorizontalOffset(state, normalizedTime, profile);
                float cursor = -state.BaseTextWidthPixels * 0.5f;
                float baseline = GetBaselinePixels(profile.FontAsset, fontScale);

                // 直接绘制到屏幕空间 Quad，Shader 会把局部顶点解释为像素矩形的角点。
                for (int glyphIndex = 0; glyphIndex < state.CharacterCount; glyphIndex++)
                {
                    GlyphInfo glyph = GetGlyph(GetUnicodeAt(state, glyphIndex));
                    float glyphCenterX = cursor + glyph.BearingXPixels + glyph.WidthPixels * 0.5f;
                    float glyphCenterY = baseline + glyph.BearingYPixels - glyph.HeightPixels * 0.5f;

                    matrices[batchCount] = Matrix4x4.TRS(
                        state.WorldPosition, Quaternion.identity, Vector3.one);
                    screenRects[batchCount] = new Vector4(
                        glyphCenterX * animationScale + horizontalOffset,
                        glyphCenterY * animationScale + verticalOffset,
                        glyph.WidthPixels * animationScale,
                        glyph.HeightPixels * animationScale);
                    uvRects[batchCount] = glyph.UvRect;
                    colors[batchCount] = ToVector4(style.TextColor, alpha);
                    outlines[batchCount] = new Vector4(
                        style.OutlineColor.r,
                        style.OutlineColor.g,
                        style.OutlineColor.b,
                        style.OutlineWidth);
                    batchCount++;
                    visibleGlyphCount++;

                    cursor += glyph.AdvancePixels + profile.LetterSpacingPixels;
                    if (batchCount == MaxInstancesPerDraw)
                    {
                        SubmitBatch(camera, batchCount);
                        drawCallCount++;
                        batchCount = 0;
                    }
                }
            }

            if (batchCount > 0)
            {
                SubmitBatch(camera, batchCount);
                drawCallCount++;
            }

            return new JumpTextRenderResult(visibleGlyphCount, drawCallCount);
        }

        /// <summary>
        /// 提交当前批次的矩阵和逐实例材质属性；阴影与光照探针均关闭以保持纯 UI Overlay 行为。
        /// </summary>
        /// <param name="camera">接收绘制的 Gameplay 相机。</param>
        /// <param name="batchCount">当前批次实例数。</param>
        private void SubmitBatch(Camera camera, int batchCount)
        {
            propertyBlock.Clear();
            propertyBlock.SetVectorArray(ScreenRectId, screenRects);
            propertyBlock.SetVectorArray(UvRectId, uvRects);
            propertyBlock.SetVectorArray(ColorId, colors);
            propertyBlock.SetVectorArray(OutlineId, outlines);
            Graphics.DrawMeshInstanced(
                quadMesh,
                0,
                material,
                matrices,
                batchCount,
                propertyBlock,
                ShadowCastingMode.Off,
                false,
                0,
                camera,
                LightProbeUsage.Off,
                null);
        }

        /// <summary>
        /// 判断世界锚点是否位于相机前方、远裁剪面内和带余量的像素视口中。
        /// </summary>
        /// <param name="camera">当前相机。</param>
        /// <param name="pixelRect">相机像素视口。</param>
        /// <param name="worldPosition">待检查世界坐标。</param>
        /// <param name="marginPixels">视口边缘额外保留的像素余量。</param>
        /// <returns>允许展开并绘制时返回 true。</returns>
        private static bool IsAnchorVisible(Camera camera, Rect pixelRect,
            Vector3 worldPosition, float marginPixels)
        {
            Vector3 screenPosition = camera.WorldToScreenPoint(worldPosition);
            if (screenPosition.z <= 0f || screenPosition.z > camera.farClipPlane)
                return false;

            return screenPosition.x >= pixelRect.xMin - marginPixels &&
                   screenPosition.x <= pixelRect.xMax + marginPixels &&
                   screenPosition.y >= pixelRect.yMin - marginPixels &&
                   screenPosition.y <= pixelRect.yMax + marginPixels;
        }

        /// <summary>求出生偏移与带方向的随机横向轨迹。</summary>
        /// <param name="state">跳字运行时记录。</param>
        /// <param name="normalizedTime">归一化生命周期。</param>
        /// <param name="profile">跳字配置。</param>
        /// <returns>当前帧横向像素偏移。</returns>
        private static float EvaluateHorizontalOffset(JumpTextRuntimeState state,
            float normalizedTime, JumpTextProfile profile)
        {
            float initialRandom = HashToUnit(state.RandomSeed);
            float direction = HashToUnit(state.RandomSeed ^ unchecked((int)0x6E624EB7u)) * 2f - 1f;
            float initial = Mathf.Lerp(profile.InitialHorizontalOffsetRange.x,
                profile.InitialHorizontalOffsetRange.y, initialRandom);
            float curve = profile.HorizontalOffsetCurve.Evaluate(normalizedTime);
            return initial + direction * profile.HorizontalSpreadPixels * curve;
        }

        /// <summary>将字体 FaceInfo 的基线换算为以文字包围盒垂直居中的像素偏移。</summary>
        /// <param name="fontAsset">当前字体资产。</param>
        /// <param name="baseFontScale">基础字体缩放。</param>
        /// <returns>像素基线偏移。</returns>
        private static float GetBaselinePixels(TMP_FontAsset fontAsset, float baseFontScale) =>
            -(fontAsset.faceInfo.ascentLine + fontAsset.faceInfo.descentLine) *
            baseFontScale * 0.5f;

        /// <summary>创建四顶点 Quad，Shader 会把局部顶点解释为逐实例像素矩形的角点。</summary>
        /// <returns>可被 Graphics.DrawMeshInstanced 使用的动态 Mesh。</returns>
        private static Mesh CreateQuadMesh()
        {
            var mesh = new Mesh
            {
                name = "[JumpText] Glyph Quad",
                hideFlags = HideFlags.HideAndDontSave,
                bounds = new Bounds(Vector3.zero, Vector3.one * 2f)
            };
            mesh.MarkDynamic();
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f)
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            return mesh;
        }

        /// <summary>把 Unity Color 转成实例化 Shader 使用的 Vector4 并应用生命周期透明度。</summary>
        /// <param name="color">样式颜色。</param>
        /// <param name="alpha">生命周期透明度。</param>
        /// <returns>逐实例颜色。</returns>
        private static Vector4 ToVector4(Color color, float alpha) =>
            new(color.r, color.g, color.b, color.a * alpha);

        /// <summary>用无分配的整数哈希生成 [0,1] 范围的确定性随机值。</summary>
        /// <param name="seed">跳字随机种子。</param>
        /// <returns>确定性伪随机浮点数。</returns>
        private static float HashToUnit(int seed)
        {
            uint value = unchecked((uint)seed);
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return (value & 0x00FFFFFFu) / 16777215f;
        }

        #endregion

        #region 数值展开

        /// <summary>取得一个 long 数值的十进制位数，零至少占一位。</summary>
        /// <param name="magnitude">非负数值绝对值。</param>
        /// <returns>十进制位数。</returns>
        internal static int GetDigitCount(long magnitude)
        {
            if (magnitude == 0L) return 1;
            int count = 0;
            while (magnitude > 0L)
            {
                magnitude /= 10L;
                count++;
            }

            return count;
        }

        /// <summary>根据符号和正号选项计算一条跳字的总字符数。</summary>
        /// <param name="value">原始整数值。</param>
        /// <param name="showPositiveSign">正数是否显示加号。</param>
        /// <returns>数字和可选符号的字符总数。</returns>
        internal static int GetCharacterCount(int value, bool showPositiveSign)
        {
            long magnitude = Math.Abs((long)value);
            int signCount = value < 0 || (value > 0 && showPositiveSign) ? 1 : 0;
            return GetDigitCount(magnitude) + signCount;
        }

        /// <summary>取得运行时记录指定索引处的 ASCII 字符码。</summary>
        /// <param name="state">跳字运行时记录。</param>
        /// <param name="index">字符索引。</param>
        /// <returns>ASCII 字符码。</returns>
        private static uint GetUnicodeAt(JumpTextRuntimeState state, int index)
        {
            int signCount = state.IsNegative ||
                            (state.Magnitude > 0L && state.ShowPositiveSign) ? 1 : 0;
            if (signCount > 0 && index == 0)
                return state.IsNegative ? '-' : '+';

            int digitIndex = index - signCount;
            int digitCount = GetDigitCount(state.Magnitude);
            long divisor = 1L;
            for (int power = 1; power < digitCount - digitIndex; power++)
                divisor *= 10L;
            int digit = (int)((state.Magnitude / divisor) % 10L);
            return (uint)('0' + digit);
        }

        #endregion

        #region 嵌套缓存类型

        /// <summary>保存一个字形的像素度量和 Atlas UV 矩形。</summary>
        private readonly struct GlyphInfo
        {
            /// <summary>字形宽度（像素）。</summary>
            internal readonly float WidthPixels;

            /// <summary>字形高度（像素）。</summary>
            internal readonly float HeightPixels;

            /// <summary>相对光标的水平 Bearing（像素）。</summary>
            internal readonly float BearingXPixels;

            /// <summary>相对基线的垂直 Bearing（像素）。</summary>
            internal readonly float BearingYPixels;

            /// <summary>光标推进宽度（像素）。</summary>
            internal readonly float AdvancePixels;

            /// <summary>Atlas 中的 UV 最小点和尺寸。</summary>
            internal readonly Vector4 UvRect;

            /// <summary>判断当前缓存是否仍为空。</summary>
            internal bool IsEmpty => WidthPixels <= 0f && HeightPixels <= 0f && AdvancePixels <= 0f;

            /// <summary>创建一个字形缓存。</summary>
            /// <param name="widthPixels">字形宽度。</param>
            /// <param name="heightPixels">字形高度。</param>
            /// <param name="bearingXPixels">水平 Bearing。</param>
            /// <param name="bearingYPixels">垂直 Bearing。</param>
            /// <param name="advancePixels">光标推进宽度。</param>
            /// <param name="uvRect">Atlas UV 矩形。</param>
            internal GlyphInfo(float widthPixels, float heightPixels,
                float bearingXPixels, float bearingYPixels, float advancePixels,
                Vector4 uvRect)
            {
                WidthPixels = widthPixels;
                HeightPixels = heightPixels;
                BearingXPixels = bearingXPixels;
                BearingYPixels = bearingYPixels;
                AdvancePixels = advancePixels;
                UvRect = uvRect;
            }
        }

        #endregion
    }
}
