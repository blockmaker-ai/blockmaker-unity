import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";

const bridgeSource = fs.readFileSync(
  new URL("../../Plugins/WebGL/BlockmakerWalletBridge.jslib", import.meta.url),
  "utf8",
);

const ADDRESS = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAY5HFKQ";

function createBridge(FakeLute, FakePera = null) {
  const messages = [];
  const opened = [];
  const windowObject = {
    BmLuteVendor: { default: FakeLute },
    ...(FakePera ? { BmPeraVendor: { PeraWalletConnect: FakePera } } : {}),
    screenX: 0,
    screenY: 0,
    open(url, name, params) {
      const popup = { url, name, params, closed: false, close() { this.closed = true; } };
      opened.push(popup);
      return popup;
    },
  };
  const context = {
    LibraryManager: { library: {} },
    mergeInto(target, source) { Object.assign(target, source); },
    autoAddDeps() {},
    UTF8ToString(value) { return value; },
    SendMessage(gameObject, callback, payload) {
      messages.push({ gameObject, callback, payload });
    },
    window: windowObject,
    document: { title: "NFTURBO", head: { appendChild() {} }, createElement() { return {}; } },
    console,
    Promise,
    Uint8Array,
    Array,
    JSON,
    Math,
    Date,
    setTimeout,
    clearTimeout,
    setInterval,
    clearInterval,
    atob(value) { return Buffer.from(value, "base64").toString("binary"); },
    btoa(value) { return Buffer.from(value, "binary").toString("base64"); },
  };

  vm.runInNewContext(bridgeSource, context, { filename: "BlockmakerWalletBridge.jslib" });
  const library = context.LibraryManager.library;
  for (const [name, value] of Object.entries(library)) {
    if (name.startsWith("$") && typeof value === "function") context[name.slice(1)] = value;
    if (!name.startsWith("$") && typeof value === "function") context[`_${name}`] = value;
  }

  return { library, messages, opened, windowObject, context };
}

{
  class UnusedLute {}
  const { library, messages, context } = createBridge(UnusedLute);
  context.bmPeraEnsureSession = () => Promise.resolve({
    signTransaction(groups) {
      return Promise.resolve(groups[0].map((_, i) => new Uint8Array([i + 1, 2, 3])));
    },
  });
  context.loadAlgosdk = () => Promise.resolve({
    decodeUnsignedTransaction(bytes) { return bytes; },
  });

  library.PeraJsSignTransactionTagged(
    "AQID", "pera-attempt-1", "Auth", "signed", "failed",
  );
  await settle();
  assert.equal(messages.pop().payload, "pera-attempt-1|AQID");

  library.PeraJsSignGroupTransactionTagged(
    '["AQID","BAUG"]', "pera-group-1", "Auth", "group", "failed",
  );
  await settle();
  const taggedGroup = messages.pop().payload;
  assert.ok(taggedGroup.startsWith("pera-group-1|"));
  assert.deepEqual(JSON.parse(taggedGroup.slice(taggedGroup.indexOf("|") + 1)), ["AQID", "AgID"]);
}

async function settle() {
  await new Promise((resolve) => setImmediate(resolve));
}

{
  let finishConnect;
  class UnusedLute {}
  class FakePera {
    constructor() {
      this.connector = { connected: false, uri: "wc:tagged-pera", on() {} };
    }
    reconnectSession() { return Promise.resolve([]); }
    connect() {
      this.connector.connected = true;
      return new Promise((resolve) => { finishConnect = resolve; });
    }
  }
  const { library, messages, context } = createBridge(UnusedLute, FakePera);
  context.loadQRCode = () => Promise.resolve({
    toDataURL() { return Promise.resolve("data:image/png;base64,UE5H"); },
  });

  library.PeraJsConnectTagged(
    "pera-connect-1", "Auth", "connected", "failed", "qr",
  );
  await new Promise((resolve) => setTimeout(resolve, 125));
  await settle();
  assert.deepEqual(messages.shift(), {
    gameObject: "Auth",
    callback: "qr",
    payload: "pera-connect-1|Pera|wc:tagged-pera|UE5H",
  });
  finishConnect([ADDRESS]);
  await settle();
  assert.deepEqual(messages.shift(), {
    gameObject: "Auth",
    callback: "connected",
    payload: `pera-connect-1|${ADDRESS}`,
  });
}

