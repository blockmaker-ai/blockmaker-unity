import { createBlockmaker as e } from "./blockmaker.js";
import { loadBlockmakerTxnLabWallets as t } from "./blockmaker-txnlab-wallet.mjs";
//#region shared/embeddedWalletIdentity.ts
function n(e, t) {
	let n = (e) => Array.isArray(e) ? e.map(n) : e && typeof e == "object" ? Object.fromEntries(Object.keys(e).sort().map((t) => [t, n(e[t])])) : e;
	return JSON.stringify({
		clientId: e,
		network: "sapphire_mainnet",
		algorand: "mainnet-v1.0",
		derivation: "txnlab-5-private_key-algorand",
		connection: JSON.stringify(n(t))
	});
}
//#endregion
//#region scripts/unity-email-identity.mjs
async function r(e) {
	let t = await fetch(e, {
		redirect: "error",
		credentials: "omit",
		cache: "no-store",
		signal: AbortSignal.timeout(15e3)
	});
	if (!t.ok) throw Error("Email wallet setup could not be verified. Please retry later.");
	let n = t.body?.getReader();
	if (!n) throw Error("Email wallet setup returned no response.");
	let r = 0, i = [];
	try {
		for (;;) {
			let e = await n.read();
			if (e.done) break;
			if (r += e.value.byteLength, r > 65536) throw Error("Email wallet setup response is too large.");
			i.push(e.value);
		}
	} finally {
		await n.cancel().catch(() => {});
	}
	let a = new Uint8Array(r), o = 0;
	for (let e of i) a.set(e, o), o += e.byteLength;
	return JSON.parse(new TextDecoder().decode(a));
}
async function i({ baseUrl: e, gameId: t, clientId: i }) {
	let a = await r(e + "/v1/integrations/managed-email-setup?gameId=" + encodeURIComponent(t)), o = a?.setup;
	if (a.success !== !0 || o?.schemaVersion !== "blockmaker-managed-email/v1" || o.gameId !== t) throw Error("The email setup does not belong to this game.");
	if (o.state === "not_requested") return;
	if (o.state !== "configured" || o.clientId !== i || o.origin !== globalThis.location?.origin || o.providerNetwork !== "sapphire_mainnet") throw Error("The game email project and exact origin need review before sign-in.");
	if (!o.identityGroup) return;
	let s = await r("https://api.web3auth.io/signer-service/api/v2/configuration?project_id=" + encodeURIComponent(i) + "&network=sapphire_mainnet"), c = s.embeddedWalletAuth?.filter((e) => e.authConnection === "email_passwordless") ?? [];
	if (c.length !== 1 || !s.whitelist?.urls?.includes(globalThis.location?.origin)) throw Error("The pinned email connection or exact provider origin changed.");
	let l = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(n(i, c[0]))), u = Array.from(new Uint8Array(l), (e) => e.toString(16).padStart(2, "0")).join("");
	if (o.identityGroup.scope !== "shared_wallet" || o.identityGroup.network !== "mainnet" || u !== o.identityGroup.fingerprint) throw Error("The shared email wallet identity changed. Contact the game owner; no replacement wallet was created.");
}
//#endregion
//#region scripts/blockmaker-unity-webgl-wallet-host.entry.mjs
var a = "__blockmakerUnityWebGlWalletPackageV1", o = Object.freeze({
	network: "mainnet",
	genesisId: "mainnet-v1.0",
	genesisHashBase64: "wGHE2Pwdvd7S12BL5FaOP20EGYesN73ktiC1qzkkit8="
}), s = Object.freeze({
	pera_lute: Object.freeze({
		id: "pera_lute",
		providerIds: Object.freeze(["pera", "lute"])
	}),
	pera_lute_txnlab_web3auth: Object.freeze({
		id: "pera_lute_txnlab_web3auth",
		providerIds: Object.freeze([
			"pera",
			"lute",
			"txnlab_web3auth"
		])
	})
}), c = Object.freeze([
	"appName",
	"baseUrl",
	"gameId",
	"restoredSession",
	"runtimeProfile"
]), l = Object.freeze([
	"accountKind",
	"authProvider",
	"walletAddress"
]), u = Object.freeze([
	"openAccount",
	"captureDeploymentEvidence",
	"downloadDeploymentEvidence",
	"acknowledge",
	"reject",
	"cancel",
	"logout",
	"openFunding",
	"closeFunding",
	"prepareTransactionGroup",
	"signPreparedTransactionGroup",
	"cancelPreparedTransactionGroup"
]), d = null;
function f(e, t, n) {
	if (!e || typeof e != "object" || Array.isArray(e)) throw Error(`${n} requires an exact object.`);
	let r = Object.keys(e).sort(), i = [...t].sort();
	if (r.length !== i.length || r.some((e, t) => e !== i[t])) throw Error(`${n} has an unsupported shape.`);
}
function p(e) {
	let t = String(e ?? "").trim();
	if (!t || t.length > 2048 || /[\u0000-\u0020\u007f-\u009f]/.test(t)) throw Error("The Unity WebGL wallet host requires an exact Blockmaker origin.");
	let n = new URL(t), r = [
		"localhost",
		"127.0.0.1",
		"[::1]"
	].includes(n.hostname);
	if (n.username || n.password || n.search || n.hash || n.pathname !== "/" && n.pathname !== "" || n.protocol !== "https:" && !(n.protocol === "http:" && r)) throw Error("The Unity WebGL wallet host requires an exact HTTPS Blockmaker origin.");
	return n.origin;
}
function m(e) {
	let t = String(e ?? "").trim();
	if (!/^[A-Za-z0-9_-]{1,128}$/.test(t) || /^sk_/i.test(t)) throw Error("The Unity WebGL wallet host requires a bounded public game ID.");
	return t;
}
function h(e) {
	if (e == null || e === "") return Object.freeze({
		...s.pera_lute,
		clientId: null
	});
	if (typeof e != "string" || e !== e.trim() || !/^[\x21-\x7e]{16,512}$/.test(e) || /\s/.test(e) || /^sk_/i.test(e)) throw Error("The Unity WebGL wallet host requires an absent or valid public Web3Auth client ID.");
	return Object.freeze({
		...s.pera_lute_txnlab_web3auth,
		clientId: e
	});
}
function g(e) {
	let t = String(e ?? "").trim();
	if (!/^[^\u0000-\u001f\u007f]{1,80}$/.test(t)) throw Error("The Unity WebGL wallet host requires a neutral app name of 1-80 characters.");
	return t;
}
function _(e, t) {
	if (e === null) return null;
	if (f(e, l, "The restored Unity player session"), !/^[A-Z2-7]{58}$/.test(String(e.walletAddress ?? "")) || e.accountKind !== "algorand_wallet" || !t.includes(e.authProvider)) throw Error("The Unity WebGL wallet host received an invalid restored player session.");
	return Object.freeze({
		walletAddress: e.walletAddress,
		accountKind: e.accountKind,
		authProvider: e.authProvider
	});
}
function v(e) {
	if (!e || typeof e != "object") throw Error("The Blockmaker SDK did not return a Unity WebGL wallet-package controller.");
	for (let t of u) if (typeof e[t] != "function") throw Error(`The Blockmaker Unity WebGL wallet-package controller is missing ${t}().`);
	return e;
}
function y(e, t) {
	let n = e?.algorandWallets;
	if (!Array.isArray(n) || n.length !== t.length || typeof e?.algosdk?.decodeUnsignedTransaction != "function" || typeof e?.algosdk?.encodeUnsignedTransaction != "function" || typeof e?.algosdk?.decodeSignedTransaction != "function" || typeof e?.algosdk?.computeGroupID != "function" || typeof e?.algosdk?.encodeAddress != "function" || typeof e?.disconnectAll != "function") throw Error("The canonical TxnLab runtime did not return the exact Unity wallet setup.");
	let r = /* @__PURE__ */ new Set();
	for (let e of n) {
		let n = String(e?.providerId ?? ""), i = e?.wallet;
		if (!t.includes(n) || r.has(n) || i?.providerId !== n || i?.networkGenesisId !== o.genesisId || typeof i?.connect != "function" || typeof i?.resumeSession != "function" || typeof i?.disconnect != "function" || typeof i?.signTransactions != "function") throw Error("The canonical TxnLab runtime did not return the exact Unity wallet profile.");
		r.add(n);
	}
	if (t.some((e) => !r.has(e))) throw Error("The canonical TxnLab runtime omitted a required Unity wallet provider.");
	return Object.freeze({
		algorandWallets: Object.freeze(n.slice()),
		algosdk: e.algosdk,
		disconnectAll: () => e.disconnectAll()
	});
}
function b(e, t) {
	return Object.freeze({
		openAccount: (n) => {
			if (t && !(n?.presentation === "unity" && (n.providerId ?? "pera") === "pera")) {
				let e = /* @__PURE__ */ Error("Email wallet setup needs review. Continue with Pera or try again later.");
				throw e.code = "PROVIDER_NOT_ENABLED", e;
			}
			return e.openAccount(n);
		},
		captureDeploymentEvidence: (t) => e.captureDeploymentEvidence(t),
		downloadDeploymentEvidence: (t) => e.downloadDeploymentEvidence(t),
		getDeploymentEvidenceStatus: () => typeof e.getDeploymentEvidenceStatus == "function" ? e.getDeploymentEvidenceStatus() : null,
		acknowledge: (t) => {
			if (e.acknowledge(t) !== void 0) throw Error("The Blockmaker wallet-package acknowledgement must return void.");
		},
		reject: (t) => e.reject(t),
		cancel: () => e.cancel(),
		logout: (t) => e.logout(t),
		openFunding: (t) => e.openFunding(t),
		closeFunding: () => e.closeFunding(),
		prepareUniversalUsername: (t) => {
			if (e.prepareUniversalUsername(t) !== void 0) throw Error("Preparing a username must return void.");
		},
		prepareTransactionGroup: (t) => {
			if (e.prepareTransactionGroup(t) !== void 0) throw Error("Preparing a Blockmaker transaction group must return void.");
		},
		signPreparedTransactionGroup: () => e.signPreparedTransactionGroup(),
		cancelPreparedTransactionGroup: () => {
			if (e.cancelPreparedTransactionGroup() !== void 0) throw Error("Cancelling a Blockmaker transaction group must return void.");
		}
	});
}
function x(n) {
	let r = n?.runtimeProfile;
	f(n, r === "pera_lute_txnlab_web3auth" ? [...c, "clientId"] : c, "The Unity WebGL wallet host installation");
	let s = p(n.baseUrl), l = m(n.gameId), u = h(n.clientId);
	if (r !== u.id) throw Error("The Unity WebGL wallet host runtime profile does not match its client-ID selector.");
	let x = g(n.appName), S = _(n.restoredSession, u.providerIds), C = JSON.stringify({
		baseUrl: s,
		gameId: l,
		runtimeProfile: u.id,
		clientId: u.clientId,
		appName: x,
		restoredSession: S
	});
	if (d) {
		if (d.configurationKey !== C) throw Error("A different Unity WebGL wallet-package configuration already owns this page.");
		return d.promise;
	}
	if (Object.prototype.hasOwnProperty.call(globalThis, a)) throw Error("The Unity WebGL wallet-package page global is already owned.");
	let w = (async () => {
		let n = u.clientId === null ? Object.freeze({
			providerIds: u.providerIds,
			appName: x
		}) : Object.freeze({
			providerIds: u.providerIds,
			clientId: u.clientId,
			appName: x
		}), r = null;
		if (u.clientId !== null) try {
			await i({
				baseUrl: s,
				gameId: l,
				clientId: u.clientId
			});
		} catch (e) {
			r = e;
		}
		let c = y(await t(n), u.providerIds), d = e(Object.freeze({
			baseUrl: s,
			gameId: l,
			storage: !1,
			clientKind: "unity_webgl"
		}));
		if (!d?.unityWebGl || typeof d.unityWebGl.walletPackage != "function") throw Error("This Blockmaker SDK does not include the Unity WebGL wallet package.");
		let f = b(v(d.unityWebGl.walletPackage(Object.freeze({
			network: o.network,
			genesisId: o.genesisId,
			genesisHashBase64: o.genesisHashBase64,
			hostRuntimeUrl: import.meta.url,
			runtimeProfile: u.id,
			restoredSession: S,
			loadAlgorandWalletSetup: () => c
		}))), r);
		if (Object.prototype.hasOwnProperty.call(globalThis, a)) throw Error("The Unity WebGL wallet-package page global was claimed during initialization.");
		if (Object.defineProperty(globalThis, a, {
			value: f,
			configurable: !1,
			enumerable: !1,
			writable: !1
		}), globalThis[a] !== f) throw Error("The Unity WebGL wallet-package page global could not be installed.");
		return f;
	})();
	return d = Object.freeze({
		configurationKey: C,
		promise: w
	}), w;
}
//#endregion
export { x as installBlockmakerUnityWebGlWalletPackage };
