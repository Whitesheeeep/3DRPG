namespace WS_Modules.MVVM
{
    /// <summary>
    /// Provides a shared ViewModel for Views or Windows that must observe the same UI state.
    /// </summary>
    /// <typeparam name="TViewModel">The shared ViewModel type.</typeparam>
    public interface IViewModelProvider<out TViewModel>
        where TViewModel : IViewModel
    {
        TViewModel GetViewModel();
    }
}
