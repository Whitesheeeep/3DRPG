#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.GameplayCue;
using WS_Modules.GAS.Generated;

namespace WS_Modules.GAS.AbilitySystemComponent
{
    /// <summary>
    /// 为 ASC Odin 真实周期测试提供场景颜色、Cue 位置脉冲和 OnGUI 状态面板。
    /// 该组件只观察正式运行时数据，不参与 GA、GE、Attribute 或 Cue 的规则计算。
    /// </summary>
    public sealed class GameplayAbilitySystemComponentTestVisualizer : MonoBehaviour
    {
        #region 常量与字段

        private const int MaxCueRecords = 6;
        private static readonly Color SourceColor = new(0.15f, 0.45f, 1f, 1f);
        private static readonly Color SourceBuffColor = new(0.15f, 0.9f, 0.35f, 1f);
        private static readonly Color TargetColor = new(0.95f, 0.25f, 0.2f, 1f);
        private static readonly Color TargetHitColor = new(1f, 0.85f, 0.15f, 1f);

        private readonly List<CueRecord> cueRecords = new();
        private GameplayAbilitySystemComponent source;
        private GameplayAbilitySystemComponent target;
        private Renderer sourceRenderer;
        private Renderer targetRenderer;
        private MaterialPropertyBlock sourcePropertyBlock;
        private MaterialPropertyBlock targetPropertyBlock;
        private GameObject pulseObject;
        private Renderer pulseRenderer;
        private MaterialPropertyBlock pulsePropertyBlock;
        private float pulseDuration = 0.75f;
        private float pulseExpiresAt;
        private float previousTargetHealth = float.NaN;
        private float targetFlashExpiresAt;
        private string runMode = "单项测试";
        private string scenarioName = "尚未开始";
        private int scenarioIndex;
        private int scenarioCount = 1;
        private int scenarioPassedBase;
        private int scenarioFailedBase;
        private bool showProjectileLane;
        private Vector3 projectileLaneStart;
        private Vector3 projectileLaneEnd;
        private bool showProjectilePosition;
        private Vector3 projectilePosition;
        private string currentStage = "尚未开始";
        private float stageStartedAt;
        private float stageDuration;
        private int passed;
        private int failed;
        private bool running;
        private bool hasSummary;
        private GUIStyle panelStyle;
        private GUIStyle titleStyle;
        private GUIStyle normalStyle;
        private GUIStyle passStyle;
        private GUIStyle failStyle;

        #endregion

        #region 生命周期

        /// <summary>逐帧刷新测试 Actor 的状态颜色并关闭到期的 Cue 位置脉冲。</summary>
        private void Update()
        {
            if (source == null || target == null) return;

            UpdateActorVisuals();
            if (pulseObject != null && pulseObject.activeSelf && Time.realtimeSinceStartup >= pulseExpiresAt)
                pulseObject.SetActive(false);
        }

