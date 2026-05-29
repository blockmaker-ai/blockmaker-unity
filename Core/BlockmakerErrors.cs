
namespace Blockmaker;
/// <summary>
/// Helpers for classifying error strings returned by sign and auth callbacks.
/// Use these to decide whether to retry, show specific UI, or fail gracefully.
/// </summary>
public static class BlockmakerErrors
{
    public static bool IsTimeout(string error) =>
        error != null && (error.IndexOf("timed out", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                          error.IndexOf("timeout", System.StringComparison.OrdinalIgnoreCase) >= 0);

    public static bool IsSessionExpired(string error) =>
        error != null && (error.Contains("session has expired") ||
                          error.Contains("session has ended") ||
                          error.Contains("sign in again"));

    public static bool IsUserCancelled(string error) =>
        error != null && (error.Contains("not approved") ||
                          error.Contains("cancelled") ||
                          error.Contains("rejected") ||
                          error.Contains("User closed") ||
                          error.Contains("User denied"));

    public static bool IsNotConnected(string error) =>
        error != null && (error.Contains("not connected") ||
                          error.Contains("connection was lost") ||
                          error.Contains("connect your wallet again"));

    public static bool IsPlatformUnsupported(string error) =>
        error != null && (error.Contains("only available in the web browser") ||
                          error.Contains("not available right now"));

    public static bool IsInterrupted(string error) =>
        error != null && error.Contains("interrupted");

    public static bool IsRetryable(string error) =>
        IsTimeout(error) || IsNotConnected(error) || IsInterrupted(error);
}
