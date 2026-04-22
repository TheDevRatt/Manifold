// Manifold — Result<T>
// Minimal discriminated result type used for operations that must not throw.
// Specifically: SteamLifecycle.Initialize(), which callers branch on without try/catch.
// Not a general-purpose algebraic type — keep it small.

namespace Manifold.Core;

/// <summary>
/// Represents the result of an operation that can fail without throwing an exception.
/// Used specifically for <see cref="SteamLifecycle.Initialize"/> and similar
/// operations where the caller must branch on success/failure explicitly.
/// </summary>
public readonly struct Result<T>
{
    /// <summary>Whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// The result value on success. Undefined (do not use) on failure.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// Human-readable error message on failure. <see cref="string.Empty"/> on success.
    /// </summary>
    public string Error { get; }

    private Result(T value)
    {
        IsSuccess = true;
        Value     = value;
        Error     = string.Empty;
    }

    private Result(string error)
    {
        IsSuccess = false;
        Value     = default;
        Error     = error;
    }

    /// <summary>Creates a successful result wrapping <paramref name="value"/>.</summary>
    public static Result<T> Ok(T value)      => new(value);

    /// <summary>Creates a failed result with the given <paramref name="message"/>.</summary>
    public static Result<T> Fail(string message) => new(message);

    /// <summary>
    /// Deconstructs the result into its components.
    /// On success: <paramref name="value"/> is the real result,
    ///             <paramref name="error"/> is <see cref="string.Empty"/>.
    /// On failure: <paramref name="value"/> is <c>default!</c> — callers must not use it;
    ///             <paramref name="error"/> carries the failure message.
    /// </summary>
    /// <returns><c>true</c> if the operation succeeded; <c>false</c> otherwise.</returns>
    public bool TryGetValue(out T value, out string error)
    {
        if (IsSuccess)
        {
            value = Value!;        // guaranteed non-null on the success path
            error = string.Empty;
        }
        else
        {
            value = default!;      // sentinel — caller must ignore this on failure
            error = Error;
        }
        return IsSuccess;
    }

    /// <inheritdoc/>
    public override string ToString() =>
        IsSuccess ? $"Ok({Value})" : $"Fail({Error})";
}