        /// <summary>绘制当前测试阶段、Attribute、Runtime 数量和最近 Cue 事件。</summary>
        private void OnGUI()
        {
            if (!running && !hasSummary) return;

            EnsureGuiStyles();
            Rect panelRect = new(16f, 16f, 520f, 420f);
            GUI.Box(panelRect, GUIContent.none, panelStyle);
            GUILayout.BeginArea(new Rect(panelRect.x + 14f, panelRect.y + 10f, panelRect.width - 28f, panelRect.height - 20f));
            GUILayout.Label("GAS ASC 真实周期可视化", titleStyle);
            GUILayout.Label($"模式：{runMode}    场景：{scenarioIndex}/{scenarioCount} {scenarioName}", normalStyle);
            GUILayout.Label($"阶段：{currentStage}", normalStyle);
            DrawStageProgress();
            GUILayout.Space(4f);
            GUILayout.Label(FormatAscState("Source", source), normalStyle);
            GUILayout.Label(FormatAscState("Target", target), normalStyle);
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"总 PASS：{passed}", passStyle, GUILayout.Width(110f));
            GUILayout.Label($"总 FAIL：{failed}", failed > 0 ? failStyle : normalStyle, GUILayout.Width(110f));
            GUILayout.Label(
                $"本场景：+{passed - scenarioPassedBase} / -{failed - scenarioFailedBase}",
                failed > scenarioFailedBase ? failStyle : normalStyle);
            GUILayout.EndHorizontal();
            if (showProjectileLane)
                GUILayout.Label($"投射物通道：{projectileLaneStart:F1} → {projectileLaneEnd:F1}", normalStyle);
            if (showProjectilePosition)
                GUILayout.Label($"Linear Projectile：{projectilePosition:F2}", normalStyle);
            GUILayout.Space(6f);
            GUILayout.Label("最近 Cue 事件", titleStyle);
            if (cueRecords.Count == 0)
                GUILayout.Label("尚未观察到 Cue。", normalStyle);
            else
                for (int i = cueRecords.Count - 1; i >= 0; i--)
                    GUILayout.Label(cueRecords[i].ToDisplayText(), normalStyle);
            GUILayout.EndArea();
        }

        /// <summary>组件销毁时移除测试专用脉冲对象。</summary>
        private void OnDestroy() => DestroyPulse();

        #endregion

        #region 公开操作

        /// <summary>绑定本轮测试的 Source、Target 和可视 Renderer。</summary>
        /// <param name="sourceAsc">本轮测试的 Source ASC。</param>
        /// <param name="targetAsc">本轮测试的 Target ASC。</param>
        /// <param name="sourceVisual">Source Actor 的 Renderer。</param>
        /// <param name="targetVisual">Target Actor 的 Renderer。</param>
        /// <param name="cuePulseDuration">立即回收 Cue 的位置脉冲保持时间。</param>
        public void Begin(
            GameplayAbilitySystemComponent sourceAsc,
            GameplayAbilitySystemComponent targetAsc,
            Renderer sourceVisual,
            Renderer targetVisual,
            float cuePulseDuration)
        {
            source = sourceAsc;
            target = targetAsc;
            sourceRenderer = sourceVisual;
            targetRenderer = targetVisual;
            pulseDuration = Mathf.Max(0.1f, cuePulseDuration);
            sourcePropertyBlock ??= new MaterialPropertyBlock();
            targetPropertyBlock ??= new MaterialPropertyBlock();
            DestroyPulse();
            cueRecords.Clear();
            passed = 0;
            failed = 0;
            running = true;
            hasSummary = true;
            previousTargetHealth = ReadCurrent(target, GameplayAttributes.Attribute_Health);
            SetStage("初始化", 0f);
            ApplyColor(sourceRenderer, sourcePropertyBlock, SourceColor);
            ApplyColor(targetRenderer, targetPropertyBlock, TargetColor);
        }

        /// <summary>设置单项或完整套件的当前场景上下文和独立断言基线。</summary>
        /// <param name="mode">单项测试或完整套件。</param>
        /// <param name="scenario">当前技能名称。</param>
        /// <param name="index">当前场景序号。</param>
        /// <param name="count">场景总数。</param>
        /// <param name="passedBeforeScenario">场景开始前累计通过数量。</param>
        /// <param name="failedBeforeScenario">场景开始前累计失败数量。</param>
        public void SetTestContext(
            string mode,
            string scenario,
            int index,
            int count,
            int passedBeforeScenario,
            int failedBeforeScenario)
        {
            runMode = mode;
            scenarioName = scenario;
            scenarioIndex = index;
            scenarioCount = count;
            scenarioPassedBase = passedBeforeScenario;
            scenarioFailedBase = failedBeforeScenario;
            SetSummary(passedBeforeScenario, failedBeforeScenario);
        }

