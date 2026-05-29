using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Blockmaker;

/// <summary>
/// Native WalletConnect v1 client for Unity.
/// Handles the full WC v1 protocol: WebSocket transport, AES-256-CBC encryption,
/// JSON-RPC session handshake, and custom requests (e.g. algo_signTxn).
/// No external dependencies — uses only System.* and UnityEngine.
/// </summary>
public class WalletConnectV1Client : IDisposable
{
    // ── Public state ──────────────────────────────────────────────────────────
    public  string Uri         { get; private set; }
    public  string Address     { get; private set; }
    public  string PeerId      { get; private set; }

    private volatile bool _connected;
    public  bool IsConnected => _connected;

    // ── Events (marshaled to the main thread via SynchronizationContext) ────────
    public event Action<string> OnSessionApproved;
    public event Action<string> OnSessionRejected;
    public event Action         OnDisconnected;
    public event Action<string> OnError;

    private readonly SynchronizationContext _syncContext;

    // ── Session params ────────────────────────────────────────────────────────
    private readonly string _bridgeUrl;
    private readonly byte[] _key;
    private readonly string _handshakeTopic;
    private readonly string _clientId;
    private readonly long   _handshakeId;
    private readonly string _appName;
    private readonly string _appDescription;
    private readonly string _appUrl;
    private readonly int    _chainId;

    // ── WebSocket ─────────────────────────────────────────────────────────────
    private ClientWebSocket _ws;
    private CancellationTokenSource _cts;

    // ── Request tracking ──────────────────────────────────────────────────────
    private readonly Dictionary<long, TaskCompletionSource<string>> _pendingRequests
        = new Dictionary<long, TaskCompletionSource<string>>();
    private long _nextRequestId;

    private volatile bool _disposed;
    public  bool IsDisposed => _disposed;

    // ── Pera bridge servers ──────────────────────────────────────────────────
    private static readonly string[] PERA_BRIDGES = new[]
    {
        "https://wallet-connect-a.perawallet.app",
        "https://wallet-connect-b.perawallet.app",
        "https://wallet-connect-c.perawallet.app",
        "https://wallet-connect-d.perawallet.app",
        "https://wallet-connect-e.perawallet.app",
        "https://wallet-connect-f.perawallet.app",
        "https://wallet-connect-g.perawallet.app",
        "https://wallet-connect-h.perawallet.app",
    };

    // ── Session persistence ──────────────────────────────────────────────────

    [Serializable]
    public class SavedSession
    {
        public string bridgeUrl;
        public string keyHex;
        public string clientId;
        public string peerId;
        public string address;
        public int    chainId;
    }

    public SavedSession GetSessionData()
    {
        if (string.IsNullOrEmpty(PeerId) || string.IsNullOrEmpty(Address)) return null;
        return new SavedSession
        {
            bridgeUrl = _bridgeUrl,
            keyHex    = BytesToHex(_key),
            clientId  = _clientId,
            peerId    = PeerId,
            address   = Address,
            chainId   = _chainId,
        };
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    public WalletConnectV1Client(
        string bridgeUrl = null,
        int    chainId   = 4160,
        string appName   = "Blockmaker",
        string appDescription = "Blockmaker SDK",
        string appUrl    = "https://blockmaker.io")
    {
        _syncContext     = SynchronizationContext.Current;
        _bridgeUrl      = bridgeUrl ?? PERA_BRIDGES[GenerateRandomBytes(1)[0] % PERA_BRIDGES.Length];
        _chainId        = chainId;
        _appName        = appName;
        _appDescription = appDescription;
        _appUrl         = appUrl;

        _key            = GenerateRandomBytes(32);
        _handshakeTopic = Guid.NewGuid().ToString();
        _clientId       = Guid.NewGuid().ToString();
        _handshakeId    = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _nextRequestId  = _handshakeId + 1;

        string hexKey = BytesToHex(_key);
        string encodedBridge = System.Uri.EscapeDataString(_bridgeUrl);
        Uri = $"wc:{_handshakeTopic}@1?bridge={encodedBridge}&key={hexKey}";

        BlockmakerLog.Info($"[WCv1] Using bridge: {_bridgeUrl}");
    }

    public static WalletConnectV1Client FromSession(SavedSession session)
    {
        return new WalletConnectV1Client(session);
    }

    private WalletConnectV1Client(SavedSession session)
    {
        _syncContext     = SynchronizationContext.Current;
        _bridgeUrl      = session.bridgeUrl;
        _chainId        = session.chainId;
        _appName        = "Blockmaker";
        _appDescription = "Blockmaker SDK";
        _appUrl         = "https://blockmaker.io";

        _key            = HexToBytes(session.keyHex);
        _handshakeTopic = "";
        _clientId       = session.clientId;
        _handshakeId    = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _nextRequestId  = _handshakeId + 1;

        PeerId  = session.peerId;
        Address = session.address;

        BlockmakerLog.Info($"[WCv1] Restored from session — bridge: {_bridgeUrl}, address: {Address}");
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task Connect()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WalletConnectV1Client));
        if (_ws != null)
            throw new InvalidOperationException("Already connected or connecting");

