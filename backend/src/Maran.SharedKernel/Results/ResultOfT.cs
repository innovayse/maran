namespace Maran.SharedKernel.Results;

/// <summary>
/// Outcome of a domain operation: a value or a typed error — never both. A FAILURE may additionally
/// carry a <see cref="ProblemExtension"/> — typed facts the problem response publishes beside the
/// error's code, such as how far a partial operation got — which is data ABOUT the failure, never a
/// second value.
/// </summary>
public sealed class Result<T>
{
    /// <summary>Backing field for <see cref="Value"/>; meaningful only when <see cref="IsSuccess"/> is true.</summary>
    private readonly T? _value;

    /// <summary>True when the operation produced a value.</summary>
    public bool IsSuccess { get; }

    /// <summary>The error of a failed result; null on success.</summary>
    public Error? Error { get; }

    /// <summary>
    /// Typed facts of a failed result that the problem response publishes as an extension member
    /// (<c>ApiResultExtensions.ToProblemResult</c>); null on success and on the failures — most —
    /// that have nothing to publish. See <see cref="ProblemExtension"/> for the convention.
    /// </summary>
    public ProblemExtension? Extension { get; }

    /// <summary>The success value. Accessing it on a failure is a programming bug.</summary>
    public T Value
    {
        get
        {
            return IsSuccess ? _value! : throw new InvalidOperationException($"Result is a failure: {Error!.Code}");
        }
    }

    /// <summary>Internal constructor; use <see cref="Ok"/> / <see cref="Fail(Results.Error)"/>.</summary>
    /// <param name="success">Whether the operation succeeded.</param>
    /// <param name="value">The success value, when <paramref name="success"/> is true.</param>
    /// <param name="error">The failure error, when <paramref name="success"/> is false.</param>
    /// <param name="extension">Typed facts of the failure for the problem response, or null.</param>
    private Result(bool success, T? value, Error? error, ProblemExtension? extension)
    {
        IsSuccess = success;
        _value = value;
        Error = error;
        Extension = extension;
    }

    /// <summary>Wraps a success value.</summary>
    public static Result<T> Ok(T value)
    {
        return new(true, value, null, null);
    }

    /// <summary>Wraps a typed failure.</summary>
    public static Result<T> Fail(Error error)
    {
        return new(false, default, error, null);
    }

    /// <summary>
    /// Wraps a typed failure carrying facts the problem response must publish — a partial
    /// operation's progress, above all.
    /// </summary>
    /// <param name="error">The failure to carry.</param>
    /// <param name="extension">The typed facts published as a problem extension member.</param>
    public static Result<T> Fail(Error error, ProblemExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        return new(false, default, error, extension);
    }

    /// <summary>Folds both branches into one value.</summary>
    /// <param name="onOk">Invoked with the value when the result is a success.</param>
    /// <param name="onFail">Invoked with the error when the result is a failure.</param>
    public TOut Match<TOut>(Func<T, TOut> onOk, Func<Error, TOut> onFail)
    {
        return IsSuccess ? onOk(_value!) : onFail(Error!);
    }
}
