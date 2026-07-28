namespace JonsboLite;

/// <summary>
/// A way to get 64-byte frames to the panel.
///
/// The protocol is identical on every OS — a vendor-defined HID device with no report ID,
/// taking a 64-byte output report. Only the plumbing differs: SetupAPI plus WriteFile on
/// Windows, /dev/hidraw on Linux. Implementations own discovery, so the send loop never
/// needs to know which it is talking to.
/// </summary>
public interface IPanelTransport : IDisposable
{
    /// <summary>True once <see cref="Open"/> has succeeded and no write has failed since.</summary>
    bool IsOpen { get; }

    /// <summary>Human-readable identity of the connected panel, or null when closed.</summary>
    string? Description { get; }

    /// <summary>Finds and opens the first supported panel. False when none is present.</summary>
    bool Open();

    /// <summary>
    /// Writes one <see cref="Frame.PayloadLength"/>-byte payload. Returns false if the
    /// device went away; the implementation closes itself so the caller can just reopen.
    /// </summary>
    bool Send(ReadOnlySpan<byte> payload);
}