        if (_bridgeUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !_bridgeUrl.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) &&
            !_bridgeUrl.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("WalletConnect bridge must use HTTPS. Insecure HTTP bridges are not allowed.");
        }

        _cts = new CancellationTokenSource();
        _ws  = new ClientWebSocket();
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);

        string wsUrl = _bridgeUrl
            .Replace("https://", "wss://")
            .Replace("http://",  "ws://");
        wsUrl += $"?protocol=wc&version=1&env=unity";

        await _ws.ConnectAsync(new System.Uri(wsUrl), _cts.Token);
        BlockmakerLog.Info("[WCv1] WebSocket connected to bridge");

        _ = ReceiveLoop();
        _ = HeartbeatLoop();

        await Subscribe(_clientId);
        await Subscribe(_handshakeTopic);

        await SendSessionRequest();
    }

    public async Task Reconnect()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WalletConnectV1Client));
        if (string.IsNullOrEmpty(PeerId))
            throw new InvalidOperationException("No session to reconnect");

        _connected = false;
        var oldCts = _cts;
        var oldWs  = _ws;
        _cts = new CancellationTokenSource();
        _ws  = new ClientWebSocket();
        try { oldCts?.Cancel(); } catch { }
        try { oldWs?.Dispose(); } catch { }
        try { oldCts?.Dispose(); } catch { }
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);

        string wsUrl = _bridgeUrl
            .Replace("https://", "wss://")
            .Replace("http://",  "ws://");
        wsUrl += $"?protocol=wc&version=1&env=unity";

        await _ws.ConnectAsync(new System.Uri(wsUrl), _cts.Token);
        BlockmakerLog.Info("[WCv1] WebSocket reconnected to bridge");

        _ = ReceiveLoop();
        _ = HeartbeatLoop();

        await Subscribe(_clientId);

        _connected = true;
        BlockmakerLog.Info($"[WCv1] Session restored — Address: {Address}, PeerId: {PeerId}");
    }

    public async Task<string> SendCustomRequest(string method, string paramsJson)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WalletConnectV1Client));
        if (!IsConnected || string.IsNullOrEmpty(PeerId))
            throw new InvalidOperationException("No active session");

        long id = Interlocked.Increment(ref _nextRequestId);
        string request = $"{{\"id\":{id},\"jsonrpc\":\"2.0\",\"method\":\"{method}\",\"params\":{paramsJson}}}";

        var tcs = new TaskCompletionSource<string>();
        lock (_pendingRequests) _pendingRequests[id] = tcs;

        await PublishEncrypted(PeerId, request);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        timeout.Token.Register(() =>
        {
            lock (_pendingRequests) _pendingRequests.Remove(id);
            tcs.TrySetException(new TimeoutException("Wallet did not respond in time"));
        });

        return await tcs.Task;
    }

    public async Task Disconnect()
    {
        bool wasConnected = _connected;

        if (_ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                if (!string.IsNullOrEmpty(PeerId))
                {
                    long id = Interlocked.Increment(ref _nextRequestId);
                    string req = $"{{\"id\":{id},\"jsonrpc\":\"2.0\",\"method\":\"wc_sessionUpdate\",\"params\":[{{\"approved\":false,\"chainId\":{_chainId},\"accounts\":[]}}]}}";
                    await PublishEncrypted(PeerId, req);
                }
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
            catch { }
        }

        _cts?.Cancel();
        _connected = false;
        Address    = null;
        PeerId     = null;

        if (wasConnected)
            PostToMain(() => OnDisconnected?.Invoke());
    }

    // ── Session request ───────────────────────────────────────────────────────

    private async Task SendSessionRequest()
    {
        string request =
            $"{{\"id\":{_handshakeId},\"jsonrpc\":\"2.0\",\"method\":\"wc_sessionRequest\"," +
            $"\"params\":[{{\"peerId\":\"{_clientId}\",\"peerMeta\":{{\"name\":\"{EscapeJson(_appName)}\"," +
            $"\"description\":\"{EscapeJson(_appDescription)}\",\"url\":\"{EscapeJson(_appUrl)}\",\"icons\":[]}}," +
            $"\"chainId\":{_chainId}}}]}}";

        await PublishEncrypted(_handshakeTopic, request);
        BlockmakerLog.Info("[WCv1] Session request sent");
    }

    private static string EscapeJson(string s) =>
        s.Replace("\\", "\\\\")
         .Replace("\"", "\\\"")
         .Replace("\n", "\\n")
         .Replace("\r", "\\r")
         .Replace("\t", "\\t");

    // ── WebSocket transport ───────────────────────────────────────────────────

    private async Task HeartbeatLoop()
    {
        var ct = _cts.Token;
        try
        {
            while (!_disposed && !ct.IsCancellationRequested && _ws != null && _ws.State == WebSocketState.Open)
            {
                await Task.Delay(TimeSpan.FromSeconds(20), ct);
                if (_ws?.State == WebSocketState.Open)
                    await Subscribe(_clientId);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private async Task Subscribe(string topic)
    {
        string msg = $"{{\"topic\":\"{topic}\",\"type\":\"sub\",\"payload\":\"\",\"silent\":true}}";
        await SendRaw(msg);
        BlockmakerLog.Info($"[WCv1] Subscribed to {topic.Substring(0, 8)}...");
    }

    private async Task PublishEncrypted(string topic, string plaintext)
    {
        string envelope = Encrypt(plaintext, _key);
        string escapedPayload = envelope.Replace("\\", "\\\\").Replace("\"", "\\\"");
        string msg = $"{{\"topic\":\"{topic}\",\"type\":\"pub\",\"payload\":\"{escapedPayload}\",\"silent\":true}}";
        await SendRaw(msg);
    }

    private async Task SendRaw(string message)
    {
        var ws = _ws;
        if (ws == null || ws.State != WebSocketState.Open) return;
        byte[] bytes = Encoding.UTF8.GetBytes(message);
        await ws.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            _cts?.Token ?? CancellationToken.None);
    }

    private async Task ReceiveLoop()
    {
        var buffer = new byte[16384];
        var sb = new StringBuilder();
        var ct = _cts.Token;

        try
        {
            while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                sb.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(
                        new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                string raw = sb.ToString();
                ProcessMessage(raw);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            if (!_disposed)
            {
                BlockmakerLog.Warning($"[WCv1] WebSocket error: {ex.Message}");
                var msg = ex.Message;
                PostToMain(() => OnError?.Invoke(msg));
            }
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                BlockmakerLog.Error($"[WCv1] Receive error: {ex.Message}");
                var msg = ex.Message;
                PostToMain(() => OnError?.Invoke(msg));
            }
        }
        finally
        {
            if (_connected && !_disposed)
            {
                _connected = false;
                PostToMain(() => OnDisconnected?.Invoke());
            }
        }
    }

    // ── Message processing ────────────────────────────────────────────────────

    private void ProcessMessage(string raw)
    {
        try
        {
            var msg = JsonUtility.FromJson<BridgeMessage>(raw);

            if (msg.type == "ack" || string.IsNullOrEmpty(msg.payload)) return;

            string decrypted = Decrypt(msg.payload, _key);
            if (decrypted == null) return;

            var rpc = JsonUtility.FromJson<JsonRpcEnvelope>(decrypted);

            if (rpc.id == _handshakeId && !IsConnected)
            {
                HandleSessionResponse(decrypted);
                return;
            }

            if (!string.IsNullOrEmpty(rpc.method))
            {
                HandleIncomingRequest(decrypted, rpc.method);
                return;
            }

            lock (_pendingRequests)
            {
                if (_pendingRequests.TryGetValue(rpc.id, out var tcs))
                {
                    _pendingRequests.Remove(rpc.id);
                    tcs.TrySetResult(decrypted);
                }
            }
        }
        catch (Exception ex)
        {
            BlockmakerLog.Warning($"[WCv1] Failed to process message: {ex.Message}");
        }
    }

    private void HandleSessionResponse(string json)
    {
        try
        {
            var response = JsonUtility.FromJson<SessionResponseWrapper>(json);

            if (response.result == null || !response.result.approved)
            {
                BlockmakerLog.Info("[WCv1] Session rejected by wallet");
                PostToMain(() => OnSessionRejected?.Invoke("Wallet rejected the connection"));
                return;
            }

            PeerId = response.result.peerId;
            if (response.result.accounts != null && response.result.accounts.Length > 0)
                Address = response.result.accounts[0];

            _connected = true;

            BlockmakerLog.Info($"[WCv1] Session approved! Address: {Address}, PeerId: {PeerId}");
            var addr = Address;
            PostToMain(() => OnSessionApproved?.Invoke(addr));
        }
        catch (Exception ex)
        {
            BlockmakerLog.Error($"[WCv1] Failed to parse session response: {ex.Message}");
            PostToMain(() => OnError?.Invoke("Failed to parse wallet response"));
        }
    }

    private void HandleIncomingRequest(string json, string method)
    {
        if (method == "wc_sessionUpdate")
        {
            var update = JsonUtility.FromJson<SessionUpdateWrapper>(json);
            if (update.@params != null && update.@params.Length > 0 && !update.@params[0].approved
                && _connected)
            {
                BlockmakerLog.Info("[WCv1] Wallet disconnected session");
                _connected = false;
                Address    = null;
                PeerId     = null;
                PostToMain(() => OnDisconnected?.Invoke());
            }
        }
    }

    // ── AES-256-CBC + HMAC-SHA256 encryption ──────────────────────────────────

    private static string Encrypt(string plaintext, byte[] key)
    {
        byte[] iv         = GenerateRandomBytes(16);
        byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);

        byte[] ciphertext;
        using (var aes = Aes.Create())
        {
            aes.Mode    = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key     = key;
            aes.IV      = iv;
            using var enc = aes.CreateEncryptor();
            ciphertext = enc.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        }

        byte[] dataToSign = new byte[ciphertext.Length + iv.Length];
        Buffer.BlockCopy(ciphertext, 0, dataToSign, 0, ciphertext.Length);
        Buffer.BlockCopy(iv, 0, dataToSign, ciphertext.Length, iv.Length);

        byte[] hmac;
        using (var hmacSha = new HMACSHA256(key))
        {
            hmac = hmacSha.ComputeHash(dataToSign);
        }

        string dataHex = BytesToHex(ciphertext);
        string ivHex   = BytesToHex(iv);
        string hmacHex = BytesToHex(hmac);

        return $"{{\"data\":\"{dataHex}\",\"iv\":\"{ivHex}\",\"hmac\":\"{hmacHex}\"}}";
    }

    private static string Decrypt(string envelopeJson, byte[] key)
    {
        var env = JsonUtility.FromJson<EncryptionEnvelope>(envelopeJson);
        if (string.IsNullOrEmpty(env.data) || string.IsNullOrEmpty(env.iv) || string.IsNullOrEmpty(env.hmac))
            return null;

        byte[] ciphertext  = HexToBytes(env.data);
        byte[] iv          = HexToBytes(env.iv);
        byte[] receivedHmac = HexToBytes(env.hmac);

        byte[] dataToVerify = new byte[ciphertext.Length + iv.Length];
        Buffer.BlockCopy(ciphertext, 0, dataToVerify, 0, ciphertext.Length);
        Buffer.BlockCopy(iv, 0, dataToVerify, ciphertext.Length, iv.Length);

        using (var hmacSha = new HMACSHA256(key))
        {
            byte[] computedHmac = hmacSha.ComputeHash(dataToVerify);
            if (!ConstantTimeEquals(computedHmac, receivedHmac))
            {
                BlockmakerLog.Warning("[WCv1] HMAC verification failed");
                return null;
            }
        }

        using var aes = Aes.Create();
        aes.Mode    = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key     = key;
        aes.IV      = iv;
        using var dec = aes.CreateDecryptor();
        byte[] plainBytes = dec.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    // ── Thread marshaling ──────────────────────────────────────────────────────

    private void PostToMain(Action action)
    {
        if (_syncContext != null)
            _syncContext.Post(_ => action(), null);
        else
            action();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static byte[] GenerateRandomBytes(int count)
    {
        byte[] bytes = new byte[count];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return bytes;
    }

    private static string BytesToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static byte[] HexToBytes(string hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length % 2 != 0)
            throw new ArgumentException("Hex string must be non-empty and even length.");
        byte[] bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    private static bool ConstantTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }

    // ── Serialization types ───────────────────────────────────────────────────

    [Serializable]
    private class BridgeMessage
    {
        public string topic;
        public string type;
        public string payload;
    }

    [Serializable]
    private class EncryptionEnvelope
    {
        public string data;
        public string iv;
        public string hmac;
    }

    [Serializable]
    private class JsonRpcEnvelope
    {
        public long   id;
        public string method;
    }

    [Serializable]
    private class SessionResponseWrapper
    {
        public long          id;
        public SessionResult result;
    }

    [Serializable]
    private class SessionResult
    {
        public bool     approved;
        public int      chainId;
        public string[] accounts;
        public string   peerId;
    }

    [Serializable]
    private class SessionUpdateWrapper
    {
        public long              id;
        public string            method;
        public SessionUpdate[]   @params;
    }

    [Serializable]
    private class SessionUpdate
    {
        public bool approved;
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed  = true;
        _connected = false;

        if (_key != null) Array.Clear(_key, 0, _key.Length);

        OnSessionApproved = null;
        OnSessionRejected = null;
        OnDisconnected    = null;
        OnError           = null;

        lock (_pendingRequests)
        {
            foreach (var tcs in _pendingRequests.Values)
                tcs.TrySetCanceled();
            _pendingRequests.Clear();
        }

        _cts?.Cancel();
        _cts?.Dispose();
        try { _ws?.Dispose(); } catch { }
    }
}
