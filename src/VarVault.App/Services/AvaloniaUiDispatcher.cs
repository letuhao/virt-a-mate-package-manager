using Avalonia.Threading;
using VarVault.Sdk.Threading;

namespace VarVault.App.Services;

/// <summary>Marshals callbacks onto Avalonia's UI thread.</summary>
internal sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public bool IsOnUiThread => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    public Task InvokeAsync(Func<Task> action) => Dispatcher.UIThread.InvokeAsync(action);

    public Task<T> InvokeAsync<T>(Func<Task<T>> action) => Dispatcher.UIThread.InvokeAsync(action);
}
