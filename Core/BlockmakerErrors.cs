
namespace Blockmaker
{
    /// <summary>
    /// Helpers for classifying error strings returned by sign and auth callbacks.
    /// Use these to decide whether to retry, show specific UI, or fail gracefully.
    /// </summary>
    public static class BlockmakerErrors
    {
        private static bool Contains(string value, string fragment) =>
            value != null && value.IndexOf(fragment, System.StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool IsTimeout(string error) =>
            Contains(error, "timed out") || Contains(error, "timeout");

        public static bool IsSessionExpired(string error) =>
            Contains(error, "session has expired") ||
            Contains(error, "session has ended") ||
            Contains(error, "sign in again");

        public static bool IsUserCancelled(string error) =>
            Contains(error, "not approved") ||
            Contains(error, "cancelled") ||
            Contains(error, "rejected") ||
            Contains(error, "user closed") ||
            Contains(error, "user denied");

        public static bool IsNotConnected(string error) =>
            Contains(error, "not connected") ||
            Contains(error, "connection was lost") ||
            Contains(error, "connect your wallet again");

        public static bool IsPlatformUnsupported(string error) =>
            Contains(error, "only available in the web browser") ||
            Contains(error, "not available right now");

        public static bool IsInterrupted(string error) =>
            Contains(error, "interrupted");

        /// <summary>
        /// Converts the small set of server-side Magic failures that need a
        /// different player action into stable UI copy. Unknown server text is
        /// never rendered directly, so diagnostics cannot leak into the game.
        /// </summary>
        public static string PlayerFacingMagicLoginError(string error)
        {
            if (Contains(error, "still being prepared") ||
                Contains(error, "temporarily unavailable"))
            {
                return "Email sign-in is temporarily unavailable. Choose another sign-in option, or try again later.";
            }

            if (Contains(error, "not enabled for this game"))
            {
                return "Email sign-in is not enabled for this game. Choose a sign-in option shown by the game.";
            }

            if (Contains(error, "different game website") ||
                Contains(error, "must be opened from the game website") ||
                Contains(error, "web address is not connected"))
            {
                return "Email sign-in cannot continue on this website. Return to the game’s official website and start again.";
            }

            if (Contains(error, "verification failed") ||
                Contains(error, "did not match this email"))
            {
                return "Email verification ended. Please start email sign-in again.";
            }

            return "Sign-in could not be completed. Please try again.";
        }

        public static bool IsGameMismatch(string error) =>
            Contains(error, "does not match your game") || Contains(error, "game id");

        public static bool IsSigningIntentError(string error) =>
            Contains(error, "transaction request expired") || Contains(error, "start the action again");

        public static bool IsRetryable(string error) =>
            IsTimeout(error) || IsNotConnected(error) || IsInterrupted(error);
    }

}
