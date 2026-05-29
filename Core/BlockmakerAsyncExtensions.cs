using System;
using System.Threading.Tasks;

namespace Blockmaker
{

    /// <summary>
    /// Async/await overloads for BlockmakerAuth and BlockmakerClient.
    /// These wrap the callback-based APIs so game code can use modern C# patterns:
    ///
    ///   var identity = await BlockmakerAuth.Instance.ConnectWalletAsync("Pera");
    ///   var result   = await BlockmakerClient.Instance.RunFlowAsync("myFlow");
    ///
    /// All methods marshal back to the Unity main thread via TaskCompletionSource.
    /// </summary>
    public static class BlockmakerAsyncExtensions
    {
        // ── BlockmakerAuth ────────────────────────────────────────────────────────

        /// <summary>Connect a wallet and return the identity, or throw on error.</summary>
        public static Task<IBlockmakerIdentity> ConnectWalletAsync(this BlockmakerAuth auth, string provider)
        {
            var tcs = new TaskCompletionSource<IBlockmakerIdentity>();
            auth.ConnectWallet(provider,
                identity => tcs.TrySetResult(identity),
                error    => tcs.TrySetException(new BlockmakerException(error)));
            return tcs.Task;
        }

        /// <summary>Connect via Magic email and return the identity, or throw on error.</summary>
        public static Task<IBlockmakerIdentity> ConnectMagicEmailAsync(this BlockmakerAuth auth, string email)
        {
            var tcs = new TaskCompletionSource<IBlockmakerIdentity>();
            auth.ConnectMagicEmail(email,
                identity => tcs.TrySetResult(identity),
                error    => tcs.TrySetException(new BlockmakerException(error)));
            return tcs.Task;
        }

        /// <summary>Connect an EVM wallet and return the identity, or throw on error.</summary>
        public static Task<IBlockmakerIdentity> ConnectEvmAsync(this BlockmakerAuth auth)
        {
            var tcs = new TaskCompletionSource<IBlockmakerIdentity>();
            auth.ConnectEvm(
                identity => tcs.TrySetResult(identity),
                error    => tcs.TrySetException(new BlockmakerException(error)));
            return tcs.Task;
        }

        /// <summary>Verify the current session. Returns true if valid.</summary>
        public static Task<bool> VerifySessionAsync(this BlockmakerAuth auth)
        {
            var tcs = new TaskCompletionSource<bool>();
            auth.VerifySession(ok => tcs.TrySetResult(ok));
            return tcs.Task;
        }

        /// <summary>Request an email OTP code, or throw on error.</summary>
        public static Task RequestEmailOTPAsync(this BlockmakerAuth auth, string email)
        {
            var tcs = new TaskCompletionSource<bool>();
            auth.RequestEmailOTP(email,
                ()    => tcs.TrySetResult(true),
                error => tcs.TrySetException(new BlockmakerException(error)));
            return tcs.Task;
        }

        /// <summary>Verify an email OTP and return the identity, or throw on error.</summary>
        public static Task<IBlockmakerIdentity> VerifyEmailOTPAsync(this BlockmakerAuth auth, string email, string otp)
        {
            var tcs = new TaskCompletionSource<IBlockmakerIdentity>();
            auth.VerifyEmailOTP(email, otp,
                identity => tcs.TrySetResult(identity),
                error    => tcs.TrySetException(new BlockmakerException(error)));
            return tcs.Task;
        }

        // ── BlockmakerClient ──────────────────────────────────────────────────────

        /// <summary>POST to a server path and return the typed response, or throw on error.</summary>
        public static Task<TRes> PostAsync<TReq, TRes>(this BlockmakerClient client, string path, TReq body) where TRes : class
        {
            var tcs = new TaskCompletionSource<TRes>();
            client.Post<TReq, TRes>(path, body,
                result => tcs.TrySetResult(result),
                error  => tcs.TrySetException(new BlockmakerException(error)));
            return tcs.Task;
        }

        /// <summary>GET from a server path and return the typed response, or throw on error.</summary>
        public static Task<TRes> GetAsync<TRes>(this BlockmakerClient client, string path) where TRes : class
        {
            var tcs = new TaskCompletionSource<TRes>();
            client.Get<TRes>(path,
                result => tcs.TrySetResult(result),
                error  => tcs.TrySetException(new BlockmakerException(error)));
            return tcs.Task;
        }

        // ── BlockmakerProfileManager ──────────────────────────────────────────────

        /// <summary>Claim a username and return the updated profile, or throw on error.</summary>
        public static Task<BlockmakerProfile> ClaimUsernameAsync(this BlockmakerProfileManager mgr, string username)
        {
            var tcs = new TaskCompletionSource<BlockmakerProfile>();
            mgr.ClaimUsername(username,
                profile => tcs.TrySetResult(profile),
                error   => tcs.TrySetException(new BlockmakerException(error)));
            return tcs.Task;
        }

        /// <summary>Change username and return the updated profile, or throw on error.</summary>
        public static Task<BlockmakerProfile> ChangeUsernameAsync(this BlockmakerProfileManager mgr, string newUsername)
        {
            var tcs = new TaskCompletionSource<BlockmakerProfile>();
            mgr.ChangeUsername(newUsername,
                profile => tcs.TrySetResult(profile),
                error   => tcs.TrySetException(new BlockmakerException(error)));
            return tcs.Task;
        }

        /// <summary>Set profile picture to an NFT and return the updated profile, or throw on error.</summary>
        public static Task<BlockmakerProfile> SetProfilePicNftAsync(this BlockmakerProfileManager mgr, long assetId)
        {
            var tcs = new TaskCompletionSource<BlockmakerProfile>();
            mgr.SetProfilePicNft(assetId,
                profile => tcs.TrySetResult(profile),
                error   => tcs.TrySetException(new BlockmakerException(error)));
            return tcs.Task;
        }
    }

    /// <summary>
    /// Exception thrown by async SDK methods when the underlying operation fails.
    /// The message is always a user-friendly string safe to display in UI.
    /// </summary>
    public class BlockmakerException : Exception
    {
        public BlockmakerException(string message) : base(message) { }
    }

}