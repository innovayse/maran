namespace Maran.SharedKernel.Results;

/// <summary>Outcome of a domain operation: a value or a typed error — never both.</summary>
public sealed class Result<T>
{
    /// <summary>Backing field for <see cref="Value"/>; meaningful only when <see cref="IsSuccess"/> is true.</summary>
    private readonly T? _value;

    /// <summary>True when the operation produced a value.</summary>
    public bool IsSuccess { get; }

    /// <summary>The error of a failed result; null on success.</summary>
    public Error? Error { get; }

    /// <summary>The success value. Accessing it on a failure is a programming bug.</summary>
    public T Value =>
        IsSuccess ? _value! : throw new InvalidOperationException($"Result is a failure: {Error!.Code}");

    /// <summary>Internal constructor; use <see cref="Ok"/> / <see cref="Fail"/>.</summary>
    /// <param name="success">Whether the operation succeeded.</param>
    /// <param name="value">The success value, when <paramref name="success"/> is true.</param>
    /// <param name="error">The failure error, when <paramref name="success"/> is false.</param>
    private Result(bool success, T? value, Error? error)
    {
        IsSuccess = success;
        _value = value;
        Error = error;
    }

    /// <summary>Wraps a success value.</summary>
    public static Result<T> Ok(T value) => new(true, value, null);

    /// <summary>Wraps a typed failure.</summary>
    public static Result<T> Fail(Error error) => new(false, default, error);

    /// <summary>Folds both branches into one value.</summary>
    /// <param name="onOk">Invoked with the value when the result is a success.</param>
    /// <param name="onFail">Invoked with the error when the result is a failure.</param>
    public TOut Match<TOut>(Func<T, TOut> onOk, Func<Error, TOut> onFail) =>
        IsSuccess ? onOk(_value!) : onFail(Error!);
}
