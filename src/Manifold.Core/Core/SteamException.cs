// Manifold — Exception hierarchy
// All Manifold-specific exceptions derive from SteamException.

namespace Manifold.Core;

/// <summary>Base class for all Manifold Steam exceptions.</summary>
public abstract class SteamException : Exception
{
    /// <inheritdoc/>
    protected SteamException(string message) : base(message) { }
    /// <inheritdoc/>
    protected SteamException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when <c>SteamAPI_Init</c> fails during <see cref="SteamLifecycle.Initialize"/>.
/// </summary>
public sealed class SteamInitException : SteamException
{
    /// <summary>The Steam EResult code returned by the failed init call, if available.</summary>
    public int ResultCode { get; }

    /// <inheritdoc/>
    public SteamInitException(string message, int resultCode = 0)
        : base(message)
    {
        ResultCode = resultCode;
    }
}

/// <summary>
/// Thrown when a Steam call result completes with <c>ioFailed = true</c>,
/// indicating a transport or network-level failure rather than a logical one.
/// </summary>
/// <remarks>
/// <para>
/// When <c>ioFailed</c> is <c>true</c>, the callback struct data may not have been
/// validly populated by the Steam backend. <see cref="BestEffortResult"/> is captured
/// on a best-effort basis and <b>must not be trusted unconditionally</b>.
/// </para>
/// <para>
/// A non-OK EResult in a successfully delivered callback struct (<c>ioFailed = false</c>)
/// is a logical failure handled by the public API wrapper as a typed return value —
/// not thrown as <see cref="SteamIOFailedException"/>.
/// </para>
/// </remarks>
public sealed class SteamIOFailedException : SteamException
{
    /// <summary>The <c>SteamAPICall_t</c> handle whose completion failed.</summary>
    public ulong ApiCall { get; }

    /// <summary>
    /// Human-readable name of the operation that was being awaited,
    /// e.g. <c>"CreateLobby"</c>. Set by the calling wrapper.
    /// </summary>
    public string OperationHint { get; }

    /// <summary>
    /// Best-effort EResult from the callback struct. May be <c>null</c> or invalid
    /// when <c>ioFailed = true</c> because the struct data may be garbage.
    /// Do not use without checking this context.
    /// </summary>
    public int? BestEffortResult { get; }

    /// <inheritdoc/>
    public SteamIOFailedException(ulong apiCall, string operationHint, int? bestEffortResult = null)
        : base($"Steam call result for '{operationHint}' (handle {apiCall}) failed with ioFailed=true." +
               (bestEffortResult.HasValue
                   ? $" Best-effort EResult: {bestEffortResult.Value} (may be invalid — do not trust unconditionally)."
                   : " No EResult available."))
    {
        ApiCall          = apiCall;
        OperationHint    = operationHint;
        BestEffortResult = bestEffortResult;
    }
}

/// <summary>
/// Thrown when a <see cref="System.Threading.CancellationToken"/> is not triggered,
/// but the internal safety timeout expires before Steam resolves a call result.
/// The Steam backend may still be processing the request.
/// </summary>
public sealed class SteamCallResultTimeoutException : SteamException
{
    /// <summary>The <c>SteamAPICall_t</c> handle that timed out.</summary>
    public ulong ApiCall { get; }

    /// <summary>The timeout duration that was exceeded.</summary>
    public TimeSpan Timeout { get; }

    /// <inheritdoc/>
    public SteamCallResultTimeoutException(ulong apiCall, TimeSpan timeout)
        : base($"Steam call result (handle {apiCall}) did not resolve within {timeout.TotalSeconds:F0}s. " +
               "The Steam backend may still be processing the request.")
    {
        ApiCall = apiCall;
        Timeout = timeout;
    }
}

/// <summary>
/// Thrown when a pending call result is cancelled because <see cref="SteamLifecycle"/>
/// is being disposed. The Steam backend may still be processing the request.
/// </summary>
public sealed class SteamShutdownException : SteamException
{
    /// <inheritdoc/>
    public SteamShutdownException()
        : base("Pending Steam call result was cancelled because SteamLifecycle is shutting down.") { }
}

/// <summary>
/// Thrown when a Manifold API method is called after <see cref="SteamLifecycle.Dispose"/>
/// has been called.
/// </summary>
public sealed class SteamDisposedException : SteamException
{
    /// <inheritdoc/>
    public SteamDisposedException(string memberName)
        : base($"Cannot call '{memberName}': SteamLifecycle has been disposed. " +
               "Re-initialization is not supported.") { }
}
