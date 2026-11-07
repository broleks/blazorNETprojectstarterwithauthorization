namespace Starter.Web.Services;

/// <summary>
/// Short confirmation messages ("Note saved") that survive navigation inside the circuit.
/// Scoped = one instance per browser tab.
/// </summary>
public sealed class ToastService
{
    public event Action? Changed;

    public string? Message { get; private set; }
    public bool IsError { get; private set; }

    public void Show(string message, bool isError = false)
    {
        Message = message;
        IsError = isError;
        Changed?.Invoke();
    }

    public void Clear()
    {
        Message = null;
        Changed?.Invoke();
    }
}
