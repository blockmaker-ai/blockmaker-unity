import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";

const bridgeSource = fs.readFileSync(
  new URL("../../Plugins/WebGL/BlockmakerWalletBridge.jslib", import.meta.url),
  "utf8",
);

const ADDRESS = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAY5HFKQ";

function createBridge(FakeLute) {
  const messages = [];
  const opened = [];
  const windowObject = {
    BmLuteVendor: { default: FakeLute },
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
    atob(value) { return Buffer.from(value, "base64").toString("binary"); },
    btoa(value) { return Buffer.from(value, "binary").toString("base64"); },
  };

  vm.runInNewContext(bridgeSource, context, { filename: "BlockmakerWalletBridge.jslib" });
  const library = context.LibraryManager.library;
  for (const [name, value] of Object.entries(library)) {
    if (name.startsWith("$") && typeof value === "function") context[name.slice(1)] = value;
    if (!name.startsWith("$") && typeof value === "function") context[`_${name}`] = value;
  }

  return { library, messages, opened, windowObject };
}

async function settle() {
  await new Promise((resolve) => setImmediate(resolve));
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

  assert.equal(library.LuteJsPrimeSignWindow(), 1);
  assert.equal(opened.length, 1);
  assert.equal(opened[0].url, "https://lute.app/sign");
  assert.equal(opened[0].name, "NFTURBO");

  library.LuteJsSignTransaction("AQID", "Auth", "signed", "failed");
  await settle();
  assert.equal(messages.pop().payload, "AQID");

  library.LuteJsSignGroupTransaction('["AQID","BAUG"]', "Auth", "group", "failed");
  await settle();
  assert.deepEqual(JSON.parse(messages.pop().payload), ["AQID", "AgID"]);
  library.LuteJsDisconnect();
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

console.log("Lute WebGL bridge tests passed (connect, popup, single sign, group sign, rejection, incomplete group).");
