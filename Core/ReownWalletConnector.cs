using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting;
using Reown.Sign;
using Reown.Sign.Unity;
using Reown.Sign.Models;
using Reown.Sign.Models.Engine;
using Reown.Sign.Models.Engine.Events;
using Reown.Core;
using Reown.Core.Models;
using Reown.Core.Models.Relay;
using Reown.Core.Network.Models;

namespace Blockmaker
{

    [Preserve]
    public class ReownWalletConnector : MonoBehaviour
    {
        public static ReownWalletConnector Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            OnQRReady = null;
            OnConnected = null;
            OnConnectionError = null;
            OnTransactionSigned = null;
            OnSignError = null;
        }

        /// <summary>
        /// Internal event — game code should subscribe to BlockmakerAuth events instead.
        /// <para><b>Warning:</b> Static event. Unsubscribe in OnDestroy() to avoid scene-load leaks.</para>
        /// </summary>
        internal static event Action<string, string, Texture2D> OnQRReady;
        /// <inheritdoc cref="OnQRReady"/>
        internal static event Action<string, string> OnConnected;
        /// <inheritdoc cref="OnQRReady"/>
        internal static event Action<string> OnConnectionError;
        /// <inheritdoc cref="OnQRReady"/>
        internal static event Action<string> OnTransactionSigned;
        /// <inheritdoc cref="OnQRReady"/>
        internal static event Action<string> OnSignError;

        internal static void FireQRReadyForBridge(string provider, string uri, Texture2D qr)
            => OnQRReady?.Invoke(provider, uri, qr);

        public bool   IsInitialized    { get; private set; }
        public bool   IsConnected      => _session != null;
        public string ConnectedAddress { get; private set; }
        public string SessionTopic     { get { var s = _session; return s?.Topic; } }

        public event Action OnInitialized;

        private SignClientUnity _signClient;
        private Session _session;
        private bool           _isInitializing;
        private bool           _isConnecting;
        private Texture2D      _lastQRTexture;
        private CancellationTokenSource _connectCts;

        private EventHandler<Exception>    _onRelayErrored;
        private EventHandler               _onRelayDisconnected;
        private EventHandler               _onRelayConnected;
        private EventHandler<MessageEvent> _onRelayMessageReceived;
        private EventHandler<JsonRpcPayload> _onProviderPayloadReceived;

        private const string ALGO_CHAIN_MAINNET = "algorand:wGHE2Pwdvd7S12BL5FaOP20EGYesN73k";
        private const string ALGO_CHAIN_TESTNET = "algorand:SGO1GKSzyE7IEPItTxCByw9x8FmnrCDe";
        private const string ALGO_METHOD = "algo_signTxn";
        private const string ALGO_METHOD_SIGN_DATA = "algo_signData";
        private const string EVM_CHAIN   = "eip155:1";
        private const string EVM_METHOD  = "eth_signTypedData_v4";

        private static float ConnectTimeoutSeconds => BlockmakerAuth.WalletSignTimeout;

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (_signClient != null)
            {
                _signClient.SessionDeleted -= OnSessionDeleted;
                _signClient.SessionExpired -= OnSessionExpired;
                _signClient.SessionConnectionErrored -= OnSessionConnectionErrored;
                _signClient.SessionConnectedUnity -= OnSessionConnectedUnity;
                _signClient.SessionRejected -= OnSessionRejectedHandler;
                _signClient.SessionApproved -= OnSessionApprovedHandler;

                if (_onRelayErrored != null)
                    _signClient.CoreClient.Relayer.OnErrored -= _onRelayErrored;
                if (_onRelayDisconnected != null)
                    _signClient.CoreClient.Relayer.OnDisconnected -= _onRelayDisconnected;
                if (_onRelayConnected != null)
                    _signClient.CoreClient.Relayer.OnConnected -= _onRelayConnected;
                if (_onRelayMessageReceived != null)
                    _signClient.CoreClient.Relayer.OnMessageReceived -= _onRelayMessageReceived;
                if (_onProviderPayloadReceived != null)
                    _signClient.CoreClient.Relayer.Provider.PayloadReceived -= _onProviderPayloadReceived;
            }
            _connectCts?.Cancel();
            _connectCts?.Dispose();
            DestroyQRTexture();
            _signClient = null;
            _session = null;