        /// <summary>设置是否在面板中显示当前投射物测试通道。</summary>
        /// <param name="start">通道世界起点。</param>
        /// <param name="end">通道世界终点。</param>
        /// <param name="visible">当前场景是否为投射物场景。</param>
        public void SetProjectileLane(Vector3 start, Vector3 end, bool visible)
        {
            projectileLaneStart = start;
            projectileLaneEnd = end;
            showProjectileLane = visible;
        }

        /// <summary>更新当前 Linear Projectile 的实际世界位置。</summary>
        /// <param name="position">当前帧观察到的位置。</param>
        /// <param name="visible">是否显示位置行。</param>
        public void SetProjectilePosition(Vector3 position, bool visible)
        {
            projectilePosition = position;
            showProjectilePosition = visible;
        }

        /// <summary>更新当前测试阶段及预计观察时长。</summary>
        /// <param name="stage">面板中显示的阶段名称。</param>
        /// <param name="duration">阶段预计持续秒数；小于等于零时不显示进度。</param>
        public void SetStage(string stage, float duration)
        {
            currentStage = stage;
            stageStartedAt = Time.realtimeSinceStartup;
            stageDuration = Mathf.Max(0f, duration);
        }

        /// <summary>同步当前断言汇总。</summary>
        /// <param name="passedCount">通过数量。</param>
        /// <param name="failedCount">失败数量。</param>
        public void SetSummary(int passedCount, int failedCount)
        {
            passed = passedCount;
            failed = failedCount;
            hasSummary = true;
        }

        /// <summary>记录真实 Cue 回调，并在 Cue 回收前捕获其世界位置。</summary>
        /// <param name="runtime">发生回调的 Cue Runtime。</param>
        /// <param name="eventType">Cue 生命周期事件。</param>
        public void RecordCue(GameplayCueRuntime runtime, GameplayCueEventType eventType)
        {
            Vector3 position = runtime.CueObject != null
                ? runtime.CueObject.transform.position
                : runtime.Position;
            string cueName = runtime.CueData != null ? runtime.CueData.name : runtime.CueTag.ToString();
            string ownerName = runtime.Target != null ? runtime.Target.name : "World";

            cueRecords.Add(new CueRecord(cueName, eventType, ownerName, position, Time.realtimeSinceStartup));
            if (cueRecords.Count > MaxCueRecords)
                cueRecords.RemoveAt(0);

            ShowPulse(position, GetCueColor(eventType));
        }

        /// <summary>结束当前测试并保留最终 OnGUI 汇总。</summary>
        /// <param name="passedCount">最终通过数量。</param>
        /// <param name="failedCount">最终失败数量。</param>
        public void Finish(int passedCount, int failedCount)
        {
            SetSummary(passedCount, failedCount);
            currentStage = failedCount == 0 ? "测试完成" : "测试完成（存在失败）";
            stageDuration = 0f;
            running = false;
        }

        /// <summary>标记测试被用户提前停止，并保留停止时的断言汇总。</summary>
        /// <param name="passedCount">停止前通过数量。</param>
        /// <param name="failedCount">停止前失败数量。</param>
        public void Stop(int passedCount, int failedCount)
        {
            SetSummary(passedCount, failedCount);
            currentStage = "测试已停止";
            stageDuration = 0f;
            running = false;
        }

        /// <summary>解除本轮 Actor 引用并清理场景中的测试脉冲，但保留最终结果面板。</summary>
        public void DetachActors()
        {
            source = null;
            target = null;
            sourceRenderer = null;
            targetRenderer = null;
            previousTargetHealth = float.NaN;
            showProjectilePosition = false;
            DestroyPulse();
        }

        #endregion

        #region 状态刷新

