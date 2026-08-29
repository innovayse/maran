namespace Maran.SharedKernel.Results;

/// <summary>
/// Outcome of a domain operation that produces no value — a delete, a restart, a suspension.
/// Exists so such operations never have to invent a placeholder payload (`Result&lt;bool&gt;` or a
/// home-grown `Unit`), which would otherwise diverge between modules.
/// </summary>
public sealed class Result
{
    /// <summary>The single success instance; a successful void result carries no state.</summary>
    private static readonly Result SuccessInstance = new(true, null);

    /// <summary>True when the operation completed.</summary>
    public bool IsSuccess { get; }

    /// <summary>The error of a failed result; null on success.</summary>
    public Error? Error { get; }

    /// <summary>Internal constructor; use <see cref="Ok"/> / <see cref="Fail"/>.</summary>
    /// <param name="success">Whether the operation completed.</param>
    /// <param name="error">The typed failure, or null on success.</param>
    private Result(bool success, Error? error)
    {
        IsSuccess = success;
        Error = error;
    }

    /// <summary>Returns the success outcome.</summary>
    public static Result Ok()
    {
        return SuccessInstance;
    }

    /// <summary>Wraps a typed failure.</summary>
    /// <param name="error">The failure to carry.</param>
    public static Result Fail(Error error)
    {
        return new(false, error);
    }

    /// <summary>Folds both branches into one value.</summary>
    /// <typeparam name="TOut">Type both branches produce.</typeparam>
    /// <param name="onOk">Invoked when the operation succeeded.</param>
    /// <param name="onFail">Invoked with the error when it failed.</param>
    public TOut Match<TOut>(Func<TOut> onOk, Func<Error, TOut> onFail)
    {
        return IsSuccess ? onOk() : onFail(Error!);
    }
}
