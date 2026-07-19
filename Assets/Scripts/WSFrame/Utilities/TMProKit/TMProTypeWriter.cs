using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(TMP_Text))]
public class TMProTypeWriter : MonoBehaviour
{
    #region Enums
    /// <summary>
    /// 文本显示模式。
    /// </summary>
    public enum RevealMode
    {
        [Tooltip("直接显示全部文本")]
        Direct,
        [Tooltip("所有文字同时淡入")]
        Fade,
        [Tooltip("逐字打印")]
        Typing
    }

    /// <summary>
    /// 打字机当前状态。
    /// </summary>
    public enum WriterState
    {
        [Tooltip("空闲")]
        Idle,
        [Tooltip("正在显示中")]
        Revealing,
        [Tooltip("显示完成")]
        Completed
    }
    #endregion

    #region Inspector
    [Tooltip("当前打字机的状态")]
    [LabelText("当前状态")]
    [ShowInInspector, ReadOnly]
    public WriterState CurrentState { get; private set; } = WriterState.Idle;

    [Header("显示设置")]
    [Tooltip("控制文本显示的模式")]
    [LabelText("显示模式")]
    public RevealMode revealMode = RevealMode.Typing;

    [Header("控制打字或淡入的速度")]
    [LabelText("打字速度")]
    [Range(0f, 100f)]
    public float typingSpeed = 20f;

    [LabelText("淡入淡出速度（每秒进度 0-1）")]
    [Range(0f, 1f)]
    public float fadeSpeed = .5f;

    [Space]
    [LabelText("不允许直接显示文本时间段（秒）")]
    [Tooltip("文本开始显示后的这段时间内禁用跳过功能，防止误操作")]
    public float noSkipDuration = 1f;
    #endregion

    #region Fields
    private TMP_Text textMeshPro;
    private float revealStartTime;
    private int revealVersion;
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        EnsureTextComponent();
    }

    private void OnDestroy()
    {
        revealVersion++;
    }
    #endregion

    #region Public API
    /// <summary>
    /// 根据选择的模式显示文本。
    /// </summary>
    public async UniTask ShowText(string text)
    {
        EnsureTextComponent();
        int version = ++revealVersion;

        if (string.IsNullOrEmpty(text))
        {
            textMeshPro.text = string.Empty;
            CurrentState = WriterState.Idle;
            return;
        }

        CurrentState = WriterState.Revealing;
        textMeshPro.text = text;
        textMeshPro.ForceMeshUpdate();
        revealStartTime = Time.time;

        switch (revealMode)
        {
            case RevealMode.Direct:
                SetAllCharactersAlpha(255);
                textMeshPro.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
                break;
            case RevealMode.Fade:
                await FadeInTextAsync(version);
                break;
            case RevealMode.Typing:
                await RevealTextAsync(version);
                break;
        }

        if (version == revealVersion && CurrentState == WriterState.Revealing)
        {
            CurrentState = WriterState.Completed;
        }
    }

    /// <summary>
    /// 停止当前文本显示流程。
    /// </summary>
    public void StopReveal(bool clearText = false)
    {
        EnsureTextComponent();
        revealVersion++;

        if (clearText)
        {
            textMeshPro.text = string.Empty;
            CurrentState = WriterState.Idle;
            return;
        }

        if (CurrentState == WriterState.Revealing)
        {
            CurrentState = WriterState.Completed;
        }
    }

    /// <summary>
    /// 直接跳过动画，立即显示所有文本。
    /// </summary>
    public void Skip()
    {
        if (CurrentState != WriterState.Revealing)
        {
            return;
        }

        if (Time.time - revealStartTime < noSkipDuration)
        {
            Debug.Log("跳过功能暂时不可用。");
            return;
        }

        revealVersion++;
        SetAllCharactersAlpha(255);
        textMeshPro.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
        CurrentState = WriterState.Completed;
    }
    #endregion

    #region Reveal Methods
    /// <summary>
    /// 异步逐字显示文本。
    /// </summary>
    private async UniTask RevealTextAsync(int version)
    {
        SetAllCharactersAlpha(0);

        TMP_TextInfo textInfo = textMeshPro.textInfo;
        int totalCharacters = textInfo.characterCount;
        int delayMilliseconds = Mathf.Max(1, Mathf.RoundToInt(100f / Mathf.Max(1f, typingSpeed)));

        for (int i = 0; i < totalCharacters; i++)
        {
            if (!IsCurrentReveal(version))
            {
                return;
            }

            if (textInfo.characterInfo[i].isVisible)
            {
                SetCharacterAlpha(i, 255);
                textMeshPro.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
            }

            await UniTask.Delay(delayMilliseconds);

            if (!IsCurrentReveal(version))
            {
                return;
            }
        }
    }

    /// <summary>
    /// 异步淡入显示所有文本。
    /// </summary>
    private async UniTask FadeInTextAsync(int version)
    {
        const int FadeUpdateIntervalMilliseconds = 33;

        SetAllCharactersAlpha(0);

        float currentAlpha = 0f;
        while (currentAlpha < 255f)
        {
            if (!IsCurrentReveal(version))
            {
                return;
            }

            float alphaIncrement = Mathf.Max(1f, fadeSpeed * (FadeUpdateIntervalMilliseconds / 1000f) * 255f);
            currentAlpha = Mathf.Min(255f, currentAlpha + alphaIncrement);

            SetAllCharactersAlpha((byte)currentAlpha);
            textMeshPro.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);

            await UniTask.Delay(FadeUpdateIntervalMilliseconds);

            if (!IsCurrentReveal(version))
            {
                return;
            }
        }

        SetAllCharactersAlpha(255);
        textMeshPro.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }
    #endregion

    #region Character Alpha
    /// <summary>
    /// 设置所有字符的 Alpha 值。
    /// </summary>
    private void SetAllCharactersAlpha(byte alpha)
    {
        if (textMeshPro == null || textMeshPro.textInfo.Equals(default(TMP_TextInfo)))
        {
            return;
        }

        for (int i = 0; i < textMeshPro.textInfo.characterCount; i++)
        {
            SetCharacterAlpha(i, alpha);
        }
    }

    /// <summary>
    /// 设置单个字符的 Alpha 值。
    /// </summary>
    private void SetCharacterAlpha(int charIndex, byte alpha)
    {
        if (textMeshPro == null || charIndex >= textMeshPro.textInfo.characterCount)
        {
            return;
        }

        TMP_CharacterInfo charInfo = textMeshPro.textInfo.characterInfo[charIndex];
        if (!charInfo.isVisible)
        {
            return;
        }

        int materialIndex = charInfo.materialReferenceIndex;
        int vertexIndex = charInfo.vertexIndex;
        Color32[] vertexColors = textMeshPro.textInfo.meshInfo[materialIndex].colors32;

        vertexColors[vertexIndex + 0].a = alpha;
        vertexColors[vertexIndex + 1].a = alpha;
        vertexColors[vertexIndex + 2].a = alpha;
        vertexColors[vertexIndex + 3].a = alpha;
    }
    #endregion

    #region Helpers
    private bool IsCurrentReveal(int version)
    {
        return version == revealVersion && this != null && textMeshPro != null;
    }

    private void EnsureTextComponent()
    {
        if (textMeshPro == null)
        {
            textMeshPro = GetComponent<TMP_Text>();
        }
    }
    #endregion
}

