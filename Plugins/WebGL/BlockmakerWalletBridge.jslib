/**
 * BlockmakerWalletBridge.jslib
 *
 * Unified wallet bridge for Unity WebGL.
 *
 * ── Connection ──────────────────────────────────────────────────────────────
 * ConnectWalletQR  — Opens a WalletConnect v2 session, generates a QR code
 *                    as a base64 PNG, sends it back to Unity, then waits for
 *                    the user to approve in their wallet app.
 *                    Works with Pera Wallet, Defly, and any WC v2 wallet.
 *
 * CancelWalletQR   — Cancels an in-progress QR connection.
 *
 * TryReconnect     — Silently restores a previous WC2 session on game load.
 *
 * ── Signing ─────────────────────────────────────────────────────────────────
 * SignTransaction  — Signs one transaction. Uses the live WC session when
 *                    connected via ConnectWalletQR; falls back to the Pera /
 *                    Defly SDK instances if those SDKs were loaded instead.
 *
 * Disconnect       — Disconnects the current wallet session.
 *
 * ── Required scripts in your WebGL index.html ────────────────────────────
 * No extra script tags needed — QRCode and SignClient are loaded on-demand
 * via dynamic import the first time ConnectWalletQR is called.
 *
 * ── C# entry points ─────────────────────────────────────────────────────────
 * BlockmakerWalletBridge.ConnectWalletQR(projectId, walletHint, goName, qrCb, successCb, errorCb)
 * BlockmakerWalletBridge.CancelWalletQR()
 * BlockmakerWalletBridge.TryReconnect(provider, goName, successCb)
 * BlockmakerWalletBridge.SignTransaction(provider, txnBase64, goName, successCb, errorCb)
 * BlockmakerWalletBridge.Disconnect(provider)
 *
 * ── Pera (official @perawallet/connect — WebGL Pera path) ──────────────────
 * PeraJsConnect      — Connects via Pera's own browser SDK. Pera renders its
 *                      OWN connect modal (QR on desktop, deep links on mobile),
 *                      so no QR is sent back to Unity on this path.
 * PeraJsReconnect    — Silently restores Pera's localStorage session on load.
 * PeraJsHasSession   — 1 when a live Pera JS session exists, 0 otherwise.
 * PeraJsSignTransaction / PeraJsSignGroupTransaction
 *                    — Sign via the Pera JS session (decodes unsigned txns
 *                      with algosdk, as Pera's signTransaction API requires).
 * PeraJsDisconnect   — Ends the Pera JS session.
 *
 * ── Magic SDK (Email Wallet) ────────────────────────────────────────────────
 * MagicLoginWithEmail — Loads Magic SDK, starts email OTP login, returns
 *                       "Magic|address|email|didToken" on success.
 * MagicSignTransaction — Signs an Algorand transaction via Magic's client-side key.
 * MagicLogout          — Logs out of Magic and clears the session.
 * MagicTryRestore      — Checks if a Magic session is still active on load.
 *
 * ── xChain EVM (zero-bundle: EIP-6963 + EIP-1193, no SDK imports) ───────────
 * EvmDiscoverWallets   — Collects EIP-6963 wallet announcements (~300ms) and
 *                        sends "rdns|name;rdns|name;…" ('' = only the legacy
 *                        window.ethereum fallback, '!none' = no provider at all).
 * EvmConnect           — Connects a chosen (or last-used) EVM wallet via
 *                        eth_requestAccounts. Returns the raw EVM address —
 *                        C# derives the Algorand LogicSig address.
 * EvmTryRestore        — Silent session restore via eth_accounts (no popup).
 * EvmSignPersonal      — personal_sign for the wallet-login proof.
 * EvmSignTypedData     — eth_signTypedData_v4 over C#-built EIP-712 typed data;
 *                        returns the raw hex signature (C# parses it and
 *                        assembles the LogicSig-signed transaction bytes).
 * EvmDisconnect        — Clears xChain state.
 */
