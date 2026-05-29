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
 * ── Magic SDK (Email Wallet) ────────────────────────────────────────────────
 * MagicLoginWithEmail — Loads Magic SDK, starts email OTP login, returns
 *                       "Magic|address|email|didToken" on success.
 * MagicSignTransaction — Signs an Algorand transaction via Magic's client-side key.
 * MagicLogout          — Logs out of Magic and clears the session.
 * MagicTryRestore      — Checks if a Magic session is still active on load.
 *
 * ── xChain EVM ──────────────────────────────────────────────────────────────
 * EvmConnect           — Connects an EVM wallet (window.ethereum), derives an
 *                        Algorand LogicSig address. Returns "EvmXChain|algoAddr|evmAddr".
 * EvmSignTransaction   — Converts an Algorand txn to EIP-712, signs via the EVM
 *                        wallet, returns a LogicSig-wrapped signed transaction.
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

  // ── Lazy-load WalletConnect Sign Client (ESM via jsDelivr) ─────────────────
  $loadSignClient: function() {
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
  Disconnect__deps: ['CancelWalletQR'],
  Disconnect: function(providerPtr) {
    var provider = UTF8ToString(providerPtr);
    _CancelWalletQR();
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

  $loadXChainSDK: function() {
    if (window._bmXChainPromise && !window._bmXChainFailed) return window._bmXChainPromise;
    window._bmXChainFailed = false;
    window._bmXChainPromise =
      import('https://cdn.jsdelivr.net/npm/@algorandfoundation/xchain-js/+esm')
        .then(function(mod) {
          window._bmXChain = mod;
          return mod;
        })
        .catch(function() {
          return import('https://cdn.jsdelivr.net/npm/algo-models/+esm')
            .then(function(mod) {
              window._bmXChain = mod;
              return mod;
            });
        })
        .catch(function(err) {
          window._bmXChainFailed = true;
          throw err;
        });
    return window._bmXChainPromise;
  },

  /**
   * EvmConnect
   * Connects an EVM wallet (window.ethereum), derives a deterministic Algorand
   * LogicSig address from the EVM address using xChain Accounts.
   * On success: successCb("EvmXChain|algorandAddress|evmAddress")
   * On error:   errorCb("error message")
   */
  EvmConnect__deps: ['$bmExitFullscreen', '$bmRestoreFullscreen', '$loadXChainSDK'],
  EvmConnect: function(gameObjectNamePtr, successCbPtr, errorCbPtr) {
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var successCb      = UTF8ToString(successCbPtr);
    var errorCb        = UTF8ToString(errorCbPtr);

    if (typeof window.ethereum === 'undefined') {
      SendMessage(gameObjectName, errorCb, 'No EVM wallet found. Please install one to continue.');
      return;
    }

    var evmAddress = null;

    bmExitFullscreen()
    .then(function() { return window.ethereum.request({ method: 'eth_requestAccounts' }); })
    .then(function(accounts) {
      if (!accounts || accounts.length === 0) throw new Error('No EVM accounts returned.');
      evmAddress = accounts[0];
      return loadXChainSDK();
    })
    .then(function(xchain) {
      var deriveAddress = xchain.deriveAddress || xchain.getAlgorandAddress || xchain.default?.deriveAddress;
      if (!deriveAddress) throw new Error('xChain SDK: address derivation function not found.');
      var algoAddr = deriveAddress(evmAddress);
      window._bmEvmAddress  = evmAddress;
      window._bmXChainAlgoAddr = algoAddr;
      console.log('[BlockmakerWalletBridge] xChain connected:', evmAddress, '->', algoAddr);
      bmRestoreFullscreen();
      SendMessage(gameObjectName, successCb, 'EvmXChain|' + algoAddr + '|' + evmAddress);
    })
    .catch(function(err) {
      var msg = (err && err.message) ? err.message : 'EVM wallet connection failed.';
      console.error('[BlockmakerWalletBridge] EvmConnect error:', msg);
      bmRestoreFullscreen();
      SendMessage(gameObjectName, errorCb, msg);
    });
  },

  /**
   * EvmSignTransaction
   * Converts an Algorand transaction to EIP-712 typed data, signs via the EVM
   * wallet, and constructs a LogicSig-wrapped signed transaction.
   * On success: successCb("base64SignedTxn")
   * On error:   errorCb("error message")
   */
  EvmSignTransaction__deps: ['$bmUint8ToBase64', '$bmBase64ToUint8', '$bmExitFullscreen', '$bmRestoreFullscreen'],
  EvmSignTransaction: function(txnBase64Ptr, evmAddressPtr, gameObjectNamePtr, successCbPtr, errorCbPtr) {
    var txnBase64      = UTF8ToString(txnBase64Ptr);
    var evmAddress     = UTF8ToString(evmAddressPtr);
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var successCb      = UTF8ToString(successCbPtr);
    var errorCb        = UTF8ToString(errorCbPtr);

    if (typeof window.ethereum === 'undefined') {
      SendMessage(gameObjectName, errorCb, 'No EVM wallet found.');
      return;
    }

    if (!window._bmXChain) {
      SendMessage(gameObjectName, errorCb, 'xChain SDK not loaded. Connect first.');
      return;
    }

    var xchain = window._bmXChain;
    var signTransaction = xchain.signTransaction || xchain.default?.signTransaction;
    if (!signTransaction) {
      SendMessage(gameObjectName, errorCb, 'xChain SDK: signTransaction function not found.');
      return;
    }

    var bytes = bmBase64ToUint8(txnBase64);

    bmExitFullscreen()
    .then(function() { return signTransaction(bytes, evmAddress, window.ethereum); })
    .then(function(signedTxnBytes) {
      var raw = signedTxnBytes instanceof Uint8Array ? signedTxnBytes : new Uint8Array(signedTxnBytes);
      var b64 = bmUint8ToBase64(raw);
      bmRestoreFullscreen();
      SendMessage(gameObjectName, successCb, b64);
    })
    .catch(function(err) {
      var msg = (err && err.message) ? err.message : 'EVM wallet signing failed.';
      bmRestoreFullscreen();
      SendMessage(gameObjectName, errorCb, msg);
    });
  },

  /**
   * EvmTryRestore — reconnects the EVM wallet and loads xChain SDK after page reload.
   * Uses eth_accounts (non-interactive) so no popup appears.
   * On success: successCb("EvmXChain|algorandAddress|evmAddress")
   * On error:   errorCb("error message")
   */
  EvmTryRestore__deps: ['$loadXChainSDK'],
  EvmTryRestore: function(expectedEvmAddrPtr, gameObjectNamePtr, successCbPtr, errorCbPtr) {
    var expectedEvmAddr = UTF8ToString(expectedEvmAddrPtr);
    var gameObjectName  = UTF8ToString(gameObjectNamePtr);
    var successCb       = UTF8ToString(successCbPtr);
    var errorCb         = UTF8ToString(errorCbPtr);

    if (typeof window.ethereum === 'undefined') {
      SendMessage(gameObjectName, errorCb, 'No EVM wallet found.');
      return;
    }

    window.ethereum.request({ method: 'eth_accounts' })
    .then(function(accounts) {
      if (!accounts || accounts.length === 0) {
        throw new Error('EVM wallet not connected.');
      }
      var evmAddress = accounts[0];
      if (expectedEvmAddr && evmAddress.toLowerCase() !== expectedEvmAddr.toLowerCase()) {
        throw new Error('EVM wallet account changed.');
      }
      window._bmEvmAddress = evmAddress;
      return loadXChainSDK();
    })
    .then(function(xchain) {
      var deriveAddress = xchain.deriveAddress || xchain.getAlgorandAddress || xchain.default?.deriveAddress;
      if (!deriveAddress) throw new Error('xChain SDK: address derivation function not found.');
      var algoAddr = deriveAddress(window._bmEvmAddress);
      window._bmXChainAlgoAddr = algoAddr;
      console.log('[BlockmakerWalletBridge] xChain session restored:', window._bmEvmAddress, '->', algoAddr);
      SendMessage(gameObjectName, successCb, 'EvmXChain|' + algoAddr + '|' + window._bmEvmAddress);
    })
    .catch(function(err) {
      var msg = (err && err.message) ? err.message : 'EVM restore failed.';
      SendMessage(gameObjectName, errorCb, msg);
    });
  },

  /**
   * EvmDisconnect — clears xChain EVM state.
   */
  EvmDisconnect: function() {
    window._bmEvmAddress     = null;
    window._bmXChainAlgoAddr = null;
    console.log('[BlockmakerWalletBridge] xChain EVM disconnected.');
  }

});