        /// <summary>根据 Armor 和受击变化刷新 Source、Target 的测试颜色。</summary>
        private void UpdateActorVisuals()
        {
            float armor = ReadCurrent(source, GameplayAttributes.Attribute_Armor);
            Color currentSourceColor = armor > 10f + Mathf.Epsilon ? SourceBuffColor : SourceColor;
            ApplyColor(sourceRenderer, sourcePropertyBlock, currentSourceColor);

            float health = ReadCurrent(target, GameplayAttributes.Attribute_Health);
            if (!float.IsNaN(previousTargetHealth) && health < previousTargetHealth - Mathf.Epsilon)
                targetFlashExpiresAt = Time.realtimeSinceStartup + 0.45f;
            previousTargetHealth = health;

            Color currentTargetColor = Time.realtimeSinceStartup < targetFlashExpiresAt
                ? TargetHitColor
                : TargetColor;
            ApplyColor(targetRenderer, targetPropertyBlock, currentTargetColor);
        }

        /// <summary>显示或复用一个测试专用位置脉冲，避免为每次 Execute Cue 重复创建对象。</summary>
        /// <param name="position">Cue 回调发生时的世界位置。</param>
        /// <param name="color">当前 Cue 阶段对应的提示颜色。</param>
        private void ShowPulse(Vector3 position, Color color)
        {
            EnsurePulse();
            pulseObject.transform.position = position;
            pulseObject.transform.rotation = Quaternion.identity;
            pulseObject.SetActive(true);
            ApplyColor(pulseRenderer, pulsePropertyBlock, color);
            pulseExpiresAt = Time.realtimeSinceStartup + pulseDuration;
        }

        /// <summary>按需创建只服务本 Tester 的小球脉冲；该对象不是正式 Cue，也不进入业务对象池。</summary>
        private void EnsurePulse()
        {
            if (pulseObject != null) return;

            pulseObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pulseObject.name = "ASC Test Cue Pulse";
            pulseObject.transform.localScale = Vector3.one * 0.32f;
            Collider pulseCollider = pulseObject.GetComponent<Collider>();
            if (pulseCollider != null)
                pulseCollider.enabled = false;
            pulseRenderer = pulseObject.GetComponent<Renderer>();
            pulsePropertyBlock = new MaterialPropertyBlock();
        }

        /// <summary>销毁测试专用 Cue 脉冲，避免测试停止后污染场景。</summary>
        private void DestroyPulse()
        {
            if (pulseObject != null)
                Destroy(pulseObject);
            pulseObject = null;
            pulseRenderer = null;
            pulsePropertyBlock = null;
        }

        #endregion

        #region GUI 与数据辅助

