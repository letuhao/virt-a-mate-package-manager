namespace VarVault.Sdk.Threading;

/// <summary>
/// Marshals work onto the UI thread. Modules/services never touch UI objects directly;
/// they post through here. The Avalonia app supplies the real implementation; headless
/// hosts (CLI, tests) get an inline one.
/// </summary>
public interface IUiDispatcher
{
    bool IsOnUiThread { get; }
    void Post(Action action);
    Task InvokeAsync(Func<Task> action);
    Task<T> InvokeAsync<T>(Func<Task<T>> action);
}