            if (Instance == this)
                Instance = null;
        }

        public static void ClearStorage()
        {
            var storagePath = $"{Application.persistentDataPath}/Reown";
            if (System.IO.Directory.Exists(storagePath))
            {
                BlockmakerLog.Verbose($"[ReownWalletConnector] Clearing Reown storage at {storagePath}");
                try { System.IO.Directory.Delete(storagePath, true); }
                catch (Exception ex) { BlockmakerLog.Warning($"[ReownWalletConnector] Storage cleanup: {ex.Message}"); }
            }
        }

        public async void Initialize(string projectId)
        {
            if (IsInitialized || _isInitializing) return;
            _isInitializing = true;

            try
            {
                var cfg = BlockmakerAuth.Instance?.blockmakerConfig;
                var url = cfg?.dAppUrl;
                if (string.IsNullOrEmpty(url)) url = Application.absoluteURL;
                if (string.IsNullOrEmpty(url)) url = cfg?.serverUrl;
                if (string.IsNullOrEmpty(url)) url = BlockmakerConfig.DefaultServerUrl;
                var icon = cfg?.dAppIconUrl;
                if (string.IsNullOrEmpty(icon)) icon = url.TrimEnd('/') + "/icon.png";

                _signClient = await SignClientUnity.Create(new SignClientOptions
                {
                    ProjectId = projectId,
                    RelayUrl  = "wss://relay.walletconnect.com",
                    Metadata = new Metadata
                    {
                        Name        = Application.productName,
                        Description = Application.productName,
                        Url         = url,
                        Icons       = new[] { icon }
                    }
                });

                if (this == null) return;

                _signClient.SessionDeleted += OnSessionDeleted;
                _signClient.SessionExpired += OnSessionExpired;
                _signClient.SessionConnectionErrored += OnSessionConnectionErrored;
                _signClient.SessionConnectedUnity += OnSessionConnectedUnity;
                _signClient.SessionRejected += OnSessionRejectedHandler;
                _signClient.SessionApproved += OnSessionApprovedHandler;

                _onRelayErrored = (_, ex) =>
                    BlockmakerLog.Error($"[ReownWalletConnector] RELAY ERROR: {ex.Message}");
                _onRelayDisconnected = (_, _) =>
                    BlockmakerLog.Warning("[ReownWalletConnector] RELAY DISCONNECTED");
                _onRelayConnected = (_, _) =>
                    BlockmakerLog.Info("[ReownWalletConnector] RELAY CONNECTED");
                _onRelayMessageReceived = (_, msg) =>
                    BlockmakerLog.Verbose($"[ReownWalletConnector] RELAY MSG on topic {msg.Topic?.Substring(0, Math.Min(8, msg.Topic?.Length ?? 0))}...");
                _onProviderPayloadReceived = (_, payload) =>
                {
                    try
                    {
                        var method = payload.IsRequest ? payload.Method : "(response)";
                        BlockmakerLog.Verbose($"[ReownWalletConnector] RAW PAYLOAD: {method}, id={payload.Id}, isReq={payload.IsRequest}");
                    }
                    catch { }
                };

                _signClient.CoreClient.Relayer.OnErrored += _onRelayErrored;
                _signClient.CoreClient.Relayer.OnDisconnected += _onRelayDisconnected;
                _signClient.CoreClient.Relayer.OnConnected += _onRelayConnected;
                _signClient.CoreClient.Relayer.OnMessageReceived += _onRelayMessageReceived;
                _signClient.CoreClient.Relayer.Provider.PayloadReceived += _onProviderPayloadReceived;

                IsInitialized = true;
                var relayConnected = _signClient.CoreClient?.Relayer?.Connected == true;
                BlockmakerLog.Verbose($"[ReownWalletConnector] Initialized. Relay: {(relayConnected ? "connected" : "not connected")}, ProjectId: {projectId.Substring(0, Math.Min(6, projectId.Length))}...");
                try
                {
                    var wsUrl = _signClient.CoreClient?.Relayer?.Provider?.Connection?.Url;
                    BlockmakerLog.Verbose($"[ReownWalletConnector] WebSocket URL: {wsUrl}");
                }
                catch { }

                try { OnInitialized?.Invoke(); }
                catch (Exception ex) { BlockmakerLog.Exception(ex); }
            }
            catch (Exception e)
            {
                BlockmakerLog.Error($"[ReownWalletConnector] Init failed: {e.Message}");
            }
            finally
            {
                _isInitializing = false;
            }
        }

        // ── QR texture management ─────────────────────────────────────────────────

        private Texture2D CreateQRTexture(string uri)
        {
            DestroyQRTexture();
            _lastQRTexture = QRTextureGenerator.Generate(uri, 1024);
            return _lastQRTexture;
        }

        private void DestroyQRTexture()
        {
            if (_lastQRTexture != null)
            {
                Destroy(_lastQRTexture);
                _lastQRTexture = null;
            }
        }

        // ── Safe event invocation ─────────────────────────────────────────────────

        private static void FireEvent<T>(Action<T> handler, T arg)
        {
            if (handler == null) return;
            foreach (var d in handler.GetInvocationList())
            {
                try { ((Action<T>)d).Invoke(arg); }
                catch (Exception ex) { BlockmakerLog.Exception(ex); }
            }
        }

        private static void FireEvent<T1, T2>(Action<T1, T2> handler, T1 a, T2 b)
        {
            if (handler == null) return;
            foreach (var d in handler.GetInvocationList())
            {
                try { ((Action<T1, T2>)d).Invoke(a, b); }
                catch (Exception ex) { BlockmakerLog.Exception(ex); }
            }
        }

        private static void FireEvent<T1, T2, T3>(Action<T1, T2, T3> handler, T1 a, T2 b, T3 c)
        {
            if (handler == null) return;
            foreach (var d in handler.GetInvocationList())
            {
                try { ((Action<T1, T2, T3>)d).Invoke(a, b, c); }
                catch (Exception ex) { BlockmakerLog.Exception(ex); }
            }
        }

        // ── Algorand wallet connect ───────────────────────────────────────────────

        public void ConnectAlgorand(
            string                 walletHint,
            Action<string, string> onConnected = null,
            Action<string>         onError     = null)
        {
            if (!IsInitialized)
            {
                BlockmakerLog.Error("[ReownWalletConnector] Not initialized.");
                onError?.Invoke("Something went wrong. Please restart the game and try again.");
                return;
            }

            if (_isConnecting)
            {
                onError?.Invoke("A connection is already in progress. Please wait.");
                return;
            }

            _isConnecting = true;
            ConnectedAddress = null;
            ConnectAlgorandAsync(walletHint, onConnected, onError);
        }

        private async void ConnectAlgorandAsync(
            string walletHint, Action<string, string> onConnected, Action<string> onError)
        {
            var oldCts = _connectCts;
            _connectCts = new CancellationTokenSource();
            oldCts?.Cancel();
            oldCts?.Dispose();
            var token = _connectCts.Token;

            try
            {
                var client = _signClient;
                if (client == null) { onError?.Invoke("Wallet connector is not ready. Please try again."); return; }

                BlockmakerLog.Verbose($"[ReownWalletConnector] Creating pairing for {walletHint}...");
                BlockmakerLog.Verbose($"[ReownWalletConnector] Relay connected: {client.CoreClient.Relayer.Connected}");

                try
                {
                    var stalePairings = client.PairingStore.Values;
                    if (stalePairings != null && stalePairings.Length > 0)
                    {
                        BlockmakerLog.Verbose($"[ReownWalletConnector] Clearing {stalePairings.Length} stale pairing(s)...");
                        foreach (var p in stalePairings)
                        {
                            try { await client.CoreClient.Pairing.Disconnect(p.Topic); }
                            catch { }
                        }
                    }
                }
                catch (Exception cleanupEx)
                {
                    BlockmakerLog.Warning($"[ReownWalletConnector] Pairing cleanup: {cleanupEx.Message}");
                }

                var connectData = await client.Connect(new ConnectOptions
                {
                    RequiredNamespaces = new RequiredNamespaces
                    {
                        {
                            "algorand", new ProposedNamespace
                            {
                                Methods = new[] { ALGO_METHOD, ALGO_METHOD_SIGN_DATA },
                                Chains  = new[] { ALGO_CHAIN_MAINNET, ALGO_CHAIN_TESTNET },
                                Events  = Array.Empty<string>()
                            }
                        }
                    }
                });

                if (_signClient == null) { onError?.Invoke("Wallet connector was reset. Please try again."); return; }
                token.ThrowIfCancellationRequested();

                var uri = connectData.Uri;
                BlockmakerLog.Verbose("[ReownWalletConnector] WC URI generated — ready for QR scan.");

                var qrTexture = CreateQRTexture(uri);
                if (qrTexture != null)
                    FireEvent(OnQRReady, walletHint, uri, qrTexture);

                BlockmakerLog.Verbose($"[ReownWalletConnector] Waiting for wallet approval (timeout={ConnectTimeoutSeconds}s)...");

                var approvalTask = connectData.Approval;
                var pollCts = CancellationTokenSource.CreateLinkedTokenSource(token);

                _ = PollForSessionAsync(client, walletHint, pollCts.Token);

                var timeoutTask  = Task.Delay(TimeSpan.FromSeconds(ConnectTimeoutSeconds));
                var completed    = await Task.WhenAny(approvalTask, timeoutTask);

                pollCts.Cancel();
                pollCts.Dispose();

                if (_signClient == null) { onError?.Invoke("Wallet connector was reset. Please try again."); return; }
                token.ThrowIfCancellationRequested();

                if (completed == timeoutTask)
                {
                    var sessions = client.Session?.Values;
                    BlockmakerLog.Warning($"[ReownWalletConnector] Timed out. Active sessions: {sessions?.Length ?? 0}");

                    if (sessions != null && sessions.Length > 0)
                    {
                        BlockmakerLog.Info("[ReownWalletConnector] Found session despite timeout — using it.");
                        var fallbackSession = sessions[^1];
                        _session = fallbackSession;

                        string fbAddress = null;
                        if (fallbackSession.Namespaces.TryGetValue("algorand", out var fbAlgo) &&
                            fbAlgo.Accounts?.Length > 0)
                            fbAddress = fbAlgo.Accounts[0].Split(':')[^1];

                        if (fbAddress != null)
                        {
                            ConnectedAddress = fbAddress;
                            BlockmakerLog.Info($"[ReownWalletConnector] {walletHint} connected (via fallback): {fbAddress}");
                            onConnected?.Invoke(walletHint, fbAddress);
                            FireEvent(OnConnected, walletHint, fbAddress);
                            return;
                        }
                    }

                    var msg = "Connection timed out. Please try again.";
                    onError?.Invoke(msg);
                    FireEvent(OnConnectionError, msg);
                    return;
                }

                BlockmakerLog.Verbose("[ReownWalletConnector] Approval task completed, extracting session...");

                var session = await approvalTask;
                _session = session;

                BlockmakerLog.Verbose($"[ReownWalletConnector] Session topic: {session.Topic}, namespaces: {string.Join(", ", session.Namespaces.Keys)}");

                string[] accounts = null;
                if (session.Namespaces.TryGetValue("algorand", out var algoNs))
                    accounts = algoNs.Accounts;

                if (accounts == null || accounts.Length == 0)
                {
                    BlockmakerLog.Warning($"[ReownWalletConnector] No Algorand accounts. Namespaces: {string.Join(", ", session.Namespaces.Keys)}");
                    var msg = "No accounts found in your wallet. Please make sure you have an Algorand account and try again.";
                    onError?.Invoke(msg);
                    FireEvent(OnConnectionError, msg);
                    return;
                }

                var address = accounts[0].Split(':').Last();
                ConnectedAddress = address;

                BlockmakerLog.Info($"[ReownWalletConnector] {walletHint} connected: {address}");
                onConnected?.Invoke(walletHint, address);
                FireEvent(OnConnected, walletHint, address);
            }
            catch (OperationCanceledException)
            {
                BlockmakerLog.Info("[ReownWalletConnector] ConnectAlgorand cancelled.");
                onError?.Invoke("Connection was cancelled.");
            }
            catch (Exception e)
            {
                BlockmakerLog.Error($"[ReownWalletConnector] ConnectAlgorand error: {e.Message}");
                var msg = "Wallet connection failed. Please try again.";
                onError?.Invoke(msg);
                FireEvent(OnConnectionError, msg);
            }
            finally
            {
                _isConnecting = false;
                DestroyQRTexture();
            }
        }

        // ── EVM wallet connect ─────────────────────────────────────────────────────

        public void ConnectEvm(
            Action<string> onConnected = null,
            Action<string> onError     = null)
        {
            if (!IsInitialized)
            {
                onError?.Invoke("Something went wrong. Please restart the game and try again.");
                return;
            }

            if (_isConnecting)
            {
                onError?.Invoke("A connection is already in progress. Please wait.");
                return;
            }

            _isConnecting = true;
            ConnectedAddress = null;
            ConnectEvmAsync(onConnected, onError);
        }

        private async void ConnectEvmAsync(Action<string> onConnected, Action<string> onError)
        {
            var oldCts = _connectCts;
            _connectCts = new CancellationTokenSource();
            oldCts?.Cancel();
            oldCts?.Dispose();
            var token = _connectCts.Token;

            try
            {
                var client = _signClient;
                if (client == null) { onError?.Invoke("Wallet connector is not ready. Please try again."); return; }

                var connectData = await client.Connect(new ConnectOptions
                {
                    RequiredNamespaces = new RequiredNamespaces
                    {
                        {
                            "eip155", new ProposedNamespace
                            {
                                Methods = new[] { EVM_METHOD, "personal_sign", "eth_sendTransaction" },
                                Chains  = new[] { EVM_CHAIN },
                                Events  = new[] { "accountsChanged", "chainChanged" }
                            }
                        }
                    }
                });

                if (_signClient == null) { onError?.Invoke("Wallet connector was reset. Please try again."); return; }
                token.ThrowIfCancellationRequested();

                var uri = connectData.Uri;
                var qrTexture = CreateQRTexture(uri);
                if (qrTexture != null)
                    FireEvent(OnQRReady, "EVM Wallet", uri, qrTexture);

                var approvalTask = connectData.Approval;
                var timeoutTask  = Task.Delay(TimeSpan.FromSeconds(ConnectTimeoutSeconds));
                var completed    = await Task.WhenAny(approvalTask, timeoutTask);

                if (_signClient == null) { onError?.Invoke("Wallet connector was reset. Please try again."); return; }
                token.ThrowIfCancellationRequested();

                if (completed == timeoutTask)
                {
                    var msg = "Connection timed out. Please try again.";
                    onError?.Invoke(msg);
                    FireEvent(OnConnectionError, msg);
                    return;
                }

                var session = await approvalTask;
                _session = session;

                string[] accounts = null;
                if (session.Namespaces.TryGetValue("eip155", out var evmNs))
                    accounts = evmNs.Accounts;

                if (accounts == null || accounts.Length == 0)
                {
                    var msg = "No accounts found in your wallet. Please try again.";
                    onError?.Invoke(msg);
                    FireEvent(OnConnectionError, msg);
                    return;
                }

                var evmAddress = accounts[0].Split(':').Last();
                ConnectedAddress = evmAddress;

                BlockmakerLog.Info($"[ReownWalletConnector] EVM wallet connected: {evmAddress}");
                onConnected?.Invoke(evmAddress);
                FireEvent(OnConnected, "EVM Wallet", evmAddress);
            }
            catch (OperationCanceledException)
            {
                BlockmakerLog.Info("[ReownWalletConnector] ConnectEvm cancelled.");
                onError?.Invoke("Connection was cancelled.");
            }
            catch (Exception e)
            {
                BlockmakerLog.Error($"[ReownWalletConnector] ConnectEvm error: {e.Message}");
                var msg = "Wallet connection failed. Please try again.";
                onError?.Invoke(msg);
                FireEvent(OnConnectionError, msg);
            }
            finally
            {
                _isConnecting = false;
                DestroyQRTexture();
            }
        }

        // ── Algorand transaction signing ──────────────────────────────────────────

        public void SignAlgorandTransaction(
            string         txnBase64,
            Action<string> onSigned = null,
            Action<string> onError  = null)
        {
            var session = _session;
            if (session == null)
            {
                onError?.Invoke("Your wallet is not connected. Please connect your wallet again to continue.");
                return;
            }

            SignAlgorandAsync(session.Topic, txnBase64, onSigned, onError);
        }

        private async void SignAlgorandAsync(
            string topic, string txnBase64, Action<string> onSigned, Action<string> onError)
        {
            try
            {
                var client = _signClient;
                if (client == null) { onError?.Invoke("Something went wrong. Please restart the game and try again."); return; }

                // algo_signTxn params: [[{txn: "base64..."}]]
                // Verify wire format matches ARC-0025 with your installed Reown.Sign version.
                var txns = new List<List<AlgoSignTxnParam>>
                {
                    new List<AlgoSignTxnParam>
                    {
                        new AlgoSignTxnParam { txn = txnBase64 }
                    }
                };

                var result = await client.Request<List<List<AlgoSignTxnParam>>, string[]>(
                    topic, txns, ALGO_CHAIN_MAINNET
                );

                if (_signClient == null)
                {
                    onError?.Invoke("Wallet was disconnected. Please reconnect and try again.");
                    return;
                }

                if (result == null || result.Length == 0 || result[0] == null)
                {
                    var msg = "Wallet declined to sign. Please approve the request in your wallet app.";
                    onError?.Invoke(msg);
                    FireEvent(OnSignError, msg);
                    return;
                }

                var signedTxn = result[0];
                BlockmakerLog.Info("[ReownWalletConnector] Transaction signed.");
                onSigned?.Invoke(signedTxn);
                FireEvent(OnTransactionSigned, signedTxn);
            }
            catch (Exception e)
            {
                BlockmakerLog.Error($"[ReownWalletConnector] Sign error: {e.Message}");
                var msg = "Something went wrong. Please try again.";
                onError?.Invoke(msg);
                FireEvent(OnSignError, msg);
            }
        }

        public void SignAlgorandTransactions(
            string[]         txnsBase64,
            Action<string[]> onSigned = null,
            Action<string>   onError  = null)
        {
            var session = _session;
            if (session == null)
            {
                onError?.Invoke("Your wallet is not connected. Please connect your wallet again to continue.");
                return;
            }

            SignAlgorandGroupAsync(session.Topic, txnsBase64, onSigned, onError);
        }

        private async void SignAlgorandGroupAsync(
            string topic, string[] txnsBase64, Action<string[]> onSigned, Action<string> onError)
        {
            try
            {
                var client = _signClient;
                if (client == null) { onError?.Invoke("Something went wrong. Please restart the game and try again."); return; }

                var txnParams = new List<AlgoSignTxnParam>();
                foreach (var b64 in txnsBase64)
                    txnParams.Add(new AlgoSignTxnParam { txn = b64 });

                var txns = new List<List<AlgoSignTxnParam>> { txnParams };

                var result = await client.Request<List<List<AlgoSignTxnParam>>, string[]>(
                    topic, txns, ALGO_CHAIN_MAINNET
                );

                if (_signClient == null)
                {
                    onError?.Invoke("Wallet was disconnected. Please reconnect and try again.");
                    return;
                }

                if (result == null || result.Length < txnsBase64.Length)
                {
                    onError?.Invoke("Wallet declined to sign. Please approve the request in your wallet app.");
                    return;
                }

                for (int i = 0; i < result.Length; i++)
                {
                    if (string.IsNullOrEmpty(result[i]))
                    {
                        onError?.Invoke("Wallet did not approve all required signatures. Please try again.");
                        return;
                    }
                }

                onSigned?.Invoke(result);
            }
            catch (Exception e)
            {
                BlockmakerLog.Error($"[ReownWalletConnector] Group sign error: {e.Message}");
                var msg = "Something went wrong. Please try again.";
                onError?.Invoke(msg);
            }
        }

        // ── EVM signing ────────────────────────────────────────────────────────────

        public void SignEvmTypedData(
            string         evmAddress,
            string         typedDataJson,
            Action<string> onSigned = null,
            Action<string> onError  = null)
        {
            var session = _session;
            if (session == null)
            {
                onError?.Invoke("Your wallet is not connected. Please connect your wallet again to continue.");
                return;
            }

            SignEvmTypedDataAsync(session.Topic, evmAddress, typedDataJson, onSigned, onError);
        }

        private async void SignEvmTypedDataAsync(
            string topic, string evmAddress, string typedDataJson,
            Action<string> onSigned, Action<string> onError)
        {
            try
            {
                var client = _signClient;
                if (client == null) { onError?.Invoke("Something went wrong. Please restart the game and try again."); return; }

                var result = await client.Request<string[], string>(
                    topic, new[] { evmAddress, typedDataJson }, EVM_CHAIN
                );

                if (_signClient == null)
                {
                    onError?.Invoke("Wallet was disconnected. Please reconnect and try again.");
                    return;
                }

                if (string.IsNullOrEmpty(result))
                {
                    onError?.Invoke("Wallet declined to sign. Please approve the request in your wallet app.");
                    return;
                }

                onSigned?.Invoke(result);
                FireEvent(OnTransactionSigned, result);
            }
            catch (Exception e)
            {
                BlockmakerLog.Error($"[ReownWalletConnector] EVM sign error: {e.Message}");
                var msg = "Something went wrong. Please try again.";
                onError?.Invoke(msg);
                FireEvent(OnSignError, msg);
            }
        }

        // ── Cancel / Disconnect ───────────────────────────────────────────────────

        public void CancelConnection()
        {
            _connectCts?.Cancel();
            _isConnecting = false;
            DestroyQRTexture();
        }

        public async void Disconnect()
        {
            CancelConnection();

            var session = _session;
            var client  = _signClient;
            _session = null;
            ConnectedAddress = null;

            if (session == null || client == null) return;

            try
            {
                await client.DisconnectAsync(session.Topic);
            }
            catch (Exception e)
            {
                BlockmakerLog.Warning($"[ReownWalletConnector] Disconnect error: {e.Message}");
            }

            BlockmakerLog.Info("[ReownWalletConnector] Disconnected.");
        }

        // ── Session restore ───────────────────────────────────────────────────────

        public string TryRestoreSession()
        {
            var client = _signClient;
            if (client == null) return null;

            try
            {
                var sessions = client.Session.Values;
                if (sessions == null || sessions.Length == 0) return null;

                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var validSessions = sessions.Where(s => s.Expiry > now).ToList();
                if (validSessions.Count == 0) return null;

                var session = validSessions.OrderByDescending(s => s.Expiry).First();

                if (session.Namespaces.TryGetValue("algorand", out var algoNs) &&
                    algoNs.Accounts != null && algoNs.Accounts.Length > 0)
                {
                    _session = session;
                    ConnectedAddress = algoNs.Accounts[0].Split(':').Last();
                    BlockmakerLog.Info($"[ReownWalletConnector] Session restored: {ConnectedAddress}");
                    return ConnectedAddress;
                }

                if (session.Namespaces.TryGetValue("eip155", out var evmNs) &&
                    evmNs.Accounts != null && evmNs.Accounts.Length > 0)
                {
                    _session = session;
                    ConnectedAddress = evmNs.Accounts[0].Split(':').Last();
                    BlockmakerLog.Info($"[ReownWalletConnector] EVM session restored: {ConnectedAddress}");
                    return ConnectedAddress;
                }
            }
            catch (Exception e)
            {
                BlockmakerLog.Warning($"[ReownWalletConnector] Session restore failed: {e.Message}");
            }

            return null;
        }

        // ── Session polling (fallback) ──────────────────────────────────────────────

        private static async Task PollForSessionAsync(SignClientUnity client, string hint, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(3000, ct);
                    var sessions = client.Session?.Values;
                    bool relayOk = client.CoreClient?.Relayer?.Connected ?? false;
                    var pairings = client.PairingStore?.Values;
                    var proposals = client.Proposal?.Values;
                    BlockmakerLog.Verbose($"[ReownWalletConnector] Poll — relay={relayOk}, sessions={sessions?.Length ?? 0}, pairings={pairings?.Length ?? 0}, proposals={proposals?.Length ?? 0}");
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                BlockmakerLog.Warning($"[ReownWalletConnector] Polling error: {ex.Message}");
            }
        }

        // ── Session events ────────────────────────────────────────────────────────

        [Preserve]
        private void OnSessionDeleted(object sender, SessionEvent e)
        {
            var session = _session;
            if (session?.Topic == e.Topic)
            {
                _session = null;
                ConnectedAddress = null;
                BlockmakerLog.Info("[ReownWalletConnector] Session deleted by wallet.");
            }
        }

        [Preserve]
        private void OnSessionExpired(object sender, Session e)
        {
            var session = _session;
            if (session?.Topic == e.Topic)
            {
                _session = null;
                ConnectedAddress = null;
                BlockmakerLog.Info("[ReownWalletConnector] Session expired.");
            }
        }

        [Preserve]
        private void OnSessionConnectionErrored(object sender, Exception e)
        {
            BlockmakerLog.Error($"[ReownWalletConnector] SessionConnectionErrored: {e.Message}");
            if (_isConnecting)
            {
                FireEvent(OnConnectionError, "Wallet connection failed. Please try again.");
            }
        }

        [Preserve]
        private void OnSessionConnectedUnity(object sender, Session session)
        {
            BlockmakerLog.Verbose($"[ReownWalletConnector] SessionConnectedUnity fired — topic: {session.Topic}");

            if (_session != null) return;

            _session = session;

            string address = null;
            if (session.Namespaces.TryGetValue("algorand", out var algoNs) &&
                algoNs.Accounts != null && algoNs.Accounts.Length > 0)
            {
                address = algoNs.Accounts[0].Split(':')[^1];
            }
            else if (session.Namespaces.TryGetValue("eip155", out var evmNs) &&
                     evmNs.Accounts != null && evmNs.Accounts.Length > 0)
            {
                address = evmNs.Accounts[0].Split(':')[^1];
            }

            if (address != null)
            {
                ConnectedAddress = address;
                BlockmakerLog.Verbose($"[ReownWalletConnector] Backup: connected via SessionConnectedUnity — {address}");
            }
        }

        [Preserve]
        private void OnSessionRejectedHandler(object sender, Session e)
        {
            BlockmakerLog.Warning($"[ReownWalletConnector] Session REJECTED by wallet. Topic: {e.Topic}");
        }

        [Preserve]
        private void OnSessionApprovedHandler(object sender, Session e)
        {
            BlockmakerLog.Verbose($"[ReownWalletConnector] Session APPROVED. Topic: {e.Topic}");
        }
    }

}