        /// <summary>延迟创建 IMGUI 样式，避免在非绘制阶段访问 GUI Skin。</summary>
        private void EnsureGuiStyles()
        {
            if (panelStyle != null) return;

            panelStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = Texture2D.grayTexture },
                padding = new RectOffset(10, 10, 10, 10)
            };
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 15,
                normal = { textColor = Color.white }
            };
            normalStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = Color.white }
            };
            passStyle = new GUIStyle(normalStyle) { normal = { textColor = new Color(0.3f, 1f, 0.4f) } };
            failStyle = new GUIStyle(normalStyle) { normal = { textColor = new Color(1f, 0.35f, 0.3f) } };
        }

        /// <summary>绘制当前阶段的实时进度条。</summary>
        private void DrawStageProgress()
        {
            if (stageDuration <= 0f) return;

            float elapsed = Time.realtimeSinceStartup - stageStartedAt;
            float progress = Mathf.Clamp01(elapsed / stageDuration);
            Rect rect = GUILayoutUtility.GetRect(10f, 8f, GUILayout.ExpandWidth(true));
            GUI.Box(rect, GUIContent.none);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * progress, rect.height), Texture2D.whiteTexture);
        }

        /// <summary>构建一个 ASC 的 Attribute 与 Runtime 实时状态文本。</summary>
        /// <param name="label">Source 或 Target 显示标签。</param>
        /// <param name="asc">待读取的 ASC。</param>
        /// <returns>可直接用于 OnGUI 的单行文本。</returns>
        private static string FormatAscState(string label, GameplayAbilitySystemComponent asc)
        {
            if (asc == null) return $"{label}：已清理";

            float health = ReadCurrent(asc, GameplayAttributes.Attribute_Health);
            float mp = ReadCurrent(asc, GameplayAttributes.Attribute_MP);
            float armor = ReadCurrent(asc, GameplayAttributes.Attribute_Armor);
            return $"{label}  HP={health:0.##}  MP={mp:0.##}  Armor={armor:0.##}  " +
                   $"GA={asc.ActiveAbilities.Count}  GE={asc.ActiveEffects.Count}  Cue={asc.Cues.ActiveCues.Count}";
        }

        /// <summary>读取 Attribute 的 CurrentValue；缺少数据时返回 NaN 便于面板暴露夹具错误。</summary>
        /// <param name="asc">Attribute 所属 ASC。</param>
        /// <param name="attribute">待读取的 Attribute。</param>
        /// <returns>找到时返回 CurrentValue，否则返回 NaN。</returns>
        private static float ReadCurrent(GameplayAbilitySystemComponent asc, GameplayAttribute attribute) =>
            asc != null && asc.TryGetCurrentValue(attribute, out float value) ? value : float.NaN;

        /// <summary>取得 Cue 生命周期事件在测试场景中的提示颜色。</summary>
        /// <param name="eventType">Cue 生命周期事件。</param>
        /// <returns>Execute 为青色、Active 为绿色、Remove 为灰色。</returns>
        private static Color GetCueColor(GameplayCueEventType eventType) => eventType switch
        {
            GameplayCueEventType.Execute => Color.cyan,
            GameplayCueEventType.Active => Color.green,
            GameplayCueEventType.Remove => Color.gray,
            _ => Color.white
        };

        /// <summary>通过 MaterialPropertyBlock 修改测试对象颜色，避免克隆或污染共享材质。</summary>
        /// <param name="targetRenderer">待修改的 Renderer。</param>
        /// <param name="propertyBlock">对应 Renderer 的属性块。</param>
        /// <param name="color">目标颜色。</param>
        private static void ApplyColor(
            Renderer targetRenderer,
            MaterialPropertyBlock propertyBlock,
            Color color)
        {
            if (targetRenderer == null || propertyBlock == null) return;
            propertyBlock.SetColor("_BaseColor", color);
            propertyBlock.SetColor("_Color", color);
            targetRenderer.SetPropertyBlock(propertyBlock);
        }

        #endregion

        #region 嵌套类型

        /// <summary>保存一条已经发生的 Cue 可视化记录，不持有可回收的 Cue GameObject。</summary>
        private readonly struct CueRecord
        {
            private readonly string cueName;
            private readonly GameplayCueEventType eventType;
            private readonly string targetName;
            private readonly Vector3 position;
            private readonly float time;

            /// <summary>创建一次不可变的 Cue 观察记录。</summary>
            /// <param name="cueName">CueData 名称或 Tag 文本。</param>
            /// <param name="eventType">Cue 生命周期事件。</param>
            /// <param name="targetName">Cue 所属目标名称。</param>
            /// <param name="position">事件发生时的世界位置。</param>
            /// <param name="time">事件发生时的真实时间。</param>
            internal CueRecord(
                string cueName,
                GameplayCueEventType eventType,
                string targetName,
                Vector3 position,
                float time)
            {
                this.cueName = cueName;
                this.eventType = eventType;
                this.targetName = targetName;
                this.position = position;
                this.time = time;
            }

            /// <summary>生成供 OnGUI 使用的紧凑事件文本。</summary>
            /// <returns>包含时间、Cue、阶段、目标和世界位置的文本。</returns>
            internal string ToDisplayText() =>
                $"[{time:0.00}] {eventType}  {cueName}  Target={targetName}  Pos={position:F1}";
        }

        #endregion
    }
}
#endif
