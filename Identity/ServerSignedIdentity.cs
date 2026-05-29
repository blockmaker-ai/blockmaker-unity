using System;
using System.Collections;
using UnityEngine;

namespace Blockmaker
{

    /// <summary>
    /// Base class for identities that sign transactions via the Blockmaker server
    /// (Email, Magic-fallback). Eliminates duplicate signing coroutine logic.
    /// </summary>
    public abstract class ServerSignedIdentity : IBlockmakerIdentity
    {

        public abstract string       Address      { get; }
        public abstract string       DisplayName  { get; }
        public abstract IdentityTier Tier         { get; }
        public abstract string       ProviderName { get; }
        public abstract bool         HasWallet    { get; }
        public abstract bool         CanSign      { get; }

        public abstract string SessionToken { get; protected set; }
        public abstract string RefreshToken { get; protected set; }

        internal void UpdateTokens(string sessionToken, string refreshToken)
        {
            if (!string.IsNullOrEmpty(sessionToken)) SessionToken = sessionToken;
            if (!string.IsNullOrEmpty(refreshToken)) RefreshToken = refreshToken;
        }

        public virtual IEnumerator SignTransaction(
            string         unsignedTxnBase64,
            Action<string> onSigned,
            Action<string> onError)
        {
            if (string.IsNullOrEmpty(unsignedTxnBase64))
            {
                onError?.Invoke("Something went wrong. Please try again.");
                yield break;
            }

            var client = BlockmakerClient.Instance;
            if (client == null)
            {
                onError?.Invoke("Something went wrong. Please restart the game and try again.");
                yield break;
            }

            bool   done   = false;
            string result = null;
            string error  = null;
            bool   gotAuthError = false;

            client.StartCoroutine(
                client.SignTransactionServerSide(
                    unsignedTxnBase64,
                    SessionToken,
                    r => { result = r; done = true; },
                    e => { error = e; done = true; },
                    bmErr => { if (bmErr != null && bmErr.IsAuthError) gotAuthError = true; }
                )
            );

            float elapsed = 0f;
            while (!done && elapsed < BlockmakerAuth.WalletSignTimeout)
            {
                if (client == null) { onError?.Invoke("Something went wrong. Please restart the game and try again."); yield break; }
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!done)
            {
                onError?.Invoke("The request timed out. Please try again.");
                yield break;
            }

            if (gotAuthError && !string.IsNullOrEmpty(RefreshToken))
            {
                var oldToken = SessionToken;
                done = false; result = null; error = null; gotAuthError = false;
                bool refreshDone = false;
                client.RefreshToken(RefreshToken, refreshResult =>
                {
                    if (refreshResult != null && !string.IsNullOrEmpty(refreshResult.sessionToken))
                    {
                        UpdateTokens(refreshResult.sessionToken, refreshResult.refreshToken);
                        SaveSession();
                    }
                    refreshDone = true;
                }, _ => { refreshDone = true; });

                float rElapsed = 0f;
                while (!refreshDone && rElapsed < 10f)
                {
                    if (client == null) { onError?.Invoke("Something went wrong. Please restart the game and try again."); yield break; }
                    rElapsed += Time.unscaledDeltaTime; yield return null;
                }

                if (SessionToken != oldToken && !string.IsNullOrEmpty(SessionToken))
                {
                    client.StartCoroutine(
                        client.SignTransactionServerSide(
                            unsignedTxnBase64, SessionToken,
                            r => { result = r; done = true; },
                            e => { error = e; done = true; },
                            _ => {}
                        )
                    );
                    elapsed = 0f;
                    while (!done && elapsed < BlockmakerAuth.WalletSignTimeout)
                    {
                        if (client == null) { onError?.Invoke("Something went wrong. Please restart the game and try again."); yield break; }
                        elapsed += Time.unscaledDeltaTime; yield return null;
                    }
                }
                else
                {
                    onError?.Invoke("Your session has expired. Please sign in again.");
                    yield break;
                }
            }

            if (!done)
            {
                onError?.Invoke("The request timed out. Please try again.");
                yield break;
            }

            if (result != null) onSigned?.Invoke(result);
            else if (error != null) onError?.Invoke(error);
            else onError?.Invoke("The request could not be completed. Please try again.");
        }

        public virtual IEnumerator SignTransactions(
            string[]         unsignedTxnsBase64,
            Action<string[]> onSigned,
            Action<string>   onError)
        {
            if (unsignedTxnsBase64 == null || unsignedTxnsBase64.Length == 0)
            {
                onError?.Invoke("Something went wrong. Please try again.");
                yield break;
            }

            var client = BlockmakerClient.Instance;
            if (client == null)
            {
                onError?.Invoke("Something went wrong. Please restart the game and try again.");
                yield break;
            }

            bool     done   = false;
            string[] result = null;
            string   error  = null;
            bool     gotAuthError = false;

            client.StartCoroutine(
                client.SignTransactionsServerSide(
                    unsignedTxnsBase64,
                    SessionToken,
                    r => { result = r; done = true; },
                    e => { error  = e; done = true; },
                    bmErr => { if (bmErr != null && bmErr.IsAuthError) gotAuthError = true; }
                )
            );

            float elapsed = 0f;
            while (!done && elapsed < BlockmakerAuth.WalletSignTimeout)
            {
                if (client == null) { onError?.Invoke("Something went wrong. Please restart the game and try again."); yield break; }
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!done)
            {
                onError?.Invoke("The request timed out. Please try again.");
                yield break;
            }

            if (gotAuthError && !string.IsNullOrEmpty(RefreshToken))
            {
                var oldToken = SessionToken;
                done = false; result = null; error = null; gotAuthError = false;
                bool refreshDone = false;
                client.RefreshToken(RefreshToken, refreshResult =>
                {
                    if (refreshResult != null && !string.IsNullOrEmpty(refreshResult.sessionToken))
                    {
                        UpdateTokens(refreshResult.sessionToken, refreshResult.refreshToken);
                        SaveSession();
                    }
                    refreshDone = true;
                }, _ => { refreshDone = true; });

                float rElapsed = 0f;
                while (!refreshDone && rElapsed < 10f)
                {
                    if (client == null) { onError?.Invoke("Something went wrong. Please restart the game and try again."); yield break; }
                    rElapsed += Time.unscaledDeltaTime; yield return null;
                }

                if (SessionToken != oldToken && !string.IsNullOrEmpty(SessionToken))
                {
                    client.StartCoroutine(
                        client.SignTransactionsServerSide(
                            unsignedTxnsBase64, SessionToken,
                            r => { result = r; done = true; },
                            e => { error = e; done = true; },
                            bmErr => { if (bmErr != null && bmErr.IsAuthError) gotAuthError = true; }
                        )
                    );
                    elapsed = 0f;
                    while (!done && elapsed < BlockmakerAuth.WalletSignTimeout)
                    {
                        if (client == null) { onError?.Invoke("Something went wrong. Please restart the game and try again."); yield break; }
                        elapsed += Time.unscaledDeltaTime; yield return null;
                    }
                }
                else
                {
                    onError?.Invoke("Your session has expired. Please sign in again.");
                    yield break;
                }
            }

            if (!done)
            {
                onError?.Invoke("The request timed out. Please try again.");
                yield break;
            }

            if (result != null) onSigned?.Invoke(result);
            else if (error != null) onError?.Invoke(error);
            else onError?.Invoke("The request could not be completed. Please try again.");
        }

        public abstract void SaveSession();
        public abstract void ClearSession();
    }

}