{
  class FakeLute {
    constructor(siteName) { this.siteName = siteName; }
    connect() { return Promise.resolve([ADDRESS]); }
    signTxns(txns) { return Promise.resolve(txns.map((_, i) => new Uint8Array([i + 1, 2, 3]))); }
  }

  const { library, messages, opened } = createBridge(FakeLute);
  library.LuteJsConnect("Auth", "connected", "failed");
  await settle();
  assert.deepEqual(messages.pop(), {
    gameObject: "Auth",
    callback: "connected",
    payload: `Lute:${ADDRESS}`,
  });

  library.LuteJsConnectTagged(
    "lute-connect-1", "Auth", "connected", "failed",
  );
  await settle();
  assert.deepEqual(messages.pop(), {
    gameObject: "Auth",
    callback: "connected",
    payload: `lute-connect-1|Lute:${ADDRESS}`,
  });

  assert.equal(library.LuteJsPrimeSignWindow(), 1);
  assert.equal(opened.length, 1);
  assert.equal(opened[0].url, "https://lute.app/sign");
  assert.equal(opened[0].name, "NFTURBO");

  library.LuteJsSignTransaction("AQID", "Auth", "signed", "failed");
  await settle();
  assert.equal(messages.pop().payload, "AQID");

  library.LuteJsSignTransactionTagged(
    "AQID", "shop-attempt-1", "Auth", "shopSigned", "shopFailed",
  );
  await settle();
  assert.deepEqual(messages.pop(), {
    gameObject: "Auth",
    callback: "shopSigned",
    payload: "shop-attempt-1|AQID",
  });

  library.LuteJsSignGroupTransaction('["AQID","BAUG"]', "Auth", "group", "failed");
  await settle();
  assert.deepEqual(JSON.parse(messages.pop().payload), ["AQID", "AgID"]);

  library.LuteJsSignGroupTransactionTagged(
    '["AQID","BAUG"]', "group-attempt-1", "Auth", "group", "failed",
  );
  await settle();
  const taggedGroup = messages.pop().payload;
  assert.ok(taggedGroup.startsWith("group-attempt-1|"));
  assert.deepEqual(JSON.parse(taggedGroup.slice(taggedGroup.indexOf("|") + 1)), ["AQID", "AgID"]);
  library.LuteJsDisconnect();
}

{
  let finishSign;
  class SlowLute {
    constructor(siteName) { this.siteName = siteName; }
    signTxns() {
      return new Promise((resolve) => { finishSign = resolve; });
    }
  }

  const { library, messages, opened, windowObject } = createBridge(SlowLute);
  assert.equal(library.LuteJsPrimeSignWindow(), 1);
  library.LuteJsSignTransactionTagged(
    "AQID", "owned-active-sign", "Auth", "signed", "failed",
  );
  assert.equal(windowObject._bmLutePrimedWindow, null);
  assert.equal(windowObject._bmLuteActiveSignWindow, opened[0]);
  assert.equal(windowObject._bmLuteActiveSignAttemptId, "owned-active-sign");

  library.LuteJsCancelActiveSignWindow("wrong-sign");
  assert.equal(opened[0].closed, false);
  library.LuteJsCancelActiveSignWindow("owned-active-sign");
  assert.equal(opened[0].closed, true);
  assert.equal(windowObject._bmLuteActiveSignWindow, null);

  finishSign([new Uint8Array([1, 2, 3])]);
  await settle();
  assert.equal(messages.pop().payload, "owned-active-sign|AQID");

  assert.equal(library.LuteJsPrimeSignWindow(), 1);
  assert.equal(opened.length, 2, "the next click must reserve a fresh Lute window");
  library.LuteJsCancelPrimedSignWindow();
  assert.equal(opened[1].closed, true);
}

{
  class RejectingLute {
    constructor(siteName) { this.siteName = siteName; }
    connect() { return Promise.reject(Object.assign(new Error("User Rejected Request"), { code: 4100 })); }
    signTxns() { return Promise.resolve([null]); }
  }

  const { library, messages } = createBridge(RejectingLute);
  library.LuteJsConnect("Auth", "connected", "failed");
  await settle();
  assert.equal(messages.pop().payload, "The request was not approved in Lute. Nothing was submitted.");

  library.LuteJsSignTransaction("AQID", "Auth", "signed", "failed");
  await settle();
  assert.equal(messages.pop().payload, "Lute did not sign the transaction. Nothing was submitted.");
}

{
  class IncompleteLute {
    constructor(siteName) { this.siteName = siteName; }
    signTxns() { return Promise.resolve([new Uint8Array([1])]); }
  }

  const { library, messages } = createBridge(IncompleteLute);
  library.LuteJsSignGroupTransaction('["AQID","BAUG"]', "Auth", "group", "failed");
  await settle();
  assert.equal(messages.pop().payload, "Lute returned an incomplete transaction group. Nothing was submitted.");
}

console.log("Pera/Lute WebGL bridge tests passed (tagged connect/QR, owned popup cancellation, tagged single/group sign, rejection, incomplete group).");
