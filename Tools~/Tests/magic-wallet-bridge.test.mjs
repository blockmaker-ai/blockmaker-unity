import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";

const bridgeSource = fs.readFileSync(
  new URL("../../Plugins/WebGL/BlockmakerWalletBridge.jslib", import.meta.url),
  "utf8",
);

function createBridge(algorand) {
  const messages = [];
  const context = {
    LibraryManager: { library: {} },
    mergeInto(target, source) { Object.assign(target, source); },
    autoAddDeps() {},
    UTF8ToString(value) { return value; },
    SendMessage(gameObject, callback, payload) {
      messages.push({ gameObject, callback, payload });
    },
    window: { _bmMagicInstance: { algorand } },
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
  return { library, messages };
}

async function settle() {
  await new Promise((resolve) => setImmediate(resolve));
}

{
  let observedCount = 0;
  const { library, messages } = createBridge({
    signGroupTransactionV2(txns) {
      observedCount = txns.length;
      return Promise.resolve(txns.map((_, index) => new Uint8Array([index + 1, 2, 3])));
    },
  });
  const unsigned = ["AQID", "BAUG", "BwgJ", "CgsM", "DQ4P", "EBES"];
  library.MagicSignGroupTransaction(JSON.stringify(unsigned), "Auth", "signed", "failed");
  await settle();
  assert.equal(observedCount, 6);
  assert.equal(messages.at(-1).callback, "signed");
  assert.deepEqual(JSON.parse(messages.at(-1).payload), ["AQID", "AgID", "AwID", "BAID", "BQID", "BgID"]);
}

{
  const { library, messages } = createBridge({
    signGroupTransactionV2() { return Promise.resolve([new Uint8Array([1])]); },
  });
  library.MagicSignGroupTransaction('["AQID","BAUG"]', "Auth", "signed", "failed");
  await settle();
  assert.equal(messages.at(-1).callback, "failed");
  assert.match(messages.at(-1).payload, /expected 2/i);
}

console.log("Magic WebGL bridge tests passed (six-transaction Garage group and incomplete-group refusal).");
