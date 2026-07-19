namespace WS_Modules.MVVM
{
    /// <summary>
    /// Allows a ViewModel provider or locator to clear cached UI state when the project lifecycle requires it.
    /// </summary>
    public interface IViewModelResettable
    {
        void ResetViewModel();
    }
}
