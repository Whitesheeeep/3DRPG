namespace WS_Modules.CustomEventSystem
{
    #region int 事件枚举
    // 事件枚举必须通过 start，end 实现 int 的连续性，避免在注册事件时出现重复的 key 导致事件覆盖的问题
    public enum E_TestEvent
    {
        start = EventIdRange.TestStart,
        TestIntEvent = start + 1,
        end,
    }

    public enum E_InputEvent
    {
        start = EventIdRange.InputStart,
        OnKeyDown = start + 1,
        OnKeyUp,
        OnKey,
        OnMouseButtonDown,
        OnMouseButtonUp,
        OnMouseButton,
        OnNewInputActionPerformed,
        OnNewInputActionStarted,
        OnNewInputActionCanceled,
        end,
    }
    #endregion
}