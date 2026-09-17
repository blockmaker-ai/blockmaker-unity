// Unity WebGL receiver for Blockmaker's unified player-wallet package.
//
// The browser host owns one TxnLab v5 manager for the exact runtime profile:
// Pera + Lute when no Web3Auth public client ID is supplied, or Pera + Lute +
// TxnLab Email/Google when a valid public client ID is supplied. Unity receives
// only a normal Blockmaker player session, public classifiers and copied signed
// bytes for one exact transaction group. This bridge never submits transactions.

using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Blockmaker
{
    [Serializable]
    public sealed class BlockmakerUnityWalletProgress
    {
        public string schemaVersion, lifecycleId, operationKind, providerId;
        public int operationId;
        public string phase, walletConnectUri, code, walletAddress;
    }

    [Serializable]
    internal sealed class BlockmakerWalletPackageWebGLPayload
    {
        public string schemaVersion;
        public string lifecycleId;
        public int operationId;
        public string operationKind;
        public string phase;
        public string runtimeProfile;
        public string sessionToken;
        public string refreshToken;
        public string walletAddress;
        public string accountKind;
        public string authProvider;
        public string[] signedTransactionsBase64;
    }

    [Serializable]
    internal sealed class BlockmakerWalletPackageWebGLTransactionGroupPayload
    {
        public string[] unsignedTransactionsBase64;
    }

    [Serializable]
    internal sealed class BlockmakerUnityUsernameGroup { public string[] unsignedTxnsBase64; }

    [Serializable]
    internal sealed class BlockmakerWalletPackageWebGLErrorPayload
    {
        public string schemaVersion;
        public string lifecycleId;
        public int operationId;
        public string operationKind;
        public string code;
    }

    [Serializable]
    internal sealed class BlockmakerWalletPackageWebGLConfirmationPayload
    {
        public string schemaVersion;
        public string lifecycleId;
        public int operationId;
        public string operationKind;
        public bool confirmed;
        public bool success;
        public string code;
    }

    internal sealed class BlockmakerWalletPackageWebGLSessionSnapshot
    {
        public string SessionToken;
        public string RefreshToken;
        public string WalletAddress;
        public string AccountKind;
        public string AuthProvider;
    }

    /// <summary>
    /// Diagnostic observation only. This is not a deployment receipt, player
    /// session acknowledgment or economic permission.
    /// </summary>
    public sealed class BlockmakerWalletPackageEvidenceStatus
    {
        public string SchemaVersion { get { return "blockmaker-wallet-package-evidence-status/v1"; } }
        public string State { get; private set; }
        public string Stage { get; private set; }
        public string Code { get; private set; }
        public bool CanDownload { get { return State == "ready"; } }
        public bool ReloadRequired { get { return State == "failed"; } }

        private BlockmakerWalletPackageEvidenceStatus(string state, string stage, string code)
        {
            State = state; Stage = stage; Code = code;
        }

        internal static BlockmakerWalletPackageEvidenceStatus FromBridgeCode(int code)
        {
            // Additive integer ABI: unknown future/malformed values fail closed.
            switch (code) {
                case 0: return new BlockmakerWalletPackageEvidenceStatus("not_started", "none", "CAPTURE_NOT_STARTED");
                case 1: return new BlockmakerWalletPackageEvidenceStatus("pending", "capture", "CAPTURE_PENDING");
                case 2: return new BlockmakerWalletPackageEvidenceStatus("ready", "complete", "NONE");
                case 3: return new BlockmakerWalletPackageEvidenceStatus("failed", "origin", "PRODUCTION_ORIGIN_REQUIRED");
                case 4: return new BlockmakerWalletPackageEvidenceStatus("failed", "manifest", "MANIFEST_UNAVAILABLE");
                case 5: return new BlockmakerWalletPackageEvidenceStatus("failed", "manifest", "MANIFEST_INVALID");
                case 6: return new BlockmakerWalletPackageEvidenceStatus("failed", "runtime", "RUNTIME_UNAVAILABLE");
                case 7: return new BlockmakerWalletPackageEvidenceStatus("failed", "runtime", "RUNTIME_INVALID");
                case 8: return new BlockmakerWalletPackageEvidenceStatus("failed", "capture", "CAPTURE_TIMEOUT");
                case 9: return new BlockmakerWalletPackageEvidenceStatus("failed", "capture", "CAPTURE_FAILED");
                default: return new BlockmakerWalletPackageEvidenceStatus("unavailable", "none", "BRIDGE_UNAVAILABLE");
            }
        }
    }

    public sealed class BlockmakerWalletPackageWebGLResult
    {
        public bool Success { get; private set; }
        public string WalletAddress { get; private set; }
        public string AccountKind { get; private set; }
        public string AuthProvider { get; private set; }
        public string Code { get; private set; }
        public string Message { get; private set; }
        public string[] SignedTransactionsBase64 { get; private set; }

        internal static BlockmakerWalletPackageWebGLResult Completed(
            string walletAddress = null,
            string accountKind = null,
            string authProvider = null,
            string[] signedTransactionsBase64 = null)
        {
            return new BlockmakerWalletPackageWebGLResult {
                Success = true,
                WalletAddress = walletAddress,
                AccountKind = accountKind,
                AuthProvider = authProvider,
                SignedTransactionsBase64 = signedTransactionsBase64 == null
                    ? null : (string[])signedTransactionsBase64.Clone(),
            };
        }

        internal static BlockmakerWalletPackageWebGLResult Failed(string code)
        {
            var safe = SafeCode(code);
            return new BlockmakerWalletPackageWebGLResult {
                Success = false,
                Code = safe,
                Message = SafeMessage(safe),
            };
        }

        private static string SafeCode(string code)
        {
            switch ((code ?? "").Trim().ToUpperInvariant()) {
                case "DEPLOYMENT_EVIDENCE_UNAVAILABLE":
                case "ORIGIN_NOT_ALLOWED":
                case "PACKAGE_LOAD_FAILED":
                case "PACKAGE_NOT_READY":
                case "PLAYER_CANCELLED":
                case "PROVIDER_CLEANUP_REQUIRED":
                case "PROVIDER_NOT_ENABLED":
                case "PROVIDER_UNAVAILABLE":
                case "NETWORK_UNAVAILABLE":
                case "REQUEST_ALREADY_PENDING":
                case "SESSION_CONFLICT":
                case "SESSION_EXPIRED":
                case "LOGOUT_UNCONFIRMED":
                case "TRANSACTION_GROUP_INVALID":
                case "WALLET_SIGNATURE_INVALID":
                    return code.Trim().ToUpperInvariant();
                default:
                    return "PROVIDER_UNAVAILABLE";
            }
        }

        private static string SafeMessage(string code)
        {
            switch (code) {
                case "DEPLOYMENT_EVIDENCE_UNAVAILABLE":
                    return "Deployment evidence is unavailable. Reload the game, wait for wallet-package readiness, and try again.";
                case "ORIGIN_NOT_ALLOWED":
                    return "This exact game website is not allowed to use the wallet package.";
                case "PACKAGE_LOAD_FAILED":
                    return "The wallet package could not be loaded. Reload the game and try again.";
                case "PACKAGE_NOT_READY":
                    return "The wallet package is still loading. Wait for readiness before opening it.";
                case "PLAYER_CANCELLED":
                    return "The wallet request was cancelled.";
                case "PROVIDER_CLEANUP_REQUIRED":
                    return "Reload this page before trying the wallet package again.";
                case "PROVIDER_NOT_ENABLED":
                    return "That wallet option is not enabled for this game.";
                case "NETWORK_UNAVAILABLE":
                    return "The wallet package could not reach Blockmaker.";
                case "REQUEST_ALREADY_PENDING":
                    return "Another wallet-package window is already active.";
                case "SESSION_CONFLICT":
                    return "Sign out of the current player account before choosing another wallet.";
                case "SESSION_EXPIRED":
                    return "This player session expired. Sign in again.";
                case "LOGOUT_UNCONFIRMED":
                    return "Sign-out could not be confirmed. The local player session was kept so you can retry safely.";
                case "TRANSACTION_GROUP_INVALID":
                    return "The reviewed Algorand transaction group is invalid or changed.";
                case "WALLET_SIGNATURE_INVALID":
                    return "The wallet did not return an exact valid signature for the reviewed transaction group.";
                default:
                    return "The wallet package is temporarily unavailable.";
            }
        }
    }

    /// <summary>
    /// Unified TxnLab v5 wallet UI for Unity WebGL. An absent Web3Auth public
    /// client ID selects exactly Pera + Lute; a valid ID selects exactly Pera +
    /// Lute + Email/Google. Put one receiver on a uniquely named GameObject.
    /// Initialize it during loading, wait for IsReady (or the Initialize
    /// callback), and call OpenAccount only from a player click/tap.
    ///
    /// Native Unity and the Unity editor deliberately fail closed. The one
    /// transaction operation signs copied, exact reviewed bytes and never
    /// broadcasts them.
    /// </summary>
    public sealed class BlockmakerWalletPackageWebGL : MonoBehaviour
    {
        public const string NativeUnavailableCode =
            "BLOCKMAKER_WALLET_PACKAGE_NATIVE_UNAVAILABLE";
        public const string BrowserBridgeGlobal =
            "__blockmakerUnityWebGlWalletPackageV1";
        public const string EnvelopeSchema =
            "blockmaker-unity-webgl-wallet-package/v2";
        public const int MaximumPayloadCharacters = 20000;
        public const int MaximumTransactionPayloadCharacters = 1500000;
        public const int MaximumTransactionBytes = 65536;
        public const int MaximumSignedTransactionBytes = 66000;
        public const int MaximumTransactionCount = 16;
        public const string TxnLabWeb3AuthProvider = "txnlab_web3auth";
        public const string PeraLuteRuntimeProfile = "pera_lute";
        public const string PeraLuteWeb3AuthRuntimeProfile =
            "pera_lute_txnlab_web3auth";

        private BlockmakerClient client;
        private readonly string lifecycleId = Guid.NewGuid().ToString("N");
        private int operationId;
        private bool initializePending;
        private bool accountPending;
        private bool accountHandoffInstalled;
        private bool accountRejecting;
        private bool logoutPending;
        private bool fundingOpening;
        private bool fundingOpen;
        private bool fundingClosing;
        private bool deploymentEvidencePending;
        private bool transactionPreparationPending;
        private bool transactionPrepared;
        private bool transactionSigning;
        private bool destroyed;
        private Action<BlockmakerWalletPackageWebGLResult> initializeCompletion;
        private Action<BlockmakerWalletPackageWebGLResult> accountCompletion;
        private Action<BlockmakerWalletPackageWebGLResult> logoutCompletion;
        private Action<BlockmakerWalletPackageWebGLResult> fundingOpenCompletion;
        private Action<BlockmakerWalletPackageWebGLResult> fundingCloseCompletion;
        private Action<BlockmakerWalletPackageWebGLResult> deploymentEvidenceCompletion;
        private Action<BlockmakerWalletPackageWebGLResult> transactionPreparationCompletion;
        private Action<BlockmakerWalletPackageWebGLResult> transactionSigningCompletion;
        private BlockmakerWalletPackageWebGLSessionSnapshot accountOpenBaseline;
        private BlockmakerWalletPackageWebGLSessionSnapshot acceptedHandoff;
        private BlockmakerWalletPackageWebGLSessionSnapshot logoutSnapshot;
        private BlockmakerWalletPackageWebGLSessionSnapshot transactionSessionSnapshot;
        private string[] preparedUnsignedTransactionsBase64;

        public bool IsReady { get; private set; }
        public bool ReloadRequired { get; private set; }
        public string RuntimeProfile { get; private set; }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void BlockmakerWalletPackageWebGL_Initialize(
            string targetName, string lifecycleId, string baseUrl, string gameId,
            string clientId, string appName, string walletAddress,
            string accountKind, string authProvider);

        [DllImport("__Internal")]
        private static extern int BlockmakerWalletPackageWebGL_AcknowledgeReady(
            string targetName, string lifecycleId, int operationId);

        [DllImport("__Internal")]
        private static extern void BlockmakerWalletPackageWebGL_OpenAccount(
            string targetName, string lifecycleId, int operationId, int unityPresentation, string providerId);

        [DllImport("__Internal")]
        private static extern void BlockmakerWalletPackageWebGL_PrepareTransactionGroup(
            string targetName, string lifecycleId, int operationId,
            string walletAddress, string accountKind, string authProvider,
            string transactionGroupJson, string usernameJson);

        [DllImport("__Internal")]
        private static extern int BlockmakerWalletPackageWebGL_SignPreparedTransactionGroup(
            string targetName, string lifecycleId, int operationId);

        [DllImport("__Internal")]
        private static extern void BlockmakerWalletPackageWebGL_Cancel(
            string lifecycleId, int operationId);

        [DllImport("__Internal")]
        private static extern int BlockmakerWalletPackageWebGL_Acknowledge(
            string targetName, string lifecycleId, int operationId);

        [DllImport("__Internal")]
        private static extern void BlockmakerWalletPackageWebGL_Reject(
            string targetName, string lifecycleId, int operationId, string code);

        [DllImport("__Internal")]
        private static extern void BlockmakerWalletPackageWebGL_Logout(
            string targetName, string lifecycleId, int operationId,
            string sessionToken, string refreshToken, string walletAddress,
            string accountKind, string authProvider);

        [DllImport("__Internal")]
        private static extern void BlockmakerWalletPackageWebGL_OpenFunding(
            string targetName, string lifecycleId, int operationId,
            string accessToken, string accountKind, string authProvider);

        [DllImport("__Internal")]
        private static extern void BlockmakerWalletPackageWebGL_CloseFunding(
            string targetName, string lifecycleId, int operationId);

        [DllImport("__Internal")]
        private static extern int BlockmakerWalletPackageWebGL_GetDeploymentEvidenceStatus(
            string targetName, string lifecycleId);

        [DllImport("__Internal")]
        private static extern int BlockmakerWalletPackageWebGL_DownloadDeploymentEvidence(
            string targetName, string lifecycleId, int operationId);
#endif

        public static bool RuntimeSupported
        {
            get {
#if UNITY_WEBGL && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        [Obsolete(
            "Initialize requires an app name and an optional public TxnLab Web3Auth client ID. "
            + "Call Initialize(client, txnLabWeb3AuthClientIdOrEmpty, appName, done).",
            false)]
        public void Initialize(
            BlockmakerClient blockmakerClient,
            Action<BlockmakerWalletPackageWebGLResult> done = null)
        {
            throw new ArgumentException(
                "WALLET_PACKAGE_APP_NAME_REQUIRED: Supply an app name and either an empty or valid public TxnLab Web3Auth client ID explicitly.");
        }

        public void Initialize(
            BlockmakerClient blockmakerClient,
            string txnLabWeb3AuthClientId,
            string appName,
            Action<BlockmakerWalletPackageWebGLResult> done = null)
        {
            if (blockmakerClient == null)
                throw new ArgumentNullException(nameof(blockmakerClient));
#if UNITY_WEBGL && !UNITY_EDITOR
            if (destroyed)
                throw new InvalidOperationException("The wallet-package receiver has been destroyed.");
            if (client != null || initializePending || IsReady)
                throw new InvalidOperationException("The Unity WebGL wallet package can be initialized only once.");
            if (!SafePublicGameId(blockmakerClient.GameId)
                || !SafeBaseUrl(blockmakerClient.BaseUrl))
                throw new ArgumentException("The wallet package requires a bounded public game ID and exact Blockmaker origin.");
            var selectedRuntimeProfile = RuntimeProfileForClientId(
                txnLabWeb3AuthClientId);
            if (selectedRuntimeProfile == null)
                throw new ArgumentException(
                    "Use an empty Web3Auth client ID for Pera + Lute, or supply one valid public ID to enable TxnLab Email/Google.",
                    nameof(txnLabWeb3AuthClientId));
            if (!SafeAppName(appName))
                throw new ArgumentException(
                    "The wallet package app name must contain 1 to 80 safe characters without surrounding whitespace.",
                    nameof(appName));
            var restoredSession = SnapshotClientSessionState(blockmakerClient);
            if (!EmptySession(restoredSession)
                && !ValidSessionForRuntimeProfile(
                    restoredSession, selectedRuntimeProfile))
                throw new ArgumentException(
                    "The current player session is incomplete or its provider is not enabled by the selected wallet runtime profile.",
                    nameof(blockmakerClient));
            RuntimeProfile = selectedRuntimeProfile;
            client = blockmakerClient;
            client.PlayerSessionRefreshed += AcknowledgedSessionRefreshed;
            initializeCompletion = done;
            initializePending = true;
            BlockmakerWalletPackageWebGL_Initialize(
                gameObject.name, lifecycleId,
                blockmakerClient.BaseUrl, blockmakerClient.GameId,
                txnLabWeb3AuthClientId ?? "", appName,
                restoredSession.WalletAddress ?? "",
                restoredSession.AccountKind ?? "",
                restoredSession.AuthProvider ?? "");
#else
            throw new PlatformNotSupportedException(
                NativeUnavailableCode + ": The unified wallet package supports web and Unity WebGL only.");
#endif
        }

        /// <summary>Opt into Unity wallet choices and progress with provider-owned email forms.</summary>
        public bool EmailEnabled { get { return RuntimeProfile == PeraLuteWeb3AuthRuntimeProfile; } }
        private string presentationProvider = "pera";
        private BlockmakerWalletPackageWebGLSessionSnapshot acknowledgedSession;
        /// <summary>Only the exact C#-installed and browser-acknowledged session can sign.</summary>
        public bool HasAcknowledgedPlayerSession { get {
            return IsReady && !ReloadRequired && acknowledgedSession != null &&
                SessionMatches(acknowledgedSession) && ValidSessionForRuntimeProfile(acknowledgedSession, RuntimeProfile);
        } }
        public bool CanSignTransactions { get { return HasAcknowledgedPlayerSession; } }
        private void AcknowledgedSessionRefreshed(string previousToken, string previousRefresh)
        {
            var current = SnapshotClientSessionState();
            if (acknowledgedSession != null && acknowledgedSession.SessionToken == previousToken &&
                acknowledgedSession.RefreshToken == previousRefresh && current.WalletAddress == acknowledgedSession.WalletAddress &&
                current.AccountKind == acknowledgedSession.AccountKind && current.AuthProvider == acknowledgedSession.AuthProvider &&
                ValidSessionForRuntimeProfile(current, RuntimeProfile)) acknowledgedSession = current;
        }
        /// <summary>Provider capability only; signing also requires the acknowledged current session.</summary>
        public static bool IsEconomicWalletProvider(string providerId) {
            return SupportedPairForRuntimeProfile("algorand_wallet", providerId, PeraLuteWeb3AuthRuntimeProfile);
        }
        public bool SupportsEconomicWallet(string providerId) {
            return SupportedPairForRuntimeProfile("algorand_wallet", providerId, RuntimeProfile);
        }

        public bool UseUnityPresentation { get; set; }
        public event Action<BlockmakerUnityWalletProgress> PresentationChanged;

        public void OnBlockmakerWalletPresentation(string json)
        {
            if (!accountPending || string.IsNullOrEmpty(json) || json.Length > 8192) return;
            BlockmakerUnityWalletProgress value;
            try { value = JsonUtility.FromJson<BlockmakerUnityWalletProgress>(json); }
            catch { return; }
            if (value == null || value.schemaVersion != "blockmaker-unity-webgl-wallet-package/v2"
                || value.lifecycleId != lifecycleId || value.operationId != operationId
                || value.operationKind != "open_account" || value.providerId != presentationProvider) return;
            if (value.phase != "connecting" && value.phase != "qr"
                && value.phase != "approval" && value.phase != "verifying" && value.phase != "provider_auth") return;
            if (value.phase == "qr" && (string.IsNullOrEmpty(value.walletConnectUri)
                || !value.walletConnectUri.StartsWith("wc:", StringComparison.Ordinal)
                || value.walletConnectUri.Length > 4096)) return;
            PresentationChanged?.Invoke(value);
        }

        public void OpenAccount(Action<BlockmakerWalletPackageWebGLResult> done)
        {
            OpenAccount("pera", done);
        }

        public void OpenAccount(string providerId, Action<BlockmakerWalletPackageWebGLResult> done)
        {
            EnsureUsable();
            if (providerId != "pera" && (providerId != TxnLabWeb3AuthProvider || !EmailEnabled))
                throw new InvalidOperationException("PROVIDER_NOT_ENABLED");
            if (providerId != "pera" && !UseUnityPresentation)
                throw new InvalidOperationException("Email selection requires Unity presentation.");
            presentationProvider = providerId;
            if (!IsReady)
                throw new InvalidOperationException(
                    "PACKAGE_NOT_READY: Wait for wallet-package readiness before opening account sign-in.");
            if (HasActiveOperation())
                throw new InvalidOperationException(
                    "REQUEST_ALREADY_PENDING: Another wallet-package window is already active.");
            var baseline = SnapshotClientSessionState();
            if (!EmptySession(baseline))
                throw new InvalidOperationException(
                    "SESSION_CONFLICT: Confirm logout before replacing the current player session.");
#if UNITY_WEBGL && !UNITY_EDITOR
            operationId = NextOperationId(operationId);
            accountCompletion = result => {
                try { PresentationChanged?.Invoke(new BlockmakerUnityWalletProgress {
                    phase = result.Success ? "authenticated" : result.Code == "PLAYER_CANCELLED" ? "cancelled" : "error",
                    code = result.Code, walletAddress = result.WalletAddress,
                    providerId = result.AuthProvider,
                }); } catch { /* A presentation callback cannot prevent session acknowledgment. */ }
                done?.Invoke(result);
            };
            accountPending = true;
            accountHandoffInstalled = false;
            accountRejecting = false;
            accountOpenBaseline = baseline;
            acceptedHandoff = null;
            BlockmakerWalletPackageWebGL_OpenAccount(
                gameObject.name, lifecycleId, operationId, UseUnityPresentation ? 1 : 0, providerId);
#else
            throw new PlatformNotSupportedException(NativeUnavailableCode);
#endif
        }

        /// <summary>
        /// Copy and prepare one exact Algorand transaction group that game code
        /// has already reviewed with BlockmakerAlgorandTransactionVerifier.
        /// This method opens no wallet UI and never submits transactions.
        /// </summary>
        public void PrepareUniversalUsername(string exactPreparedJson,
            Action<BlockmakerWalletPackageWebGLResult> done)
        {
            if (string.IsNullOrEmpty(exactPreparedJson) || exactPreparedJson.Length > 131072)
                throw new ArgumentException("The exact username plan is required.", nameof(exactPreparedJson));
            var value = JsonUtility.FromJson<BlockmakerUnityUsernameGroup>(exactPreparedJson);
            if (value == null || value.unsignedTxnsBase64 == null || value.unsignedTxnsBase64.Length != 2)
                throw new ArgumentException("The exact two-transaction username plan is required.");
            PrepareTransactionGroupInternal(value.unsignedTxnsBase64, exactPreparedJson, done);
        }

        public void PrepareTransactionGroup(
            string[] unsignedTransactionsBase64,
            Action<BlockmakerWalletPackageWebGLResult> done)
        {
            PrepareTransactionGroupInternal(unsignedTransactionsBase64, null, done);
        }

        private void PrepareTransactionGroupInternal(string[] unsignedTransactionsBase64,
            string usernameJson, Action<BlockmakerWalletPackageWebGLResult> done)
        {
            EnsureUsable();
            if (!IsReady)
                throw new InvalidOperationException(
                    "PACKAGE_NOT_READY: Wait for wallet-package readiness before preparing a transaction group.");
            if (HasActiveOperation())
                throw new InvalidOperationException(
                    "REQUEST_ALREADY_PENDING: Another wallet-package operation is already active.");
            var snapshot = SnapshotClientSession();
            if (snapshot == null || !ValidSession(snapshot))
                throw new InvalidOperationException(
                    "SESSION_EXPIRED: Sign in with a supported Algorand wallet before preparing transactions.");
            var copiedGroup = CopyCanonicalTransactionGroup(
                unsignedTransactionsBase64, MaximumTransactionBytes,
                "unsignedTransactionsBase64");
            var requestJson = JsonUtility.ToJson(
                new BlockmakerWalletPackageWebGLTransactionGroupPayload {
                    unsignedTransactionsBase64 = copiedGroup,
                });
            if (!BoundedTransactionJson(requestJson))
                throw new ArgumentException(
                    "The transaction group exceeds the wallet-package boundary.",
                    nameof(unsignedTransactionsBase64));
#if UNITY_WEBGL && !UNITY_EDITOR
            operationId = NextOperationId(operationId);
            transactionSessionSnapshot = snapshot;
            preparedUnsignedTransactionsBase64 = copiedGroup;
            transactionPreparationCompletion = done;
            transactionSigningCompletion = null;
            transactionPreparationPending = true;
            transactionPrepared = false;
            transactionSigning = false;
            BlockmakerWalletPackageWebGL_PrepareTransactionGroup(
                gameObject.name, lifecycleId, operationId,
                snapshot.WalletAddress, snapshot.AccountKind,
                snapshot.AuthProvider, requestJson, usernameJson);
#else
            throw new PlatformNotSupportedException(NativeUnavailableCode);
#endif
        }

        /// <summary>
        /// Launch signing for the previously prepared exact group. Invoke this
        /// method directly from the final Unity click/tap handler so Lute keeps
        /// the browser's popup activation. Returned bytes are copied base64 and
        /// are never broadcast by this adapter.
        /// </summary>
        public void SignPreparedTransactionGroup(
            Action<BlockmakerWalletPackageWebGLResult> done)
        {
            EnsureUsable();
            if (!IsReady)
                throw new InvalidOperationException(
                    "PACKAGE_NOT_READY: Wait for wallet-package readiness before signing.");
            if (!transactionPrepared || transactionPreparationPending
                || transactionSigning || preparedUnsignedTransactionsBase64 == null)
                throw new InvalidOperationException(
                    "TRANSACTION_GROUP_INVALID: Prepare one exact transaction group before signing it.");
            if (!SessionMatches(transactionSessionSnapshot)
                || !ValidSession(transactionSessionSnapshot)) {
                AbortTransaction("SESSION_CONFLICT", done);
                return;
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            transactionSigning = true;
            transactionSigningCompletion = done;
            var accepted = BlockmakerWalletPackageWebGL_SignPreparedTransactionGroup(
                gameObject.name, lifecycleId, operationId);
            // The reviewed .jslib invokes the facade synchronously and reports
            // errors synchronously when launch is impossible. This guard keeps
            // a missing/mismatched export from stranding the prepared group.
            if (accepted != 1 && transactionSigning)
                AbortTransaction("PROVIDER_UNAVAILABLE",
                    transactionSigningCompletion);
#else
            throw new PlatformNotSupportedException(NativeUnavailableCode);
#endif
        }

        public void Cancel()
        {
            if (!accountPending && !TransactionActive()) return;
            if (TransactionActive()) {
                var transactionDone = transactionPreparationPending
                    ? transactionPreparationCompletion
                    : transactionSigning ? transactionSigningCompletion : null;
#if UNITY_WEBGL && !UNITY_EDITOR
                BlockmakerWalletPackageWebGL_Cancel(lifecycleId, operationId);
#endif
                ClearTransactionState();
                operationId = NextOperationId(operationId);
                transactionDone?.Invoke(BlockmakerWalletPackageWebGLResult.Failed(
                    "PLAYER_CANCELLED"));
                return;
            }
            var cancelledOperation = operationId;
            var done = accountCompletion;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (accountHandoffInstalled) {
                ClearAcceptedHandoffIfOwned();
                accountHandoffInstalled = false;
                accountRejecting = true;
                accountOpenBaseline = null;
                BlockmakerWalletPackageWebGL_Reject(
                    gameObject.name, lifecycleId, cancelledOperation,
                    "PLAYER_CANCELLED");
                return;
            } else {
                BlockmakerWalletPackageWebGL_Cancel(
                    lifecycleId, cancelledOperation);
            }
#endif
            accountPending = false;
            accountCompletion = null;
            accountHandoffInstalled = false;
            accountRejecting = false;
            accountOpenBaseline = null;
            acceptedHandoff = null;
            operationId = NextOperationId(operationId);
            done?.Invoke(BlockmakerWalletPackageWebGLResult.Failed(
                "PLAYER_CANCELLED"));
        }

        public void Logout(Action<BlockmakerWalletPackageWebGLResult> done = null)
        {
            EnsureUsable();
            if (!IsReady)
                throw new InvalidOperationException(
                    "PACKAGE_NOT_READY: Wait for wallet-package readiness before signing out.");
            // Explicit sign-out also cancels an incomplete account attempt.
            // Economic requests retain their own cancellation/recovery boundary.
            if (accountPending && !accountHandoffInstalled && !accountRejecting) Cancel();
            if (HasActiveOperation())
                throw new InvalidOperationException(
                    "REQUEST_ALREADY_PENDING: Close the active wallet-package window before signing out.");
            var snapshot = SnapshotClientSession();
            if (snapshot != null && !ValidSession(snapshot))
                throw new InvalidOperationException(
                    "SESSION_CONFLICT: The current player session is not owned by this wallet package.");
#if UNITY_WEBGL && !UNITY_EDITOR
            operationId = NextOperationId(operationId);
            logoutSnapshot = snapshot;
            logoutCompletion = done;
            logoutPending = true;
            BlockmakerWalletPackageWebGL_Logout(
                gameObject.name, lifecycleId, operationId,
                snapshot?.SessionToken ?? "", snapshot?.RefreshToken ?? "",
                snapshot?.WalletAddress ?? "", snapshot?.AccountKind ?? "",
                snapshot?.AuthProvider ?? "");
#else
            throw new PlatformNotSupportedException(NativeUnavailableCode);
#endif
        }

        public void OpenFundingGuide(
            Action<BlockmakerWalletPackageWebGLResult> done = null)
        {
            EnsureUsable();
            if (!IsReady)
                throw new InvalidOperationException(
                    "PACKAGE_NOT_READY: Wait for wallet-package readiness before opening funding help.");
            if (HasActiveOperation())
                throw new InvalidOperationException(
                    "REQUEST_ALREADY_PENDING: Another wallet-package window is already active.");
            var snapshot = SnapshotClientSession();
            if (snapshot == null || !ValidSession(snapshot))
                throw new InvalidOperationException(
                    "SESSION_EXPIRED: Sign in with an enabled wallet before opening funding help.");
#if UNITY_WEBGL && !UNITY_EDITOR
            operationId = NextOperationId(operationId);
            fundingOpening = true;
            fundingOpenCompletion = done;
            // Only the short-lived access token and public session classifiers
            // cross back to the browser. Refresh credentials and wallet
            // addresses are deliberately absent from this ABI.
            BlockmakerWalletPackageWebGL_OpenFunding(
                gameObject.name, lifecycleId, operationId,
                snapshot.SessionToken, snapshot.AccountKind,
                snapshot.AuthProvider);
#else
            throw new PlatformNotSupportedException(NativeUnavailableCode);
#endif
        }

        public void CloseFundingGuide(
            Action<BlockmakerWalletPackageWebGLResult> done = null)
        {
            if (!fundingOpening && !fundingOpen) {
                done?.Invoke(BlockmakerWalletPackageWebGLResult.Completed());
                return;
            }
            if (fundingClosing)
                throw new InvalidOperationException(
                    "REQUEST_ALREADY_PENDING: Wallet funding help is already closing.");
#if UNITY_WEBGL && !UNITY_EDITOR
            fundingClosing = true;
            fundingCloseCompletion = done;
            BlockmakerWalletPackageWebGL_CloseFunding(
                gameObject.name, lifecycleId, operationId);
#else
            throw new PlatformNotSupportedException(NativeUnavailableCode);
#endif
        }

        /// <summary>
        /// Read a sanitized diagnostic snapshot without taking wallet ownership,
        /// retrying capture, or changing readiness/session/cleanup state.
        /// A failed capture needs its cause fixed and a fresh page lifecycle.
        /// </summary>
        public BlockmakerWalletPackageEvidenceStatus GetDeploymentEvidenceStatus()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!destroyed && client != null)
                return BlockmakerWalletPackageEvidenceStatus.FromBridgeCode(
                    BlockmakerWalletPackageWebGL_GetDeploymentEvidenceStatus(
                        gameObject.name, lifecycleId));
#endif
            return BlockmakerWalletPackageEvidenceStatus.FromBridgeCode(10);
        }

        /// <summary>
        /// Download the canonical deployment-evidence receipt captured during
        /// the exact initialize/ready bridge roundtrip. Call this directly from
        /// a player or operator click so browser download activation is kept.
        /// </summary>
        public void DownloadDeploymentEvidence(
            Action<BlockmakerWalletPackageWebGLResult> done = null)
        {
            EnsureUsable();
            if (!IsReady)
                throw new InvalidOperationException(
                    "PACKAGE_NOT_READY: Wait for wallet-package readiness before downloading deployment evidence.");
            if (HasActiveOperation())
                throw new InvalidOperationException(
                    "REQUEST_ALREADY_PENDING: Close the active wallet-package operation before downloading deployment evidence.");
#if UNITY_WEBGL && !UNITY_EDITOR
            operationId = NextOperationId(operationId);
            deploymentEvidencePending = true;
            deploymentEvidenceCompletion = done;
            var accepted = BlockmakerWalletPackageWebGL_DownloadDeploymentEvidence(
                gameObject.name, lifecycleId, operationId);
            // The reviewed .jslib completes synchronously. Retain this guard so
            // a missing/mismatched export cannot strand game code indefinitely.
            if (accepted != 1 && deploymentEvidencePending)
                FinishDeploymentEvidenceDownload(
                    false, "DEPLOYMENT_EVIDENCE_UNAVAILABLE");
#else
            throw new PlatformNotSupportedException(NativeUnavailableCode);
#endif
        }

        // Called only by the reviewed .jslib through Unity SendMessage.
        public void OnBlockmakerWalletPackageSuccess(string json)
        {
            if (destroyed) return;
            if (!BoundedTransactionJson(json)) { FailCurrent("PROVIDER_UNAVAILABLE"); return; }
            BlockmakerWalletPackageWebGLPayload value;
            try { value = JsonUtility.FromJson<BlockmakerWalletPackageWebGLPayload>(json); }
            catch { FailCurrent("PROVIDER_UNAVAILABLE"); return; }
            if (value == null) { FailCurrent("PROVIDER_UNAVAILABLE"); return; }
            if (value.operationKind != "sign_transaction_group"
                && !BoundedJson(json)) {
                FailCurrent("PROVIDER_UNAVAILABLE");
                return;
            }
            if (value.lifecycleId != lifecycleId) return;
            if (value.schemaVersion != EnvelopeSchema) {
                FailCurrent("PROVIDER_UNAVAILABLE");
                return;
            }

            if (value.operationKind == "initialize" && value.operationId == 0
                && value.phase == "ready" && initializePending) {
                var done = initializeCompletion;
                if (value.runtimeProfile != RuntimeProfile) {
                    initializePending = false;
                    initializeCompletion = null;
                    IsReady = false;
                    ReloadRequired = true;
                    done?.Invoke(BlockmakerWalletPackageWebGLResult.Failed(
                        "PACKAGE_LOAD_FAILED"));
                    return;
                }
#if UNITY_WEBGL && !UNITY_EDITOR
                // This synchronous acknowledgement is the proof that the exact
                // v2 profile-bound ready envelope crossed .jslib into this C#
                // receiver. The
                // browser host may capture evidence only after it accepts this
                // lifecycle-bound acknowledgement.
                var acknowledged = BlockmakerWalletPackageWebGL_AcknowledgeReady(
                    gameObject.name, lifecycleId, 0);
                if (acknowledged != 1) {
                    initializePending = false;
                    initializeCompletion = null;
                    IsReady = false;
                    ReloadRequired = true;
                    done?.Invoke(BlockmakerWalletPackageWebGLResult.Failed(
                        "DEPLOYMENT_EVIDENCE_UNAVAILABLE"));
                    return;
                }
#endif
                initializePending = false;
                initializeCompletion = null;
                IsReady = true;
                done?.Invoke(BlockmakerWalletPackageWebGLResult.Completed());
                return;
            }

            if (value.operationId != operationId) {
#if UNITY_WEBGL && !UNITY_EDITOR
                if (value.operationKind == "open_account"
                    && value.phase == "handoff" && value.operationId > 0)
                    BlockmakerWalletPackageWebGL_Reject(
                        gameObject.name, lifecycleId, value.operationId,
                        "PLAYER_CANCELLED");
#endif
                return;
            }

            if (value.operationKind == "open_account"
                && value.phase == "handoff" && accountPending) {
                AcceptAccountHandoff(value);
                return;
            }
            if (value.operationKind == "sign_transaction_group"
                && value.phase == "prepared" && transactionPreparationPending) {
                if (!SessionMatches(transactionSessionSnapshot)
                    || !ValidSession(transactionSessionSnapshot)) {
                    AbortTransaction("SESSION_CONFLICT",
                        transactionPreparationCompletion);
                    return;
                }
                var done = transactionPreparationCompletion;
                transactionPreparationCompletion = null;
                transactionPreparationPending = false;
                transactionPrepared = true;
                done?.Invoke(BlockmakerWalletPackageWebGLResult.Completed(
                    transactionSessionSnapshot.WalletAddress,
                    transactionSessionSnapshot.AccountKind,
                    transactionSessionSnapshot.AuthProvider));
                return;
            }
            if (value.operationKind == "sign_transaction_group"
                && value.phase == "signed" && transactionSigning) {
                if (!SessionMatches(transactionSessionSnapshot)
                    || !ValidSession(transactionSessionSnapshot)) {
                    FinishTransactionFailure("SESSION_CONFLICT");
                    return;
                }
                string[] copiedSigned;
                try {
                    copiedSigned = CopyCanonicalTransactionGroup(
                        value.signedTransactionsBase64,
                        MaximumSignedTransactionBytes,
                        "signedTransactionsBase64");
                } catch {
                    FinishTransactionFailure("WALLET_SIGNATURE_INVALID");
                    return;
                }
                if (preparedUnsignedTransactionsBase64 == null
                    || copiedSigned.Length
                        != preparedUnsignedTransactionsBase64.Length) {
                    FinishTransactionFailure("WALLET_SIGNATURE_INVALID");
                    return;
                }
                FinishTransactionSuccess(copiedSigned);
                return;
            }
            if (value.operationKind == "funding"
                && value.phase == "opened" && fundingOpening
                && !fundingClosing) {
                var done = fundingOpenCompletion;
                fundingOpening = false;
                fundingOpen = true;
                fundingOpenCompletion = null;
                done?.Invoke(BlockmakerWalletPackageWebGLResult.Completed());
                return;
            }
            if (value.operationKind == "deployment_evidence"
                && value.phase == "downloaded"
                && deploymentEvidencePending) {
                FinishDeploymentEvidenceDownload(true, null);
                return;
            }
            FailCurrent("PROVIDER_UNAVAILABLE");
        }

        public void OnBlockmakerWalletPackageError(string json)
        {
            if (destroyed) return;
            if (!BoundedJson(json)) { FailCurrent("PROVIDER_UNAVAILABLE"); return; }
            BlockmakerWalletPackageWebGLErrorPayload value;
            try { value = JsonUtility.FromJson<BlockmakerWalletPackageWebGLErrorPayload>(json); }
            catch { FailCurrent("PROVIDER_UNAVAILABLE"); return; }
            if (value == null) { FailCurrent("PROVIDER_UNAVAILABLE"); return; }
            if (value.lifecycleId != lifecycleId) return;
            if (value.schemaVersion != EnvelopeSchema) {
                FailCurrent("PROVIDER_UNAVAILABLE");
                return;
            }
            if (value.operationKind == "initialize" && value.operationId == 0
                && initializePending) {
                var done = initializeCompletion;
                initializePending = false;
                initializeCompletion = null;
                IsReady = false;
                if (value.code == "PROVIDER_CLEANUP_REQUIRED") ReloadRequired = true;
                done?.Invoke(BlockmakerWalletPackageWebGLResult.Failed(value.code));
                return;
            }
            if (value.operationId != operationId) return;
            if (value.code == "PROVIDER_CLEANUP_REQUIRED") ReloadRequired = true;
            if (value.operationKind == "open_account" && accountPending) {
                var done = accountCompletion;
                ClearAcceptedHandoffIfOwned();
                accountPending = false;
                accountHandoffInstalled = false;
                accountRejecting = false;
                accountOpenBaseline = null;
                accountCompletion = null;
                acceptedHandoff = null;
                done?.Invoke(BlockmakerWalletPackageWebGLResult.Failed(value.code));
                return;
            }
            if (value.operationKind == "sign_transaction_group"
                && TransactionActive()) {
                FinishTransactionFailure(value.code);
                return;
            }
            if (value.operationKind == "funding" && fundingOpening) {
                var done = fundingOpenCompletion;
                fundingOpening = false;
                fundingOpen = false;
                fundingOpenCompletion = null;
                done?.Invoke(BlockmakerWalletPackageWebGLResult.Failed(value.code));
                return;
            }
            if (value.operationKind == "deployment_evidence"
                && deploymentEvidencePending) {
                FinishDeploymentEvidenceDownload(false, value.code);
            }
        }

        public void OnBlockmakerWalletPackageLogout(string json)
        {
            if (destroyed || !logoutPending || !BoundedJson(json)) return;
            BlockmakerWalletPackageWebGLConfirmationPayload value;
            try { value = JsonUtility.FromJson<BlockmakerWalletPackageWebGLConfirmationPayload>(json); }
            catch { FinishLogout(false); return; }
            if (value == null || value.schemaVersion != EnvelopeSchema
                || value.lifecycleId != lifecycleId
                || value.operationId != operationId
                || value.operationKind != "logout") return;
            FinishLogout(value.confirmed);
        }

        public void OnBlockmakerWalletPackageFundingClosed(string json)
        {
            if (destroyed || !fundingClosing || !BoundedJson(json)) return;
            BlockmakerWalletPackageWebGLConfirmationPayload value;
            try { value = JsonUtility.FromJson<BlockmakerWalletPackageWebGLConfirmationPayload>(json); }
            catch { FinishFundingClose(false); return; }
            if (value == null || value.schemaVersion != EnvelopeSchema
                || value.lifecycleId != lifecycleId
                || value.operationId != operationId
                || value.operationKind != "funding") return;
            FinishFundingClose(value.success);
        }

        private void AcceptAccountHandoff(BlockmakerWalletPackageWebGLPayload value)
        {
            var snapshot = new BlockmakerWalletPackageWebGLSessionSnapshot {
                SessionToken = value.sessionToken,
                RefreshToken = value.refreshToken,
                WalletAddress = value.walletAddress,
                AccountKind = value.accountKind,
                AuthProvider = value.authProvider,
            };
            if (!ValidSession(snapshot)) {
                RejectCurrentAccount("PROVIDER_UNAVAILABLE");
                return;
            }
            // OpenAccount starts only from a fully empty five-field session.
            // Recheck that exact snapshot immediately before installation so a
            // refresh, restore, or public SetPlayerSession call cannot be
            // overwritten while the browser wallet UI is in flight.
            if (!SessionMatches(accountOpenBaseline)) {
                RejectCurrentAccount("SESSION_CONFLICT");
                return;
            }
            try {
                client.SetPlayerSession(
                    snapshot.SessionToken, snapshot.RefreshToken,
                    snapshot.WalletAddress, snapshot.AccountKind,
                    snapshot.AuthProvider);
            } catch {
                RejectCurrentAccount("PROVIDER_UNAVAILABLE");
                return;
            }
            acceptedHandoff = snapshot;
            accountHandoffInstalled = true;
            accountRejecting = false;
#if UNITY_WEBGL && !UNITY_EDITOR
            // The browser retains the exact opaque handoff until Unity has
            // installed the ordinary player session successfully. A
            // synchronous integer result avoids a second SendMessage whose
            // loss could otherwise split ownership between C# and JavaScript.
            var acknowledged = BlockmakerWalletPackageWebGL_Acknowledge(
                gameObject.name, lifecycleId, operationId);
            if (acknowledged != 1) {
                var failed = accountCompletion;
                ClearAcceptedHandoffIfOwned();
                accountPending = false;
                accountHandoffInstalled = false;
                accountRejecting = false;
                accountOpenBaseline = null;
                accountCompletion = null;
                acceptedHandoff = null;
                ReloadRequired = true;
                failed?.Invoke(BlockmakerWalletPackageWebGLResult.Failed(
                    "PROVIDER_CLEANUP_REQUIRED"));
                return;
            }
#endif
            var done = accountCompletion;
            var completed = acceptedHandoff;
            acknowledgedSession = completed;
            accountPending = false;
            accountHandoffInstalled = false;
            accountRejecting = false;
            accountOpenBaseline = null;
            accountCompletion = null;
            acceptedHandoff = null;
            done?.Invoke(BlockmakerWalletPackageWebGLResult.Completed(
                completed.WalletAddress, completed.AccountKind,
                completed.AuthProvider));
        }

        private void RejectCurrentAccount(string code)
        {
            ClearAcceptedHandoffIfOwned();
            accountHandoffInstalled = false;
            accountRejecting = true;
            accountOpenBaseline = null;
            acceptedHandoff = null;
#if UNITY_WEBGL && !UNITY_EDITOR
            BlockmakerWalletPackageWebGL_Reject(
                gameObject.name, lifecycleId, operationId, code);
#endif
        }

        private void FinishLogout(bool confirmed)
        {
            var done = logoutCompletion;
            var snapshot = logoutSnapshot;
            logoutPending = false;
            logoutCompletion = null;
            logoutSnapshot = null;
            if (confirmed) {
                // Do not erase a newer session if game code changed the shared
                // client while exact-family revocation was in flight.
                if (SessionMatches(snapshot)) client.ClearPlayerSession();
                done?.Invoke(BlockmakerWalletPackageWebGLResult.Completed());
            } else {
                ReloadRequired = true;
                done?.Invoke(BlockmakerWalletPackageWebGLResult.Failed(
                    "LOGOUT_UNCONFIRMED"));
            }
        }

        private void FinishFundingClose(bool success)
        {
            var done = fundingCloseCompletion;
            fundingClosing = false;
            fundingCloseCompletion = null;
            if (success) {
                fundingOpening = false;
                fundingOpen = false;
                fundingOpenCompletion = null;
                done?.Invoke(BlockmakerWalletPackageWebGLResult.Completed());
            } else {
                ReloadRequired = true;
                done?.Invoke(BlockmakerWalletPackageWebGLResult.Failed(
                    "PROVIDER_CLEANUP_REQUIRED"));
            }
        }

        private void FinishDeploymentEvidenceDownload(bool success, string code)
        {
            var done = deploymentEvidenceCompletion;
            deploymentEvidencePending = false;
            deploymentEvidenceCompletion = null;
            done?.Invoke(success
                ? BlockmakerWalletPackageWebGLResult.Completed()
                : BlockmakerWalletPackageWebGLResult.Failed(code));
        }

        private void AbortTransaction(
            string code,
            Action<BlockmakerWalletPackageWebGLResult> done)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            BlockmakerWalletPackageWebGL_Cancel(lifecycleId, operationId);
#endif
            ClearTransactionState();
            operationId = NextOperationId(operationId);
            done?.Invoke(BlockmakerWalletPackageWebGLResult.Failed(code));
        }

        private void FinishTransactionFailure(string code)
        {
            var done = transactionSigning
                ? transactionSigningCompletion
                : transactionPreparationCompletion;
            ClearTransactionState();
            done?.Invoke(BlockmakerWalletPackageWebGLResult.Failed(code));
        }

        private void FinishTransactionSuccess(string[] signedTransactionsBase64)
        {
            var done = transactionSigningCompletion;
            var snapshot = transactionSessionSnapshot;
            ClearTransactionState();
            done?.Invoke(BlockmakerWalletPackageWebGLResult.Completed(
                snapshot == null ? null : snapshot.WalletAddress,
                snapshot == null ? null : snapshot.AccountKind,
                snapshot == null ? null : snapshot.AuthProvider,
                signedTransactionsBase64));
        }

        private void ClearTransactionState()
        {
            transactionPreparationPending = false;
            transactionPrepared = false;
            transactionSigning = false;
            transactionPreparationCompletion = null;
            transactionSigningCompletion = null;
            transactionSessionSnapshot = null;
            preparedUnsignedTransactionsBase64 = null;
        }

        private void FailCurrent(string code)
        {
            if (deploymentEvidencePending) {
                FinishDeploymentEvidenceDownload(false, code);
                return;
            }
            if (accountPending) {
                RejectCurrentAccount(code);
                return;
            }
            if (TransactionActive()) {
                var done = transactionSigning
                    ? transactionSigningCompletion
                    : transactionPreparationCompletion;
                AbortTransaction(code, done);
                return;
            }
            if (fundingOpening) {
                var done = fundingOpenCompletion;
                fundingOpenCompletion = null;
#if UNITY_WEBGL && !UNITY_EDITOR
                if (!fundingClosing) {
                    fundingClosing = true;
                    BlockmakerWalletPackageWebGL_CloseFunding(
                        gameObject.name, lifecycleId, operationId);
                }
#endif
                done?.Invoke(BlockmakerWalletPackageWebGLResult.Failed(code));
                return;
            }
            if (initializePending) {
                var done = initializeCompletion;
                initializePending = false;
                initializeCompletion = null;
                done?.Invoke(BlockmakerWalletPackageWebGLResult.Failed(code));
            }
        }

        private void OnDestroy()
        {
            if (destroyed) return;
            destroyed = true;
            if (client != null) client.PlayerSessionRefreshed -= AcknowledgedSessionRefreshed;
            // Scene teardown owns active package UI only. It never signs out or
            // clears an already acknowledged Blockmaker player session.
            if (accountPending) {
                var activeOperation = operationId;
#if UNITY_WEBGL && !UNITY_EDITOR
                if (accountHandoffInstalled)
                    BlockmakerWalletPackageWebGL_Reject(
                        gameObject.name, lifecycleId, activeOperation,
                        "PLAYER_CANCELLED");
                else
                    BlockmakerWalletPackageWebGL_Cancel(
                        lifecycleId, activeOperation);
#endif
                if (accountHandoffInstalled) ClearAcceptedHandoffIfOwned();
            }
            if (TransactionActive()) {
#if UNITY_WEBGL && !UNITY_EDITOR
                BlockmakerWalletPackageWebGL_Cancel(
                    lifecycleId, operationId);
#endif
            }
            if (fundingOpening || fundingOpen) {
#if UNITY_WEBGL && !UNITY_EDITOR
                BlockmakerWalletPackageWebGL_CloseFunding(
                    gameObject.name, lifecycleId, operationId);
#endif
            }
            initializeCompletion = null;
            accountCompletion = null;
            logoutCompletion = null;
            fundingOpenCompletion = null;
            fundingCloseCompletion = null;
            deploymentEvidenceCompletion = null;
            transactionPreparationCompletion = null;
            transactionSigningCompletion = null;
            accountPending = false;
            accountRejecting = false;
            accountOpenBaseline = null;
            logoutPending = false;
            fundingOpening = false;
            fundingOpen = false;
            fundingClosing = false;
            deploymentEvidencePending = false;
            transactionPreparationPending = false;
            transactionPrepared = false;
            transactionSigning = false;
            acceptedHandoff = null;
            logoutSnapshot = null;
            transactionSessionSnapshot = null;
            preparedUnsignedTransactionsBase64 = null;
        }

        private void EnsureUsable()
        {
            if (client == null)
                throw new InvalidOperationException(
                    "Initialize the Unity WebGL wallet package first.");
            if (destroyed)
                throw new InvalidOperationException(
                    "The Unity WebGL wallet-package receiver was destroyed.");
#if !(UNITY_WEBGL && !UNITY_EDITOR)
            throw new PlatformNotSupportedException(
                NativeUnavailableCode + ": The unified wallet package supports web and Unity WebGL only.");
#endif
        }

        private bool HasActiveOperation()
        {
            return accountPending || logoutPending || fundingOpening
                || fundingOpen || fundingClosing || deploymentEvidencePending
                || TransactionActive();
        }

        private bool TransactionActive()
        {
            return transactionPreparationPending || transactionPrepared
                || transactionSigning;
        }

        private BlockmakerWalletPackageWebGLSessionSnapshot SnapshotClientSession()
        {
            var value = SnapshotClientSessionState();
            return EmptySession(value) ? null : value;
        }

        private BlockmakerWalletPackageWebGLSessionSnapshot SnapshotClientSessionState()
        {
            return SnapshotClientSessionState(client);
        }

        private static BlockmakerWalletPackageWebGLSessionSnapshot SnapshotClientSessionState(
            BlockmakerClient value)
        {
            if (value == null) return null;
            return new BlockmakerWalletPackageWebGLSessionSnapshot {
                SessionToken = value.SessionToken,
                RefreshToken = value.RefreshToken,
                WalletAddress = value.WalletAddress,
                AccountKind = value.AccountKind,
                AuthProvider = value.AuthProvider,
            };
        }

        private static bool EmptySession(
            BlockmakerWalletPackageWebGLSessionSnapshot value)
        {
            return value != null
                && string.IsNullOrEmpty(value.SessionToken)
                && string.IsNullOrEmpty(value.RefreshToken)
                && string.IsNullOrEmpty(value.WalletAddress)
                && string.IsNullOrEmpty(value.AccountKind)
                && string.IsNullOrEmpty(value.AuthProvider);
        }

        private bool SessionMatches(BlockmakerWalletPackageWebGLSessionSnapshot value)
        {
            return value != null && client != null
                && client.SessionToken == value.SessionToken
                && client.RefreshToken == value.RefreshToken
                && client.WalletAddress == value.WalletAddress
                && client.AccountKind == value.AccountKind
                && client.AuthProvider == value.AuthProvider;
        }

        private void ClearAcceptedHandoffIfOwned()
        {
            if (SessionMatches(acceptedHandoff)) client.ClearPlayerSession();
        }

        private bool ValidSession(BlockmakerWalletPackageWebGLSessionSnapshot value)
        {
            return value != null
                && SafeToken(value.SessionToken, 8192)
                && SafeToken(value.RefreshToken, 1024)
                && AlgorandAddress(value.WalletAddress)
                && SupportedPair(value.AccountKind, value.AuthProvider);
        }

        private static bool ValidSessionForRuntimeProfile(
            BlockmakerWalletPackageWebGLSessionSnapshot value,
            string runtimeProfile)
        {
            return value != null
                && SafeToken(value.SessionToken, 8192)
                && SafeToken(value.RefreshToken, 1024)
                && AlgorandAddress(value.WalletAddress)
                && SupportedPairForRuntimeProfile(
                    value.AccountKind, value.AuthProvider, runtimeProfile);
        }

        private bool SupportedPair(string accountKind, string authProvider)
        {
            return SupportedPairForRuntimeProfile(
                accountKind, authProvider, RuntimeProfile);
        }

        private static bool SupportedPairForRuntimeProfile(
            string accountKind,
            string authProvider,
            string runtimeProfile)
        {
            if (accountKind != "algorand_wallet") return false;
            if (authProvider == BlockmakerWalletProviders.Pera
                || authProvider == BlockmakerWalletProviders.Lute) {
                return runtimeProfile == PeraLuteRuntimeProfile
                    || runtimeProfile == PeraLuteWeb3AuthRuntimeProfile;
            }
            return authProvider == TxnLabWeb3AuthProvider
                && runtimeProfile == PeraLuteWeb3AuthRuntimeProfile;
        }

        private static int NextOperationId(int current)
        {
            return current == Int32.MaxValue ? 1 : current + 1;
        }

        private static bool BoundedJson(string value)
        {
            return !string.IsNullOrEmpty(value)
                && value.Length <= MaximumPayloadCharacters;
        }

        private static bool BoundedTransactionJson(string value)
        {
            return !string.IsNullOrEmpty(value)
                && value.Length <= MaximumTransactionPayloadCharacters;
        }

        private static string[] CopyCanonicalTransactionGroup(
            string[] values,
            int maximumBytes,
            string parameterName)
        {
            if (values == null || values.Length < 1
                || values.Length > MaximumTransactionCount)
                throw new ArgumentException(
                    "Use one complete Algorand transaction group containing 1 to 16 transactions.",
                    parameterName);
            var copied = new string[values.Length];
            for (var index = 0; index < values.Length; index++) {
                var value = values[index];
                if (string.IsNullOrEmpty(value)
                    || value.Length > ((maximumBytes + 2) / 3) * 4
                    || value != value.Trim())
                    throw new ArgumentException(
                        "Every transaction must be bounded canonical base64.",
                        parameterName);
                byte[] bytes;
                try { bytes = Convert.FromBase64String(value); }
                catch {
                    throw new ArgumentException(
                        "Every transaction must be bounded canonical base64.",
                        parameterName);
                }
                if (bytes.Length == 0 || bytes.Length > maximumBytes
                    || Convert.ToBase64String(bytes) != value)
                    throw new ArgumentException(
                        "Every transaction must be bounded canonical base64.",
                        parameterName);
                copied[index] = value;
            }
            return copied;
        }

        private static bool SafeToken(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maximumLength
                || value.StartsWith("sk_", StringComparison.OrdinalIgnoreCase)) return false;
            foreach (char character in value)
                if (character < 0x21 || character > 0x7e) return false;
            return true;
        }

        private static bool AlgorandAddress(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 58) return false;
            foreach (char character in value)
                if (!((character >= 'A' && character <= 'Z')
                    || (character >= '2' && character <= '7'))) return false;
            return true;
        }

        private static bool SafePublicGameId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 128
                || value.StartsWith("sk_", StringComparison.OrdinalIgnoreCase)) return false;
            foreach (char character in value)
                if (!((character >= 'a' && character <= 'z')
                    || (character >= 'A' && character <= 'Z')
                    || (character >= '0' && character <= '9')
                    || character == '-' || character == '_')) return false;
            return true;
        }

        private static string RuntimeProfileForClientId(string value)
        {
            if (string.IsNullOrEmpty(value)) return PeraLuteRuntimeProfile;
            return SafePublicClientId(value)
                ? PeraLuteWeb3AuthRuntimeProfile
                : null;
        }

        private static bool SafePublicClientId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 16
                || value.Length > 512 || value != value.Trim()
                || value.StartsWith("sk_", StringComparison.OrdinalIgnoreCase))
                return false;
            foreach (char character in value)
                if (character < 0x21 || character > 0x7e
                    || Char.IsWhiteSpace(character)) return false;
            return true;
        }

        private static bool SafeAppName(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 80
                || value != value.Trim()) return false;
            foreach (char character in value)
                if (character < 0x20 || character == 0x7f) return false;
            return true;
        }

        private static bool SafeBaseUrl(string value)
        {
            Uri parsed;
            if (string.IsNullOrEmpty(value) || value.Length > 2048
                || !Uri.TryCreate(value, UriKind.Absolute, out parsed)
                || !string.IsNullOrEmpty(parsed.UserInfo)
                || (parsed.AbsolutePath != "/" && parsed.AbsolutePath != "")
                || !string.IsNullOrEmpty(parsed.Query)
                || !string.IsNullOrEmpty(parsed.Fragment)) return false;
            var loopback = parsed.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || parsed.Host == "127.0.0.1" || parsed.Host == "[::1]"
                || parsed.Host == "::1";
            return parsed.Scheme == Uri.UriSchemeHttps
                || (parsed.Scheme == Uri.UriSchemeHttp && loopback);
        }
    }
}
