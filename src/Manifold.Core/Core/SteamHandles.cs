// Manifold — Steam handle types
// Strongly-typed wrappers for HSteamNetConnection and HSteamListenSocket.
// Prevents accidental handle confusion in the networking layer.

using System.Diagnostics;

namespace Manifold.Core;

/// <summary>
/// A strongly-typed wrapper for a <c>HSteamNetConnection</c> handle.
/// Value <c>0</c> represents an invalid/unset connection.
/// </summary>
[DebuggerDisplay("{DebugDisplay,nq}")]
public readonly record struct NetConnection(uint Value)
{
    /// <summary>An invalid/unset connection handle.</summary>
    public static readonly NetConnection Invalid = new(0);

    /// <summary><c>true</c> if this handle represents a real connection.</summary>
    public bool IsValid => Value != 0;

    /// <inheritdoc/>
    public override string ToString() => $"NetConnection({Value})";
    private string DebugDisplay => IsValid ? $"NetConnection({Value})" : "NetConnection(Invalid)";

    /// <summary>Implicitly converts to the underlying <see cref="uint"/>.</summary>
    public static implicit operator uint(NetConnection c)   => c.Value;
    /// <summary>Explicitly wraps a raw <see cref="uint"/> as a <see cref="NetConnection"/>.</summary>
    public static explicit operator NetConnection(uint v)   => new(v);
}

/// <summary>
/// A strongly-typed wrapper for a <c>HSteamListenSocket</c> handle.
/// Value <c>0</c> represents an invalid/unset socket.
/// </summary>
[DebuggerDisplay("{DebugDisplay,nq}")]
public readonly record struct ListenSocket(uint Value)
{
    /// <summary>An invalid/unset listen socket handle.</summary>
    public static readonly ListenSocket Invalid = new(0);

    /// <summary><c>true</c> if this handle represents a real listen socket.</summary>
    public bool IsValid => Value != 0;

    /// <inheritdoc/>
    public override string ToString() => $"ListenSocket({Value})";
    private string DebugDisplay => IsValid ? $"ListenSocket({Value})" : "ListenSocket(Invalid)";

    /// <summary>Implicitly converts to the underlying <see cref="uint"/>.</summary>
    public static implicit operator uint(ListenSocket s)    => s.Value;
    /// <summary>Explicitly wraps a raw <see cref="uint"/> as a <see cref="ListenSocket"/>.</summary>
    public static explicit operator ListenSocket(uint v)    => new(v);
}
