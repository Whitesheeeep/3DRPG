namespace WS_Modules.MVVM
{
    /// <summary>
    /// Binds a View to a ViewModel and releases that binding when the View is destroyed or rebuilt.
    /// </summary>
    /// <typeparam name="TViewModel">The ViewModel type owned or provided by the composition root.</typeparam>
    public interface IView<in TViewModel>
        where TViewModel : IViewModel
    {
        void Bind(TViewModel viewModel);
        void Unbind();
    }
}
