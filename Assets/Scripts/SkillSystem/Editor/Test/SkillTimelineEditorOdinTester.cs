#if UNITY_EDITOR
using System;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 通过 Odin Inspector 按钮执行技能时间轴编辑器的手动验收。
    /// </summary>
    public sealed class SkillTimelineEditorOdinTester : MonoBehaviour
    {
        #region Test inputs and output

        [SerializeField, Required, LabelText("技能配置")]
        private SkillConfig skillConfig;

        [SerializeField, Min(1f), LabelText("每帧像素")]
        private float pixelsPerFrame = 12f;

        [ShowInInspector, ReadOnly, MultiLineProperty(6), LabelText("测试结果")]
        private string result = "尚未运行";

        #endregion

        #region Manual test scenarios

        /// <summary>
        /// 创建动画、特效和事件三类轨道及默认内容，用于窗口交互验收。
        /// </summary>
        [Button("创建基础测试数据", ButtonSizes.Large)]
        public void CreateBasicTestData() => Run("创建基础测试数据", document =>
        {
            AddTrackAndItem(document, SkillTrackKind.Animation);
            AddTrackAndItem(document, SkillTrackKind.Vfx);
            AddTrackAndItem(document, SkillTrackKind.Event);
        });

        /// <summary>
        /// 校验当前配置的 GUID、帧范围、排序和同轨重叠规则。
        /// </summary>
        [Button("校验配置")]
        public void ValidateConfig() => Run("校验配置", document =>
        {
            var errors = document.Validate();
            Require(errors.Count == 0, string.Join("\n", errors));
        });

        /// <summary>
        /// 验证缩放和水平偏移后的帧与像素坐标能够无误差往返。
        /// </summary>
        [Button("测试坐标映射")]
        public void TestCoordinateMapping()
        {
            try
            {
                SkillTimelineEditorConfig editorConfig = AssetDatabase.LoadAssetAtPath<SkillTimelineEditorConfig>(
                    "Assets/Scripts/SkillSystem/Editor/Config/SkillTimelineEditorConfig.asset");
                Require(editorConfig != null, "缺少技能时间轴 Editor 配置。");
                var viewport = new SkillTimelineViewportController(editorConfig);
                viewport.SetZoom(pixelsPerFrame);
                viewport.SetScrollOffset(new Vector2(37f, 19f));
                var mapper = new SkillTimelineCoordinateMapper(viewport);
                float viewportX = mapper.FrameToViewportX(17, viewport.ScrollOffset.x);
                Require(mapper.ViewportXToFrame(viewportX, viewport.ScrollOffset.x) == 17, "视口坐标往返失败。");
                Require(mapper.ContentXToFrame(mapper.FrameToContentX(17)) == 17, "内容坐标往返失败。");
                Pass("测试坐标映射");
            }
            catch (Exception exception)
            {
                Fail("测试坐标映射", exception);
            }
        }

        /// <summary>
        /// 验证播放头定位、前后帧、停止和配置帧边界夹紧。
        /// </summary>
        [Button("测试播放控制")]
        public void TestPlaybackController()
        {
            try
            {
                Require(skillConfig != null, "请先指定技能配置。");
                using var playback = new SkillTimelinePlaybackController();
                playback.SetSkillConfig(skillConfig);
                playback.Seek(skillConfig.DurationFrames + 100);
                Require(playback.CurrentFrame == skillConfig.DurationFrames - 1, "末帧夹紧失败。");
                playback.StepPreviousFrame();
                playback.StepNextFrame();
                playback.Stop();
                Require(playback.CurrentFrame == 0 && !playback.IsPlaying, "停止状态错误。");
                Pass("测试播放控制");
            }
            catch (Exception exception)
            {
                Fail("测试播放控制", exception);
            }
        }

        /// <summary>验证标尺在常用缩放下同时提供主刻度与小刻度。</summary>
        [Button("测试标尺刻度")]
        public void TestRulerTicks()
        {
            try
            {
                float[] zooms = { 4f, 12f, 24f, 48f };
                foreach (float zoom in zooms)
                {
                    int minor = SkillTimelineTickUtility.GetMinorStep(zoom);
                    int major = SkillTimelineTickUtility.GetMajorStep(zoom);
                    Require(minor > 0 && major >= minor, "刻度间隔无效。");
                    Require(minor * zoom >= 4f, "小刻度过密。");
                    Require(major * zoom >= 40f, "主刻度标签过密。");
                }
                Pass("测试标尺刻度");
            }
            catch (Exception exception) { Fail("测试标尺刻度", exception); }
        }

        #endregion

        #region Test execution helpers

        /// <summary>
        /// 在统一异常处理下打开编辑文档、运行测试并保存资产。
        /// </summary>
        private void Run(string name, Action<SkillTimelineEditorDocument> test)
        {
            try
            {
                Require(skillConfig != null, "请先指定技能配置。");
                using var document = new SkillTimelineEditorDocument();
                document.Open(skillConfig);
                test(document);
                AssetDatabase.SaveAssets();
                Pass(name);
            }
            catch (Exception exception)
            {
                Fail(name, exception);
            }
        }

        /// <summary>
        /// 为手动测试创建指定类型的轨道及一个默认内容项。
        /// </summary>
        private static void AddTrackAndItem(SkillTimelineEditorDocument document, SkillTrackKind kind)
        {
            string id = document.AddTrack(kind);
            Require(!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(document.AddItem(kind, id)),
                kind + " 创建失败。");
        }

        /// <summary>
        /// 断言测试条件成立，否则以异常中断当前测试。
        /// </summary>
        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        /// <summary>
        /// 记录并输出手动测试通过结果。
        /// </summary>
        private void Pass(string name)
        {
            result = name + "：通过";
            Debug.Log("[SkillTimelineTest] " + result, this);
        }

        /// <summary>
        /// 记录测试失败原因并将完整异常输出到 Console。
        /// </summary>
        private void Fail(string name, Exception exception)
        {
            result = name + "：失败\n" + exception.Message;
            Debug.LogException(exception, this);
        }

        #endregion
    }
}
#endif
