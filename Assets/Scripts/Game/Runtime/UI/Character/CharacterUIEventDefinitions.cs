namespace RPG.Game.UI.Character
{
    /// <summary>角色窗口打开请求的来源。</summary>
    public enum E_CharacterWindowOpenSource
    {
        /// <summary>来自 HUD 角色按钮。</summary>
        HudButton = 0,
        /// <summary>来自键盘或手柄快捷键。</summary>
        Shortcut = 1
    }

    /// <summary>请求打开角色窗口的全局 UI 事件参数。</summary>
    public readonly struct CharacterWindowOpenRequestedEventArgs
    {
        /// <summary>创建角色窗口打开请求。</summary>
        /// <param name="source">请求来源。</param>
        public CharacterWindowOpenRequestedEventArgs(E_CharacterWindowOpenSource source)
        {
            Source = source;
        }

        /// <summary>获取请求来源。</summary>
        public E_CharacterWindowOpenSource Source { get; }
    }
}