mergeInto(LibraryManager.library, {

  // ── Shared helper: Uint8Array → base64 (safe for any size) ────────────────
  $bmUint8ToBase64: function(u8) {
    var CHUNK = 0x8000;
    var parts = [];
    for (var i = 0; i < u8.length; i += CHUNK) {
      parts.push(String.fromCharCode.apply(null, u8.subarray(i, i + CHUNK)));
    }
    return btoa(parts.join(''));
  },

  // ── Shared helper: base64 → Uint8Array ────────────────────────────────────
  $bmBase64ToUint8: function(b64) {
    var binary = atob(b64);
    var bytes = new Uint8Array(binary.length);
    for (var i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    return bytes;
  },

  // ── Fullscreen helpers ────────────────────────────────────────────────────
  // Wallet popups (EVM browser extensions) and DOM overlays (Magic iframe)
  // are hidden or blocked in fullscreen. These helpers exit fullscreen
  // before wallet interactions and let the game re-enter afterward.

  $bmExitFullscreen: function() {
    var fsEl = document.fullscreenElement || document.webkitFullscreenElement;
    if (!fsEl) return Promise.resolve(false);
    window._bmWasFullscreen = true;
    window._bmFullscreenElement = fsEl;
    var exit = document.exitFullscreen || document.webkitExitFullscreen;
    if (!exit) return Promise.resolve(false);
    return exit.call(document).then(function() { return true; }).catch(function() { return false; });
  },

  $bmRestoreFullscreen: function() {
    if (!window._bmWasFullscreen) return;
    window._bmWasFullscreen = false;
    var el = window._bmFullscreenElement || document.querySelector('canvas');
    if (el && el.requestFullscreen) {
      el.requestFullscreen().catch(function() {});
    } else if (el && el.webkitRequestFullscreen) {
      el.webkitRequestFullscreen();
    }
  },

  /**
   * IsFullscreen — returns 1 if the browser is currently in fullscreen, 0 otherwise.
   */
  IsFullscreen: function() {
    return (document.fullscreenElement || document.webkitFullscreenElement) ? 1 : 0;
  },

  /**
   * ExitFullscreen — exits browser fullscreen mode. Safe to call when not in fullscreen.
   */
  ExitFullscreen__deps: ['$bmExitFullscreen'],
  ExitFullscreen: function() {
    bmExitFullscreen();
  },

  /**
   * RequestFullscreen — requests fullscreen on the Unity canvas.
   * Must be called in response to a user gesture (click/tap) or the browser will reject it.
   */
  RequestFullscreen: function() {
    var canvas = document.querySelector('canvas') || document.getElementById('unity-canvas');
    if (!canvas) return;
    if (canvas.requestFullscreen) canvas.requestFullscreen().catch(function() {});
    else if (canvas.webkitRequestFullscreen) canvas.webkitRequestFullscreen();
  },

  /**
   * BmIsMobileBrowser — returns 1 when the WebGL build is running in a mobile
   * browser (phone/tablet), 0 otherwise. Used to decide whether "Open in wallet
   * app" deep-link buttons should be shown next to the WalletConnect QR code.
   * Covers standard mobile UAs plus iPadOS 13+, which masquerades as desktop
   * Safari ("MacIntel") but exposes multi-touch.
   */
  BmIsMobileBrowser: function() {
    try {
      var ua = navigator.userAgent || navigator.vendor || '';
      if (/android|webos|iphone|ipad|ipod|blackberry|iemobile|opera mini|mobile|silk|kindle/i.test(ua)) return 1;
      if (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1) return 1;
    } catch (e) {}
    return 0;
  },

  // ── Lazy-load WalletConnect Sign Client ────────────────────────────────────
  // Prefers a page-vendored bundle over the CDN: when the hosting page ships
  // window.BmWCVendor ({ SignClient, QRCode } — a self-contained IIFE bundle
  // of @walletconnect/sign-client@2.17.3 + qrcode@1.5.4 loaded via a script
  // tag before the Unity loader), it is used directly with no network fetch —
  // immune to ad-blockers, DNS filters and CDN outages. NFTurbo ships it as
  // TemplateData/bm-wc-vendor.js in its WebGL template. Falls back to the
  // pinned jsDelivr +esm dynamic import otherwise, so consumers without the
  // vendor file keep working unchanged. (The vendor bundle also assigns the
  // global QRCode, which $loadQRCode below already resolves first.)
  $loadSignClient: function() {
    if (window.BmWCVendor && window.BmWCVendor.SignClient) {
      window._bmSignClientClass = window.BmWCVendor.SignClient;
      window._bmSignClientFailed = false;
      window._bmSignClientPromise = Promise.resolve(window.BmWCVendor.SignClient);
      return window._bmSignClientPromise;
    }
    if (window._bmSignClientPromise && !window._bmSignClientFailed) return window._bmSignClientPromise;
    window._bmSignClientFailed = false;
    window._bmSignClientPromise =
      import('https://cdn.jsdelivr.net/npm/@walletconnect/sign-client@2.17.3/+esm')
        .then(function(mod) {
          var SC = mod.SignClient || (mod.default && mod.default.SignClient);
          if (!SC) throw new Error('SignClient not found in module');
          window._bmSignClientClass = SC;
          return SC;
        })
        .catch(function(err) {
          window._bmSignClientFailed = true;
          throw err;
        });
    return window._bmSignClientPromise;
  },

  // ── Lazy-load QRCode library ───────────────────────────────────────────────
  $loadQRCode: function() {
    if (typeof QRCode !== 'undefined') return Promise.resolve(QRCode);
    if (window._bmQRCodePromise && !window._bmQRCodeFailed) return window._bmQRCodePromise;
    window._bmQRCodeFailed = false;
    window._bmQRCodePromise = new Promise(function(resolve, reject) {
      var s    = document.createElement('script');
      s.src    = 'https://cdn.jsdelivr.net/npm/qrcode@1.5.4/build/qrcode.min.js';
      s.onload = function() { resolve(QRCode); };
      s.onerror = function() { window._bmQRCodeFailed = true; reject(new Error('Failed to load QRCode library')); };
      document.head.appendChild(s);
    });
    return window._bmQRCodePromise;
  },

  /**
   * ConnectWalletQR
   *
   * 1. Loads WalletConnect SignClient + QRCode library.
   * 2. Creates a WC v2 session for the Algorand mainnet namespace.
   * 3. Converts the pairing URI into a base64 PNG QR code.
   * 4. Sends  "<walletHint>|<wcUri>|<base64PNG>"  to Unity via qrCb.
   *    Unity displays the QR inside its own UI.
   * 5. Waits for the user to approve in Pera / Defly.
   * 6. Sends  "<walletHint>:<address>"  to Unity via successCb.
   *    On failure sends the error message via errorCb.
   *
   * @param {string} projectId   - WalletConnect Cloud project ID
   * @param {string} walletHint  - "Pera" or "Defly" (display label only)
   */
  ConnectWalletQR__deps: ['$loadSignClient', '$loadQRCode'],
  ConnectWalletQR: function(projectIdPtr, walletHintPtr, gameObjectNamePtr, qrCbPtr, successCbPtr, errorCbPtr) {
    var projectId      = UTF8ToString(projectIdPtr);
    var walletHint     = UTF8ToString(walletHintPtr);
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var qrCb           = UTF8ToString(qrCbPtr);
    var successCb      = UTF8ToString(successCbPtr);
    var errorCb        = UTF8ToString(errorCbPtr);

    var ALGORAND_CHAIN = 'algorand:wGHE2Pwdvd7S12BL5FaOP20EGYesN73k'; // mainnet

    // Cancel any previous in-progress connection
    if (window._bmCancelQR) { window._bmCancelQR(); window._bmCancelQR = null; }

    var cancelled = false;
    window._bmCancelQR = function() { cancelled = true; };

    Promise.all([
      loadSignClient(),
      loadQRCode()
    ])
    .then(function(results) {
      if (cancelled) return;
      if (window._bmWCClient) return window._bmWCClient;
      var SignClient = results[0];
      return SignClient.init({
        projectId: projectId,
        metadata: {
          name:        'Blockmaker',
          description: 'Blockmaker Unity Game',
          url:         window.location.origin,
          icons:       []
        }
      });
    })
    .then(function(client) {
      if (cancelled) return;
      if (!window._bmWCClient) window._bmWCClient = client;
      client = window._bmWCClient;

      return client.connect({
        requiredNamespaces: {
          algorand: {
            methods: ['algo_signTxn'],
            chains:  [ALGORAND_CHAIN],
            events:  []
          }
        }
      });
    })
    .then(function(result) {
      if (cancelled) return;
      var uri      = result.uri;
      var approval = result.approval;

      return QRCode.toDataURL(uri, {
        width:                256,
        margin:               2,
        errorCorrectionLevel: 'M',
        color: { dark: '#0f0f1c', light: '#ffffff' }
      }).then(function(dataUrl) {
        if (cancelled) return;
        // Strip "data:image/png;base64," prefix — Unity only needs the raw bytes
        var b64 = dataUrl.replace(/^data:image\/png;base64,/, '');
        // Send QR to Unity: "Pera|wc:xxxx...|<base64>"
        SendMessage(gameObjectName, qrCb, walletHint + '|' + uri + '|' + b64);
        return approval();
      });
    })
    .then(function(session) {
      if (!session || cancelled) return;
      window._bmWCSession      = session;
      window._bmWCSessionTopic = session.topic;
      window._bmCancelQR       = null;

      // Extract Algorand address — accounts are "algorand:chainId:address"
      var nsAccounts = (session.namespaces.algorand || {}).accounts || [];
      if (nsAccounts.length === 0) {
        SendMessage(gameObjectName, errorCb, 'No Algorand accounts returned by wallet.');
        return;
      }
      var address = nsAccounts[0].split(':').pop();
      console.log('[BlockmakerWalletBridge] WalletConnect session established:', address);
      SendMessage(gameObjectName, successCb, walletHint + ':' + address);
    })
    .catch(function(err) {
      if (cancelled) return;
      window._bmCancelQR = null;
      var msg = (err && err.message) ? err.message : 'WalletConnect failed.';
      console.error('[BlockmakerWalletBridge] ConnectWalletQR error:', msg);
      SendMessage(gameObjectName, errorCb, msg);
    });
  },

  /**
   * CancelWalletQR
   * Cancels any in-progress ConnectWalletQR call without sending an error.
   */
  CancelWalletQR: function() {
    if (window._bmCancelQR) {
      window._bmCancelQR();
      window._bmCancelQR = null;
    }
    // Disconnect pending pairing if approval hasn't resolved
    if (window._bmWCClient && window._bmWCSessionTopic == null) {
      try {
        var active = window._bmWCClient.pairing.getAll({ active: true }) || [];
        active.forEach(function(p) {
          window._bmWCClient.pairing.delete(p.topic, { code: 6000, message: 'User cancelled' })
            .catch(function() {});
        });
      } catch(e) {}
    }
  },

  /**
   * Disconnect — ends the active session.
   * provider is accepted for API consistency but WC v2 has one session at a time.
   */
  Disconnect__deps: ['CancelWalletQR', 'PeraJsDisconnect'],
  Disconnect: function(providerPtr) {
    var provider = UTF8ToString(providerPtr);
    _CancelWalletQR();
    // Also end the official Pera JS SDK session if one exists (WebGL Pera path)
    _PeraJsDisconnect();
    if (window._bmWCClient && window._bmWCSessionTopic) {
      try {
        window._bmWCClient.disconnect({
          topic:  window._bmWCSessionTopic,
          reason: { code: 6000, message: 'User disconnected' }
        }).catch(function() {});
      } catch(e) {}
      window._bmWCSession      = null;
      window._bmWCSessionTopic = null;
      console.log('[BlockmakerWalletBridge] Disconnected (' + provider + ').');
    }
    // Also disconnect Pera / Defly SDK instances if they were used
    if (window._peraInstance)  try { window._peraInstance.disconnect();  } catch(e) {}
    if (window._deflyInstance) try { window._deflyInstance.disconnect(); } catch(e) {}
  },

  /**
   * TryReconnect — silently restore a previous WC v2 session on game load.
   * If successful, successCb receives "Provider:address".
   */
  TryReconnect: function(providerPtr, gameObjectNamePtr, successCbPtr, errorCbPtr) {
    var provider       = UTF8ToString(providerPtr);
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var successCb      = UTF8ToString(successCbPtr);
    var errorCb        = UTF8ToString(errorCbPtr);

    if (!window._bmWCClient) {
      SendMessage(gameObjectName, errorCb, 'No previous session found.');
      return;
    }

    try {
      var sessions = window._bmWCClient.session.getAll() || [];
      if (sessions.length === 0) {
        SendMessage(gameObjectName, errorCb, 'No previous session found.');
        return;
      }

      var session = sessions[sessions.length - 1];
      var now = Math.floor(Date.now() / 1000);
      if (session.expiry && session.expiry < now) {
        SendMessage(gameObjectName, errorCb, 'Previous session has expired.');
        return;
      }
      var nsAccounts = (session.namespaces.algorand || {}).accounts || [];
      if (nsAccounts.length === 0) {
        SendMessage(gameObjectName, errorCb, 'No accounts in previous session.');
        return;
      }

      var address = nsAccounts[0].split(':').pop();
      window._bmWCSession      = session;
      window._bmWCSessionTopic = session.topic;
      console.log('[BlockmakerWalletBridge] WC session restored:', address);
      SendMessage(gameObjectName, successCb, provider + ':' + address);
    } catch(e) {
      SendMessage(gameObjectName, errorCb, 'Session restore failed: ' + (e.message || e));
    }
  },

  /**
   * SignTransaction — sign a single unsigned msgpack transaction.
   * Uses the live WC v2 session when connected via ConnectWalletQR.
   * txnBase64: base64-encoded unsigned transaction bytes.
   * On success: successCb("base64SignedTxn")
   * On error:   errorCb("error message")
   */
  SignTransaction__deps: ['$bmUint8ToBase64', '$bmBase64ToUint8'],
  SignTransaction: function(providerPtr, txnBase64Ptr, gameObjectNamePtr, successCbPtr, errorCbPtr) {
    var provider       = UTF8ToString(providerPtr);
    var txnBase64      = UTF8ToString(txnBase64Ptr);
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var successCb      = UTF8ToString(successCbPtr);
    var errorCb        = UTF8ToString(errorCbPtr);

    // ── Path A: WalletConnect v2 direct session ──
    if (window._bmWCClient && window._bmWCSessionTopic) {
      window._bmWCClient.request({
        topic:   window._bmWCSessionTopic,
        chainId: 'algorand:wGHE2Pwdvd7S12BL5FaOP20EGYesN73k',
        request: {
          method: 'algo_signTxn',
          params: [[{ txn: txnBase64 }]]
        }
      })
      .then(function(result) {
        // result is an array of base64-signed txns or null entries
        var signed = result[0];
        if (!signed) { SendMessage(gameObjectName, errorCb, 'Wallet declined to sign.'); return; }
        SendMessage(gameObjectName, successCb, signed);
      })
      .catch(function(err) {
        var msg = (err && err.message) ? err.message : 'Signing failed.';
        SendMessage(gameObjectName, errorCb, msg);
      });
      return;
    }

    // ── Path B: Pera / Defly SDK fallback ───────────────────────────────────
    var wallet = null;
    var p = provider.toLowerCase();
    if (p === 'pera'  && window._peraInstance)  wallet = window._peraInstance;
    if (p === 'defly' && window._deflyInstance) wallet = window._deflyInstance;

    if (!wallet) {
      SendMessage(gameObjectName, errorCb, provider + ' wallet not connected.');
      return;
    }

    var bytes = bmBase64ToUint8(txnBase64);

    wallet.signTransaction([[{ txn: bytes }]])
      .then(function(signedTxns) {
        var b64 = bmUint8ToBase64(new Uint8Array(signedTxns[0]));
        SendMessage(gameObjectName, successCb, b64);
      })
      .catch(function(err) {
        var msg = (err && err.message) ? err.message : provider + ' signing failed.';
        SendMessage(gameObjectName, errorCb, msg);
      });
  },

  /**
   * SignGroupTransaction — sign a group of unsigned msgpack transactions atomically.
   * txnsJsonPtr: JSON string — array of base64-encoded unsigned txn bytes.
   * On success: successCb(JSON array of base64 signed txns)
   * On error:   errorCb("error message")
   */
  SignGroupTransaction__deps: ['$bmUint8ToBase64', '$bmBase64ToUint8'],
  SignGroupTransaction: function(providerPtr, txnsJsonPtr, gameObjectNamePtr, successCbPtr, errorCbPtr) {
    var provider       = UTF8ToString(providerPtr);
    var txnsJson       = UTF8ToString(txnsJsonPtr);
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var successCb      = UTF8ToString(successCbPtr);
    var errorCb        = UTF8ToString(errorCbPtr);

    var b64Array;
    try { b64Array = JSON.parse(txnsJson); }
    catch(e) { SendMessage(gameObjectName, errorCb, 'Invalid transaction data.'); return; }

    if (!Array.isArray(b64Array) || b64Array.length === 0) {
      SendMessage(gameObjectName, errorCb, 'No transactions provided.');
      return;
    }

    var txnParams = b64Array.map(function(b64) { return { txn: b64 }; });
    var expected = b64Array.length;

    // ── Path A: WalletConnect v2 direct session ──
    if (window._bmWCClient && window._bmWCSessionTopic) {
      window._bmWCClient.request({
        topic:   window._bmWCSessionTopic,
        chainId: 'algorand:wGHE2Pwdvd7S12BL5FaOP20EGYesN73k',
        request: {
          method: 'algo_signTxn',
          params: [txnParams]
        }
      })
      .then(function(result) {
        if (!Array.isArray(result) || result.length !== expected) {
          SendMessage(gameObjectName, errorCb, 'Wallet returned unexpected number of signatures.');
          return;
        }
        for (var i = 0; i < result.length; i++) {
          if (!result[i]) {
            SendMessage(gameObjectName, errorCb, 'Wallet declined to sign transaction ' + (i + 1) + ' of ' + expected + '.');
            return;
          }
        }
        SendMessage(gameObjectName, successCb, JSON.stringify(result));
      })
      .catch(function(err) {
        var msg = (err && err.message) ? err.message : 'Signing failed.';
        SendMessage(gameObjectName, errorCb, msg);
      });
      return;
    }

    // ── Path B: Pera / Defly SDK fallback ───────────────────────────────────
    var wallet = null;
    var p = provider.toLowerCase();
    if (p === 'pera'  && window._peraInstance)  wallet = window._peraInstance;
    if (p === 'defly' && window._deflyInstance) wallet = window._deflyInstance;

    if (!wallet) {
      SendMessage(gameObjectName, errorCb, provider + ' wallet not connected.');
      return;
    }

    var txnGroup = b64Array.map(function(b64) { return { txn: bmBase64ToUint8(b64) }; });

    wallet.signTransaction([txnGroup])
      .then(function(signedTxns) {
        var out = [];
        for (var i = 0; i < signedTxns.length; i++) {
          if (!signedTxns[i]) {
            SendMessage(gameObjectName, errorCb, 'Wallet declined to sign transaction ' + (i + 1) + ' of ' + expected + '.');
            return;
          }
          out.push(bmUint8ToBase64(new Uint8Array(signedTxns[i])));
        }
        SendMessage(gameObjectName, successCb, JSON.stringify(out));
      })
      .catch(function(err) {
        var msg = (err && err.message) ? err.message : provider + ' signing failed.';
        SendMessage(gameObjectName, errorCb, msg);
      });
  },

  // ══════════════════════════════════════════════════════════════════════════
  // ══  Pera official JS SDK (@perawallet/connect) — WebGL Pera path  ════════
  // ══════════════════════════════════════════════════════════════════════════
  //
  // Pera Wallet speaks WalletConnect v1 only, and the SDK's native WCv1 client
  // (System.Net.WebSockets) cannot run on WebGL. @perawallet/connect is Pera's
  // own browser SDK: it talks Pera's v1 bridges, renders Pera's own connect
  // modal (QR on desktop, built-in deep links on mobile) and persists its
  // session in localStorage (PeraJsReconnect restores it after a page reload).
  //
  // Pinned CDN builds (jsDelivr +esm dynamic imports, same pattern as the
  // loaders above — the package ships no UMD build):
  //   @perawallet/connect@1.5.2 — named export PeraWalletConnect
  //   algosdk@3.5.2             — the EXACT version the Pera +esm bundle itself
  //                               imports, so both resolve to one shared module
  //                               instance and decodeUnsignedTransaction yields
  //                               the Transaction type Pera's signer expects.

  $loadPeraConnect: function() {
    // Vendored bundle shipped with the build (TemplateData/bm-pera-vendor.js) —
    // zero-network path; the pinned jsDelivr import below is only a fallback.
    if (window.BmPeraVendor && window.BmPeraVendor.PeraWalletConnect) {
      window._bmPeraWalletClass = window.BmPeraVendor.PeraWalletConnect;
      return Promise.resolve(window.BmPeraVendor.PeraWalletConnect);
    }
    if (window._bmPeraConnectPromise && !window._bmPeraConnectFailed) return window._bmPeraConnectPromise;
    window._bmPeraConnectFailed = false;
    window._bmPeraConnectPromise =
      import('https://cdn.jsdelivr.net/npm/@perawallet/connect@1.5.2/+esm')
        .then(function(mod) {
          var PWC = mod.PeraWalletConnect || (mod.default && mod.default.PeraWalletConnect);
          if (!PWC) throw new Error('PeraWalletConnect not found in module');
          window._bmPeraWalletClass = PWC;
          return PWC;
        })
        .catch(function(err) {
          window._bmPeraConnectFailed = true;
          throw err;
        });
    return window._bmPeraConnectPromise;
  },

  $loadAlgosdk: function() {
    if (window.BmPeraVendor && window.BmPeraVendor.algosdk) {
      window._bmAlgosdk = window.BmPeraVendor.algosdk;
      return Promise.resolve(window.BmPeraVendor.algosdk);
    }
    if (window._bmAlgosdkPromise && !window._bmAlgosdkFailed) return window._bmAlgosdkPromise;
    window._bmAlgosdkFailed = false;
    window._bmAlgosdkPromise =
      import('https://cdn.jsdelivr.net/npm/algosdk@3.5.2/+esm')
        .then(function(mod) {
          var sdk = (mod && mod.decodeUnsignedTransaction) ? mod
                  : (mod.default && mod.default.decodeUnsignedTransaction) ? mod.default
                  : null;
          if (!sdk) throw new Error('algosdk.decodeUnsignedTransaction not found in module');
          window._bmAlgosdk = sdk;
          return sdk;
        })
        .catch(function(err) {
          window._bmAlgosdkFailed = true;
          throw err;
        });
    return window._bmAlgosdkPromise;
  },

  $getPeraWallet__deps: ['$loadPeraConnect'],
  $getPeraWallet: function() {
    return loadPeraConnect().then(function(PeraWalletConnect) {
      if (!window._bmPeraWallet) {
        window._bmPeraWallet = new PeraWalletConnect({ shouldShowSignTxnToast: false });
      }
      return window._bmPeraWallet;
    });
  },

  // Wire Pera's WC v1 connector 'disconnect' event once per connector instance
  // so a wallet-side disconnect clears our live-session flag.
  $bmPeraWireDisconnect: function(wallet) {
    try {
      var connector = wallet.connector;
      if (connector && connector.on && !connector._bmDisconnectWired) {
        connector._bmDisconnectWired = true;
        connector.on('disconnect', function() {
          window._bmPeraConnected = false;
          console.log('[BlockmakerWalletBridge] Pera JS session disconnected by wallet.');
        });
      }
    } catch(e) {}
  },

  // Resolve a wallet with a live session: use the current one, otherwise try a
  // silent localStorage restore. Rejects with 'PERA_NOT_CONNECTED' when neither
  // exists (mapped to a friendly message by bmPeraSignError).
  $bmPeraEnsureSession__deps: ['$getPeraWallet', '$bmPeraWireDisconnect'],
  $bmPeraEnsureSession: function() {
    return getPeraWallet().then(function(wallet) {
      if (window._bmPeraConnected) return wallet;
      return wallet.reconnectSession()
        .then(function(accounts) {
          if (!accounts || accounts.length === 0) throw new Error('PERA_NOT_CONNECTED');
          window._bmPeraConnected = true;
          bmPeraWireDisconnect(wallet);
          return wallet;
        })
        .catch(function() { throw new Error('PERA_NOT_CONNECTED'); });
    });
  },

  // Map Pera SDK errors to the same user-facing tone the other signers use.
  $bmPeraSignError: function(err) {
    var msg  = (err && err.message) ? err.message : 'Pera signing failed.';
    var type = (err && err.data && err.data.type) ? err.data.type : '';
    if (msg === 'PERA_NOT_CONNECTED')
      return 'Your Pera wallet is not connected. Please connect your wallet again to continue.';
    if (type === 'SIGN_TXN_CANCELLED' || /reject|cancel|denied|declined|closed by user/i.test(msg))
      return 'The transaction was not approved in your wallet. Please try again.';
    return msg;
  },

  /**
   * PeraJsConnect — connect via Pera's official browser SDK, HEADLESS.
   * Pera's own DOM modal is suppressed (browser element-fullscreen hides DOM
   * overlays, and sign-in must never leave fullscreen). Instead the WC v1 URI
   * is read off the connector and sent to Unity ("Pera|<uri>|<qrB64>") for the
   * usual in-canvas QR + mobile deep-link button. The lib still runs the whole
   * v1 protocol, bridges and session storage (Pera-founder-recommended path).
   * On success: successCb("<AlgorandAddress>"). On error: errorCb(message) —
   * "PERA_CONNECT_CANCELLED" when the pairing was abandoned.
   */
  PeraJsConnect__deps: ['$getPeraWallet', '$bmPeraWireDisconnect', '$loadQRCode'],
  PeraJsConnect: function(gameObjectNamePtr, successCbPtr, errorCbPtr, qrCbPtr) {
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var successCb      = UTF8ToString(successCbPtr);
    var errorCb        = UTF8ToString(errorCbPtr);
    var qrCb           = qrCbPtr ? UTF8ToString(qrCbPtr) : '';

    // Kill Pera's modal before it can ever render (idempotent).
    if (!window._bmPeraModalKiller) {
      var killer = document.createElement('style');
      killer.textContent = '#pera-wallet-connect-modal-wrapper{display:none !important;}';
      document.head.appendChild(killer);
      window._bmPeraModalKiller = killer;
    }

    var qrPoll = null;
    function stopQrPoll() { if (qrPoll) { clearInterval(qrPoll); qrPoll = null; } }

    getPeraWallet()
    .then(function(wallet) {
      // Silent restore first — but only trust it when the underlying WC v1
      // connector is actually live; stale localStorage otherwise fakes a
      // connect with a dead transport and no QR is ever shown.
      return wallet.reconnectSession()
        .catch(function() { return []; })
        .then(function(accounts) {
          if (accounts && accounts.length > 0 &&
              wallet.connector && wallet.connector.connected) return accounts;

          var connectPromise = wallet.connect();

          // The v1 pairing URI appears on the connector right after connect()
          // starts. Poll briefly, then hand it to Unity for the in-canvas QR.
          if (qrCb) {
            var tries = 0;
            qrPoll = setInterval(function() {
              tries++;
              var uri = wallet.connector && wallet.connector.uri;
              if (uri) {
                stopQrPoll();
                loadQRCode().then(function(QR) {
                  return QR.toDataURL(uri, {
                    width:                256,
                    margin:               2,
                    errorCorrectionLevel: 'M',
                    color: { dark: '#0f0f1c', light: '#ffffff' }
                  });
                }).then(function(dataUrl) {
                  var b64 = dataUrl.replace(/^data:image\/png;base64,/, '');
                  SendMessage(gameObjectName, qrCb, 'Pera|' + uri + '|' + b64);
                }).catch(function(qErr) {
                  console.warn('[BlockmakerWalletBridge] Pera QR render failed, sending URI only:', qErr);
                  SendMessage(gameObjectName, qrCb, 'Pera|' + uri + '|');
                });
              } else if (tries > 100) {
                stopQrPoll();
                console.warn('[BlockmakerWalletBridge] Pera WC v1 URI never appeared on the connector.');
              }
            }, 100);
          }

          return connectPromise.then(
            function(accounts2) { stopQrPoll(); return accounts2; },
            function(err)       { stopQrPoll(); throw err; }
          );
        })
        .then(function(accounts) {
          if (!accounts || accounts.length === 0) throw new Error('No Algorand accounts returned by Pera.');
          window._bmPeraConnected = true;
          bmPeraWireDisconnect(wallet);
          console.log('[BlockmakerWalletBridge] Pera JS session established:', accounts[0]);
          SendMessage(gameObjectName, successCb, accounts[0]);
        });
    })
    .catch(function(err) {
      stopQrPoll();
      var type = (err && err.data && err.data.type) ? err.data.type : '';
      var msg  = (err && err.message) ? err.message : 'Pera connection failed.';
      if (type === 'CONNECT_MODAL_CLOSED' || /closed by user/i.test(msg)) msg = 'PERA_CONNECT_CANCELLED';
      console.error('[BlockmakerWalletBridge] PeraJsConnect error:', msg);
      SendMessage(gameObjectName, errorCb, msg);
    });
  },

  /**
   * PeraJsReconnect — silently restore Pera's localStorage session on load.
   * On success: successCb("Pera:<address>") — same payload shape as
   * TryReconnect, so the existing C# reconnect receivers are reused.
   * On error: errorCb("No previous Pera session found.").
   */
  PeraJsReconnect__deps: ['$getPeraWallet', '$bmPeraWireDisconnect'],
  PeraJsReconnect: function(gameObjectNamePtr, successCbPtr, errorCbPtr) {
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var successCb      = UTF8ToString(successCbPtr);
    var errorCb        = UTF8ToString(errorCbPtr);

    getPeraWallet()
    .then(function(wallet) {
      return wallet.reconnectSession().then(function(accounts) {
        if (!accounts || accounts.length === 0 ||
            !(wallet.connector && wallet.connector.connected)) {
          SendMessage(gameObjectName, errorCb, 'No previous Pera session found.');
          return;
        }
        window._bmPeraConnected = true;
        bmPeraWireDisconnect(wallet);
        console.log('[BlockmakerWalletBridge] Pera JS session restored:', accounts[0]);
        SendMessage(gameObjectName, successCb, 'Pera:' + accounts[0]);
      });
    })
    .catch(function(err) {
      var msg = (err && err.message) ? err.message : 'No previous Pera session found.';
      SendMessage(gameObjectName, errorCb, msg);
    });
  },

  /**
   * PeraJsHasSession — 1 when a live Pera JS session exists, 0 otherwise.
   * Used by the C# signing path to route Pera signs to the Pera JS signer.
   */
  PeraJsHasSession: function() {
    return (window._bmPeraWallet && window._bmPeraConnected) ? 1 : 0;
  },

  /**
   * PeraJsSignTransaction — sign one unsigned msgpack txn via the Pera JS
   * session. txnBase64: base64-encoded unsigned transaction bytes.
   * On success: successCb("base64SignedTxn"). On error: errorCb(message).
   */
  PeraJsSignTransaction__deps: ['$bmPeraEnsureSession', '$loadAlgosdk', '$bmPeraSignError', '$bmUint8ToBase64', '$bmBase64ToUint8'],
  PeraJsSignTransaction: function(txnBase64Ptr, gameObjectNamePtr, successCbPtr, errorCbPtr) {
    var txnBase64      = UTF8ToString(txnBase64Ptr);
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var successCb      = UTF8ToString(successCbPtr);
    var errorCb        = UTF8ToString(errorCbPtr);

    Promise.all([bmPeraEnsureSession(), loadAlgosdk()])
    .then(function(results) {
      var wallet  = results[0];
      var algosdk = results[1];
      var group = [{ txn: algosdk.decodeUnsignedTransaction(bmBase64ToUint8(txnBase64)) }];
      return wallet.signTransaction([group]);
    })
    .then(function(signed) {
      if (!signed || signed.length !== 1 || !signed[0]) {
        SendMessage(gameObjectName, errorCb, 'The transaction was not approved in your wallet. Please try again.');
        return;
      }
      var raw = signed[0] instanceof Uint8Array ? signed[0] : new Uint8Array(signed[0]);
      SendMessage(gameObjectName, successCb, bmUint8ToBase64(raw));
    })
    .catch(function(err) {
      var msg = bmPeraSignError(err);
      console.error('[BlockmakerWalletBridge] PeraJsSignTransaction error:', msg);
      SendMessage(gameObjectName, errorCb, msg);
    });
  },

  /**
   * PeraJsSignGroupTransaction — sign a group of unsigned msgpack txns
   * atomically via the Pera JS session, preserving order.
   * txnsJsonPtr: JSON string — array of base64-encoded unsigned txn bytes
   * (same wire format as SignGroupTransaction above).
   * On success: successCb(JSON array of base64 signed txns).
   * On error:   errorCb(message).
   */
  PeraJsSignGroupTransaction__deps: ['$bmPeraEnsureSession', '$loadAlgosdk', '$bmPeraSignError', '$bmUint8ToBase64', '$bmBase64ToUint8'],
  PeraJsSignGroupTransaction: function(txnsJsonPtr, gameObjectNamePtr, successCbPtr, errorCbPtr) {
    var txnsJson       = UTF8ToString(txnsJsonPtr);
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var successCb      = UTF8ToString(successCbPtr);
    var errorCb        = UTF8ToString(errorCbPtr);

    var b64Array;
    try { b64Array = JSON.parse(txnsJson); }
    catch(e) { SendMessage(gameObjectName, errorCb, 'Invalid transaction data.'); return; }

    if (!Array.isArray(b64Array) || b64Array.length === 0) {
      SendMessage(gameObjectName, errorCb, 'No transactions provided.');
      return;
    }

    var expected = b64Array.length;

    Promise.all([bmPeraEnsureSession(), loadAlgosdk()])
    .then(function(results) {
      var wallet  = results[0];
      var algosdk = results[1];
      // One SignerTransaction group, in original order. Every txn is presented
      // for signing (no {signers: []} entries) — same all-or-nothing contract
      // as the WCv1 / WC v2 / bridge group-signing paths above.
      var group = b64Array.map(function(b64) {
        return { txn: algosdk.decodeUnsignedTransaction(bmBase64ToUint8(b64)) };
      });
      return wallet.signTransaction([group]);
    })
    .then(function(signed) {
      if (!Array.isArray(signed) || signed.length !== expected) {
        SendMessage(gameObjectName, errorCb, 'Wallet returned ' + (signed ? signed.length : 0) + ' signed transactions, expected ' + expected + '.');
        return;
      }
      var out = [];
      for (var i = 0; i < signed.length; i++) {
        if (!signed[i]) {
          SendMessage(gameObjectName, errorCb, 'Wallet declined to sign transaction ' + (i + 1) + ' of ' + expected + '.');
          return;
        }
        out.push(bmUint8ToBase64(signed[i] instanceof Uint8Array ? signed[i] : new Uint8Array(signed[i])));
      }
      SendMessage(gameObjectName, successCb, JSON.stringify(out));
    })
    .catch(function(err) {
      var msg = bmPeraSignError(err);
      console.error('[BlockmakerWalletBridge] PeraJsSignGroupTransaction error:', msg);
      SendMessage(gameObjectName, errorCb, msg);
    });
  },

  /**
   * PeraJsDisconnect — ends the Pera JS session and clears state.
   */
  PeraJsDisconnect: function() {
    window._bmPeraConnected = false;
    if (window._bmPeraWallet) {
      try { window._bmPeraWallet.disconnect().catch(function() {}); } catch(e) {}
      console.log('[BlockmakerWalletBridge] Pera JS session disconnected.');
    }
  },

  // ══════════════════════════════════════════════════════════════════════════
  // ══  Magic SDK (Email Wallet)  ════════════════════════════════════════════
  // ══════════════════════════════════════════════════════════════════════════

  $loadMagicSDK: function() {
    if (window._bmMagicPromise && !window._bmMagicFailed) return window._bmMagicPromise;
    window._bmMagicFailed = false;
    window._bmMagicPromise = Promise.all([
      import('https://cdn.jsdelivr.net/npm/magic-sdk@33.7.1/+esm'),
      import('https://cdn.jsdelivr.net/npm/@magic-ext/algorand@26.2.0/+esm')
    ]).then(function(mods) {
      var Magic = mods[0].Magic || (mods[0].default && mods[0].default.Magic) || mods[0].default;
      var AlgorandExtension = mods[1].AlgorandExtension || (mods[1].default && mods[1].default.AlgorandExtension) || mods[1].default;
      if (!Magic) throw new Error('Magic class not found in module');
      if (!AlgorandExtension) throw new Error('AlgorandExtension not found in module');
      window._bmMagicClass = Magic;
      window._bmMagicAlgoExt = AlgorandExtension;
      return { Magic: Magic, AlgorandExtension: AlgorandExtension };
    }).catch(function(err) {
      window._bmMagicFailed = true;
      throw err;
    });
    return window._bmMagicPromise;
  },

  $getMagicInstance: function(apiKey) {
    if (window._bmMagicInstance && window._bmMagicApiKey === apiKey) {
      return window._bmMagicInstance;
    }
    if (!window._bmMagicClass || !window._bmMagicAlgoExt) {
      throw new Error('Magic SDK not loaded yet. Call _loadMagicSDK first.');
    }
    if (window._bmMagicInstance) {
      try { window._bmMagicInstance.user.logout(); } catch(e) {}
    }
    var ext = new window._bmMagicAlgoExt({ rpcUrl: 'https://mainnet-api.algonode.cloud' });
    window._bmMagicInstance = new window._bmMagicClass(apiKey, {
      extensions: [ext]
    });
    window._bmMagicApiKey = apiKey;
    return window._bmMagicInstance;
  },

  /**
   * MagicLoginWithEmail
   * Loads Magic SDK, starts email OTP login (Magic handles its own UI).
   * On success: successCb("Magic|algorandAddress|email|didToken")
   * On error:   errorCb("error message")
   */
  MagicLoginWithEmail__deps: ['$bmExitFullscreen', '$bmRestoreFullscreen', '$loadMagicSDK', '$getMagicInstance'],
  MagicLoginWithEmail: function(apiKeyPtr, emailPtr, gameObjectNamePtr, successCbPtr, errorCbPtr) {
    var apiKey         = UTF8ToString(apiKeyPtr);
    var email          = UTF8ToString(emailPtr);
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var successCb      = UTF8ToString(successCbPtr);
    var errorCb        = UTF8ToString(errorCbPtr);

    bmExitFullscreen()
    .then(function() { return loadMagicSDK(); })
    .then(function() {
      var magic = getMagicInstance(apiKey);
      return magic.auth.loginWithEmailOTP({ email: email });
    })
    .then(function(didToken) {
      var magic = window._bmMagicInstance;
      return Promise.all([
        Promise.resolve(didToken),
        magic.user.getInfo()
      ]);
    })
    .then(function(results) {
      var didToken = results[0];
      var userInfo = results[1];
      var address  = userInfo.publicAddress;
      if (!address) throw new Error('Magic did not return an Algorand address.');
      console.log('[BlockmakerWalletBridge] Magic login success:', email, address);
      bmRestoreFullscreen();
      SendMessage(gameObjectName, successCb, 'Magic|' + address + '|' + email + '|' + didToken);
    })
    .catch(function(err) {
      var msg = (err && err.message) ? err.message : 'Magic login failed.';
      console.error('[BlockmakerWalletBridge] MagicLoginWithEmail error:', msg);
      bmRestoreFullscreen();
      SendMessage(gameObjectName, errorCb, msg);
    });
  },

  /**
   * MagicSignTransaction
   * Signs a single Algorand transaction using the Magic client-side key.
   * txnBase64: base64-encoded unsigned transaction bytes.
   * On success: successCb("base64SignedTxn")
   * On error:   errorCb("error message")
   */
  MagicSignTransaction__deps: ['$bmUint8ToBase64', '$bmBase64ToUint8'],
  MagicSignTransaction: function(txnBase64Ptr, gameObjectNamePtr, successCbPtr, errorCbPtr) {
    var txnBase64      = UTF8ToString(txnBase64Ptr);
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var successCb      = UTF8ToString(successCbPtr);
    var errorCb        = UTF8ToString(errorCbPtr);

    if (!window._bmMagicInstance) {
      SendMessage(gameObjectName, errorCb, 'Magic SDK not initialized. Log in first.');
      return;
    }

    var magic = window._bmMagicInstance;

    var bytes = bmBase64ToUint8(txnBase64);

    magic.algorand.signTransaction(bytes)
    .then(function(signedBlob) {
      var raw = signedBlob instanceof Uint8Array ? signedBlob : new Uint8Array(signedBlob);
      var b64 = bmUint8ToBase64(raw);
      SendMessage(gameObjectName, successCb, b64);
    })
    .catch(function(err) {
      var msg = (err && err.message) ? err.message : 'Magic signing failed.';
      SendMessage(gameObjectName, errorCb, msg);
    });
  },

  /**
   * MagicSignGroupTransaction
   * Signs a group of Algorand transactions atomically using
   * magic.algorand.signGroupTransactionV2().
   * txnsJsonPtr: JSON string — array of base64-encoded unsigned txn bytes.
   * On success: successCb(JSON array of base64 signed txns)
   * On error:   errorCb("error message")
   */
  MagicSignGroupTransaction__deps: ['$bmUint8ToBase64', '$bmBase64ToUint8'],
  MagicSignGroupTransaction: function(txnsJsonPtr, gameObjectNamePtr, successCbPtr, errorCbPtr) {
    var txnsJson       = UTF8ToString(txnsJsonPtr);
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var successCb      = UTF8ToString(successCbPtr);
    var errorCb        = UTF8ToString(errorCbPtr);

    if (!window._bmMagicInstance) {
      SendMessage(gameObjectName, errorCb, 'Magic SDK not initialized. Log in first.');
      return;
    }

    var magic = window._bmMagicInstance;
    var b64Array;
    try { b64Array = JSON.parse(txnsJson); }
    catch(e) { SendMessage(gameObjectName, errorCb, 'Invalid transaction data.'); return; }

    if (!Array.isArray(b64Array) || b64Array.length === 0) {
      SendMessage(gameObjectName, errorCb, 'No transactions provided.');
      return;
    }

    var txnBytes = b64Array.map(function(b64) { return bmBase64ToUint8(b64); });
    var expected = txnBytes.length;

    var promise;
    try {
      if (typeof magic.algorand.signGroupTransactionV2 !== 'function') {
        throw new Error('signGroupTransactionV2 not available in this Magic SDK version.');
      }
      promise = magic.algorand.signGroupTransactionV2(txnBytes);
    } catch(e) {
      SendMessage(gameObjectName, errorCb, (e && e.message) ? e.message : 'Magic group signing failed.');
      return;
    }

    promise
    .then(function(signedResults) {
      if (!Array.isArray(signedResults) || signedResults.length !== expected) {
        SendMessage(gameObjectName, errorCb, 'Wallet returned ' + (signedResults ? signedResults.length : 0) + ' signed transactions, expected ' + expected + '.');
        return;
      }
      var out = [];
      for (var i = 0; i < signedResults.length; i++) {
        var blob = signedResults[i];
        if (blob == null) {
          SendMessage(gameObjectName, errorCb, 'Wallet declined to sign transaction ' + (i + 1) + ' of ' + expected + '.');
          return;
        }
        if (blob.blob) blob = blob.blob;
        var raw = blob instanceof Uint8Array ? blob : new Uint8Array(blob);
        out.push(bmUint8ToBase64(raw));
      }
      SendMessage(gameObjectName, successCb, JSON.stringify(out));
    })
    .catch(function(err) {
      var msg = (err && err.message) ? err.message : 'Magic group signing failed.';
      SendMessage(gameObjectName, errorCb, msg);
    });
  },

  /**
   * MagicLogout — logs out of Magic and clears cached state.
   */
  MagicLogout: function() {
    if (window._bmMagicInstance) {
      window._bmMagicInstance.user.logout().catch(function() {});
      window._bmMagicInstance = null;
      window._bmMagicApiKey   = null;
    }
  },

  /**
   * MagicTryRestore — checks if a Magic session is still active.
   * If active: successCb("Magic|address|email")
   * If not:    errorCb("No active session")
   */
  MagicTryRestore__deps: ['$loadMagicSDK', '$getMagicInstance'],
  MagicTryRestore: function(apiKeyPtr, gameObjectNamePtr, successCbPtr, errorCbPtr) {
    var apiKey         = UTF8ToString(apiKeyPtr);
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var successCb      = UTF8ToString(successCbPtr);
    var errorCb        = UTF8ToString(errorCbPtr);

    loadMagicSDK()
    .then(function() {
      var magic = getMagicInstance(apiKey);
      return magic.user.isLoggedIn();
    })
    .then(function(isLoggedIn) {
      if (!isLoggedIn) {
        SendMessage(gameObjectName, errorCb, 'No active session');
        return;
      }
      return window._bmMagicInstance.user.getInfo().then(function(info) {
        var address = info.publicAddress;
        var email   = info.email || '';
        console.log('[BlockmakerWalletBridge] Magic session restored:', email, address);
        SendMessage(gameObjectName, successCb, 'Magic|' + address + '|' + email);
      });
    })
    .catch(function(err) {
      var msg = (err && err.message) ? err.message : 'Magic restore failed.';
      SendMessage(gameObjectName, errorCb, msg);
    });
  },

  // ══════════════════════════════════════════════════════════════════════════
  // ══  xChain EVM  ════════════════════════════════════════════════════════
  // ══════════════════════════════════════════════════════════════════════════
  //
  // Zero-bundle design: NO SDK imports. The C# side (XChainAddressDeriver /
  // EvmXChainIdentity) derives the Algorand LogicSig address, builds the
  // EIP-712 typed data and assembles the final LogicSig-signed transaction
  // bytes — these functions are a thin transport to the browser wallet's
  // EIP-1193 provider (account requests + signing only). Wallets are found
  // via EIP-6963 (multi-wallet safe, keyed by info.rdns which is stable
  // across page loads); window.ethereum remains as a legacy fallback when
  // nothing announces itself.

  // Collect EIP-6963 announcements into window._bmEvmProviders (rdns → {info,
  // provider}). Wallets re-announce on every 'eip6963:requestProvider'
  // dispatch, so this is safely re-runnable. Resolves with the map after
  // ~waitMs (the announce events are synchronous in practice, but the spec
  // doesn't require it).
  $bmEvmDiscover: function(waitMs) {
    if (!window._bmEvmProviders) window._bmEvmProviders = {};
    if (!window._bmEvmAnnounceWired) {
      window._bmEvmAnnounceWired = true;
      window.addEventListener('eip6963:announceProvider', function(e) {
        try {
          var d = e.detail;
          if (d && d.info && d.info.rdns && d.provider)
            window._bmEvmProviders[d.info.rdns] = d;
        } catch(err) {}
      });
    }
    try { window.dispatchEvent(new Event('eip6963:requestProvider')); } catch(e) {}
    return new Promise(function(resolve) {
      setTimeout(function() { resolve(window._bmEvmProviders); }, waitMs || 300);
    });
  },

  // Pick a provider entry ({info, provider}): explicit rdns first, then the
  // last-used rdns remembered in localStorage, then the first announced
  // wallet, then legacy window.ethereum. Returns null when no provider
  // exists at all. Call after bmEvmDiscover so the map is populated.
  $bmEvmPickProvider: function(rdns) {
    var map = window._bmEvmProviders || {};
    if (rdns && map[rdns]) return map[rdns];
    var last = null;
    try { last = localStorage.getItem('bm_evm_last_wallet_rdns'); } catch(e) {}
    if (last && map[last]) return map[last];
    var keys = Object.keys(map);
    if (keys.length > 0) return map[keys[0]];
    if (typeof window.ethereum !== 'undefined' && window.ethereum)
      return { info: null, provider: window.ethereum };
    return null;
  },

  // The provider chosen by EvmConnect / EvmTryRestore, falling back to
  // window.ethereum so signing still works if state was lost (e.g. a sign
  // request lands before any connect/restore ran).
  $bmEvmActiveProvider: function() {
    if (window._bmEvmProviderEntry && window._bmEvmProviderEntry.provider)
      return window._bmEvmProviderEntry.provider;
    if (typeof window.ethereum !== 'undefined' && window.ethereum) return window.ethereum;
    return null;
  },

  /**
   * EvmDiscoverWallets — collect EIP-6963 wallet announcements (~300ms window).
   * Callback payload: "rdns|name;rdns|name;…" — one entry per announced wallet;
   * '' when only the legacy window.ethereum fallback exists;
   * '!none' when no EVM provider is available at all.
   */
  EvmDiscoverWallets__deps: ['$bmEvmDiscover'],
  EvmDiscoverWallets: function(gameObjectNamePtr, callbackPtr) {
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var callback       = UTF8ToString(callbackPtr);

    bmEvmDiscover(300).then(function(map) {
      var keys = Object.keys(map);
      if (keys.length === 0) {
        var hasLegacy = (typeof window.ethereum !== 'undefined' && window.ethereum);
        SendMessage(gameObjectName, callback, hasLegacy ? '' : '!none');
        return;
      }
      var parts = [];
      for (var i = 0; i < keys.length; i++) {
        var info = map[keys[i]].info;
        // '|' and ';' are structural in the payload — strip them from names.
        var name = String(info.name || info.rdns).replace(/[|;]/g, ' ');
        parts.push(info.rdns + '|' + name);
      }
      SendMessage(gameObjectName, callback, parts.join(';'));
    }).catch(function(err) {
      // Discovery must never leave the C# side waiting on an unhandled rejection —
      // report "no provider" so the flow fails fast with a friendly message (the
      // C#-side timeout would eventually recover anyway, this is just quicker).
      console.error('[Blockmaker] EVM wallet discovery failed:', err);
      try { SendMessage(gameObjectName, callback, '!none'); } catch (_) {}
    });
  },

  /**
   * EvmConnect — connect an EVM wallet via eth_requestAccounts.
   * rdns selects a specific EIP-6963 wallet; pass '' to auto-pick (last-used
   * rdns from localStorage → first announced wallet → window.ethereum).
   * On success: successCb("0xEvmAddress") — the C# side derives the Algorand
   * LogicSig address. On error: errorCb(message).
   */
  EvmConnect__deps: ['$bmExitFullscreen', '$bmRestoreFullscreen', '$bmEvmDiscover', '$bmEvmPickProvider'],
  EvmConnect: function(rdnsPtr, gameObjectNamePtr, successCbPtr, errorCbPtr) {
    var rdns           = UTF8ToString(rdnsPtr);
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var successCb      = UTF8ToString(successCbPtr);
    var errorCb        = UTF8ToString(errorCbPtr);

    bmExitFullscreen()
    .then(function() { return bmEvmDiscover(300); })
    .then(function() {
      var entry = bmEvmPickProvider(rdns);
      if (!entry) throw new Error('No EVM wallet found. Please install one to continue.');
      return entry.provider.request({ method: 'eth_requestAccounts' })
        .then(function(accounts) {
          if (!accounts || accounts.length === 0) throw new Error('No EVM accounts returned.');
          var evmAddress = accounts[0];
          window._bmEvmProviderEntry = entry;
          window._bmEvmAddress       = evmAddress;
          try {
            if (entry.info && entry.info.rdns) localStorage.setItem('bm_evm_last_wallet_rdns', entry.info.rdns);
            else localStorage.removeItem('bm_evm_last_wallet_rdns');
          } catch(e) {}
          var label = (entry.info && entry.info.name) ? entry.info.name : 'window.ethereum';
          console.log('[BlockmakerWalletBridge] EVM wallet connected (' + label + '):', evmAddress);
          bmRestoreFullscreen();
          SendMessage(gameObjectName, successCb, evmAddress);
        });
    })
    .catch(function(err) {
      var msg = (err && err.message) ? err.message : 'EVM wallet connection failed.';
      console.error('[BlockmakerWalletBridge] EvmConnect error:', msg);
      bmRestoreFullscreen();
      SendMessage(gameObjectName, errorCb, msg);
    });
  },

  /**
   * EvmTryRestore — silently re-attach the EVM wallet after a page reload.
   * Re-discovers wallets (preferring the last-used rdns) and uses eth_accounts
   * (non-interactive) so no popup appears.
   * On success: successCb("0xEvmAddress"). On error: errorCb(message).
   */
  EvmTryRestore__deps: ['$bmEvmDiscover', '$bmEvmPickProvider'],
  EvmTryRestore: function(expectedEvmAddrPtr, gameObjectNamePtr, successCbPtr, errorCbPtr) {
    var expectedEvmAddr = UTF8ToString(expectedEvmAddrPtr);
    var gameObjectName  = UTF8ToString(gameObjectNamePtr);
    var successCb       = UTF8ToString(successCbPtr);
    var errorCb         = UTF8ToString(errorCbPtr);

    bmEvmDiscover(300)
    .then(function() {
      var entry = bmEvmPickProvider('');
      if (!entry) throw new Error('No EVM wallet found.');
      return entry.provider.request({ method: 'eth_accounts' })
        .then(function(accounts) {
          if (!accounts || accounts.length === 0) {
            throw new Error('EVM wallet not connected.');
          }
          var evmAddress = accounts[0];
          if (expectedEvmAddr && evmAddress.toLowerCase() !== expectedEvmAddr.toLowerCase()) {
            throw new Error('EVM wallet account changed.');
          }
          window._bmEvmProviderEntry = entry;
          window._bmEvmAddress       = evmAddress;
          console.log('[BlockmakerWalletBridge] EVM wallet session restored:', evmAddress);
          SendMessage(gameObjectName, successCb, evmAddress);
        });
    })
    .catch(function(err) {
      var msg = (err && err.message) ? err.message : 'EVM restore failed.';
      SendMessage(gameObjectName, errorCb, msg);
    });
  },

  /**
   * EvmSignPersonal — sign an arbitrary UTF-8 message with the EVM wallet via
   * personal_sign (sign-in proof). The message is hex-encoded; the wallet applies
   * EIP-191 framing. On success: successCb("0xHexSignature"); on error: errorCb(msg).
   */
  EvmSignPersonal__deps: ['$bmExitFullscreen', '$bmRestoreFullscreen', '$bmEvmActiveProvider'],
  EvmSignPersonal: function(messagePtr, evmAddressPtr, gameObjectNamePtr, successCbPtr, errorCbPtr) {
    var message        = UTF8ToString(messagePtr);
    var evmAddress     = UTF8ToString(evmAddressPtr);
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var successCb      = UTF8ToString(successCbPtr);
    var errorCb        = UTF8ToString(errorCbPtr);

    var provider = bmEvmActiveProvider();
    if (!provider) {
      SendMessage(gameObjectName, errorCb, 'No EVM wallet found.');
      return;
    }

    // Hex-encode the UTF-8 message bytes for personal_sign.
    var msgBytes = new TextEncoder().encode(message);
    var hex = '0x';
    for (var i = 0; i < msgBytes.length; i++) {
      hex += ('0' + msgBytes[i].toString(16)).slice(-2);
    }

    bmExitFullscreen()
    .then(function() {
      return provider.request({ method: 'personal_sign', params: [hex, evmAddress] });
    })
    .then(function(signature) {
      bmRestoreFullscreen();
      if (!signature) { SendMessage(gameObjectName, errorCb, 'Signature was empty or rejected.'); return; }
      SendMessage(gameObjectName, successCb, signature);
    })
    .catch(function(err) {
      var msg = (err && err.message) ? err.message : 'EVM wallet signing failed.';
      bmRestoreFullscreen();
      SendMessage(gameObjectName, errorCb, msg);
    });
  },

  /**
   * EvmSignTypedData — sign C#-built EIP-712 typed data (the xChain
   * "Algorand Transaction" envelope over a TxID or a 32-byte group id) via
   * eth_signTypedData_v4. Returns the RAW 0x-hex signature — the C# side
   * parses it (ParseEvmSignature) and assembles the LogicSig-signed
   * transaction bytes (BuildSignedTransaction).
   * On success: successCb("0xHexSignature"). On error: errorCb(message).
   */
  EvmSignTypedData__deps: ['$bmExitFullscreen', '$bmRestoreFullscreen', '$bmEvmActiveProvider'],
  EvmSignTypedData: function(evmAddressPtr, typedDataJsonPtr, gameObjectNamePtr, successCbPtr, errorCbPtr) {
    var evmAddress     = UTF8ToString(evmAddressPtr);
    var typedDataJson  = UTF8ToString(typedDataJsonPtr);
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var successCb      = UTF8ToString(successCbPtr);
    var errorCb        = UTF8ToString(errorCbPtr);

    var provider = bmEvmActiveProvider();
    if (!provider) {
      SendMessage(gameObjectName, errorCb, 'No EVM wallet found.');
      return;
    }

    bmExitFullscreen()
    .then(function() {
      return provider.request({ method: 'eth_signTypedData_v4', params: [evmAddress, typedDataJson] });
    })
    .then(function(signature) {
      bmRestoreFullscreen();
      if (!signature) { SendMessage(gameObjectName, errorCb, 'Signature was empty or rejected.'); return; }
      SendMessage(gameObjectName, successCb, signature);
    })
    .catch(function(err) {
      var msg = (err && err.message) ? err.message : 'EVM wallet signing failed.';
      bmRestoreFullscreen();
      SendMessage(gameObjectName, errorCb, msg);
    });
  },

  /**
   * EvmDisconnect — clears xChain EVM state (including the remembered
   * last-used wallet, so the next connect starts from a clean slate).
   */
  EvmDisconnect: function() {
    window._bmEvmProviderEntry = null;
    window._bmEvmAddress       = null;
    try { localStorage.removeItem('bm_evm_last_wallet_rdns'); } catch(e) {}
    console.log('[BlockmakerWalletBridge] xChain EVM disconnected.');
  }

});
