using VarVault.Sdk.Threading;

namespace VarVault.Infrastructure.Threading;

/// <summary>
/// Headless dispatcher for CLI/tests: runs everything inline on the calling thread.
/// The Avalonia app replaces this with one backed by the UI thread's dispatcher.
/// </summary>
internal sealed class InlineUiDispatcher : IUiDispatcher
{
    public bool IsOnUiThread => true;
    public void Post(Action action) => action();
    public Task InvokeAsync(Func<Task> action) => action();
    public Task<T> InvokeAsync<T>(Func<Task<T>> action) => action();
}
