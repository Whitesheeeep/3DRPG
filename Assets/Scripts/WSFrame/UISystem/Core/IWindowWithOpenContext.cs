namespace WS_Modules.UIModule
{
    /// <summary>
    /// 支持临时打开参数的窗口接口，OpenContext 只表示本次打开输入，不用于注入 ViewModel。
    /// </summary>
    /// <typeparam name="TOpenContext">临时打开参数类型。</typeparam>
    public interface IWindowWithOpenContext<in TOpenContext>
    {
        /// <summary>
        /// 应用本次打开窗口时传入的临时参数。
        /// </summary>
        /// <param name="context">本次打开参数。</param>
        void ApplyOpenContext(TOpenContext context);
    }
}
