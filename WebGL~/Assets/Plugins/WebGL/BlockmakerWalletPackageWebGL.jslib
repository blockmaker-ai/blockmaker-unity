// Unity WebGL boundary for Blockmaker's one-manager TxnLab v5 wallet package.
// An absent Web3Auth public client ID selects exactly Pera + Lute. A valid
// public ID selects exactly Pera + Lute + TxnLab Email/Google.
//
// Host contract (installed before player interaction):
//   globalThis.__blockmakerUnityWebGlWalletPackageV1 = {
//     captureDeploymentEvidence(exactReadyEnvelope),
//     downloadDeploymentEvidence(exactDownloadRequest),
//     openAccount(), cancel(), acknowledge(exactHandoff), reject(exactHandoff),
//     prepareTransactionGroup(exactCopiedGroup), signPreparedTransactionGroup(),
//     cancelPreparedTransactionGroup(), logout(exactSession),
//     openFunding(accessAndClassifiers), closeFunding()
//   }
//
// `openAccount`, `signPreparedTransactionGroup` and `openFunding` are invoked
// synchronously from their .jslib exports so the browser's user-activation task
// reaches provider UI unchanged. This bridge never broadcasts transactions.

mergeInto(LibraryManager.library, {
  BlockmakerWalletPackageWebGL_Initialize: function (
    targetPointer, lifecyclePointer, baseUrlPointer, gameIdPointer,
    clientIdPointer, appNamePointer, walletAddressPointer,
    accountKindPointer, authProviderPointer
  ) {
    var target = UTF8ToString(targetPointer);
    var lifecycleId = UTF8ToString(lifecyclePointer);
    var baseUrl = UTF8ToString(baseUrlPointer);
    var gameId = UTF8ToString(gameIdPointer);
    var clientId = UTF8ToString(clientIdPointer);
    var appName = UTF8ToString(appNamePointer);
    var restoredWalletAddress = UTF8ToString(walletAddressPointer);
    var restoredAccountKind = UTF8ToString(accountKindPointer);
    var restoredAuthProvider = UTF8ToString(authProviderPointer);
    var root = typeof globalThis === 'object' ? globalThis : window;
    var runtimeKey = '__blockmakerUnityWebGlWalletPackageRuntimeV1';
    var ownerKey = '__blockmakerUnityWebGlWalletPackageOwnerV1';
    var facadeKey = '__blockmakerUnityWebGlWalletPackageV1';
    var cleanupKey = '__blockmakerUnityWebGlWalletPackageCleanupRequiredV1';
    var schema = 'blockmaker-unity-webgl-wallet-package/v2';
    var validTarget = function (value) {
      return typeof value === 'string' && value.length > 0 && value.length <= 128
        && !/[\u0000-\u001f\u007f-\u009f]/.test(value);
    };
    var validLifecycle = /^[a-f0-9]{32}$/.test(String(lifecycleId || ''));
    var send = function (method, value) {
      if (!validTarget(target) || !validLifecycle) return;
      try { SendMessage(target, method, JSON.stringify(value)); } catch (_) {}
    };
    var error = function (code) {
      send('OnBlockmakerWalletPackageError', {
        schemaVersion: schema,
        lifecycleId: lifecycleId,
        operationId: 0,
        operationKind: 'initialize',
        code: code
      });
    };
    var safeBaseUrl = function (value) {
      if (typeof value !== 'string' || value.length < 1 || value.length > 2048
        || /[\u0000-\u0020\u007f-\u009f]/.test(value)) return null;
      try {
        var parsed = new URL(value);
        var loopback = parsed.hostname === 'localhost' || parsed.hostname === '127.0.0.1'
          || parsed.hostname === '[::1]' || parsed.hostname === '::1';
        if (parsed.username || parsed.password || parsed.search || parsed.hash
          || (parsed.pathname !== '/' && parsed.pathname !== '')
          || (parsed.protocol !== 'https:' && !(parsed.protocol === 'http:' && loopback))) return null;
        return parsed.origin;
      } catch (_) { return null; }
    };
    var safeGameId = typeof gameId === 'string' && /^[A-Za-z0-9_-]{1,128}$/.test(gameId)
      && !/^sk_/i.test(gameId);
    var safeClientId = typeof clientId === 'string' && clientId.length >= 16
      && clientId.length <= 512 && clientId === clientId.trim()
      && !/^sk_/i.test(clientId) && /^[\x21-\x7e]+$/.test(clientId)
      && !/\s/.test(clientId);
    var runtimeProfile = clientId === '' ? 'pera_lute'
      : safeClientId ? 'pera_lute_txnlab_web3auth' : null;
    var providerIds = runtimeProfile === 'pera_lute'
      ? Object.freeze(['pera', 'lute'])
      : runtimeProfile === 'pera_lute_txnlab_web3auth'
        ? Object.freeze(['pera', 'lute', 'txnlab_web3auth']) : null;
    var safeAppName = typeof appName === 'string' && appName.length >= 1
      && appName.length <= 80 && appName === appName.trim()
      && !/[\u0000-\u001f\u007f]/.test(appName);
    var restoredEmpty = restoredWalletAddress === '' && restoredAccountKind === ''
      && restoredAuthProvider === '';
    var restoredValid = /^[A-Z2-7]{58}$/.test(restoredWalletAddress)
      && restoredAccountKind === 'algorand_wallet'
      && providerIds !== null
      && providerIds.indexOf(restoredAuthProvider) !== -1;
    var normalizedBaseUrl = safeBaseUrl(baseUrl);
    if (!validLifecycle || !validTarget(target) || !normalizedBaseUrl || !safeGameId
      || !runtimeProfile || !safeAppName || (!restoredEmpty && !restoredValid)) {
      error('PACKAGE_LOAD_FAILED');
      return;
    }

    var runtime = root[runtimeKey];
    if (runtime && (runtime.schemaVersion !== schema
      || runtime.runtimeProfile !== runtimeProfile)) {
      error('PACKAGE_LOAD_FAILED');
      return;
    }
    if (!runtime) {
      var supportedPair = function (accountKind, authProvider) {
        return accountKind === 'algorand_wallet'
          && providerIds.indexOf(authProvider) !== -1;
      };
      var printable = function (value, maximum) {
        return typeof value === 'string' && value.length > 0 && value.length <= maximum
          && !/^sk_/i.test(value) && /^[\x21-\x7e]+$/.test(value);
      };
      var canonicalBase64 = function (value, maximumBytes) {
        if (typeof value !== 'string' || value.length < 4
          || value.length > Math.ceil(maximumBytes / 3) * 4
          || value !== value.trim()
          || !/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(value)
          || typeof root.atob !== 'function' || typeof root.btoa !== 'function') return null;
        try {
          var decoded = root.atob(value);
          if (decoded.length < 1 || decoded.length > maximumBytes
            || root.btoa(decoded) !== value) return null;
          return value;
        } catch (_) { return null; }
      };
      runtime = {
        schemaVersion: schema,
        runtimeProfile: runtimeProfile,
        providerIds: providerIds,
        initialization: null,
        facade: null,
        ready: false,
        pendingReady: null,
        readyAckWindow: null,
        acceptedReady: null,
        validTarget: validTarget,
        validLifecycle: function (value) {
          return /^[a-f0-9]{32}$/.test(String(value || ''));
        },
        validOperation: function (value) {
          return Number.isSafeInteger(value) && value > 0;
        },
        supportedPair: supportedPair,
        economicPair: function (accountKind, authProvider) {
          return supportedPair(accountKind, authProvider);
        },
        printable: printable,
        safeFailureCode: function (failure, fallback) {
          var code = String(failure && failure.code || '').trim().toUpperCase();
          return code === 'PLAYER_CANCELLED' || code === 'PROVIDER_CLEANUP_REQUIRED'
            || code === 'NETWORK_UNAVAILABLE' || code === 'ORIGIN_NOT_ALLOWED'
            || code === 'PROVIDER_NOT_ENABLED' || code === 'WALLET_ACCOUNT_MISSING'
            || code === 'PROVIDER_UNAVAILABLE' || code === 'SESSION_CONFLICT'
            || code === 'SESSION_EXPIRED' || code === 'REQUEST_ALREADY_PENDING'
            || code === 'TRANSACTION_GROUP_INVALID' || code === 'WALLET_SIGNATURE_INVALID'
            ? code : fallback;
        },
        safeUnsignedTransactionGroup: function (json) {
          if (typeof json !== 'string' || json.length < 1 || json.length > 1500000) return null;
          var parsed;
          try { parsed = JSON.parse(json); } catch (_) { return null; }
          if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)
            || Object.keys(parsed).sort().join(',') !== 'unsignedTransactionsBase64'
            || !Array.isArray(parsed.unsignedTransactionsBase64)
            || parsed.unsignedTransactionsBase64.length < 1
            || parsed.unsignedTransactionsBase64.length > 16) return null;
          var copied = [];
          for (var index = 0; index < parsed.unsignedTransactionsBase64.length; index++) {
            var transaction = canonicalBase64(
              parsed.unsignedTransactionsBase64[index], 65536);
            if (!transaction) return null;
            copied.push(transaction);
          }
          return Object.freeze(copied);
        },
        copiedSignedTransactionGroup: function (value, expectedCount) {
          if (!Array.isArray(value) || value.length !== expectedCount
            || value.length < 1 || value.length > 16
            || typeof Uint8Array !== 'function') return null;
          var encoded = [];
          for (var index = 0; index < value.length; index++) {
            var transaction = value[index];
            if (!transaction
              || Object.prototype.toString.call(transaction) !== '[object Uint8Array]'
              || !Number.isSafeInteger(transaction.byteLength)
              || transaction.byteLength < 1 || transaction.byteLength > 66000) return null;
            var copy;
            try {
              copy = new Uint8Array(transaction.byteLength);
              copy.set(transaction);
            } catch (_) { return null; }
            var binary = '';
            for (var offset = 0; offset < copy.length; offset += 8192) {
              var end = Math.min(offset + 8192, copy.length);
              var chunk = '';
              for (var cursor = offset; cursor < end; cursor++)
                chunk += String.fromCharCode(copy[cursor]);
              binary += chunk;
            }
            var canonical;
            try { canonical = root.btoa(binary); } catch (_) { return null; }
            if (!canonicalBase64(canonical, 66000)) return null;
            encoded.push(canonical);
          }
          return Object.freeze(encoded);
        },
        safeSession: function (value) {
          if (!value || !printable(value.sessionToken, 8192)
            || !printable(value.refreshToken, 1024)
            || !/^[A-Z2-7]{58}$/.test(String(value.walletAddress || ''))
            || !supportedPair(value.accountKind, value.authProvider)) return null;
          return {
            sessionToken: value.sessionToken,
            refreshToken: value.refreshToken,
            walletAddress: value.walletAddress,
            accountKind: value.accountKind,
            authProvider: value.authProvider
          };
        },
        validFacade: function (value) {
          return !!value && typeof value.openAccount === 'function'
            && typeof value.captureDeploymentEvidence === 'function'
            && typeof value.downloadDeploymentEvidence === 'function'
            && typeof value.cancel === 'function'
            && typeof value.acknowledge === 'function'
            && typeof value.reject === 'function'
            && typeof value.prepareTransactionGroup === 'function'
            && typeof value.signPreparedTransactionGroup === 'function'
            && typeof value.cancelPreparedTransactionGroup === 'function'
            && typeof value.logout === 'function'
            && typeof value.openFunding === 'function'
            && typeof value.closeFunding === 'function';
        },
        send: function (receiver, method, value) {
          if (!validTarget(receiver)) return false;
          try {
            SendMessage(receiver, method, JSON.stringify(value));
            return true;
          } catch (_) { return false; }
        },
        envelope: function (owner, extra) {
          var value = {
            schemaVersion: schema,
            lifecycleId: owner.lifecycleId,
            operationId: owner.operationId,
            operationKind: owner.kind
          };
          if (extra) for (var key in extra)
            if (Object.prototype.hasOwnProperty.call(extra, key)) value[key] = extra[key];
          return value;
        },
        setOwner: function (owner) {
          if (Object.prototype.hasOwnProperty.call(root, ownerKey)) return false;
          try {
            Object.defineProperty(root, ownerKey, {
              value: owner,
              configurable: true,
              enumerable: false,
              writable: false
            });
          } catch (_) { return false; }
          return root[ownerKey] === owner;
        },
        clearOwner: function (owner) {
          if (root[ownerKey] !== owner) return false;
          try { delete root[ownerKey]; } catch (_) { return false; }
          return root[ownerKey] !== owner;
        },
        finishCancelledTransaction: function (owner) {
          if (root[ownerKey] !== owner || owner.kind !== 'sign_transaction_group'
            || owner.state !== 'cancelling' || owner.operationSettled !== true
            || owner.cancelSettled !== true) return;
          var completion = owner.cancelCompletion;
          owner.cancelCompletion = null;
          if (!runtime.clearOwner(owner)) {
            runtime.latchCleanup();
            if (completion) completion(false);
            return;
          }
          if (completion) completion(true);
        },
        cancelTransactionOwner: function (owner, completion) {
          if (root[ownerKey] !== owner || owner.kind !== 'sign_transaction_group') return false;
          if (owner.state === 'cancelling') return true;
          owner.state = 'cancelling';
          owner.cancelSettled = false;
          owner.cancelCompletion = completion || null;
          var cancelled;
          try { cancelled = runtime.facade.cancelPreparedTransactionGroup(); }
          catch (_) {
            runtime.latchCleanup();
            if (owner.cancelCompletion) {
              owner.cancelCompletion(false);
              owner.cancelCompletion = null;
            }
            return false;
          }
          Promise.resolve(cancelled).then(function () {
            if (root[ownerKey] !== owner || owner.state !== 'cancelling') return;
            owner.cancelSettled = true;
            runtime.finishCancelledTransaction(owner);
          }).catch(function () {
            if (root[ownerKey] !== owner || owner.state !== 'cancelling') return;
            runtime.latchCleanup();
            if (owner.cancelCompletion) {
              owner.cancelCompletion(false);
              owner.cancelCompletion = null;
            }
          });
          return true;
        },
        latchCleanup: function () {
          try {
            Object.defineProperty(root, cleanupKey, {
              value: true,
              configurable: false,
              enumerable: false,
              writable: false
            });
          } catch (_) {}
        },
        cleanupRequired: function () { return root[cleanupKey] === true; }
      };
      try {
        Object.defineProperty(root, runtimeKey, {
          value: runtime,
          configurable: false,
          enumerable: false,
          writable: false
        });
      } catch (_) {
        error('PACKAGE_LOAD_FAILED');
        return;
      }
    }

    var restoredSession = restoredEmpty ? null : Object.freeze({
      walletAddress: restoredWalletAddress,
      accountKind: restoredAccountKind,
      authProvider: restoredAuthProvider
    });
    var configurationKey = normalizedBaseUrl + '\n' + gameId + '\n'
      + runtimeProfile + '\n' + clientId
      + '\n' + appName + '\n' + (restoredSession
        ? restoredSession.walletAddress + '\n' + restoredSession.accountKind
          + '\n' + restoredSession.authProvider
        : '');
    if (runtime.initialization && runtime.initialization.configurationKey !== configurationKey) {
      error('SESSION_CONFLICT');
      return;
    }
    if (!runtime.initialization) {
      var streamingAssetsUrl = typeof Module === 'object'
        && typeof Module.streamingAssetsUrl === 'string'
        ? Module.streamingAssetsUrl : '';
      var moduleUrl = null;
      try {
        if (!streamingAssetsUrl || streamingAssetsUrl.length > 2048
          || /[\u0000-\u001f\u007f-\u009f]/.test(streamingAssetsUrl)) throw new Error();
        var pageUrl = new URL(
          typeof location === 'object' && typeof location.href === 'string'
            ? location.href
            : typeof document === 'object' && document.baseURI
              ? document.baseURI
              : normalizedBaseUrl + '/');
        var pageLoopback = pageUrl.hostname === 'localhost'
          || pageUrl.hostname === '127.0.0.1'
          || pageUrl.hostname === '[::1]'
          || pageUrl.hostname === '::1';
        if (pageUrl.username || pageUrl.password
          || (pageUrl.protocol !== 'https:'
            && !(pageUrl.protocol === 'http:' && pageLoopback))) throw new Error();
        var base = new URL(streamingAssetsUrl,
          pageUrl.href);
        if (base.username || base.password || base.search || base.hash
          || base.origin !== pageUrl.origin) throw new Error();
        moduleUrl = new URL('Blockmaker/blockmaker-unity-webgl-wallet-host.mjs',
          base.href.replace(/\/?$/, '/')).href;
        if (new URL(moduleUrl).origin !== pageUrl.origin) throw new Error();
      } catch (_) {
        error('PACKAGE_LOAD_FAILED');
        return;
      }
      var installation = import(moduleUrl).then(function (module) {
        if (!module || typeof module.installBlockmakerUnityWebGlWalletPackage !== 'function')
          throw new Error('wallet host installer unavailable');
        var options = {
          baseUrl: normalizedBaseUrl,
          gameId: gameId,
          appName: appName,
          restoredSession: restoredSession,
          runtimeProfile: runtimeProfile
        };
        if (runtimeProfile === 'pera_lute_txnlab_web3auth')
          options.clientId = clientId;
        return module.installBlockmakerUnityWebGlWalletPackage(
          Object.freeze(options));
      }).then(function () {
        var facade = root[facadeKey];
        if (!runtime.validFacade(facade)) throw new Error('wallet host facade unavailable');
        runtime.facade = facade;
        return facade;
      });
      runtime.initialization = {
        configurationKey: configurationKey,
        moduleUrl: moduleUrl,
        promise: installation
      };
    }
    runtime.initialization.promise.then(function () {
      if (runtime.acceptedReady) {
        error('SESSION_CONFLICT');
        return;
      }
      if (runtime.pendingReady
        && (runtime.pendingReady.lifecycleId !== lifecycleId
          || runtime.pendingReady.target !== target)) {
        error('SESSION_CONFLICT');
        return;
      }
      var readyEnvelope = Object.freeze({
        schemaVersion: schema,
        lifecycleId: lifecycleId,
        operationId: 0,
        operationKind: 'initialize',
        phase: 'ready',
        runtimeProfile: runtime.runtimeProfile,
        callbackMethod: 'OnBlockmakerWalletPackageSuccess'
      });
      if (!runtime.acceptedReady) {
        runtime.pendingReady = {
          target: target,
          lifecycleId: lifecycleId,
          envelope: readyEnvelope,
          state: 'sending'
        };
      }
      // callbackMethod is a capture-only binding; the public C# envelope keeps
      // the same exact v2 runtime-profile binding without exposing the method.
      runtime.readyAckWindow = runtime.pendingReady;
      send('OnBlockmakerWalletPackageSuccess', {
        schemaVersion: readyEnvelope.schemaVersion,
        lifecycleId: readyEnvelope.lifecycleId,
        operationId: readyEnvelope.operationId,
        operationKind: readyEnvelope.operationKind,
        phase: readyEnvelope.phase,
        runtimeProfile: readyEnvelope.runtimeProfile
      });
      runtime.readyAckWindow = null;
      if (!runtime.acceptedReady && runtime.pendingReady) {
        runtime.pendingReady.state = 'missed';
        runtime.pendingReady = null;
        runtime.ready = false;
      }
    }).catch(function () {
      runtime.ready = false;
      error('PACKAGE_LOAD_FAILED');
    });
  },

  BlockmakerWalletPackageWebGL_AcknowledgeReady: function (
    targetPointer, lifecyclePointer, operationId
  ) {
    var target = UTF8ToString(targetPointer);
    var lifecycleId = UTF8ToString(lifecyclePointer);
    var root = typeof globalThis === 'object' ? globalThis : window;
    var runtime = root.__blockmakerUnityWebGlWalletPackageRuntimeV1;
    var facade = root.__blockmakerUnityWebGlWalletPackageV1;
    var pending = runtime && runtime.pendingReady;
    var envelope = pending && pending.envelope;
    if (!runtime || !pending || pending.state !== 'sending'
      || runtime.readyAckWindow !== pending || operationId !== 0
      || !runtime.validTarget(target) || !runtime.validLifecycle(lifecycleId)
      || pending.target !== target || pending.lifecycleId !== lifecycleId
      || !envelope || Object.keys(envelope).sort().join(',')
        !== 'callbackMethod,lifecycleId,operationId,operationKind,phase,runtimeProfile,schemaVersion'
      || envelope.schemaVersion !== runtime.schemaVersion
      || envelope.lifecycleId !== lifecycleId || envelope.operationId !== 0
      || envelope.operationKind !== 'initialize' || envelope.phase !== 'ready'
      || envelope.runtimeProfile !== runtime.runtimeProfile
      || envelope.callbackMethod !== 'OnBlockmakerWalletPackageSuccess'
      || runtime.facade !== facade || !runtime.validFacade(facade)) return 0;
    var captured;
    pending.state = 'capturing';
    try {
      captured = facade.captureDeploymentEvidence(pending.envelope);
      // Capture is deliberately synchronous: returning a Promise would let C#
      // report ready before the bridge roundtrip was durably accepted.
      if (captured !== undefined) throw new Error('capture must return void');
    } catch (_) {
      pending.state = 'failed';
      runtime.ready = false;
      runtime.latchCleanup();
      return 0;
    }
    pending.state = 'accepted';
    runtime.acceptedReady = Object.freeze({
      target: target,
      lifecycleId: lifecycleId,
      envelope: pending.envelope
    });
    runtime.pendingReady = null;
    runtime.ready = true;
    return 1;
  },

  // Read-only integer ABI. It deliberately does not take an owner, increment an
  // operation ID or use a wallet success/error callback. Unknown data is never
  // serialized into Unity, and old facades remain usable without this method.
  BlockmakerWalletPackageWebGL_GetDeploymentEvidenceStatus: function (
    targetPointer, lifecyclePointer
  ) {
    var target = UTF8ToString(targetPointer);
    var lifecycleId = UTF8ToString(lifecyclePointer);
    var root = typeof globalThis === 'object' ? globalThis : window;
    var runtime = root.__blockmakerUnityWebGlWalletPackageRuntimeV1;
    var facade = root.__blockmakerUnityWebGlWalletPackageV1;
    if (!runtime || !runtime.acceptedReady
      || !runtime.validTarget(target) || !runtime.validLifecycle(lifecycleId)
      || runtime.acceptedReady.target !== target
      || runtime.acceptedReady.lifecycleId !== lifecycleId
      || runtime.facade !== facade || !runtime.validFacade(facade)) return 10;
    try {
      if (typeof facade.getDeploymentEvidenceStatus !== 'function') return 10;
      var value = facade.getDeploymentEvidenceStatus();
      if (!value || typeof value !== 'object' || Array.isArray(value)
        || Object.keys(value).sort().join(',')
          !== 'canDownload,code,reloadRequired,schemaVersion,stage,state'
        || value.schemaVersion !== 'blockmaker-wallet-package-evidence-status/v1') return 10;
      var statuses = [
        ['not_started', 'none', 'CAPTURE_NOT_STARTED'],
        ['pending', 'capture', 'CAPTURE_PENDING'],
        ['ready', 'complete', 'NONE'],
        ['failed', 'origin', 'PRODUCTION_ORIGIN_REQUIRED'],
        ['failed', 'manifest', 'MANIFEST_UNAVAILABLE'],
        ['failed', 'manifest', 'MANIFEST_INVALID'],
        ['failed', 'runtime', 'RUNTIME_UNAVAILABLE'],
        ['failed', 'runtime', 'RUNTIME_INVALID'],
        ['failed', 'capture', 'CAPTURE_TIMEOUT'],
        ['failed', 'capture', 'CAPTURE_FAILED'],
        ['unavailable', 'none', 'BRIDGE_UNAVAILABLE']
      ];
      for (var index = 0; index < statuses.length; index++) {
        var expected = statuses[index];
        if (value.state === expected[0] && value.stage === expected[1]
          && value.code === expected[2] && value.canDownload === (index === 2)
          && value.reloadRequired === (expected[0] === 'failed')) return index;
      }
    } catch (_) { /* Never expose upstream exceptions or affect wallet ownership. */ }
    return 10;
  },

  BlockmakerWalletPackageWebGL_DownloadDeploymentEvidence: function (
    targetPointer, lifecyclePointer, operationId
  ) {
    var target = UTF8ToString(targetPointer);
    var lifecycleId = UTF8ToString(lifecyclePointer);
    var root = typeof globalThis === 'object' ? globalThis : window;
    var runtime = root.__blockmakerUnityWebGlWalletPackageRuntimeV1;
    var facade = root.__blockmakerUnityWebGlWalletPackageV1;
    var sendError = function (code) {
      if (!runtime || !runtime.validTarget(target)
        || !runtime.validLifecycle(lifecycleId)) return;
      runtime.send(target, 'OnBlockmakerWalletPackageError', {
        schemaVersion: runtime.schemaVersion,
        lifecycleId: lifecycleId,
        operationId: operationId,
        operationKind: 'deployment_evidence',
        code: code
      });
    };
    if (!runtime || !runtime.ready || !runtime.acceptedReady
      || runtime.acceptedReady.target !== target
      || runtime.acceptedReady.lifecycleId !== lifecycleId
      || !runtime.validOperation(operationId)
      || !runtime.validTarget(target) || !runtime.validLifecycle(lifecycleId)
      || runtime.facade !== facade || !runtime.validFacade(facade)) {
      sendError('DEPLOYMENT_EVIDENCE_UNAVAILABLE');
      return 0;
    }
    var owner = {
      lifecycleId: lifecycleId,
      operationId: operationId,
      kind: 'deployment_evidence',
      target: target,
      state: 'downloading'
    };
    if (!runtime.setOwner(owner)) {
      sendError('REQUEST_ALREADY_PENDING');
      return 0;
    }
    var request = Object.freeze({
      schemaVersion: runtime.schemaVersion,
      lifecycleId: lifecycleId,
      operationId: operationId,
      operationKind: 'deployment_evidence',
      phase: 'download'
    });
    var downloaded;
    try {
      // Keep the browser download in the direct C# invocation task.
      downloaded = facade.downloadDeploymentEvidence(request);
      if (downloaded !== undefined)
        throw new Error('deployment evidence download must return void');
    } catch (_) {
      runtime.clearOwner(owner);
      sendError('DEPLOYMENT_EVIDENCE_UNAVAILABLE');
      return 0;
    }
    runtime.clearOwner(owner);
    var delivered = runtime.send(target, 'OnBlockmakerWalletPackageSuccess', {
      schemaVersion: runtime.schemaVersion,
      lifecycleId: lifecycleId,
      operationId: operationId,
      operationKind: 'deployment_evidence',
      phase: 'downloaded'
    });
    return delivered ? 1 : 0;
  },

  BlockmakerWalletPackageWebGL_OpenAccount: function (
    targetPointer, lifecyclePointer, operationId, unityPresentation, providerPointer
  ) {
    var target = UTF8ToString(targetPointer);
    var lifecycleId = UTF8ToString(lifecyclePointer);
    var root = typeof globalThis === 'object' ? globalThis : window;
    var runtime = root.__blockmakerUnityWebGlWalletPackageRuntimeV1;
    var ownerKey = '__blockmakerUnityWebGlWalletPackageOwnerV1';
    var facade = root.__blockmakerUnityWebGlWalletPackageV1;
    var fallbackSend = function (code) {
      if (typeof target !== 'string' || target.length < 1 || target.length > 128
        || !/^[a-f0-9]{32}$/.test(String(lifecycleId || ''))) return;
      try {
        SendMessage(target, 'OnBlockmakerWalletPackageError', JSON.stringify({
          schemaVersion: 'blockmaker-unity-webgl-wallet-package/v2',
          lifecycleId: lifecycleId,
          operationId: operationId,
          operationKind: 'open_account',
          code: code
        }));
      } catch (_) {}
    };
    if (!runtime || !runtime.validLifecycle(lifecycleId)
      || !runtime.validOperation(operationId) || !runtime.validTarget(target)) {
      fallbackSend('PACKAGE_NOT_READY');
      return;
    }
    if (runtime.cleanupRequired()) {
      fallbackSend('PROVIDER_CLEANUP_REQUIRED');
      return;
    }
    if (!runtime.ready || runtime.facade !== facade || !runtime.validFacade(facade)) {
      fallbackSend('PACKAGE_NOT_READY');
      return;
    }
    var owner = {
      lifecycleId: lifecycleId,
      operationId: operationId,
      kind: 'open_account',
      target: target,
      state: 'opening',
      handoff: null,
      session: null
    };
    if (!runtime.setOwner(owner)) {
      fallbackSend('REQUEST_ALREADY_PENDING');
      return;
    }
    var matches = function () { return root[ownerKey] === owner; };
    var sendError = function (code) {
      runtime.send(target, 'OnBlockmakerWalletPackageError',
        runtime.envelope(owner, { code: code }));
    };
    var rejectOrLatch = function (handoff, code, notify) {
      var rejected;
      try { rejected = facade.reject(handoff); }
      catch (_) {
        runtime.latchCleanup();
        if (notify) sendError('PROVIDER_CLEANUP_REQUIRED');
        return;
      }
      Promise.resolve(rejected).then(function () {
        if (matches()) runtime.clearOwner(owner);
        if (notify) sendError(code);
      }).catch(function () {
        runtime.latchCleanup();
        if (notify) sendError('PROVIDER_CLEANUP_REQUIRED');
      });
    };
    var opened;
    // Keep this direct call in the user-activation task. Do not place an await,
    // import, fetch or promise hop before it.
    var providerId = providerPointer ? UTF8ToString(providerPointer) : 'pera';
    try { if (unityPresentation === 1) opened = facade.openAccount({
      presentation: 'unity', providerId: providerId,
      onProgress: function (value) {
        if (!matches() || owner.state !== 'opening' || !value
          || value.providerId !== providerId
          || ['connecting', 'qr', 'approval', 'verifying', 'provider_auth'].indexOf(value.phase) < 0) return;
        var uri = value.phase === 'qr' ? value.walletConnectUri : '';
        if (value.phase === 'qr' && (typeof uri !== 'string'
          || uri.length > 4096 || !/^wc:[^\s]+$/.test(uri))) return;
        runtime.send(target, 'OnBlockmakerWalletPresentation', runtime.envelope(owner, {
          phase: value.phase, providerId: providerId, walletConnectUri: uri || ''
        }));
      }
    }); else opened = facade.openAccount(); }
    catch (failure) {
      runtime.clearOwner(owner);
      var code = runtime.safeFailureCode(failure, 'PROVIDER_UNAVAILABLE');
      if (code === 'PROVIDER_CLEANUP_REQUIRED') runtime.latchCleanup();
      sendError(code);
      return;
    }
    Promise.resolve(opened).then(function (handoff) {
      if (!matches() || owner.state !== 'opening') {
        rejectOrLatch(handoff, 'PLAYER_CANCELLED', false);
        return;
      }
      var session = null;
      try { session = runtime.safeSession(handoff); } catch (_) { session = null; }
      if (!session) {
        owner.handoff = handoff;
        owner.state = 'rejecting';
        rejectOrLatch(handoff, 'PROVIDER_UNAVAILABLE', true);
        return;
      }
      owner.handoff = handoff;
      owner.session = session;
      owner.state = 'handoff';
      var delivered = runtime.send(target, 'OnBlockmakerWalletPackageSuccess',
        runtime.envelope(owner, {
          phase: 'handoff',
          sessionToken: session.sessionToken,
          refreshToken: session.refreshToken,
          walletAddress: session.walletAddress,
          accountKind: session.accountKind,
          authProvider: session.authProvider
        }));
      if (!delivered && matches() && owner.state === 'handoff') {
        owner.state = 'rejecting';
        rejectOrLatch(handoff, 'PROVIDER_UNAVAILABLE', false);
      }
    }).catch(function (failure) {
      if (!matches()) return;
      runtime.clearOwner(owner);
      var code = runtime.safeFailureCode(failure, 'PROVIDER_UNAVAILABLE');
      if (code === 'PROVIDER_CLEANUP_REQUIRED') {
        runtime.latchCleanup();
        sendError('PROVIDER_CLEANUP_REQUIRED');
        return;
      }
      sendError(code);
    });
  },

  BlockmakerWalletPackageWebGL_PrepareTransactionGroup: function (
    targetPointer, lifecyclePointer, operationId, walletAddressPointer,
    accountKindPointer, authProviderPointer, transactionGroupJsonPointer, usernameJsonPointer
  ) {
    var target = UTF8ToString(targetPointer);
    var lifecycleId = UTF8ToString(lifecyclePointer);
    var walletAddress = UTF8ToString(walletAddressPointer);
    var accountKind = UTF8ToString(accountKindPointer);
    var authProvider = UTF8ToString(authProviderPointer);
    var transactionGroupJson = UTF8ToString(transactionGroupJsonPointer);
    var root = typeof globalThis === 'object' ? globalThis : window;
    var runtime = root.__blockmakerUnityWebGlWalletPackageRuntimeV1;
    var ownerKey = '__blockmakerUnityWebGlWalletPackageOwnerV1';
    var facade = root.__blockmakerUnityWebGlWalletPackageV1;
    var sendError = function (code) {
      if (!runtime || !runtime.validTarget(target)
        || !runtime.validLifecycle(lifecycleId)) return;
      runtime.send(target, 'OnBlockmakerWalletPackageError', {
        schemaVersion: runtime.schemaVersion,
        lifecycleId: lifecycleId,
        operationId: operationId,
        operationKind: 'sign_transaction_group',
        code: code
      });
    };
    if (!runtime || !runtime.ready || !runtime.acceptedReady
      || runtime.cleanupRequired() || runtime.facade !== facade
      || !runtime.validFacade(facade)) {
      sendError(runtime && runtime.cleanupRequired()
        ? 'PROVIDER_CLEANUP_REQUIRED' : 'PACKAGE_NOT_READY');
      return;
    }
    var unsignedTransactionsBase64 = runtime.safeUnsignedTransactionGroup(
      transactionGroupJson);
    if (!runtime.validOperation(operationId) || !runtime.validTarget(target)
      || !runtime.validLifecycle(lifecycleId)
      || runtime.acceptedReady.target !== target
      || runtime.acceptedReady.lifecycleId !== lifecycleId
      || !/^[A-Z2-7]{58}$/.test(walletAddress)
      || !runtime.supportedPair(accountKind, authProvider)
      || !unsignedTransactionsBase64) {
      sendError('TRANSACTION_GROUP_INVALID');
      return;
    }
    var owner = {
      lifecycleId: lifecycleId,
      operationId: operationId,
      kind: 'sign_transaction_group',
      target: target,
      state: 'preparing',
      operationSettled: false,
      cancelSettled: false,
      cancelCompletion: null,
      expectedCount: unsignedTransactionsBase64.length,
      walletAddress: walletAddress,
      accountKind: accountKind,
      authProvider: authProvider
    };
    if (!runtime.setOwner(owner)) {
      sendError('REQUEST_ALREADY_PENDING');
      return;
    }
    var prepared;
    try {
      var request = {
        address: walletAddress,
        providerId: authProvider,
        unsignedTransactionsBase64: unsignedTransactionsBase64
      };
      if (usernameJsonPointer) {
        var usernameJson = UTF8ToString(usernameJsonPointer);
        if (usernameJson.length < 1 || usernameJson.length > 131072
          || typeof facade.prepareUniversalUsername !== 'function') throw new Error('Invalid username package request');
        request.prepared = JSON.parse(usernameJson);
        prepared = facade.prepareUniversalUsername(Object.freeze(request));
      } else prepared = facade.prepareTransactionGroup(Object.freeze(request));
    } catch (failure) {
      runtime.clearOwner(owner);
      sendError(runtime.safeFailureCode(failure, 'TRANSACTION_GROUP_INVALID'));
      return;
    }
    Promise.resolve(prepared).then(function () {
      if (root[ownerKey] !== owner) return;
      owner.operationSettled = true;
      if (owner.state === 'cancelling') {
        runtime.finishCancelledTransaction(owner);
        return;
      }
      if (owner.state !== 'preparing') return;
      owner.state = 'prepared';
      var delivered = runtime.send(target, 'OnBlockmakerWalletPackageSuccess',
        runtime.envelope(owner, { phase: 'prepared' }));
      if (!delivered && root[ownerKey] === owner && owner.state === 'prepared')
        runtime.cancelTransactionOwner(owner, null);
    }).catch(function (failure) {
      if (root[ownerKey] !== owner) return;
      owner.operationSettled = true;
      if (owner.state === 'cancelling') {
        runtime.finishCancelledTransaction(owner);
        return;
      }
      runtime.clearOwner(owner);
      sendError(runtime.safeFailureCode(failure, 'TRANSACTION_GROUP_INVALID'));
    });
  },

  BlockmakerWalletPackageWebGL_SignPreparedTransactionGroup: function (
    targetPointer, lifecyclePointer, operationId
  ) {
    var target = UTF8ToString(targetPointer);
    var lifecycleId = UTF8ToString(lifecyclePointer);
    var root = typeof globalThis === 'object' ? globalThis : window;
    var runtime = root.__blockmakerUnityWebGlWalletPackageRuntimeV1;
    var ownerKey = '__blockmakerUnityWebGlWalletPackageOwnerV1';
    var owner = root[ownerKey];
    var sendDirectError = function (code) {
      if (!runtime || !runtime.validTarget(target)
        || !runtime.validLifecycle(lifecycleId)) return;
      runtime.send(target, 'OnBlockmakerWalletPackageError', {
        schemaVersion: runtime.schemaVersion,
        lifecycleId: lifecycleId,
        operationId: operationId,
        operationKind: 'sign_transaction_group',
        code: code
      });
    };
    if (!runtime || !runtime.ready || runtime.cleanupRequired()
      || !runtime.validOperation(operationId) || !runtime.validTarget(target)
      || !runtime.validLifecycle(lifecycleId)
      || runtime.facade !== root.__blockmakerUnityWebGlWalletPackageV1
      || !runtime.validFacade(runtime.facade)) {
      sendDirectError(runtime && runtime.cleanupRequired()
        ? 'PROVIDER_CLEANUP_REQUIRED' : 'PACKAGE_NOT_READY');
      return 0;
    }
    if (owner && owner.kind === 'sign_transaction_group'
      && owner.lifecycleId === lifecycleId && owner.operationId === operationId
      && owner.target === target && owner.state !== 'prepared') return 0;
    if (!owner || owner.kind !== 'sign_transaction_group'
      || owner.lifecycleId !== lifecycleId || owner.operationId !== operationId
      || owner.target !== target || owner.state !== 'prepared') {
      sendDirectError(owner ? 'REQUEST_ALREADY_PENDING' : 'TRANSACTION_GROUP_INVALID');
      return 0;
    }
    owner.state = 'signing';
    owner.operationSettled = false;
    var signed;
    // This is the final direct-click boundary. Never place an import, fetch,
    // await, Promise.resolve or other task hop before this exact facade call.
    try { signed = runtime.facade.signPreparedTransactionGroup(); }
    catch (failure) {
      owner.operationSettled = true;
      var synchronousCode = runtime.safeFailureCode(failure, 'PROVIDER_UNAVAILABLE');
      runtime.cancelTransactionOwner(owner, function (cleaned) {
        sendDirectError(cleaned ? synchronousCode : 'PROVIDER_CLEANUP_REQUIRED');
      });
      return 1;
    }
    Promise.resolve(signed).then(function (value) {
      if (root[ownerKey] !== owner) return;
      owner.operationSettled = true;
      if (owner.state === 'cancelling') {
        runtime.finishCancelledTransaction(owner);
        return;
      }
      if (owner.state !== 'signing') return;
      var copied = runtime.copiedSignedTransactionGroup(value, owner.expectedCount);
      if (!copied) {
        runtime.cancelTransactionOwner(owner, function (cleaned) {
          sendDirectError(cleaned ? 'WALLET_SIGNATURE_INVALID'
            : 'PROVIDER_CLEANUP_REQUIRED');
        });
        return;
      }
      if (!runtime.clearOwner(owner)) {
        runtime.latchCleanup();
        sendDirectError('PROVIDER_CLEANUP_REQUIRED');
        return;
      }
      runtime.send(target, 'OnBlockmakerWalletPackageSuccess', {
        schemaVersion: runtime.schemaVersion,
        lifecycleId: lifecycleId,
        operationId: operationId,
        operationKind: 'sign_transaction_group',
        phase: 'signed',
        signedTransactionsBase64: copied
      });
    }).catch(function (failure) {
      if (root[ownerKey] !== owner) return;
      owner.operationSettled = true;
      if (owner.state === 'cancelling') {
        runtime.finishCancelledTransaction(owner);
        return;
      }
      var code = runtime.safeFailureCode(failure, 'PROVIDER_UNAVAILABLE');
      runtime.cancelTransactionOwner(owner, function (cleaned) {
        sendDirectError(cleaned ? code : 'PROVIDER_CLEANUP_REQUIRED');
      });
    });
    return 1;
  },

  BlockmakerWalletPackageWebGL_Cancel: function (lifecyclePointer, operationId) {
    var lifecycleId = UTF8ToString(lifecyclePointer);
    var root = typeof globalThis === 'object' ? globalThis : window;
    var runtime = root.__blockmakerUnityWebGlWalletPackageRuntimeV1;
    var ownerKey = '__blockmakerUnityWebGlWalletPackageOwnerV1';
    var owner = root[ownerKey];
    if (!runtime || !owner
      || owner.lifecycleId !== lifecycleId || owner.operationId !== operationId) return;
    if (owner.kind === 'sign_transaction_group') {
      if (owner.state === 'preparing' || owner.state === 'prepared'
        || owner.state === 'signing') runtime.cancelTransactionOwner(owner, null);
      return;
    }
    if (owner.kind !== 'open_account'
      || (owner.state !== 'opening' && owner.state !== 'cancelling'
        && owner.state !== 'handoff' && owner.state !== 'acknowledging')) return;
    var facade = runtime.facade;
    if ((owner.state === 'handoff' || owner.state === 'acknowledging')
      && owner.handoff) {
      owner.state = 'rejecting';
      var abandoned;
      try { abandoned = facade.reject(owner.handoff); }
      catch (_) {
        runtime.latchCleanup();
        return;
      }
      Promise.resolve(abandoned).then(function () {
        if (root[ownerKey] === owner) runtime.clearOwner(owner);
      }).catch(function () {
        runtime.latchCleanup();
      });
      return;
    }
    if (owner.state === 'cancelling') return;
    owner.state = 'cancelling';
    var cancelled;
    try { cancelled = facade.cancel(); }
    catch (_) { return; }
    Promise.resolve(cancelled).then(function () {
      // `cancel` closes provider UI, but only the exact openAccount promise can
      // prove that the operation is terminal. Keep global ownership until that
      // promise rejects or yields a handoff that can be rejected exactly.
    }).catch(function () {
      // Keep exact ownership until openAccount settles and its handoff can be
      // rejected. A new operation cannot race uncertain provider UI.
    });
  },

  BlockmakerWalletPackageWebGL_Acknowledge: function (
    targetPointer, lifecyclePointer, operationId
  ) {
    var target = UTF8ToString(targetPointer);
    var lifecycleId = UTF8ToString(lifecyclePointer);
    var root = typeof globalThis === 'object' ? globalThis : window;
    var runtime = root.__blockmakerUnityWebGlWalletPackageRuntimeV1;
    var ownerKey = '__blockmakerUnityWebGlWalletPackageOwnerV1';
    var owner = root[ownerKey];
    if (!runtime || !owner || owner.kind !== 'open_account'
      || owner.lifecycleId !== lifecycleId || owner.operationId !== operationId
      || owner.target !== target || owner.state !== 'handoff' || !owner.handoff) {
      if (runtime) runtime.latchCleanup();
      return 0;
    }
    var facade = runtime.facade;
    owner.state = 'acknowledging';
    try {
      var acknowledged = facade.acknowledge(owner.handoff);
      if (acknowledged !== undefined)
        throw new Error('acknowledgement must return void');
      if (!runtime.clearOwner(owner))
        throw new Error('acknowledgement ownership could not be released');
      return 1;
    } catch (_) {
      // The C# caller treats 0 as terminal and clears its installed snapshot.
      // Keep the page closed to further wallet work while the exact family is
      // revoked, because a throwing/non-void controller may have transferred
      // browser ownership before failing.
      owner.state = 'ack_cleanup';
      runtime.latchCleanup();
      var cleanup;
      try { cleanup = facade.logout(owner.handoff); }
      catch (_) { return 0; }
      Promise.resolve(cleanup).then(function (value) {
        if (root[ownerKey] !== owner || owner.state !== 'ack_cleanup') return;
        if (!value || value.confirmed !== true)
          throw new Error('acknowledgement cleanup was not confirmed');
        runtime.clearOwner(owner);
      }).catch(function () {
        // Retain the immutable cleanup latch and exact owner. Reload/recovery
        // is required; no second package lifecycle may start.
      });
      return 0;
    }
  },

  BlockmakerWalletPackageWebGL_Reject: function (
    targetPointer, lifecyclePointer, operationId, codePointer
  ) {
    var target = UTF8ToString(targetPointer);
    var lifecycleId = UTF8ToString(lifecyclePointer);
    var requestedCode = UTF8ToString(codePointer);
    var root = typeof globalThis === 'object' ? globalThis : window;
    var runtime = root.__blockmakerUnityWebGlWalletPackageRuntimeV1;
    var ownerKey = '__blockmakerUnityWebGlWalletPackageOwnerV1';
    var owner = root[ownerKey];
    if (!runtime || !owner || owner.kind !== 'open_account'
      || owner.lifecycleId !== lifecycleId || owner.operationId !== operationId
      || owner.target !== target || !owner.handoff
      || (owner.state !== 'handoff' && owner.state !== 'acknowledging')) return;
    var allowed = {
      PLAYER_CANCELLED: true,
      PROVIDER_UNAVAILABLE: true,
      SESSION_CONFLICT: true
    };
    var code = String(requestedCode || '').trim().toUpperCase();
    if (!allowed[code]) code = 'PROVIDER_UNAVAILABLE';
    owner.state = 'rejecting';
    var rejected;
    try { rejected = runtime.facade.reject(owner.handoff); }
    catch (_) {
      runtime.latchCleanup();
      runtime.send(target, 'OnBlockmakerWalletPackageError',
        runtime.envelope(owner, { code: 'PROVIDER_CLEANUP_REQUIRED' }));
      return;
    }
    Promise.resolve(rejected).then(function () {
      if (root[ownerKey] === owner) runtime.clearOwner(owner);
      runtime.send(target, 'OnBlockmakerWalletPackageError',
        runtime.envelope(owner, { code: code }));
    }).catch(function () {
      runtime.latchCleanup();
      runtime.send(target, 'OnBlockmakerWalletPackageError',
        runtime.envelope(owner, { code: 'PROVIDER_CLEANUP_REQUIRED' }));
    });
  },

  BlockmakerWalletPackageWebGL_Logout: function (
    targetPointer, lifecyclePointer, operationId, sessionTokenPointer,
    refreshTokenPointer, walletAddressPointer, accountKindPointer,
    authProviderPointer
  ) {
    var target = UTF8ToString(targetPointer);
    var lifecycleId = UTF8ToString(lifecyclePointer);
    var root = typeof globalThis === 'object' ? globalThis : window;
    var runtime = root.__blockmakerUnityWebGlWalletPackageRuntimeV1;
    var ownerKey = '__blockmakerUnityWebGlWalletPackageOwnerV1';
    var session = {
      sessionToken: UTF8ToString(sessionTokenPointer),
      refreshToken: UTF8ToString(refreshTokenPointer),
      walletAddress: UTF8ToString(walletAddressPointer),
      accountKind: UTF8ToString(accountKindPointer),
      authProvider: UTF8ToString(authProviderPointer)
    };
    var send = function (confirmed) {
      if (!runtime || !runtime.validTarget(target)
        || !runtime.validLifecycle(lifecycleId)) return;
      runtime.send(target, 'OnBlockmakerWalletPackageLogout', {
        schemaVersion: runtime.schemaVersion,
        lifecycleId: lifecycleId,
        operationId: operationId,
        operationKind: 'logout',
        confirmed: confirmed === true,
        code: confirmed === true ? null : 'LOGOUT_UNCONFIRMED'
      });
    };
    if (!runtime || !runtime.ready || runtime.cleanupRequired()
      || !runtime.validOperation(operationId) || !runtime.validTarget(target)
      || !runtime.validLifecycle(lifecycleId) || !runtime.safeSession(session)
      || runtime.facade !== root.__blockmakerUnityWebGlWalletPackageV1) {
      send(false);
      return;
    }
    var owner = {
      lifecycleId: lifecycleId,
      operationId: operationId,
      kind: 'logout',
      target: target,
      state: 'logging_out'
    };
    if (!runtime.setOwner(owner)) {
      send(false);
      return;
    }
    var confirmation;
    try { confirmation = runtime.facade.logout(Object.freeze(session)); }
    catch (_) { confirmation = Promise.reject(new Error('logout failed')); }
    Promise.resolve(confirmation).then(function (value) {
      if (root[ownerKey] !== owner) return;
      if (!value || value.confirmed !== true) throw new Error('logout was not confirmed');
      runtime.clearOwner(owner);
      send(true);
    }).catch(function () {
      if (root[ownerKey] !== owner) return;
      runtime.latchCleanup();
      runtime.clearOwner(owner);
      send(false);
    });
  },

  BlockmakerWalletPackageWebGL_OpenFunding: function (
    targetPointer, lifecyclePointer, operationId, accessTokenPointer,
    accountKindPointer, authProviderPointer
  ) {
    var target = UTF8ToString(targetPointer);
    var lifecycleId = UTF8ToString(lifecyclePointer);
    var accessToken = UTF8ToString(accessTokenPointer);
    var accountKind = UTF8ToString(accountKindPointer);
    var authProvider = UTF8ToString(authProviderPointer);
    var root = typeof globalThis === 'object' ? globalThis : window;
    var runtime = root.__blockmakerUnityWebGlWalletPackageRuntimeV1;
    var ownerKey = '__blockmakerUnityWebGlWalletPackageOwnerV1';
    var sendError = function (code) {
      if (!runtime || !runtime.validTarget(target)
        || !runtime.validLifecycle(lifecycleId)) return;
      runtime.send(target, 'OnBlockmakerWalletPackageError', {
        schemaVersion: runtime.schemaVersion,
        lifecycleId: lifecycleId,
        operationId: operationId,
        operationKind: 'funding',
        code: code
      });
    };
    if (!runtime || !runtime.ready || runtime.cleanupRequired()
      || runtime.facade !== root.__blockmakerUnityWebGlWalletPackageV1) {
      sendError(runtime && runtime.cleanupRequired()
        ? 'PROVIDER_CLEANUP_REQUIRED' : 'PACKAGE_NOT_READY');
      return;
    }
    if (!runtime.validOperation(operationId) || !runtime.validTarget(target)
      || !runtime.validLifecycle(lifecycleId)
      || !runtime.printable(accessToken, 8192)) {
      sendError('PROVIDER_UNAVAILABLE');
      return;
    }
    if (!runtime.economicPair(accountKind, authProvider)) {
      sendError('PROVIDER_UNAVAILABLE');
      return;
    }
    var owner = {
      lifecycleId: lifecycleId,
      operationId: operationId,
      kind: 'funding',
      target: target,
      state: 'opening'
    };
    if (!runtime.setOwner(owner)) {
      sendError('REQUEST_ALREADY_PENDING');
      return;
    }
    var opened;
    // This call remains in the user-activation task. Only a short-lived access
    // JWT and the exact public TxnLab wallet classifiers cross this ABI.
    try {
      opened = runtime.facade.openFunding(Object.freeze({
        accessToken: accessToken,
        accountKind: accountKind,
        authProvider: authProvider
      }));
    } catch (_) {
      runtime.clearOwner(owner);
      sendError('PROVIDER_UNAVAILABLE');
      return;
    }
    Promise.resolve(opened).then(function () {
      if (root[ownerKey] !== owner || owner.state !== 'opening') return;
      owner.state = 'open';
      runtime.send(target, 'OnBlockmakerWalletPackageSuccess',
        runtime.envelope(owner, { phase: 'opened' }));
    }).catch(function () {
      if (root[ownerKey] !== owner || owner.state === 'closing') return;
      runtime.clearOwner(owner);
      sendError('PROVIDER_UNAVAILABLE');
    });
  },

  BlockmakerWalletPackageWebGL_CloseFunding: function (
    targetPointer, lifecyclePointer, operationId
  ) {
    var target = UTF8ToString(targetPointer);
    var lifecycleId = UTF8ToString(lifecyclePointer);
    var root = typeof globalThis === 'object' ? globalThis : window;
    var runtime = root.__blockmakerUnityWebGlWalletPackageRuntimeV1;
    var ownerKey = '__blockmakerUnityWebGlWalletPackageOwnerV1';
    var owner = root[ownerKey];
    if (!runtime || !owner || owner.kind !== 'funding'
      || owner.lifecycleId !== lifecycleId || owner.operationId !== operationId
      || owner.target !== target
      || (owner.state !== 'opening' && owner.state !== 'open')) return;
    owner.state = 'closing';
    var closed;
    try { closed = runtime.facade.closeFunding(); }
    catch (_) { closed = Promise.reject(new Error('funding close failed')); }
    Promise.resolve(closed).then(function () {
      if (root[ownerKey] !== owner || owner.state !== 'closing') return;
      runtime.clearOwner(owner);
      runtime.send(target, 'OnBlockmakerWalletPackageFundingClosed',
        runtime.envelope(owner, { success: true, code: null }));
    }).catch(function () {
      if (root[ownerKey] !== owner || owner.state !== 'closing') return;
      runtime.latchCleanup();
      runtime.send(target, 'OnBlockmakerWalletPackageFundingClosed',
        runtime.envelope(owner, {
          success: false,
          code: 'PROVIDER_CLEANUP_REQUIRED'
        }));
    });
  }
});
