/**
 * Blockmaker Browser SDK v0.1
 *
 * Safe for browser/WebGL-facing code: it accepts a public gameId and player
 * sessions only. It deliberately rejects server/API keys.
 */

export class BlockmakerError extends Error {
  constructor(message, options = {}) {
    super(message)
    this.name = 'BlockmakerError'
    this.status = options.status ?? 0
    this.code = options.code ?? 'REQUEST_FAILED'
    this.requestId = options.requestId ?? null
    this.retryAfter = options.retryAfter ?? null
    this.fieldPath = options.fieldPath ?? null
    this.mayHaveApplied = options.mayHaveApplied === true
    this.developerDiagnostic = options.developerDiagnostic ?? null
    this.details = options.details
  }
}

const memoryStorage = () => {
  const values = new Map()
  return {
    getItem: key => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, String(value)),
    removeItem: key => values.delete(key),
  }
}

const isSafeApiOrigin = url => url.protocol === 'https:' || (
  url.protocol === 'http:' && ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname)
)

const safeExternalPageUrl = value => {
  try {
    const url = new URL(String(value ?? ''))
    if (url.username || url.password) return null
    if (url.protocol === 'https:' || (
      url.protocol === 'http:' && ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname)
    )) return url.toString()
  } catch { /* invalid or relative URL */ }
  return null
}

const isAlgorandAddressShape = value => /^[A-Z2-7]{58}$/.test(String(value ?? '').trim())
const isEvmAddressShape = value => /^0x[0-9a-fA-F]{40}$/.test(String(value ?? '').trim())
const automationStableIdPattern = /^[A-Za-z0-9][A-Za-z0-9:._-]{0,127}$/
const automationSimpleKeyPattern = /^[a-z0-9][a-z0-9_.-]{0,79}$/
const automationSubjectKindPattern = /^[a-z0-9][a-z0-9_-]{0,31}$/
const automationSubjectIdPattern = /^[A-Za-z0-9][A-Za-z0-9:._-]{0,127}$/
const automationActionStatuses = new Set([
  'proposed', 'prepared', 'awaiting_approval', 'leased', 'submitted',
  'confirmed', 'skipped', 'failed', 'expired', 'cancelled',
])
const automationWalletOverrideKeys = new Set([
  'wallet', 'walletaddress', 'ownerwallet', 'ownerwalletaddress',
  'playerwallet', 'playerwalletaddress', 'sender', 'senderaddress',
  'from', 'fromaddress', 'signer', 'signeraddress', 'authaddr', 'authorizedsigner',
])

const assertNoAutomationWalletOverride = value => {
  const seen = new WeakSet()
  const inspect = item => {
    if (!item || typeof item !== 'object' || seen.has(item)) return
    seen.add(item)
    if (Array.isArray(item)) {
      item.forEach(inspect)
      return
    }
    for (const [key, child] of Object.entries(item)) {
      if (automationWalletOverrideKeys.has(key.toLowerCase().replace(/[^a-z0-9]/g, '')))
        throw new Error('Automation cannot accept a wallet override. The player wallet comes from the wallet-bound Blockmaker session.')
      inspect(child)
    }
  }
  inspect(value)
}

const requireAutomationObject = (value, method) => {
  if (!value || typeof value !== 'object' || Array.isArray(value))
    throw new Error(`automation.${method} requires an input object.`)
  assertNoAutomationWalletOverride(value)
  return value
}

const requireAutomationStableId = (value, label) => {
  const normalized = String(value ?? '').trim()
  if (!automationStableIdPattern.test(normalized))
    throw new Error(`${label} must be a stable 1–128 character identifier.`)
  return normalized
}

const requireAutomationSimpleKey = (value, label) => {
  const normalized = String(value ?? '').trim().toLowerCase()
  if (!automationSimpleKeyPattern.test(normalized)) throw new Error(`${label} has an invalid format.`)
  return normalized
}

const requireAutomationSubjectKind = value => {
  const normalized = String(value ?? '').trim().toLowerCase()
  if (!automationSubjectKindPattern.test(normalized)) throw new Error('subjectKind has an invalid format.')
  return normalized
}

const requireAutomationSubjectId = value => {
  const normalized = String(value ?? '').trim()
  if (!automationSubjectIdPattern.test(normalized)) throw new Error('subjectId has an invalid format.')
  return normalized
}

const requireAutomationVersion = (value, label) => {
  const normalized = String(value ?? '').trim()
  if (!/^[A-Za-z0-9][A-Za-z0-9._-]{0,31}$/.test(normalized)) throw new Error(`${label} has an invalid format.`)
  return normalized
}

const requireAutomationRevision = (value, label = 'expectedRevision') => {
  if (!Number.isSafeInteger(value) || value < 0) throw new Error(`${label} must be a non-negative safe whole number.`)
  return value
}

const requireAutomationPolicyConfig = value => {
  if (!value || typeof value !== 'object' || Array.isArray(value))
    throw new Error('Automation policy config must be a JSON object.')
  const seen = new WeakSet()
  let keys = 0
  const inspect = (item, depth) => {
    if (depth > 16) throw new Error('Automation policy config is nested too deeply.')
    if (item === null || typeof item === 'string' || typeof item === 'boolean') return
    if (typeof item === 'number' && Number.isFinite(item)) return
    if (Array.isArray(item)) {
      if (seen.has(item)) throw new Error('Automation policy config must not contain circular data.')
      seen.add(item)
      item.forEach(child => inspect(child, depth + 1))
      return
    }
    if (!item || typeof item !== 'object' || Object.getPrototypeOf(item) !== Object.prototype)
      throw new Error('Automation policy config must contain only JSON values.')
    if (seen.has(item)) throw new Error('Automation policy config must not contain circular data.')
    seen.add(item)
    for (const key of Object.keys(item)) {
      keys += 1
      if (keys > 512 || !key || key.length > 80 || /[\u0000-\u001f\u007f-\u009f]/u.test(key) || ['__proto__', 'constructor', 'prototype'].includes(key))
        throw new Error('Automation policy config contains an invalid field.')
      inspect(item[key], depth + 1)
    }
  }
  inspect(value, 0)
  let json
  try { json = JSON.stringify(value) }
  catch { throw new Error('Automation policy config must contain only JSON values.') }
  if (!json || json.length > 16_384) throw new Error('Automation policy config is too large.')
  return value
}

const requireAutomationPageLimit = value => {
  if (value == null) return undefined
  if (!Number.isInteger(value) || value < 1 || value > 200)
    throw new Error('Automation list limit must be a whole number from 1 to 200.')
  return value
}

const requireSignedAutomationGroup = value => {
  if (!Array.isArray(value) || value.length < 1 || value.length > 16)
    throw new Error('automation.submitApprovalIntent requires 1–16 signed transactions.')
  return value.map(item => {
    const encoded = String(item ?? '').trim()
    if (!encoded || encoded.length > 200_000 || encoded.length % 4 !== 0 || !/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(encoded))
      throw new Error('automation.submitApprovalIntent received an invalid signed transaction.')
    return encoded
  })
}
const ACCOUNT_KINDS = new Set(['managed_email', 'magic', 'algorand_wallet', 'auth_only_email_wallet', 'evm_wallet'])
const normalizedAccountKind = value => ACCOUNT_KINDS.has(String(value ?? '')) ? String(value) : undefined
const isAuthenticationOnlyEmailSession = value =>
  String(value?.accountKind ?? '').trim() === 'auth_only_email_wallet'
  || String(value?.authProvider ?? '').trim() === 'web3auth_avm_email'
const UNIVERSAL_USERNAME_METHOD = '6896b057'
const UNIVERSAL_USERNAME_BENEFICIARY_A = 'IC6Q7LOQWCUYD3PQS2E43BEOIDVPTDBZHZ6T5VGEBLBCRQVOMYKSZBUU6I'
const UNIVERSAL_USERNAME_BENEFICIARY_B = 'RKR5F5G7RQ62PXOV2VJJJVO2XCQR2DSIYMMNP4VSBRMKSFGEZBNONJBH5Y'

// Fixed official destinations; responses cannot substitute links.
const OFFICIAL_FUNDING_LINKS = Object.freeze({
  peraWebsite: 'https://perawallet.app/',
  peraFundingHelp: 'https://support.perawallet.app/en/article/pera-fund-supported-payment-methods-by-region-n4mvzk/',
  peraCreateWallet: 'https://support.perawallet.app/en/article/create-a-new-algorand-account-on-pera-wallet-1ehbj11/',
  peraRecoverySafety: 'https://support.perawallet.app/en/article/backing-up-your-recovery-passphrase-uacy9k/',
})

// Consensus identities pinned at the wallet boundary.
const SHOP_ALGORAND_NETWORK_IDENTITIES = Object.freeze({
  mainnet: Object.freeze({
    genesisId: 'mainnet-v1.0',
    genesisHashBase64: 'wGHE2Pwdvd7S12BL5FaOP20EGYesN73ktiC1qzkkit8=',
  }),
  testnet: Object.freeze({
    genesisId: 'testnet-v1.0',
    genesisHashBase64: 'SGO1GKSzyE7IEPItTxCByw9x8FmnrCDexi9/cOUJOiI=',
  }),
})

// Frozen future-package Pera/Lute authentication contract. Keep this
// browser-side mirror byte-for-byte compatible with shared/playerWalletAuthV3.ts;
// the server remains authoritative and revalidates every field again.
const PLAYER_WALLET_AUTH_V3 = Object.freeze({
  challengeVersion: 3,
  purpose: 'player-sign-in',
  chain: 'algorand',
  network: 'mainnet',
  genesisId: 'mainnet-v1.0',
  genesisHash: 'wGHE2Pwdvd7S12BL5FaOP20EGYesN73ktiC1qzkkit8=',
  floorVersion: 3,
  floorContractRevision: 1,
  floorContractFingerprint: '24a93f7994bc84824ce3db126d0eb4ee1099c23ba8e53602f46993c360137aaa',
  maximumMessageBytes: 1_000,
  maximumLifetimeMs: 5 * 60_000,
})

const PLAYER_WALLET_AUTH_V3_RESPONSE_KEYS = Object.freeze([
  'success', 'challengeVersion', 'purpose', 'chain', 'gameId', 'providerId',
  'clientKind', 'gameOrigin', 'nonce', 'message', 'expiresAt', 'walletAddress',
  'authorizedSigner', 'network', 'genesisId', 'genesisHash',
  'authFloorVersion', 'authFloorContractRevision', 'authFloorContractFingerprint',
  'authFloorStateRevision', 'authFloorAuthEpoch', 'authFloorRestoreEpoch',
  'providerPolicyRevision', 'providerPolicyFingerprint', 'originRevision',
  'originFingerprint', 'issuedAt',
])

const BLOCKMAKER_BROWSER_RUNTIME_URL = import.meta.url
const WEB_WALLET_PACKAGE_PROVIDER_POLICY = Object.freeze({
  mode: 'exact',
  providerIds: Object.freeze(['pera', 'lute', 'web3auth_avm_email']),
  requiredEconomicProviders: Object.freeze(['pera', 'lute']),
  authOnlyProvider: 'web3auth_avm_email',
  unlistedAdaptersAllowed: false,
  fundingGuide: Object.freeze({
    optional: true,
    accountKind: 'algorand_wallet',
    authProviders: Object.freeze(['pera', 'lute']),
    authOnlyEmailAddressExposed: false,
  }),
})
const UNITY_WEBGL_WALLET_PACKAGE_PROVIDER_POLICY_V2 = Object.freeze({
  mode: 'exact',
  providerIds: Object.freeze(['pera', 'lute', 'txnlab_web3auth']),
  requiredEconomicProviders: Object.freeze(['pera', 'lute', 'txnlab_web3auth']),
  authOnlyProvider: null,
  unlistedAdaptersAllowed: false,
  fundingGuide: Object.freeze({
    optional: true,
    accountKind: 'algorand_wallet',
    authProviders: Object.freeze(['pera', 'lute', 'txnlab_web3auth']),
    authOnlyEmailAddressExposed: false,
  }),
})
const UNITY_WEBGL_WALLET_PACKAGE_PROVIDER_POLICY = Object.freeze({
  mode: 'exact_runtime_profile',
  selection: Object.freeze({
    discriminator: 'txnlab_web3auth_public_client_id',
    absent: 'pera_lute',
    valid: 'pera_lute_txnlab_web3auth',
    invalid: 'reject',
  }),
  profiles: Object.freeze([
    Object.freeze({
      id: 'pera_lute',
      clientId: 'absent',
      providerIds: Object.freeze(['pera', 'lute']),
      requiredEconomicProviders: Object.freeze(['pera', 'lute']),
      authOnlyProvider: null,
      fundingGuide: Object.freeze({
        optional: true,
        accountKind: 'algorand_wallet',
        authProviders: Object.freeze(['pera', 'lute']),
        authOnlyEmailAddressExposed: false,
      }),
    }),
    Object.freeze({
      id: 'pera_lute_txnlab_web3auth',
      clientId: 'valid_public',
      providerIds: Object.freeze(['pera', 'lute', 'txnlab_web3auth']),
      requiredEconomicProviders: Object.freeze(['pera', 'lute', 'txnlab_web3auth']),
      authOnlyProvider: null,
      fundingGuide: Object.freeze({
        optional: true,
        accountKind: 'algorand_wallet',
        authProviders: Object.freeze(['pera', 'lute', 'txnlab_web3auth']),
        authOnlyEmailAddressExposed: false,
      }),
    }),
  ]),
  unlistedAdaptersAllowed: false,
})
const UNITY_WEBGL_RUNTIME_PROFILE_PROVIDER_IDS = Object.freeze({
  pera_lute: Object.freeze(['pera', 'lute']),
  pera_lute_txnlab_web3auth: Object.freeze(['pera', 'lute', 'txnlab_web3auth']),
})
const UNITY_WEBGL_PLAYER_REQUEST_ONCE = Object.freeze({
  method: 'RequestPlayerOnce',
  routeScope: 'caller_validated_game_routes',
  callerValidationRequired: true,
  requiresExistingSession: true,
  maximumTransmissions: 1,
  minimumTimeoutSeconds: 35,
  automaticRefresh: false,
  automaticRetry: false,
  mutatesPlayerSession: false,
  copiesResponse: true,
  pathPolicy: 'origin_locked_relative_path_without_fragment_or_game_id',
  excludedStoreRoute: '/v1/pack-shop/submit',
  bodyHandling: 'caller_supplied_unchanged',
  contentType: 'application_json_when_body_present',
  outcomeMetadata: Object.freeze(['TransmissionAttempted', 'MayHaveApplied']),
})
const UNITY_WEBGL_STORE_SUBMISSION_ONCE = Object.freeze({
  methods: Object.freeze(['SubmitStorePaymentOnce', 'SubmitStoreAssetAcceptanceOnce']),
  requiresExistingSession: true,
  maximumTransmissions: 1,
  minimumTimeoutSeconds: 35,
  automaticRefresh: false,
  automaticRetry: false,
  mutatesPlayerSession: false,
  copiesResponse: true,
  validation: Object.freeze({
    commitId: 'canonical_store_commit_id',
    signedTransactions: 'canonical_base64_group',
    paymentMaximumTransactions: 4,
    assetAcceptanceMaximumTransactions: 16,
  }),
  outcomeMetadata: Object.freeze(['TransmissionAttempted', 'MayHaveApplied']),
})
const WEB_WALLET_PACKAGE_DEPENDENCIES = Object.freeze([
  Object.freeze({ package: 'algosdk', version: '3.6.0' }),
  Object.freeze({ package: '@perawallet/connect', version: '1.6.0' }),
  Object.freeze({ package: 'lute-connect', version: '2.0.1' }),
])
const UNITY_WEBGL_WALLET_PACKAGE_DEPENDENCIES = Object.freeze([
  Object.freeze({ package: 'algosdk', version: '3.6.0' }),
  Object.freeze({ package: '@txnlab/use-wallet', version: '5.0.0' }),
  Object.freeze({ package: '@txnlab/use-wallet-pera', version: '5.0.0' }),
  Object.freeze({ package: '@txnlab/use-wallet-lute', version: '5.0.0' }),
  Object.freeze({ package: '@txnlab/use-wallet-web3auth', version: '5.0.0' }),
])
const WALLET_PACKAGE_SPECIFICATIONS = Object.freeze({
  web: Object.freeze({
    manifestKey: 'webWalletPackage',
    schemaVersion: 'blockmaker-web-wallet-package/v1',
    packagePrefix: 'web-wallet-package',
    target: 'browser',
    clientKind: 'web',
    dependencyDelivery: 'consumer_install',
    providerPolicy: WEB_WALLET_PACKAGE_PROVIDER_POLICY,
    dependencies: WEB_WALLET_PACKAGE_DEPENDENCIES,
    members: Object.freeze([
      Object.freeze({ file: 'blockmaker.js', installPath: 'public/vendor/blockmaker/blockmaker.js', role: 'browser_runtime' }),
      Object.freeze({ file: 'blockmaker.d.ts', installPath: 'public/vendor/blockmaker/blockmaker.d.ts', role: 'typescript_declarations' }),
    ]),
  }),
  unity_webgl: Object.freeze({
    manifestKey: 'unityWebGlWalletPackage',
    schemaVersion: 'blockmaker-unity-webgl-package/v3',
    legacySchemaVersion: 'blockmaker-unity-webgl-package/v2',
    packagePrefix: 'unity-webgl-package',
    target: 'unity_webgl',
    clientKind: 'unity_webgl',
    dependencyDelivery: 'embedded',
    providerPolicy: UNITY_WEBGL_WALLET_PACKAGE_PROVIDER_POLICY,
    legacyProviderPolicy: UNITY_WEBGL_WALLET_PACKAGE_PROVIDER_POLICY_V2,
    playerRequestOnce: UNITY_WEBGL_PLAYER_REQUEST_ONCE,
    storeSubmissionOnce: UNITY_WEBGL_STORE_SUBMISSION_ONCE,
    dependencies: UNITY_WEBGL_WALLET_PACKAGE_DEPENDENCIES,
    members: Object.freeze([
      Object.freeze({ file: 'blockmaker.js', installPath: 'Assets/StreamingAssets/Blockmaker/blockmaker.js', role: 'browser_runtime' }),
      Object.freeze({ file: 'blockmaker-unity-webgl-wallet-host.mjs', installPath: 'Assets/StreamingAssets/Blockmaker/blockmaker-unity-webgl-wallet-host.mjs', role: 'wallet_host' }),
      Object.freeze({ file: 'blockmaker-txnlab-wallet.mjs', installPath: 'Assets/StreamingAssets/Blockmaker/blockmaker-txnlab-wallet.mjs', role: 'wallet_runtime' }),
      Object.freeze({ file: 'blockmaker-txnlab-wallet.d.mts', installPath: 'Assets/StreamingAssets/Blockmaker/blockmaker-txnlab-wallet.d.mts', role: 'wallet_runtime_declarations' }),
      Object.freeze({ file: 'BlockmakerClient.cs', installPath: 'Assets/Blockmaker/Runtime/BlockmakerClient.cs', role: 'unity_client' }),
      Object.freeze({ file: 'BlockmakerWalletPackageWebGL.cs', installPath: 'Assets/Blockmaker/Runtime/BlockmakerWalletPackageWebGL.cs', role: 'unity_wallet_facade' }),
      Object.freeze({ file: 'BlockmakerWalletPackageWebGL.jslib', installPath: 'Assets/Plugins/WebGL/BlockmakerWalletPackageWebGL.jslib', role: 'unity_webgl_bridge' }),
      Object.freeze({ file: 'blockmaker-txnlab-wallet.NOTICES.txt', installPath: 'Assets/StreamingAssets/Blockmaker/blockmaker-txnlab-wallet.NOTICES.txt', role: 'third_party_notices' }),
      Object.freeze({ file: 'BlockmakerUnityPresentation.cs', installPath: 'Assets/Blockmaker/Runtime/BlockmakerUnityPresentation.cs', role: 'unity_presentation' }),
    ]),
  }),
})

const canonicalWalletPackageJson = value => `${JSON.stringify(value, null, 2)}\n`

const walletPackageBase64 = bytes => {
  let binary = ''
  for (let offset = 0; offset < bytes.length; offset += 0x8000)
    binary += String.fromCharCode(...bytes.subarray(offset, offset + 0x8000))
  return globalThis.btoa(binary)
}

const walletPackageSha256 = async bytes => {
  if (!globalThis.crypto?.subtle)
    throw new BlockmakerError('This browser cannot create reviewed package evidence.', { code: 'PACKAGE_EVIDENCE_UNAVAILABLE' })
  return new Uint8Array(await globalThis.crypto.subtle.digest('SHA-256', bytes))
}

const walletPackageIntegrity = async value => {
  const bytes = typeof value === 'string' ? new TextEncoder().encode(value) : value
  return `sha256-${walletPackageBase64(await walletPackageSha256(bytes))}`
}

const walletPackageBase64Url = bytes => walletPackageBase64(bytes)
  .replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/, '')

const walletPackageRandomId = () => {
  if (!globalThis.crypto?.getRandomValues)
    throw new BlockmakerError('This browser cannot create reviewed package evidence.', { code: 'PACKAGE_EVIDENCE_UNAVAILABLE' })
  return `wpexec_${walletPackageBase64Url(globalThis.crypto.getRandomValues(new Uint8Array(32)))}`
}

const latchUnityWalletPackageCleanup = () => {
  try {
    Object.defineProperty(globalThis, '__blockmakerUnityWebGlWalletPackageCleanupRequiredV1', {
      value: true,
      configurable: false,
      enumerable: false,
      writable: false,
    })
  } catch { /* an existing non-configurable truthy latch remains authoritative */ }
}

const unityWalletPackageCleanupLatched = () =>
  globalThis.__blockmakerUnityWebGlWalletPackageCleanupRequiredV1 === true

const exactWalletPackageRuntimeUrl = (value, productionOrigin, file) => {
  try {
    const url = new URL(value)
    if (url.origin !== productionOrigin || url.username || url.password || url.search || url.hash
      || url.pathname.split('/').at(-1) !== file) throw new Error()
    return url.toString()
  } catch {
    throw new BlockmakerError(`The deployed ${file} runtime URL is not the exact same-origin package member.`, {
      code: 'PACKAGE_EVIDENCE_UNAVAILABLE',
    })
  }
}

const exactSiblingUnityHostRuntimeUrl = value => {
  try {
    const browser = new URL(BLOCKMAKER_BROWSER_RUNTIME_URL)
    const host = new URL(value)
    const directory = url => url.pathname.slice(0, url.pathname.lastIndexOf('/') + 1)
    if (host.origin !== browser.origin || host.username || host.password || host.search || host.hash
      || host.pathname.split('/').at(-1) !== 'blockmaker-unity-webgl-wallet-host.mjs'
      || browser.pathname.split('/').at(-1) !== 'blockmaker.js'
      || directory(host) !== directory(browser)) throw new Error()
    return host.toString()
  } catch {
    walletPackageEvidenceError('The executed Unity wallet host is not the exact sibling of blockmaker.js.')
  }
}

const exactWalletPackageProductionOrigin = () => {
  const origin = exactPlayerWalletAuthV3Origin(globalThis.location?.origin)
  const parsed = origin ? new URL(origin) : null
  if (!parsed || parsed.protocol !== 'https:'
    || /(^127\.\d+\.\d+\.\d+$|(^|\.)localhost\.?$|^\[::1\]$)/i.test(parsed.hostname)) {
    throw new BlockmakerError('Deployment evidence requires the exact production HTTPS game origin.', {
      code: 'PACKAGE_EVIDENCE_UNAVAILABLE',
    })
  }
  return origin
}

// Diagnostic-only integer ABI; keep positions stable.
const WALLET_PACKAGE_EVIDENCE_STATUS = Object.freeze([
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
  ['unavailable', 'none', 'BRIDGE_UNAVAILABLE'],
].map(([state, stage, code]) => Object.freeze({
  schemaVersion: 'blockmaker-wallet-package-evidence-status/v1',
  state, stage, code,
  canDownload: state === 'ready',
  reloadRequired: state === 'failed',
})))
const EVIDENCE_CAPTURE_TIMEOUT_MS = 30_000
const evidenceStatus = state => Object.freeze({
  ...WALLET_PACKAGE_EVIDENCE_STATUS[state?.status ?? 0],
})

const walletPackageEvidenceError = (message, code = 'PACKAGE_EVIDENCE_INVALID') => {
  throw new BlockmakerError(message, { code })
}

const walletPackageRecord = (value, label) => {
  if (!value || typeof value !== 'object' || Array.isArray(value))
    walletPackageEvidenceError(`${label} must be an object.`)
  return value
}

const walletPackageExactKeys = (value, expected, label) => {
  const actual = Object.keys(walletPackageRecord(value, label)).sort()
  const keys = [...expected].sort()
  if (actual.length !== keys.length || actual.some((key, index) => key !== keys[index]))
    walletPackageEvidenceError(`${label} does not match the exact reviewed wallet-package schema.`)
}

const walletPackageExactJson = (actual, expected, label) => {
  if (JSON.stringify(actual) !== JSON.stringify(expected))
    walletPackageEvidenceError(`${label} does not match the reviewed wallet-package contract.`)
  return expected
}

const walletPackageDigest = (integrity, label) => {
  if (typeof integrity !== 'string' || !/^sha256-[A-Za-z0-9+/]{43}=$/.test(integrity))
    walletPackageEvidenceError(`${label} must be one canonical SHA-256 integrity value.`)
  let bytes
  try {
    const binary = globalThis.atob(integrity.slice(7))
    bytes = Uint8Array.from(binary, character => character.charCodeAt(0))
  } catch {
    walletPackageEvidenceError(`${label} is not a valid SHA-256 integrity value.`)
  }
  if (bytes.byteLength !== 32)
    walletPackageEvidenceError(`${label} is not a 32-byte SHA-256 integrity value.`)
  const sha256 = walletPackageBase64Url(bytes)
  return Object.freeze({ integrity, sha256, release: `sha256-${sha256}` })
}

const parseWalletPackageManifestInBrowser = async (value, { apiOrigin, clientKind }) => {
  const specification = WALLET_PACKAGE_SPECIFICATIONS[clientKind]
  if (!specification) walletPackageEvidenceError('This wallet-package target is not supported.')
  const input = walletPackageRecord(value, 'The wallet-package manifest')
  walletPackageExactKeys(input, [
    'schemaVersion', 'packageId', 'releaseVersion', 'target', 'clientKind', 'complete',
    'activation', 'network', 'providerPolicy',
    ...(specification.playerRequestOnce ? ['playerRequestOnce'] : []),
    ...(specification.storeSubmissionOnce ? ['storeSubmissionOnce'] : []),
    'dependencies', 'members', 'provenance',
  ], 'The wallet-package manifest')
  const legacySchema = input.schemaVersion === specification.legacySchemaVersion
  if ((input.schemaVersion !== specification.schemaVersion && !legacySchema)
    || input.target !== specification.target || input.clientKind !== specification.clientKind
    || input.complete !== true || input.activation !== 'requires_game_qualification'
    || typeof input.releaseVersion !== 'string'
    || !/^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$/.test(input.releaseVersion)) {
    walletPackageEvidenceError('The wallet-package manifest target or release contract is invalid.')
  }
  const network = Object.freeze({
    chain: PLAYER_WALLET_AUTH_V3.chain,
    network: PLAYER_WALLET_AUTH_V3.network,
    genesisId: PLAYER_WALLET_AUTH_V3.genesisId,
    genesisHashBase64: PLAYER_WALLET_AUTH_V3.genesisHash,
  })
  walletPackageExactKeys(input.network, Object.keys(network), 'The wallet-package network')
  walletPackageExactJson(input.network, network, 'The wallet-package network')
  const providerPolicy = legacySchema
    ? specification.legacyProviderPolicy
    : specification.providerPolicy
  walletPackageExactKeys(input.providerPolicy, Object.keys(providerPolicy), 'The wallet-package provider policy')
  walletPackageExactJson(input.providerPolicy, providerPolicy, 'The wallet-package provider policy')
  let playerRequestOnce
  if (specification.playerRequestOnce) {
    walletPackageExactKeys(
      input.playerRequestOnce,
      Object.keys(specification.playerRequestOnce),
      'The Unity player-request contract',
    )
    playerRequestOnce = walletPackageExactJson(
      input.playerRequestOnce,
      specification.playerRequestOnce,
      'The Unity player-request contract',
    )
  }
  let storeSubmissionOnce
  if (specification.storeSubmissionOnce) {
    walletPackageExactKeys(
      input.storeSubmissionOnce,
      Object.keys(specification.storeSubmissionOnce),
      'The Unity Store submission contract',
    )
    storeSubmissionOnce = walletPackageExactJson(
      input.storeSubmissionOnce,
      specification.storeSubmissionOnce,
      'The Unity Store submission contract',
    )
  }
  if (!Array.isArray(input.dependencies) || input.dependencies.length !== specification.dependencies.length)
    walletPackageEvidenceError('The wallet-package dependency closure is incomplete.')
  const dependencies = input.dependencies.map((entry, index) => {
    walletPackageExactKeys(entry, ['package', 'version', 'delivery'], `Wallet-package dependency ${index + 1}`)
    const expected = specification.dependencies[index]
    const normalized = Object.freeze({
      package: expected.package,
      version: expected.version,
      delivery: specification.dependencyDelivery,
    })
    walletPackageExactJson(entry, normalized, `Wallet-package dependency ${index + 1}`)
    return normalized
  })
  if (!Array.isArray(input.members) || (input.members.length !== specification.members.length
    && !(specification.clientKind === 'unity_webgl' && input.members.length === 8)))
    walletPackageEvidenceError('The wallet-package member closure is incomplete.')
  const members = input.members.map((entry, index) => {
    const expected = specification.members[index]
    walletPackageExactKeys(entry, [
      'file', 'installPath', 'role', 'url', 'release', 'sha256', 'integrity',
      'rawBytes', 'gzipBytes', 'mediaType',
    ], `Wallet-package member ${index + 1}`)
    const digest = walletPackageDigest(entry.integrity, `Wallet-package member ${entry.file || index + 1}`)
    const mediaType = expected.file.endsWith('.js') || expected.file.endsWith('.mjs')
      ? 'text/javascript; charset=utf-8'
      : 'text/plain; charset=utf-8'
    const exactUrl = `${apiOrigin}/sdk/${digest.release}/${expected.file}`
    if (entry.file !== expected.file || entry.installPath !== expected.installPath
      || entry.role !== expected.role || entry.release !== digest.release
      || entry.sha256 !== digest.sha256 || entry.url !== exactUrl
      || entry.mediaType !== mediaType
      || !Number.isSafeInteger(entry.rawBytes) || entry.rawBytes < 1 || entry.rawBytes > 10_000_000
      || !Number.isSafeInteger(entry.gzipBytes) || entry.gzipBytes < 1 || entry.gzipBytes > 10_000_000) {
      walletPackageEvidenceError(`Wallet-package member ${index + 1} is not the exact reviewed artifact.`)
    }
    return Object.freeze({
      file: expected.file,
      installPath: expected.installPath,
      role: expected.role,
      url: exactUrl,
      release: digest.release,
      sha256: digest.sha256,
      integrity: digest.integrity,
      rawBytes: entry.rawBytes,
      gzipBytes: entry.gzipBytes,
      mediaType,
    })
  })
  walletPackageExactKeys(input.provenance, [
    'sourceCommit', 'contentAddressing', 'allMembersVerifiedBeforePublication',
  ], 'The wallet-package provenance')
  if (input.provenance.sourceCommit !== null
    && (typeof input.provenance.sourceCommit !== 'string'
      || !/^[0-9a-f]{7,64}$/i.test(input.provenance.sourceCommit))) {
    walletPackageEvidenceError('The wallet-package source commit is invalid.')
  }
  if (input.provenance.contentAddressing !== 'sha256'
    || input.provenance.allMembersVerifiedBeforePublication !== true) {
    walletPackageEvidenceError('The wallet-package provenance is incomplete.')
  }
  const manifest = Object.freeze({
    schemaVersion: input.schemaVersion,
    packageId: String(input.packageId),
    releaseVersion: input.releaseVersion,
    target: specification.target,
    clientKind: specification.clientKind,
    complete: true,
    activation: 'requires_game_qualification',
    network,
    providerPolicy,
    ...(playerRequestOnce ? { playerRequestOnce } : {}),
    ...(storeSubmissionOnce ? { storeSubmissionOnce } : {}),
    dependencies: Object.freeze(dependencies),
    members: Object.freeze(members),
    provenance: Object.freeze({
      sourceCommit: input.provenance.sourceCommit,
      contentAddressing: 'sha256',
      allMembersVerifiedBeforePublication: true,
    }),
  })
  const identityInput = JSON.stringify({
    schemaVersion: manifest.schemaVersion,
    releaseVersion: manifest.releaseVersion,
    clientKind: manifest.clientKind,
    network: manifest.network,
    providerPolicy: manifest.providerPolicy,
    ...(specification.playerRequestOnce ? { playerRequestOnce: manifest.playerRequestOnce } : {}),
    ...(specification.storeSubmissionOnce ? { storeSubmissionOnce: manifest.storeSubmissionOnce } : {}),
    dependencies: manifest.dependencies,
    members: manifest.members.map(member => ({
      file: member.file,
      installPath: member.installPath,
      role: member.role,
      release: member.release,
      integrity: member.integrity,
      rawBytes: member.rawBytes,
      mediaType: member.mediaType,
    })),
  })
  const packageId = `${specification.packagePrefix}-sha256-${walletPackageBase64Url(
    await walletPackageSha256(new TextEncoder().encode(identityInput)),
  )}`
  if (manifest.packageId !== packageId)
    walletPackageEvidenceError('The wallet-package ID does not bind its exact artifact and policy closure.')
  return manifest
}

const verifiedWalletPackageRuntime = async (url, expectedMember, { signal, failure } = {}) => {
  if (signal?.aborted)
    walletPackageEvidenceError('Wallet-package evidence capture has ended.', 'PACKAGE_EVIDENCE_UNAVAILABLE')
  failure?.(6)
  const runtimeFetch = globalThis.fetch?.bind(globalThis)
  if (!runtimeFetch)
    walletPackageEvidenceError('This browser cannot verify its loaded wallet-package runtime.', 'PACKAGE_EVIDENCE_UNAVAILABLE')
  let response
  try {
    response = await runtimeFetch(url, {
      method: 'GET',
      redirect: 'error',
      credentials: 'same-origin',
      cache: 'no-store',
      headers: { Accept: expectedMember.mediaType },
      signal,
    })
  } catch {
    walletPackageEvidenceError(`The deployed ${expectedMember.file} runtime could not be verified.`)
  }
  if (!response?.ok)
    walletPackageEvidenceError(`The deployed ${expectedMember.file} runtime could not be verified.`)
  const bytes = new Uint8Array(await response.arrayBuffer())
  failure?.(7)
  if (bytes.byteLength !== expectedMember.rawBytes
    || await walletPackageIntegrity(bytes) !== expectedMember.integrity) {
    walletPackageEvidenceError(`The deployed ${expectedMember.file} runtime differs from the reviewed package.`)
  }
}

const exactPlayerWalletAuthV3Origin = value => {
  if (typeof value !== 'string' || !value || value === 'null' || value.length > 255) return null
  try {
    const url = new URL(value)
    const loopback = ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname)
    if (url.username || url.password || url.pathname !== '/' || url.search || url.hash
      || (url.protocol !== 'https:' && !(url.protocol === 'http:' && loopback))
      || url.origin !== value) return null
    return value
  } catch { return null }
}

const exactPlayerWalletAuthV3Integer = (value, minimum) =>
  Number.isSafeInteger(value) && value >= minimum && value <= 2_147_483_647

const validatePlayerWalletAuthV3Challenge = (challenge, expected) => {
  const invalid = () => {
    throw new BlockmakerError(
      'Blockmaker returned an invalid strong wallet sign-in challenge. Nothing was signed.',
      { code: 'AUTH_INVALID' },
    )
  }
  if (!challenge || typeof challenge !== 'object' || Array.isArray(challenge)) invalid()
  const keys = Object.keys(challenge).sort()
  const expectedKeys = [...PLAYER_WALLET_AUTH_V3_RESPONSE_KEYS].sort()
  if (keys.length !== expectedKeys.length || keys.some((key, index) => key !== expectedKeys[index])) invalid()
  const pageOrigin = exactPlayerWalletAuthV3Origin(globalThis.location?.origin)
  if (!pageOrigin
    || challenge.success !== true
    || challenge.challengeVersion !== PLAYER_WALLET_AUTH_V3.challengeVersion
    || challenge.purpose !== PLAYER_WALLET_AUTH_V3.purpose
    || challenge.chain !== PLAYER_WALLET_AUTH_V3.chain
    || challenge.gameId !== expected.gameId
    || !/^[A-Za-z0-9._:-]{1,128}$/.test(String(challenge.gameId ?? ''))
    || challenge.providerId !== expected.providerId
    || (challenge.providerId !== 'pera' && challenge.providerId !== 'lute')
    || challenge.clientKind !== expected.clientKind
    || challenge.gameOrigin !== pageOrigin
    || challenge.walletAddress !== expected.walletAddress
    || !isAlgorandAddressShape(challenge.authorizedSigner)
    || challenge.network !== PLAYER_WALLET_AUTH_V3.network
    || challenge.genesisId !== PLAYER_WALLET_AUTH_V3.genesisId
    || challenge.genesisHash !== PLAYER_WALLET_AUTH_V3.genesisHash
    || challenge.authFloorVersion !== PLAYER_WALLET_AUTH_V3.floorVersion
    || challenge.authFloorContractRevision !== PLAYER_WALLET_AUTH_V3.floorContractRevision
    || challenge.authFloorContractFingerprint !== PLAYER_WALLET_AUTH_V3.floorContractFingerprint
    || !exactPlayerWalletAuthV3Integer(challenge.authFloorStateRevision, 1)
    || !exactPlayerWalletAuthV3Integer(challenge.authFloorAuthEpoch, 1)
    || !exactPlayerWalletAuthV3Integer(challenge.authFloorRestoreEpoch, 1)
    || !exactPlayerWalletAuthV3Integer(challenge.providerPolicyRevision, 0)
    || !exactPlayerWalletAuthV3Integer(challenge.originRevision, 1)
    || !/^[a-f0-9]{64}$/.test(String(challenge.providerPolicyFingerprint ?? ''))
    || !/^[a-f0-9]{64}$/.test(String(challenge.originFingerprint ?? ''))
    || !/^[A-Za-z0-9_-]{32}$/.test(String(challenge.nonce ?? ''))
    || !Number.isSafeInteger(challenge.issuedAt) || challenge.issuedAt <= 0
    || challenge.issuedAt > Date.now() + 30_000
    || !Number.isSafeInteger(challenge.expiresAt)
    || challenge.expiresAt <= challenge.issuedAt
    || challenge.expiresAt - challenge.issuedAt > PLAYER_WALLET_AUTH_V3.maximumLifetimeMs
    || challenge.expiresAt <= Date.now()
    || typeof challenge.message !== 'string' || challenge.message.includes('\r')) invalid()
  const lines = [
    'Blockmaker player sign-in v3',
    `game=${challenge.gameId}`,
    `purpose=${challenge.purpose}`,
    `chain=${challenge.chain}`,
    `provider=${challenge.providerId}`,
    `client=${challenge.clientKind}`,
    `origin=${challenge.gameOrigin}`,
    `owner=${challenge.walletAddress}`,
    `signer=${challenge.authorizedSigner}`,
    `gid=${challenge.genesisId}`,
    `gh=${challenge.genesisHash}`,
    `floor=${challenge.authFloorVersion}`,
    `fcr=${challenge.authFloorContractRevision}`,
    `fsr=${challenge.authFloorStateRevision}`,
    `fae=${challenge.authFloorAuthEpoch}`,
    `fre=${challenge.authFloorRestoreEpoch}`,
    `pr=${challenge.providerPolicyRevision}`,
    `pf=${challenge.providerPolicyFingerprint}`,
    `or=${challenge.originRevision}`,
    `of=${challenge.originFingerprint}`,
    `nonce=${challenge.nonce}`,
    `iat=${challenge.issuedAt}`,
    `exp=${challenge.expiresAt}`,
  ]
  const message = lines.join('\n')
  if (challenge.message !== message
    || new TextEncoder().encode(message).byteLength > PLAYER_WALLET_AUTH_V3.maximumMessageBytes) invalid()
  return message
}

const formatAlgoBalance = microalgo => {
  if (!Number.isFinite(microalgo) || microalgo < 0) return null
  const algo = microalgo / 1_000_000
  return `${algo.toLocaleString(undefined, { maximumFractionDigits: 6 })} ALGO`
}

const bytesFromBase64 = value => {
  const encoded = String(value ?? '').trim()
  if (!encoded || encoded.length > 200_000 || !/^[A-Za-z0-9+/]*={0,2}$/.test(encoded))
    throw new BlockmakerError('Blockmaker returned an invalid wallet sign-in transaction.', { code: 'TX_INVALID' })
  let binary
  try { binary = globalThis.atob(encoded) }
  catch { throw new BlockmakerError('Blockmaker returned an invalid wallet sign-in transaction.', { code: 'TX_INVALID' }) }
  const bytes = new Uint8Array(binary.length)
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i)
  return bytes
}

const base64FromBytes = value => {
  if (!(value instanceof Uint8Array) || value.byteLength === 0 || value.byteLength > 200_000)
    throw new BlockmakerError('The wallet did not return a signed transaction.', { code: 'WALLET_SIGNATURE_MISSING' })
  let binary = ''
  for (let offset = 0; offset < value.length; offset += 0x8000)
    binary += String.fromCharCode(...value.subarray(offset, offset + 0x8000))
  return globalThis.btoa(binary)
}

const playerFacingError = (error, fallback) => {
  if (!(error instanceof BlockmakerError)) return fallback
  const message = String(error.message ?? '').trim()
  if (!message || message.length > 240) return fallback
  if (error.code === 'RATE_LIMITED') return 'Too many attempts were made. Please wait a moment and try again.'
  if (error.code === 'TIMEOUT' || error.code === 'NETWORK_ERROR') return 'We could not connect securely. Check your connection and try again.'
  return message
}

const WALLET_ERROR_COPY = Object.freeze({
  PLAYER_CANCELLED: 'Nothing changed. Choose a wallet whenever you are ready.',
  PROVIDER_NOT_ENABLED: 'That sign-in option is not enabled for this game.',
  PROVIDER_UNAVAILABLE: 'That wallet is temporarily unavailable. Choose another supported option.',
  PROVIDER_CLEANUP_REQUIRED: 'Wallet cleanup could not be confirmed. Reload this page before reconnecting your wallet.',
  REQUEST_ALREADY_PENDING: 'A wallet request is already open. Finish or cancel it before trying again.',
  WALLET_ACCOUNT_MISSING: 'No account was returned. Unlock the wallet, select an account and try again.',
  SDK_PIN_MISMATCH: 'The game wallet client does not match its tested Blockmaker release. Refresh the integration before continuing.',
  ORIGIN_NOT_ALLOWED: 'This web address is not connected to the game yet. The game owner needs to add it in Blockmaker.',
  NETWORK_UNAVAILABLE: 'The wallet network could not be reached. Check your connection and try again.',
  UNSAFE_TRANSACTION: 'The wallet request did not match the reviewed action, so Blockmaker stopped safely.',
  SESSION_EXPIRED: 'Your secure session expired. Sign in again to continue.',
})
const WALLET_PROVIDER_IDS = new Set([
  'pera', 'lute', 'magic_email', 'xchain', 'defly', 'defly_web',
  'web3auth_avm_email', 'txnlab_web3auth',
])
const WEB3AUTH_AVM_START_SCHEMA = 'blockmaker-web3auth-avm-start/v1'
const WEB3AUTH_AVM_COMPLETE_SCHEMA = 'blockmaker-web3auth-avm-complete/v1'
const WEB3AUTH_AVM_QUALIFICATION_START_SCHEMA = 'blockmaker-web3auth-avm-qualification-start/v1'
const WEB3AUTH_AVM_QUALIFICATION_COMPLETE_SCHEMA = 'blockmaker-web3auth-avm-qualification-complete/v1'
const WEB3AUTH_AVM_QUALIFICATION_RESULT_SCHEMA = 'blockmaker-web3auth-avm-qualification-control-result/v1'
const WEB3AUTH_AVM_RELAY_READY = 'blockmaker-web3auth-avm-relay-ready/v1'
const WEB3AUTH_AVM_RELAY_START = 'blockmaker-web3auth-avm-relay-start/v1'
const WEB3AUTH_AVM_RELAY_ACK = 'blockmaker-web3auth-avm-relay-ack/v1'
const UNITY_WALLET_PACKAGE_COMPLETE_AFTER_LOGIN = Symbol('Blockmaker Unity WebGL wallet-package immediate handoff')
const UNITY_WALLET_PACKAGE_LOGIN_TRACKER = Symbol('Blockmaker Unity WebGL wallet-package login tracker')
const UNITY_WALLET_PACKAGE_STAGED_LUTE_LOGIN = Symbol('Blockmaker Unity WebGL staged Lute login')
const WEB3AUTH_AVM_ATTEMPT_ID = /^[A-Za-z0-9][A-Za-z0-9_-]{23,159}$/
const WEB3AUTH_AVM_START_SECRET = /^[A-Za-z0-9_-]{32,160}$/
const WEB3AUTH_AVM_CLEANUP_LATCH = '__blockmakerWeb3AuthAvmCleanupRequiredV1'
let web3AuthAvmCleanupLatchFallback = false

const web3AuthAvmCleanupLatched = () =>
  web3AuthAvmCleanupLatchFallback
  || Object.prototype.hasOwnProperty.call(globalThis, WEB3AUTH_AVM_CLEANUP_LATCH)

const latchWeb3AuthAvmCleanup = () => {
  if (web3AuthAvmCleanupLatched()) return
  web3AuthAvmCleanupLatchFallback = true
  try {
    Object.defineProperty(globalThis, WEB3AUTH_AVM_CLEANUP_LATCH, {
      value: true,
      configurable: false,
      enumerable: false,
      writable: false,
    })
  } catch { /* a blocked global property is itself a fail-closed latch */ }
}

const safeWeb3AuthAvmError = error => {
  const sourceCode = String(error?.code ?? '').trim().toUpperCase()
  const code = (() => {
    if (['PLAYER_CANCELLED', 'ATTEMPT_CANCELLED'].includes(sourceCode)) return 'PLAYER_CANCELLED'
    if (sourceCode === 'AUTH_SUPERSEDED') return 'AUTH_SUPERSEDED'
    if (sourceCode === 'ORIGIN_NOT_ALLOWED') return 'ORIGIN_NOT_ALLOWED'
    if (['PROVIDER_NOT_ENABLED', 'FEATURE_DISABLED'].includes(sourceCode)) return 'PROVIDER_NOT_ENABLED'
    if (sourceCode === 'REQUEST_ALREADY_PENDING') return 'REQUEST_ALREADY_PENDING'
    if (sourceCode === 'SESSION_CONFLICT') return 'SESSION_CONFLICT'
    if (sourceCode === 'PROVIDER_CLEANUP_REQUIRED') return 'PROVIDER_CLEANUP_REQUIRED'
    if (['ATTEMPT_EXPIRED', 'SESSION_EXPIRED', 'WEB3AUTH_AVM_ATTEMPT_EXPIRED',
      'WEB3AUTH_AVM_ATTEMPT_ALREADY_COMPLETED'].includes(sourceCode)) return 'SESSION_EXPIRED'
    if (['NETWORK_ERROR', 'TIMEOUT', 'RATE_LIMITED'].includes(sourceCode)
      || Number(error?.status) >= 500) return 'NETWORK_UNAVAILABLE'
    return 'PROVIDER_UNAVAILABLE'
  })()
  const messages = {
    PLAYER_CANCELLED: 'Email-wallet sign-in was cancelled.',
    AUTH_SUPERSEDED: 'This sign-in request was cancelled or replaced.',
    ORIGIN_NOT_ALLOWED: 'This exact game website is not allowed to use email-wallet sign-in.',
    PROVIDER_NOT_ENABLED: 'Email-wallet sign-in is not enabled for this game.',
    REQUEST_ALREADY_PENDING: 'An email-wallet sign-in is already in progress.',
    SESSION_CONFLICT: 'Sign out of the current player account before using email-wallet sign-in.',
    PROVIDER_CLEANUP_REQUIRED: 'The isolated email wallet could not confirm local credential cleanup. Email sign-in remains unavailable for this browser profile; use Pera or Lute. Retry only after provider-hosted sign-in state has expired or been cleared under MetaMask Embedded Wallets/Web3Auth controls.',
    SESSION_EXPIRED: 'This secure email-wallet sign-in expired. Start again.',
    NETWORK_UNAVAILABLE: 'Secure email-wallet sign-in could not reach Blockmaker. Check your connection and try again.',
    PROVIDER_UNAVAILABLE: 'Email-wallet sign-in is temporarily unavailable.',
  }
  return new BlockmakerError(messages[code], { code })
}

const walletCancellationSignal = error => {
  const code = String(error?.code ?? '').trim().toUpperCase()
  const type = String(error?.data?.type ?? '').trim().toUpperCase()
  const message = String(error?.message ?? error ?? '').trim()
  return Number(error?.code) === 4001
    || ['PLAYER_CANCELLED', 'WALLET_REQUEST_REJECTED', 'USER_REJECTED', 'ACTION_CANCELLED', 'CONNECT_MODAL_CLOSED', 'CONNECT_CANCELLED', 'OPERATION_CANCELLED'].includes(code)
    || ['CONNECT_MODAL_CLOSED', 'CONNECT_CANCELLED', 'OPERATION_CANCELLED'].includes(type)
    // lute-connect 2.0.1 emits this exact Error on a normal popup close.
    // Do not classify arbitrary provider failures containing "cancel" as consent.
    || /^Operation Cancelled$/i.test(message)
    // Pinned MetaMask Modal 10.15 rejects its normal close with this exact Error.
    || message === 'User closed the modal'
    || Number(error?.code) === 5114 // Web3Auth WalletLoginError.popupClosed
    || /(?:connect(?: modal)? is (?:closed|cancelled|canceled) by user|operation (?:cancelled|canceled) by user|user (?:cancelled|canceled|rejected|declined)(?: (?:the )?request)?|request (?:cancelled|canceled|rejected|declined) by user)/i.test(message)
}

const walletPendingSignal = error => {
  const code = String(error?.code ?? '').trim().toUpperCase()
  const message = String(error?.message ?? error ?? '').trim()
  return Number(error?.code) === -32002
    || ['ACCOUNT_DIALOG_ACTIVE', 'PROFILE_DIALOG_ACTIVE', 'REQUEST_ALREADY_PENDING', 'WALLET_REQUEST_PENDING'].includes(code)
    || /(?:request|wallet (?:window|approval)).{0,24}(?:already (?:open|pending)|in progress|waiting)/i.test(message)
}

const normalizedWalletError = error => {
  const originalCode = String(error?.code ?? '').trim().toUpperCase()
  const code = (() => {
    if (walletCancellationSignal(error)) return 'PLAYER_CANCELLED'
    if (walletPendingSignal(error)) return 'REQUEST_ALREADY_PENDING'
    if (['WALLET_ACCOUNT_MISSING', 'WALLET_ADDRESS_MISSING', 'WALLET_CONNECTION_EMPTY'].includes(originalCode)) return 'WALLET_ACCOUNT_MISSING'
    if (['ORIGIN_NOT_ALLOWED', 'ORIGIN_FORBIDDEN', 'CORS_ORIGIN_DENIED'].includes(originalCode)) return 'ORIGIN_NOT_ALLOWED'
    if (['TIMEOUT', 'NETWORK_ERROR', 'NETWORK_UNAVAILABLE', 'SERVICE_UNAVAILABLE'].includes(originalCode)) return 'NETWORK_UNAVAILABLE'
    if (['AUTH_MISSING', 'AUTH_EXPIRED', 'SESSION_EXPIRED', 'TOKEN_EXPIRED'].includes(originalCode)) return 'SESSION_EXPIRED'
    if (['TX_INVALID', 'TX_MISMATCH', 'UNSAFE_TRANSACTION', 'WALLET_SIGNATURE_INVALID', 'GROUP_MISMATCH'].includes(originalCode)) return 'UNSAFE_TRANSACTION'
    if (originalCode === 'SDK_PIN_MISMATCH') return 'SDK_PIN_MISMATCH'
    if (originalCode === 'PROVIDER_NOT_ENABLED') return 'PROVIDER_NOT_ENABLED'
    return 'PROVIDER_UNAVAILABLE'
  })()
  return Object.freeze({
    code,
    originalCode: originalCode || null,
    message: WALLET_ERROR_COPY[code],
    retryable: ['PLAYER_CANCELLED', 'REQUEST_ALREADY_PENDING', 'WALLET_ACCOUNT_MISSING', 'NETWORK_UNAVAILABLE', 'SESSION_EXPIRED'].includes(code),
  })
}

const UI_TOKEN_PROPERTIES = Object.freeze({
  backdrop: '--bm-backdrop',
  surface: '--bm-surface',
  surfaceRaised: '--bm-surface-raised',
  text: '--bm-text',
  mutedText: '--bm-muted',
  accent: '--bm-accent',
  accentText: '--bm-accent-text',
  border: '--bm-border',
  danger: '--bm-danger',
  focus: '--bm-focus',
  fontFamily: '--bm-font',
  radius: '--bm-radius',
  shadow: '--bm-shadow',
})

const safeUiToken = (key, raw) => {
  const value = String(raw ?? '').trim()
  if (!value || value.length > 180 || /[;{}<>\\]|url\s*\(|expression\s*\(|@import/i.test(value)) return null
  if (key === 'fontFamily') return /^[A-Za-z0-9\s"'.,_-]+$/.test(value) ? value : null
  if (key === 'radius') {
    const match = /^(\d+(?:\.\d+)?)(px|rem|em)$/.exec(value)
    if (!match) return null
    const amount = Number(match[1])
    return Number.isFinite(amount) && amount <= (match[2] === 'px' ? 48 : 4) ? value : null
  }
  if (key === 'shadow') return /^[A-Za-z0-9#(),.\s%/+_-]+$/.test(value) ? value : null
  return /^(?:#[0-9a-fA-F]{3,8}|(?:rgb|hsl)a?\([0-9.%,\s/-]+\)|[A-Za-z]{3,24}|var\(--[A-Za-z0-9_-]+\))$/.test(value)
    ? value : null
}

const safeImageUrl = value => {
  const safe = safeExternalPageUrl(value)
  return safe || null
}

const BLOCKMAKER_UI_CSS = `
.bm-ui-overlay{--bm-backdrop:rgba(4,6,10,.82);--bm-surface:#101319;--bm-surface-raised:#181c24;--bm-text:#f7f8fb;--bm-muted:#a8b0bf;--bm-accent:#f0e95a;--bm-accent-text:#101208;--bm-border:rgba(255,255,255,.13);--bm-danger:#ff8b9a;--bm-focus:#fff76b;--bm-font:Inter,ui-sans-serif,system-ui,-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif;--bm-radius:22px;--bm-shadow:0 28px 90px rgba(0,0,0,.62);position:fixed;inset:0;z-index:2147483646;display:flex;align-items:center;justify-content:center;width:100%;max-width:100vw;min-width:0;overflow:hidden;padding:max(14px,env(safe-area-inset-top)) max(14px,env(safe-area-inset-right)) max(14px,env(safe-area-inset-bottom)) max(14px,env(safe-area-inset-left));box-sizing:border-box;background:var(--bm-backdrop);backdrop-filter:blur(10px);-webkit-backdrop-filter:blur(10px);color:var(--bm-text);font-family:var(--bm-font)}
.bm-ui-overlay[data-bm-scheme="light"]{--bm-backdrop:rgba(19,23,28,.45);--bm-surface:#fbfbf8;--bm-surface-raised:#f1f2ec;--bm-text:#171914;--bm-muted:#60665c;--bm-border:rgba(18,21,16,.15);--bm-danger:#a92339;--bm-focus:#645f00;--bm-shadow:0 28px 90px rgba(10,14,18,.3)}
.bm-ui-panel{position:relative;display:flex;flex-direction:column;flex:0 1 540px;width:100%;max-width:540px;min-width:0;max-height:calc(100vh - 28px);max-height:calc(100dvh - 28px);overflow:hidden;border:1px solid var(--bm-border);border-radius:var(--bm-radius);background:var(--bm-surface);box-shadow:var(--bm-shadow);box-sizing:border-box}
.bm-ui-panel[data-bm-dialog-kind="account"]{min-height:min(400px,calc(100dvh - 28px))}
.bm-ui-panel[data-bm-dialog-kind="profile"]{flex-basis:720px;max-width:720px}
.bm-ui-panel[data-bm-dialog-kind="marketplace"]{flex-basis:960px;max-width:960px}
.bm-ui-header{position:relative;display:block;padding:26px 64px 14px 28px;border-bottom:1px solid var(--bm-border)}
.bm-ui-brand{margin:0 0 8px;color:var(--bm-accent);font-size:11px;font-weight:850;letter-spacing:.14em;text-transform:uppercase}
.bm-ui-title{margin:0;color:var(--bm-text);font-size:clamp(25px,5vw,34px);line-height:1.08;letter-spacing:-.03em}
.bm-ui-description{margin:9px 0 0;color:var(--bm-muted);font-size:14px;line-height:1.55}
.bm-ui-close{position:absolute;top:14px;right:14px;display:grid;place-items:center;width:44px;height:44px;padding:0;border:1px solid var(--bm-border);border-radius:999px;background:var(--bm-surface-raised);color:var(--bm-text);font:400 27px/1 var(--bm-font);cursor:pointer}
.bm-ui-scroll{min-height:0;overflow:auto;overscroll-behavior:contain;padding:22px 28px 28px;-webkit-overflow-scrolling:touch}
.bm-ui-status{display:none;margin:0 0 16px;padding:12px 14px;border:1px solid color-mix(in srgb,var(--bm-accent) 32%,transparent);border-radius:13px;background:color-mix(in srgb,var(--bm-accent) 9%,transparent);color:var(--bm-text);font-size:13px;line-height:1.5}
.bm-ui-status[data-bm-tone="error"]{display:block;border-color:color-mix(in srgb,var(--bm-danger) 42%,transparent);background:color-mix(in srgb,var(--bm-danger) 10%,transparent);color:var(--bm-danger)}
.bm-ui-status[data-bm-tone="info"],.bm-ui-status[data-bm-tone="success"]{display:block}
.bm-ui-field{display:block;margin:0 0 8px;color:var(--bm-text);font-size:13px;font-weight:760}
.bm-ui-input{width:100%;height:50px;padding:0 14px;border:1px solid var(--bm-border);border-radius:13px;outline:0;background:var(--bm-surface-raised);color:var(--bm-text);font:500 16px/1 var(--bm-font);box-sizing:border-box}
.bm-ui-button{display:inline-flex;width:auto;min-height:48px;align-items:center;justify-content:center;gap:9px;padding:12px 17px;border:1px solid var(--bm-border);border-radius:13px;background:var(--bm-surface-raised);color:var(--bm-text);font:780 14px/1.25 var(--bm-font);text-align:center;text-transform:none;letter-spacing:normal;cursor:pointer;box-sizing:border-box}
.bm-ui-button[data-bm-variant="primary"]{border-color:var(--bm-accent);background:var(--bm-accent);color:var(--bm-accent-text)}
.bm-ui-button[data-bm-variant="quiet"]{background:transparent;color:var(--bm-muted)}
.bm-ui-button[data-bm-variant="danger"]{color:var(--bm-danger)}
.bm-ui-button[data-bm-full="true"]{width:100%}
.bm-ui-button:disabled,.bm-ui-input:disabled{cursor:not-allowed;opacity:.5}
.bm-ui-button:focus-visible,.bm-ui-input:focus-visible,.bm-ui-close:focus-visible,.bm-ui-link:focus-visible{outline:3px solid var(--bm-focus);outline-offset:3px}
.bm-ui-stack{display:grid;gap:10px}.bm-ui-row{display:flex;align-items:center;gap:10px;flex-wrap:wrap}.bm-ui-row>.bm-ui-input{flex:1 1 220px}.bm-ui-row>.bm-ui-button{flex:0 0 auto}
.bm-ui-divider{display:flex;align-items:center;gap:12px;margin:20px 0;color:var(--bm-muted);font-size:11px;font-weight:800;letter-spacing:.12em;text-transform:uppercase}.bm-ui-divider:before,.bm-ui-divider:after{content:"";height:1px;flex:1;background:var(--bm-border)}
.bm-ui-wallet-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:9px}.bm-ui-wallet{position:relative;justify-content:flex-start;min-height:58px;padding-right:58px;text-align:left}.bm-ui-wallet-mark{display:grid;flex:0 0 30px;width:30px;height:30px;place-items:center;overflow:hidden;border:1px solid var(--bm-border);border-radius:9px;background:var(--bm-surface);color:var(--bm-accent);font-size:10px;font-weight:850;letter-spacing:.04em}.bm-ui-wallet-mark img{width:100%;height:100%;object-fit:cover}.bm-ui-wallet-name{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.bm-ui-chain{position:absolute;right:12px;color:var(--bm-muted);font-size:10px;font-weight:800;letter-spacing:.06em;text-transform:uppercase}
.bm-ui-more{display:grid;gap:9px;margin-top:9px}.bm-ui-more[hidden]{display:none}.bm-ui-more-toggle{margin-top:9px}
.bm-ui-help{margin:10px 2px 0;color:var(--bm-muted);font-size:12px;line-height:1.5}.bm-ui-link{color:var(--bm-muted);text-underline-offset:3px}.bm-ui-policies{display:flex;justify-content:center;gap:16px;flex-wrap:wrap;margin-top:18px;font-size:12px}
.bm-ui-card{padding:16px;border:1px solid var(--bm-border);border-radius:16px;background:var(--bm-surface-raised)}.bm-ui-card-title{display:block;margin:0 0 6px;color:var(--bm-text);font-size:14px}.bm-ui-card-copy{margin:0;color:var(--bm-muted);font-size:13px;line-height:1.5}
.bm-ui-address{display:block;overflow:hidden;margin-top:8px;color:var(--bm-muted);font:650 12px/1.4 ui-monospace,SFMono-Regular,Menlo,monospace;text-overflow:ellipsis;white-space:nowrap}
.bm-ui-section{padding:18px 0;border-top:1px solid var(--bm-border)}.bm-ui-section:first-child{padding-top:0;border-top:0}.bm-ui-section-title{margin:0 0 6px;color:var(--bm-text);font-size:17px}.bm-ui-section-copy{margin:0 0 14px;color:var(--bm-muted);font-size:13px;line-height:1.5}
.bm-ui-profile-head{display:grid;grid-template-columns:72px minmax(0,1fr);gap:16px;align-items:center;margin-bottom:20px}.bm-ui-avatar{display:grid;width:72px;height:72px;place-items:center;overflow:hidden;border:1px solid var(--bm-border);border-radius:20px;background:var(--bm-surface-raised);color:var(--bm-accent);font-size:26px;font-weight:850}.bm-ui-avatar img{width:100%;height:100%;object-fit:cover}
.bm-ui-nft-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:9px;margin-top:12px}.bm-ui-nft{min-height:64px;align-items:flex-start;justify-content:center;flex-direction:column;text-align:left}.bm-ui-nft small{color:var(--bm-muted)}.bm-ui-nft[aria-disabled="true"]{cursor:not-allowed;opacity:.62}.bm-ui-nft-reason{display:block;color:var(--bm-danger)!important;line-height:1.35}.bm-ui-nft-more{display:flex;justify-content:center;margin-top:12px}.bm-ui-preview{display:grid;grid-template-columns:96px minmax(0,1fr);gap:14px;align-items:center;margin-top:14px}.bm-ui-preview img{width:96px;height:96px;object-fit:cover;border:1px solid var(--bm-border);border-radius:15px;background:var(--bm-surface-raised)}
.bm-market-toolbar{display:flex;align-items:center;gap:9px;flex-wrap:wrap;margin-bottom:16px}.bm-market-toolbar .bm-ui-input{flex:1 1 220px}.bm-market-select{height:50px;max-width:220px;padding:0 38px 0 13px;border:1px solid var(--bm-border);border-radius:13px;background:var(--bm-surface-raised);color:var(--bm-text);font:700 13px/1 var(--bm-font)}
.bm-market-tabs{display:flex;gap:5px;margin:0 0 18px;padding:4px;border:1px solid var(--bm-border);border-radius:15px;background:var(--bm-surface-raised)}.bm-market-tabs .bm-ui-button{flex:1;min-height:42px;border-color:transparent;background:transparent}.bm-market-tabs .bm-ui-button[aria-selected="true"]{border-color:var(--bm-border);background:var(--bm-surface);color:var(--bm-text)}
.bm-market-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}.bm-market-item{display:flex;min-width:0;flex-direction:column;overflow:hidden;padding:0;border:1px solid var(--bm-border);border-radius:17px;background:var(--bm-surface-raised)}.bm-market-art{display:grid;width:100%;aspect-ratio:1/1;place-items:center;overflow:hidden;background:color-mix(in srgb,var(--bm-surface-raised) 75%,var(--bm-accent) 8%);color:var(--bm-muted);font-size:12px}.bm-market-art img{width:100%;height:100%;object-fit:cover}.bm-market-info{display:grid;gap:7px;padding:13px}.bm-market-name{overflow:hidden;margin:0;color:var(--bm-text);font-size:14px;line-height:1.3;text-overflow:ellipsis;white-space:nowrap}.bm-market-meta{display:flex;align-items:center;justify-content:space-between;gap:9px;color:var(--bm-muted);font-size:11px}.bm-market-price{color:var(--bm-text);font-size:15px;font-weight:850}.bm-market-empty{padding:34px 18px;border:1px dashed var(--bm-border);border-radius:17px;color:var(--bm-muted);text-align:center;line-height:1.55}.bm-market-confirm{display:grid;grid-template-columns:150px minmax(0,1fr);gap:18px;align-items:start}.bm-market-confirm .bm-market-art{border:1px solid var(--bm-border);border-radius:17px}.bm-market-facts{display:grid;gap:8px;margin:15px 0;padding:13px;border:1px solid var(--bm-border);border-radius:14px;background:var(--bm-surface-raised)}.bm-market-fact{display:flex;justify-content:space-between;gap:14px;color:var(--bm-muted);font-size:12px}.bm-market-fact strong{color:var(--bm-text);text-align:right}
.bm-ui-sr{position:absolute!important;width:1px!important;height:1px!important;padding:0!important;margin:-1px!important;overflow:hidden!important;clip:rect(0,0,0,0)!important;white-space:nowrap!important;border:0!important}
@media(prefers-color-scheme:light){.bm-ui-overlay[data-bm-scheme="system"]{--bm-backdrop:rgba(19,23,28,.45);--bm-surface:#fbfbf8;--bm-surface-raised:#f1f2ec;--bm-text:#171914;--bm-muted:#60665c;--bm-border:rgba(18,21,16,.15);--bm-danger:#a92339;--bm-focus:#645f00;--bm-shadow:0 28px 90px rgba(10,14,18,.3)}}
@media(max-width:760px){.bm-market-grid{grid-template-columns:repeat(2,minmax(0,1fr))}}
@media(max-width:600px){.bm-ui-overlay{align-items:flex-end;padding:max(8px,env(safe-area-inset-top)) 0 0}.bm-ui-panel,.bm-ui-panel[data-bm-dialog-kind="account"],.bm-ui-panel[data-bm-dialog-kind="profile"],.bm-ui-panel[data-bm-dialog-kind="marketplace"]{min-height:0;max-width:none;max-height:calc(100dvh - max(8px,env(safe-area-inset-top)));border-right:0;border-bottom:0;border-left:0;border-radius:var(--bm-radius) var(--bm-radius) 0 0}.bm-ui-header{padding:22px 60px 13px 20px}.bm-ui-scroll{padding:18px 20px max(24px,env(safe-area-inset-bottom))}.bm-ui-wallet-grid,.bm-ui-nft-grid,.bm-market-grid{grid-template-columns:1fr}.bm-market-confirm{grid-template-columns:92px minmax(0,1fr)}.bm-ui-profile-head{grid-template-columns:60px minmax(0,1fr)}.bm-ui-avatar{width:60px;height:60px;border-radius:16px}.bm-ui-preview{grid-template-columns:76px minmax(0,1fr)}.bm-ui-preview img{width:76px;height:76px}}
@media(prefers-reduced-motion:reduce){.bm-ui-overlay *{scroll-behavior:auto!important;transition:none!important;animation:none!important}}
@media(forced-colors:active){.bm-ui-panel,.bm-ui-card,.bm-ui-input,.bm-ui-button,.bm-ui-close{border:1px solid CanvasText}.bm-ui-button[data-bm-variant="primary"]{background:Highlight;color:HighlightText}}
`

const normalizeSession = value => {
  if (value == null) return null
  if (typeof value !== 'object') throw new Error('Blockmaker session must be an object or null.')
  const sessionToken = String(value.sessionToken ?? '').trim()
  const refreshToken = String(value.refreshToken ?? '').trim()
  if (!sessionToken) throw new Error('Blockmaker session is missing its player sessionToken.')
  if (sessionToken.startsWith('sk_') || refreshToken.startsWith('sk_'))
    throw new Error('A Blockmaker server key cannot be used as a browser player session.')
  return {
    sessionToken,
    refreshToken,
    walletAddress: String(value.walletAddress ?? '').trim(),
    displayName: value.displayName == null ? undefined : String(value.displayName),
    accountKind: normalizedAccountKind(value.accountKind),
    authProvider: value.authProvider == null ? undefined : String(value.authProvider),
  }
}

const invalidShopInfoV1 = reason => {
  throw new BlockmakerError(`Blockmaker returned invalid Store v1 information: ${reason}`, {
    code: 'SHOP_INFO_INVALID',
  })
}

const validShopInfoDirectSplitContract = value => {
  if (!value || typeof value !== 'object' || Array.isArray(value)
    || value.state !== 'ready' || value.mode !== 'direct_split'
    || value.paymentPlanVersion !== 3
    || !['mainnet', 'testnet'].includes(value.network)
    || !Number.isSafeInteger(value.policyRevision) || value.policyRevision < 1
    || !/^[a-f0-9]{64}$/.test(value.policyHash)
    || !Array.isArray(value.destinations)
    || value.destinations.length < 2 || value.destinations.length > 4
    || typeof value.remainderDestinationId !== 'string'
    || value.treasuryUsed !== false || value.settlementWorkerUsed !== false)
    return false
  const ids = new Set()
  const addresses = new Set()
  let bpsTotal = 0
  let remainderCount = 0
  for (const [ordinal, destination] of value.destinations.entries()) {
    if (!destination || typeof destination !== 'object' || Array.isArray(destination)
      || destination.ordinal !== ordinal
      || !/^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$/.test(destination.destinationId)
      || ids.has(destination.destinationId)
      || !/^[A-Z2-7]{58}$/.test(destination.address)
      || addresses.has(destination.address)
      || !Number.isSafeInteger(destination.bps)
      || destination.bps < 1 || destination.bps > 10_000
      || typeof destination.isRemainder !== 'boolean'
      || typeof destination.label !== 'string' || !destination.label.trim()
      || destination.label.length > 80) return false
    ids.add(destination.destinationId)
    addresses.add(destination.address)
    bpsTotal += destination.bps
    if (destination.isRemainder) {
      remainderCount += 1
      if (destination.destinationId !== value.remainderDestinationId) return false
    }
  }
  if (bpsTotal !== 10_000 || remainderCount !== 1
    || !ids.has(value.remainderDestinationId)) return false
  try {
    if (!/^[1-9][0-9]*$/.test(value.maxSingleGrossMicroalgo)
      || !/^[1-9][0-9]*$/.test(value.dailyGrossLimitMicroalgo)
      || BigInt(value.maxSingleGrossMicroalgo) > BigInt(value.dailyGrossLimitMicroalgo)) return false
  } catch { return false }
  const signing = value.signingContract
  return !!signing && typeof signing === 'object' && !Array.isArray(signing)
    && signing.walletRequestCount === 1
    && signing.completeAtomicGroupRequired === true
    && Array.isArray(signing.signerTypes)
    && signing.signerTypes.length === 1
    && signing.signerTypes[0] === 'ed25519'
    && signing.rekeyedEd25519Supported === true
    && signing.falcon1024Supported === false
    && signing.omitSignerHintsWhenWalletKnowsSender === true
}

/** Fail-closed generic randomized-ASA-pack Store v1 normalization. */
export function normalizeShopInfoV1(value) {
  if (!value || typeof value !== 'object' || Array.isArray(value))
    invalidShopInfoV1('the response must be an object.')
  if (value.success !== true) invalidShopInfoV1('success must be true.')
  if (value.storeApiVersion !== 1) invalidShopInfoV1('storeApiVersion must be 1.')
  if (value.storeKind !== 'randomized_asa_pack')
    invalidShopInfoV1('storeKind must be randomized_asa_pack.')
  if (value.mode !== 'smart_treasury') invalidShopInfoV1('mode must be smart_treasury.')
  if (typeof value.enabled !== 'boolean') invalidShopInfoV1('enabled must be a boolean.')
  if (value.acceptingPayments !== undefined
    && (typeof value.acceptingPayments !== 'boolean' || value.acceptingPayments !== value.enabled))
    invalidShopInfoV1('acceptingPayments must exactly match enabled when present.')
  if (value.salesRequested !== undefined && typeof value.salesRequested !== 'boolean')
    invalidShopInfoV1('salesRequested must be a boolean when present.')
  if (value.pricesAvailable !== undefined && typeof value.pricesAvailable !== 'boolean')
    invalidShopInfoV1('pricesAvailable must be a boolean when present.')
  if (value.catalogueProvider !== undefined
    && !['configured_assets', 'nfturbo_parts'].includes(value.catalogueProvider))
    invalidShopInfoV1('catalogueProvider is not supported.')
  if (value.paymentAssetId !== undefined && value.paymentAssetId !== 0)
    invalidShopInfoV1('paymentAssetId must identify native ALGO.')
  if (value.paymentAssetLabel !== undefined && value.paymentAssetLabel !== 'ALGO')
    invalidShopInfoV1('paymentAssetLabel must be ALGO.')
  if (value.paymentAssetDecimals !== undefined && value.paymentAssetDecimals !== 6)
    invalidShopInfoV1('paymentAssetDecimals must be 6.')

  if (!Array.isArray(value.bundles) || value.bundles.length > 8)
    invalidShopInfoV1('bundles must be an array with at most 8 entries.')
  const seenPacks = new Set()
  const bundles = value.bundles.map((bundle, index) => {
    if (!bundle || typeof bundle !== 'object' || Array.isArray(bundle))
      invalidShopInfoV1(`bundles[${index}] must be an object.`)
    if (!Number.isSafeInteger(bundle.packs) || bundle.packs < 1 || bundle.packs > 10)
      invalidShopInfoV1(`bundles[${index}].packs must be an integer from 1 to 10.`)
    if (seenPacks.has(bundle.packs))
      invalidShopInfoV1(`bundles[${index}].packs duplicates an existing bundle.`)
    seenPacks.add(bundle.packs)
    if (!Number.isSafeInteger(bundle.priceAtomic) || bundle.priceAtomic < 1)
      invalidShopInfoV1(`bundles[${index}].priceAtomic must be a positive safe integer.`)
    if (!Number.isSafeInteger(bundle.priceMicro) || bundle.priceMicro !== bundle.priceAtomic)
      invalidShopInfoV1(`bundles[${index}].priceMicro must exactly equal priceAtomic.`)
    if (bundle.priceAlgo !== undefined
      && (typeof bundle.priceAlgo !== 'number' || !Number.isFinite(bundle.priceAlgo)
        || bundle.priceAlgo !== bundle.priceAtomic / 1_000_000))
      invalidShopInfoV1(`bundles[${index}].priceAlgo must exactly represent priceAtomic when present.`)
    return Object.freeze({ ...bundle })
  })
  if (!Number.isSafeInteger(value.partsPerPack)
    || value.partsPerPack < 1 || value.partsPerPack > 20)
    invalidShopInfoV1('partsPerPack must be an integer from 1 to 20.')
  if (bundles.some(bundle => bundle.packs * value.partsPerPack > 50))
    invalidShopInfoV1('no Store offer may deliver more than 50 items with the current protected vault.')
  const capacityPacks = value.enabled
    ? bundles.reduce((largest, bundle) => Math.max(largest, bundle.packs), 0) : 0
  if (value.packsAvailable !== capacityPacks
    || value.poolSize !== capacityPacks * value.partsPerPack)
    invalidShopInfoV1('capacity sentinels must derive only from configured offers and be zero while blocked.')
  if (value.inStock !== value.enabled)
    invalidShopInfoV1('inStock must exactly match effective payment readiness.')

  let readiness
  if (value.readiness !== undefined) {
    const candidate = value.readiness
    if (!candidate || typeof candidate !== 'object' || Array.isArray(candidate))
      invalidShopInfoV1('readiness must be an object when present.')
    if (!['ready', 'blocked', 'unavailable'].includes(candidate.status))
      invalidShopInfoV1('readiness.status is not supported.')
    if (candidate.code !== null && typeof candidate.code !== 'string')
      invalidShopInfoV1('readiness.code must be a string or null.')
    if (!Number.isSafeInteger(candidate.checkedAt) || candidate.checkedAt < 0)
      invalidShopInfoV1('readiness.checkedAt must be a non-negative safe integer.')
    if (!Number.isSafeInteger(candidate.validUntil) || candidate.validUntil < candidate.checkedAt)
      invalidShopInfoV1('readiness.validUntil must be a safe integer at or after checkedAt.')
    if (candidate.retryAfterSeconds !== null
      && (!Number.isSafeInteger(candidate.retryAfterSeconds) || candidate.retryAfterSeconds < 1))
      invalidShopInfoV1('readiness.retryAfterSeconds must be a positive safe integer or null.')
    readiness = Object.freeze({ ...candidate })
  }

  const enabled = value.enabled
  if (enabled && (
    value.acceptingPayments !== true
    || value.salesRequested !== true
    || value.pricesAvailable !== true
    || !['configured_assets', 'nfturbo_parts'].includes(value.catalogueProvider)
    || value.paymentAssetId !== 0
    || value.paymentAssetLabel !== 'ALGO'
    || value.paymentAssetDecimals !== 6
    || bundles.length < 1
    || !Number.isSafeInteger(value.packsAvailable) || value.packsAvailable < 1
    || value.inStock !== true
    || !readiness || readiness.status !== 'ready' || readiness.code !== null
    || readiness.retryAfterSeconds !== null
    || !validShopInfoDirectSplitContract(value.paymentContract)
  )) invalidShopInfoV1('enabled Stores require the complete ready ALGO payment-plan-3 contract.')
  return Object.freeze({
    ...value,
    enabled,
    acceptingPayments: enabled,
    availability: enabled ? 'accepting_payments' : 'not_accepting_payments',
    bundles: Object.freeze(bundles),
    ...(readiness ? { readiness } : {}),
  })
}

const requireGroupKey = (value, label, scope = false) => {
  if (typeof value !== 'string') throw new Error(`groups.${label} requires a stable identifier.`)
  const key = value.trim()
  if (!(scope ? /^[a-z0-9][a-z0-9_-]{0,63}$/ : /^[A-Za-z0-9][A-Za-z0-9:._-]{0,127}$/).test(key))
    throw new Error(`groups.${label} requires a stable identifier.`)
  return key
}
const requireGroupInput = (input, allowed) => {
  if (!input || typeof input !== 'object' || Array.isArray(input)
    || Object.keys(input).some(name => !allowed.includes(name)))
    throw new Error(`groups input may contain only ${allowed.join(', ')}.`)
  return input
}
const requireGroupInteger = (value, min, max, label) => {
  if (!Number.isSafeInteger(value) || value < min || value > max)
    throw new Error(`groups.${label} must be a whole number from ${min} to ${max}.`)
  return value
}
const requireGroupWallet = value => {
  if (typeof value !== 'string' || !/^[A-Z2-7]{58}$/.test(value))
    throw new Error('groups walletAddress must be a valid Algorand address.')
  return value
}

export function createBlockmaker(options = {}) {
  if (options.apiKey || options.secretKey || options.serverKey)
    throw new Error('Blockmaker browser clients must not receive a server/API key. Use only baseUrl + public gameId.')

  const baseUrl = String(options.baseUrl ?? '').trim().replace(/\/$/, '')
  const gameId = String(options.gameId ?? '').trim()
  if (!/^https?:\/\//.test(baseUrl)) throw new Error('Blockmaker baseUrl must be an http(s) URL.')
  const base = new URL(baseUrl)
  if (base.username || base.password || base.pathname !== '/' || base.search || base.hash)
    throw new Error('Blockmaker baseUrl must be an exact origin with no credentials, path, query, or fragment.')
  if (!isSafeApiOrigin(base))
    throw new Error('Blockmaker baseUrl must use HTTPS (HTTP is allowed only for localhost development).')
  if (!gameId || gameId.startsWith('sk_')) throw new Error('Blockmaker gameId is required and must be the public game ID, not a secret key.')
  const clientKind = options.clientKind === undefined ? 'web' : String(options.clientKind)
  if (clientKind !== 'web' && clientKind !== 'unity_webgl')
    throw new Error('Blockmaker clientKind must be web or unity_webgl. Native Unity is not supported by this package.')
  const clientHeader = clientKind === 'unity_webgl' ? 'unity-webgl' : 'web'

  const fetchImpl = options.fetch ?? globalThis.fetch?.bind(globalThis)
  if (!fetchImpl) throw new Error('This environment does not provide fetch().')
  const timeoutMs = Number.isFinite(options.timeoutMs) ? Math.max(1000, options.timeoutMs) : 12_000
  let storage = memoryStorage()
  if (options.storage !== false) {
    try { storage = options.storage ?? globalThis.sessionStorage ?? storage }
    catch { /* privacy/sandboxed contexts may block even reading sessionStorage */ }
  }
  const storageKey = `blockmaker:${gameId}:session`
  const onrampOrderStorageKey = `blockmaker:${gameId}:onramp-order`

  let session = null
  // In-memory public adapter/account cache; never keys or signatures.
  let rememberedAlgorandSigner = null
  try { session = normalizeSession(JSON.parse(storage.getItem(storageKey) || 'null')) }
  catch {
    session = null
    try { storage.removeItem(storageKey) } catch { /* storage may be unavailable */ }
  }

  const storeSession = value => {
    const normalized = normalizeSession(value)
    session = normalized
    try {
      if (normalized) storage.setItem(storageKey, JSON.stringify(normalized))
      else storage.removeItem(storageKey)
    } catch { /* keep the in-memory session when storage is unavailable/full */ }
  }

  const setSessionManually = value => {
    const normalized = normalizeSession(value)
    if (isAuthenticationOnlyEmailSession(session)) {
      const exactEmailFamily = normalized
        && isAuthenticationOnlyEmailSession(normalized)
        && normalized.refreshToken === session.refreshToken
        && normalized.walletAddress === session.walletAddress
      if (!exactEmailFamily) {
        throw new BlockmakerError(
          'Call and await blockmaker.auth.logout() before replacing an authentication-only email session.',
          { code: 'SESSION_CONFLICT' },
        )
      }
    }
    invalidateLoginAttempts()
    storeSession(normalized)
  }

  let lastOnrampOrderId = ''
  try {
    const savedOrderId = String(storage.getItem(onrampOrderStorageKey) ?? '').trim()
    if (/^[A-Za-z0-9_-]{1,128}$/.test(savedOrderId)) lastOnrampOrderId = savedOrderId
  } catch { /* storage may be unavailable */ }
  const storeLastOnrampOrder = value => {
    const id = String(value ?? '').trim()
    lastOnrampOrderId = /^[A-Za-z0-9_-]{1,128}$/.test(id) ? id : ''
    try {
      if (lastOnrampOrderId) storage.setItem(onrampOrderStorageKey, lastOnrampOrderId)
      else storage.removeItem(onrampOrderStorageKey)
    } catch { /* in-memory tracking still works */ }
  }

  const urlFor = path => {
    if (typeof path !== 'string' || !path.startsWith('/') || path.startsWith('//'))
      throw new Error('Blockmaker request paths must start with one / and cannot be absolute URLs.')
    const url = new URL(path, `${baseUrl}/`)
    if (url.origin !== base.origin) throw new Error('Blockmaker requests cannot leave the configured API origin.')
    if (!url.searchParams.has('gameId')) url.searchParams.set('gameId', gameId)
    return url
  }

  const rawRequest = async (path, request = {}, exactSessionToken = null) => {
    const headers = new Headers(request.headers ?? {})
    if (headers.has('Authorization'))
      throw new Error('Do not pass Authorization to the browser SDK. Sign in with blockmaker.auth and let the SDK attach the player session.')
    const controller = new AbortController()
    const timeout = setTimeout(() => controller.abort(), request.timeoutMs ?? timeoutMs)
    headers.set('Accept', 'application/json')
    headers.set('X-Blockmaker-Game', gameId)
    headers.set('X-Blockmaker-Client', clientHeader)
    if (exactSessionToken !== null) {
      if (typeof exactSessionToken !== 'string' || exactSessionToken.length < 1
        || exactSessionToken.length > 8_192 || !/^[\x21-\x7e]+$/.test(exactSessionToken)
        || /^sk_/i.test(exactSessionToken)) {
        throw new Error('The internal player session token is invalid.')
      }
      headers.set('Authorization', `Bearer ${exactSessionToken}`)
    } else if (request.auth !== false && session?.sessionToken) {
      headers.set('Authorization', `Bearer ${session.sessionToken}`)
    }
    let body = request.body
    if (body !== undefined && !(body instanceof FormData) && typeof body !== 'string') {
      headers.set('Content-Type', 'application/json')
      body = JSON.stringify(body)
    }

    // Validate/resolve before the network try/catch so caller mistakes (especially
    // absolute URLs) are not disguised as connectivity failures.
    const requestUrl = urlFor(path)
    let response
    try {
      response = await fetchImpl(requestUrl, { ...request, headers, body, signal: controller.signal, redirect: 'error' })
    } catch (error) {
      if (error?.name === 'AbortError')
        throw new BlockmakerError('Blockmaker request timed out.', { code: 'TIMEOUT' })
      let originHint = ''
      try {
        const callerOrigin = globalThis.location?.origin
        if (callerOrigin === 'null')
          originHint = ' This page is running from a file or sandboxed preview; serve it from http://localhost during development.'
        else if (callerOrigin && callerOrigin !== base.origin)
          originHint = ` If this is a web game, add the exact address ${callerOrigin} under Blockmaker → Connect game → Live game address.`
      } catch { /* location is unavailable in some runtimes */ }
      throw new BlockmakerError(`Could not reach Blockmaker.${originHint}`, { code: 'NETWORK_ERROR', details: error })
    } finally { clearTimeout(timeout) }

    const contentType = response.headers.get('content-type') || ''
    const data = contentType.includes('json') ? await response.json().catch(() => ({})) : await response.text()
    if (!response.ok || data?.success === false) {
      const retryHeaderValue = response.headers.get('retry-after')
      const retryBodyValue = data?.retryAfterSeconds
      const retryHeader = retryHeaderValue == null || retryHeaderValue.trim() === ''
        ? null
        : Number(retryHeaderValue)
      const retryBody = retryBodyValue == null || String(retryBodyValue).trim() === ''
        ? null
        : Number(retryBodyValue)
      throw new BlockmakerError(data?.error || `Blockmaker request failed (${response.status}).`, {
        status: response.status,
        code: data?.code || 'REQUEST_FAILED',
        requestId: data?.requestId || response.headers.get('x-request-id'),
        retryAfter: retryHeader != null && Number.isFinite(retryHeader)
          ? retryHeader
          : retryBody != null && Number.isFinite(retryBody) ? retryBody : null,
        fieldPath: typeof data?.fieldPath === 'string' ? data.fieldPath : null,
        mayHaveApplied: data?.mayHaveApplied === true || data?.mayAlreadyHaveApplied === true,
        developerDiagnostic: typeof data?.developerDiagnostic === 'string'
          ? data.developerDiagnostic
          : null,
        details: data,
      })
    }
    return data
  }

  // Coalesce rotation so one refresh token is never submitted twice.
  let refreshPromise = null
  const refresh = async () => {
    if (refreshPromise) return refreshPromise
    const sessionAtStart = session
    const refreshToken = sessionAtStart?.refreshToken
    if (!refreshToken) throw new BlockmakerError('No refresh token is available.', { status: 401, code: 'AUTH_MISSING' })

    refreshPromise = (async () => {
      const data = await rawRequest('/v1/auth/refresh', {
        method: 'POST', auth: false, body: { refreshToken, gameId },
      })
      // A deliberate logout or a different login while the network call was in
      // flight must win over this older refresh response.
      if (session?.refreshToken === refreshToken) {
        storeSession({ ...sessionAtStart, ...data, sessionToken: data.sessionToken, refreshToken: data.refreshToken })
      }
      return data
    })()

    try { return await refreshPromise }
    finally { refreshPromise = null }
  }

  const request = async (path, init = {}) => {
    try { return await rawRequest(path, init) }
    catch (error) {
      if (!init._retried && init.auth !== false && error instanceof BlockmakerError && error.status === 401 && session?.refreshToken) {
        const failedRefreshToken = session.refreshToken
        try { await refresh() }
        catch {
          // Do not erase a newer login that replaced the failed session while
          // refresh was in flight.
          if (session?.refreshToken === failedRefreshToken) storeSession(null)
          throw error
        }
        return rawRequest(path, { ...init, _retried: true })
      }
      throw error
    }
  }

  const walletPackageMemberByRole = (manifest, role) => {
    const member = manifest.members.find(candidate => candidate.role === role)
    if (!member) walletPackageEvidenceError(`The wallet package is missing its ${role} member.`)
    return member
  }

  const exactWalletPackageBridgeEnvelope = envelope => {
    const envelopeSchemaVersion = envelope?.schemaVersion
    const versionTwo = envelopeSchemaVersion === 'blockmaker-unity-webgl-wallet-package/v2'
    walletPackageExactKeys(envelope, [
      'schemaVersion', 'lifecycleId', 'operationId', 'operationKind', 'phase', 'callbackMethod',
      ...(versionTwo ? ['runtimeProfile'] : []),
    ], 'The Unity WebGL package ready envelope')
    if ((envelopeSchemaVersion !== 'blockmaker-unity-webgl-wallet-package/v1' && !versionTwo)
      || typeof envelope.lifecycleId !== 'string' || !/^[a-f0-9]{32}$/.test(envelope.lifecycleId)
      || envelope.operationId !== 0 || envelope.operationKind !== 'initialize'
      || envelope.phase !== 'ready'
      || envelope.callbackMethod !== 'OnBlockmakerWalletPackageSuccess'
      || (versionTwo && !UNITY_WEBGL_RUNTIME_PROFILE_PROVIDER_IDS[envelope.runtimeProfile])) {
      walletPackageEvidenceError('The Unity WebGL package ready envelope is invalid.')
    }
    return Object.freeze({
      schemaVersion: envelopeSchemaVersion,
      lifecycleId: envelope.lifecycleId,
      operationId: 0,
      operationKind: 'initialize',
      phase: 'ready',
      callbackMethod: 'OnBlockmakerWalletPackageSuccess',
      ...(versionTwo ? { runtimeProfile: envelope.runtimeProfile } : {}),
    })
  }

  const materializeWalletPackageEvidence = async ({
    unityBridgeEnvelope = null,
    unityHostRuntimeUrl = null,
    signal,
    failure = () => {},
  } = {}) => {
    if (!/^[A-Za-z0-9][A-Za-z0-9_-]{5,127}$/.test(gameId))
      walletPackageEvidenceError('Deployment evidence requires a canonical public Blockmaker game ID.')
    const specification = WALLET_PACKAGE_SPECIFICATIONS[clientKind]
    failure(3)
    const origin = exactWalletPackageProductionOrigin()
    failure(4)
    // Public, capture-owned read; never attaches a player session.
    const response = await fetchImpl(urlFor('/v1/integrations/manifest'), {
      method: 'GET', redirect: 'error', credentials: 'omit', cache: 'no-store', signal,
      headers: {
        Accept: 'application/json',
        'X-Blockmaker-Game': gameId,
        'X-Blockmaker-Client': clientHeader,
      },
    })
    if (!response?.ok)
      walletPackageEvidenceError('The public wallet-package manifest is unavailable.', 'PACKAGE_EVIDENCE_UNAVAILABLE')
    const manifestText = await response.text()
    failure(5)
    if (manifestText.length > 1_000_000)
      walletPackageEvidenceError('The public wallet-package manifest exceeds its size bound.')
    let manifestResponse
    try { manifestResponse = JSON.parse(manifestText) }
    catch { walletPackageEvidenceError('The public wallet-package manifest is not JSON.') }
    const packageManifest = await parseWalletPackageManifestInBrowser(
      manifestResponse?.[specification.manifestKey],
      { apiOrigin: base.origin, clientKind },
    )
    const packageLock = Object.freeze({
      schemaVersion: 'blockmaker-wallet-package-lock/v1',
      gameId,
      apiOrigin: base.origin,
      package: packageManifest,
    })
    const packageLockIntegrity = await walletPackageIntegrity(canonicalWalletPackageJson(packageLock))
    const browserRuntime = walletPackageMemberByRole(packageManifest, 'browser_runtime')
    failure(7)
    const browserRuntimeUrl = exactWalletPackageRuntimeUrl(
      BLOCKMAKER_BROWSER_RUNTIME_URL,
      origin,
      browserRuntime.file,
    )
    const loadedRuntimeMembers = []
    let envelope = null
    if (clientKind === 'unity_webgl') {
      envelope = exactWalletPackageBridgeEnvelope(unityBridgeEnvelope)
      const currentPackage = packageManifest.schemaVersion === 'blockmaker-unity-webgl-package/v3'
      if ((currentPackage && envelope.schemaVersion !== 'blockmaker-unity-webgl-wallet-package/v2')
        || (!currentPackage && envelope.schemaVersion !== 'blockmaker-unity-webgl-wallet-package/v1')) {
        walletPackageEvidenceError('The executed Unity bridge envelope does not match the package schema.')
      }
      if (currentPackage && !packageManifest.providerPolicy.profiles.some(
        profile => profile.id === envelope.runtimeProfile,
      )) {
        walletPackageEvidenceError('The executed Unity runtime profile is not bound by this package.')
      }
      const host = walletPackageMemberByRole(packageManifest, 'wallet_host')
      const hostRuntimeUrl = exactWalletPackageRuntimeUrl(
        unityHostRuntimeUrl,
        origin,
        host.file,
      )
      if (new URL(hostRuntimeUrl).pathname.replace(/[^/]+$/, '')
        !== new URL(browserRuntimeUrl).pathname.replace(/[^/]+$/, '')) {
        walletPackageEvidenceError('The executed Unity wallet host is not the exact sibling of blockmaker.js.')
      }
      await verifiedWalletPackageRuntime(hostRuntimeUrl, host, { signal, failure })
      loadedRuntimeMembers.push(Object.freeze({
        file: host.file,
        url: hostRuntimeUrl,
        integrity: host.integrity,
      }))
    }
    await verifiedWalletPackageRuntime(browserRuntimeUrl, browserRuntime, { signal, failure })
    loadedRuntimeMembers.push(Object.freeze({
      file: browserRuntime.file,
      url: browserRuntimeUrl,
      integrity: browserRuntime.integrity,
    }))
    if (clientKind === 'unity_webgl') {
      const walletRuntime = walletPackageMemberByRole(packageManifest, 'wallet_runtime')
      const walletRuntimeUrl = exactWalletPackageRuntimeUrl(
        new URL(walletRuntime.file, unityHostRuntimeUrl).toString(),
        origin,
        walletRuntime.file,
      )
      if (new URL(walletRuntimeUrl).pathname.replace(/[^/]+$/, '')
        !== new URL(browserRuntimeUrl).pathname.replace(/[^/]+$/, '')) {
        walletPackageEvidenceError('The executed TxnLab wallet runtime is not an exact package sibling.')
      }
      await verifiedWalletPackageRuntime(walletRuntimeUrl, walletRuntime, { signal, failure })
      loadedRuntimeMembers.push(Object.freeze({
        file: walletRuntime.file,
        url: walletRuntimeUrl,
        integrity: walletRuntime.integrity,
      }))
    }
    const actuallyLoadedBrowserRuntime = Object.freeze({
      file: browserRuntime.file,
      url: browserRuntimeUrl,
      integrity: browserRuntime.integrity,
    })
    const execution = clientKind === 'unity_webgl'
      ? packageManifest.schemaVersion === 'blockmaker-unity-webgl-package/v3'
        ? Object.freeze({
          schemaVersion: 'blockmaker-unity-webgl-wallet-package-execution/v2',
          executionId: envelope.lifecycleId,
          envelopeSchemaVersion: envelope.schemaVersion,
          callbackMethod: envelope.callbackMethod,
          lifecycleId: envelope.lifecycleId,
          operationId: 0,
          operationKind: 'initialize',
          phase: 'ready',
          runtimeProfile: envelope.runtimeProfile,
          hostIntegrity: walletPackageMemberByRole(packageManifest, 'wallet_host').integrity,
          browserRuntimeIntegrity: browserRuntime.integrity,
          unityClientIntegrity: walletPackageMemberByRole(packageManifest, 'unity_client').integrity,
          unityFacadeIntegrity: walletPackageMemberByRole(packageManifest, 'unity_wallet_facade').integrity,
          webGlBridgeIntegrity: walletPackageMemberByRole(packageManifest, 'unity_webgl_bridge').integrity,
        })
        : Object.freeze({
          schemaVersion: 'blockmaker-unity-webgl-wallet-package-execution/v1',
          executionId: envelope.lifecycleId,
          envelopeSchemaVersion: envelope.schemaVersion,
          callbackMethod: envelope.callbackMethod,
          lifecycleId: envelope.lifecycleId,
          operationId: 0,
          operationKind: 'initialize',
          phase: 'ready',
          hostIntegrity: walletPackageMemberByRole(packageManifest, 'wallet_host').integrity,
          browserRuntimeIntegrity: browserRuntime.integrity,
          unityClientIntegrity: walletPackageMemberByRole(packageManifest, 'unity_client').integrity,
          unityFacadeIntegrity: walletPackageMemberByRole(packageManifest, 'unity_wallet_facade').integrity,
          webGlBridgeIntegrity: walletPackageMemberByRole(packageManifest, 'unity_webgl_bridge').integrity,
        })
      : Object.freeze({
          schemaVersion: 'blockmaker-web-wallet-package-execution/v1',
          executionId: walletPackageRandomId(),
          operationKind: 'initialize',
          phase: 'ready',
          browserRuntimeIntegrity: browserRuntime.integrity,
        })
    const evidence = Object.freeze({
      schemaVersion: 'blockmaker-wallet-package-deployed-evidence/v1',
      gameId,
      apiOrigin: base.origin,
      origin,
      target: specification.target,
      clientKind: specification.clientKind,
      packageId: packageManifest.packageId,
      packageLockIntegrity,
      observationKind: 'runtime_loaded',
      complete: true,
      members: Object.freeze(packageManifest.members.map(member => Object.freeze({
        file: member.file,
        integrity: member.integrity,
        rawBytes: member.rawBytes,
      }))),
      actuallyLoadedRuntimeMembers: Object.freeze(loadedRuntimeMembers),
      actuallyLoadedBrowserRuntime,
      execution,
    })
    const canonicalJson = canonicalWalletPackageJson(evidence)
    return Object.freeze({
      evidence,
      canonicalJson,
      packageId: packageManifest.packageId,
      packageLockIntegrity,
      fileName: `blockmaker-${gameId}-${clientKind}-deployed-evidence.json`,
    })
  }

  const downloadWalletPackageEvidence = materialized => {
    const doc = globalThis.document
    if (!doc?.createElement || !globalThis.URL?.createObjectURL || typeof globalThis.Blob !== 'function')
      walletPackageEvidenceError('Deployment evidence download requires a browser.', 'PACKAGE_EVIDENCE_UNAVAILABLE')
    const url = globalThis.URL.createObjectURL(new Blob([materialized.canonicalJson], {
      type: 'application/json; charset=utf-8',
    }))
    try {
      const anchor = doc.createElement('a')
      anchor.href = url
      anchor.download = materialized.fileName
      anchor.rel = 'noopener'
      anchor.style.display = 'none'
      doc.body?.appendChild?.(anchor)
      anchor.click()
      anchor.remove?.()
    } finally {
      globalThis.URL.revokeObjectURL(url)
    }
  }

  const beginEvidenceCapture = options => {
    const state = { status: 1, materialized: null, promise: null }
    const controller = new AbortController()
    let settled = false
    let failedStatus = 9
    let timer
    const deadline = Date.now() + EVIDENCE_CAPTURE_TIMEOUT_MS
    state.promise = new Promise((resolve, reject) => {
      const fail = (error, status) => {
        if (settled) return
        settled = true
        state.status = status
        clearTimeout(timer)
        controller.abort()
        reject(error)
      }
      const expire = () => fail(new BlockmakerError(
        'Wallet-package evidence capture timed out.', { code: 'PACKAGE_EVIDENCE_UNAVAILABLE' },
      ), 8)
      timer = setTimeout(expire, EVIDENCE_CAPTURE_TIMEOUT_MS)
      Promise.resolve().then(() => materializeWalletPackageEvidence({
        ...options,
        signal: controller.signal,
        failure: status => { if (!settled) failedStatus = status },
      })).then(materialized => {
        if (settled) return
        if (Date.now() >= deadline) { expire(); return }
        settled = true
        clearTimeout(timer)
        state.materialized = materialized
        state.status = 2
        resolve(materialized)
      }, error => fail(error, failedStatus))
    })
    // Observe rejection for Unity's void capture.
    void state.promise.catch(() => {})
    return state
  }

  let webEvidenceState = null
  const captureBrowserWalletPackageEvidence = async () => {
    if (clientKind !== 'web')
      walletPackageEvidenceError('Unity WebGL deployment evidence is captured only by its validated C# bridge roundtrip.')
    if (!webEvidenceState)
      webEvidenceState = beginEvidenceCapture()
    const materialized = await webEvidenceState.promise
    return materialized.evidence
  }

  const downloadBrowserWalletPackageEvidence = async () => {
    await captureBrowserWalletPackageEvidence()
    const materialized = await webEvidenceState.promise
    downloadWalletPackageEvidence(materialized)
  }

  // Clear locally first; connector disconnect is bounded and best effort.
  const disconnectRememberedAlgorandWallet = async () => {
    const wallet = rememberedAlgorandSigner?.wallet
    rememberedAlgorandSigner = null
    if (typeof wallet?.disconnect !== 'function') return
    let timer
    try {
      await Promise.race([
        Promise.resolve(wallet.disconnect()),
        new Promise(resolve => { timer = setTimeout(resolve, 1_500) }),
      ])
    } catch { /* local sign-out remains complete when a provider cannot disconnect */ }
    finally { if (timer) clearTimeout(timer) }
  }

  const logoutPlayer = async (logoutOptions = {}) => {
    invalidateLoginAttempts()
    const sessionAtStart = session
    const refreshToken = sessionAtStart?.refreshToken ?? ''
    const authenticationOnlyEmail = isAuthenticationOnlyEmailSession(sessionAtStart)
    // Local sign-out wins immediately over an in-flight refresh or provider.
    storeSession(null)
    const revoke = refreshToken
      ? request('/v1/auth/logout', { method: 'POST', auth: false, body: { refreshToken, gameId } })
      : Promise.resolve()
    const [revocation] = await Promise.allSettled([revoke, disconnectRememberedAlgorandWallet()])
    if (authenticationOnlyEmail && (!refreshToken || revocation.status === 'rejected')) {
      latchWeb3AuthAvmCleanup()
      throw safeWeb3AuthAvmError({ code: 'PROVIDER_CLEANUP_REQUIRED' })
    }
    // The headless auth API preserves its historical ability to report a
    // failed server revocation. The drop-in UI still signs out locally and
    // remains usable when the network is offline.
    if (logoutOptions.reportRevocationFailure === true && revocation.status === 'rejected')
      throw revocation.reason
  }

  // Resolve tenant policy before provider UI opens.
  const providerPolicyCheck = async providerId => {
    const normalized = String(providerId ?? '').trim().toLowerCase()
    const config = await rawRequest('/v1/integrations/config', { auth: false })
    const policy = config?.walletProviderPolicy
    const allowUnlistedAdapters = policy == null || policy.allowUnlistedAdapters === true
    const providers = Array.isArray(config?.walletProviders) ? config.walletProviders : []
    // Preserve pre-policy deployments; modern manifests fail closed below.
    if (policy == null && providers.length === 0)
      return { config, providerId: WALLET_PROVIDER_IDS.has(normalized) ? normalized : null }
    if (!normalized || !WALLET_PROVIDER_IDS.has(normalized)) {
      if (allowUnlistedAdapters) return { config, providerId: null }
      throw new BlockmakerError('That wallet adapter is not enabled for this game. Choose one of the available sign-in options.', {
        status: 403,
        code: 'PROVIDER_NOT_ENABLED',
      })
    }
    const provider = providers.find(item => item?.id === normalized)
    if (provider?.enabled === true
      || (allowUnlistedAdapters && provider?.status === 'legacy_compatibility')) {
      return { config, providerId: normalized }
    }
    const code = provider?.unavailableCode === 'PROVIDER_NOT_ENABLED'
      ? 'PROVIDER_NOT_ENABLED'
      : 'PROVIDER_UNAVAILABLE'
    throw new BlockmakerError(
      String(provider?.unavailableReason || WALLET_ERROR_COPY[code]),
      { status: 403, code },
    )
  }

  let loginAttemptSequence = 0
  const beginLoginAttempt = () => ++loginAttemptSequence
  const invalidateLoginAttempts = () => { loginAttemptSequence += 1 }

  const getPlayerSession = async () => {
    const sessionTokenAtStart = session?.sessionToken
    const data = await request('/v1/auth/session')
    if (sessionTokenAtStart && session?.sessionToken === sessionTokenAtStart)
      storeSession({ ...session, ...data })
    return data
  }

  const revokeUnacceptedLogin = async (value, keepalive = false) => {
    const refreshToken = String(value?.refreshToken ?? '')
    if (!refreshToken) return true
    try {
      await rawRequest('/v1/auth/logout', {
        method: 'POST', auth: false, keepalive, body: { refreshToken, gameId },
      })
      return true
    } catch { return false }
  }

  const acceptLogin = async (data, attemptId, cleanupContext = null) => {
    const cleanupKeepalive = cleanupContext?.keepalive === true
    if (attemptId !== loginAttemptSequence) {
      const revoked = await revokeUnacceptedLogin(data, cleanupKeepalive)
      if (!revoked) {
        if (data?.authProvider === 'web3auth_avm_email') latchWeb3AuthAvmCleanup()
        throw new BlockmakerError('The replaced player session could not be safely revoked. Reload is required.', {
          code: 'PROVIDER_CLEANUP_REQUIRED',
        })
      }
      throw new BlockmakerError('A newer sign-in replaced this request.', { code: 'AUTH_SUPERSEDED' })
    }
    const next = {
      sessionToken: data.sessionToken,
      refreshToken: data.refreshToken,
      walletAddress: data.walletAddress,
      displayName: data.displayName,
      accountKind: data.accountKind,
      authProvider: data.authProvider,
    }
    const previous = session
    if (isAuthenticationOnlyEmailSession(previous)
      && previous?.refreshToken
      && previous.refreshToken !== next.refreshToken) {
      const previousRefreshToken = previous.refreshToken
      try {
        await rawRequest('/v1/auth/logout', {
          method: 'POST', auth: false,
          body: { refreshToken: previousRefreshToken, gameId },
        })
      } catch {
        await revokeUnacceptedLogin(next, cleanupKeepalive)
        latchWeb3AuthAvmCleanup()
        throw safeWeb3AuthAvmError({ code: 'PROVIDER_CLEANUP_REQUIRED' })
      }
      if (attemptId !== loginAttemptSequence
        || session?.refreshToken !== previousRefreshToken) {
        if (session?.refreshToken === previousRefreshToken) storeSession(null)
        if (!await revokeUnacceptedLogin(next, cleanupKeepalive)) latchWeb3AuthAvmCleanup()
        throw new BlockmakerError('A newer sign-in replaced this request.', { code: 'AUTH_SUPERSEDED' })
      }
    }
    if (attemptId !== loginAttemptSequence) {
      if (!await revokeUnacceptedLogin(next, cleanupKeepalive)) latchWeb3AuthAvmCleanup()
      throw new BlockmakerError('A newer sign-in replaced this request.', { code: 'AUTH_SUPERSEDED' })
    }
    if (rememberedAlgorandSigner?.walletAddress !== next.walletAddress)
      rememberedAlgorandSigner = null
    storeSession(next)
    return data
  }

  let activeWeb3AuthAvmEmail = null
  const loginWithWeb3AuthAvmEmailTransport = async (
    loginOptions = {},
    clientKind = 'web',
    qualificationPermit = null,
  ) => {
    const qualificationMode = qualificationPermit !== null
    if (!loginOptions || typeof loginOptions !== 'object' || Array.isArray(loginOptions))
      throw new Error('loginWithWeb3AuthAvmEmail options must be an object.')
    if (!['web', 'unity_webgl'].includes(clientKind))
      throw new Error('Email-wallet sign-in supports browser and Unity WebGL only.')
    if (loginOptions.signal != null
      && (typeof loginOptions.signal !== 'object' || typeof loginOptions.signal.addEventListener !== 'function'))
      throw new Error('loginWithWeb3AuthAvmEmail signal must be an AbortSignal.')
    if (loginOptions.onPhase != null && typeof loginOptions.onPhase !== 'function')
      throw new Error('loginWithWeb3AuthAvmEmail onPhase must be a function.')
    if (activeWeb3AuthAvmEmail)
      throw safeWeb3AuthAvmError({ code: 'REQUEST_ALREADY_PENDING' })
    if (web3AuthAvmCleanupLatched())
      throw safeWeb3AuthAvmError({ code: 'PROVIDER_CLEANUP_REQUIRED' })
    if (!qualificationMode && session?.sessionToken)
      throw safeWeb3AuthAvmError({ code: 'SESSION_CONFLICT' })
    if (loginOptions.signal?.aborted)
      throw safeWeb3AuthAvmError({ code: 'PLAYER_CANCELLED' })

    const pageOrigin = (() => {
      try {
        const value = globalThis.location?.origin
        const parsed = new URL(value)
        if (parsed.origin !== value || parsed.username || parsed.password) return ''
        if (parsed.protocol === 'https:'
          || (parsed.protocol === 'http:' && ['localhost', '127.0.0.1', '[::1]'].includes(parsed.hostname)))
          return parsed.origin
      } catch { /* unavailable */ }
      return ''
    })()
    if (!pageOrigin) throw safeWeb3AuthAvmError({ code: 'ORIGIN_NOT_ALLOWED' })
    if (globalThis.navigator?.userActivation
      && globalThis.navigator.userActivation.isActive !== true) {
      throw new BlockmakerError(
        'Start email-wallet sign-in directly from a click or tap so the secure window can open.',
        { code: 'PROVIDER_UNAVAILABLE' },
      )
    }
    if (typeof globalThis.open !== 'function' || typeof globalThis.addEventListener !== 'function')
      throw new BlockmakerError('Email-wallet sign-in requires a browser window.', { code: 'PROVIDER_UNAVAILABLE' })

    // Open synchronously while the click/tap activation is live. No wallet code
    // or credential is placed in this same-origin placeholder document.
    const popup = globalThis.open(
      'about:blank',
      '_blank',
      'popup=yes,width=520,height=720,resizable=yes,scrollbars=yes',
    )
    if (!popup) {
      throw new BlockmakerError(
        'Your browser blocked the secure email-wallet window. Allow pop-ups for this game and try again.',
        { code: 'PROVIDER_UNAVAILABLE' },
      )
    }

    const localAttemptSequence = beginLoginAttempt()
    const operation = { cancelled: false }
    activeWeb3AuthAvmEmail = operation
    let attemptId = ''
    let startSecret = ''
    let brokerOrigin = ''
    let expiresAt = 0
    let accepted = false
    let relayStarted = false
    let relayStartedAt = 0
    let brokerBound = false
    let qualification = null
    let messageHandler = null
    const onAbort = () => {
      operation.cancelled = true
      try { popup.close() } catch {}
    }
    loginOptions.signal?.addEventListener('abort', onAbort, { once: true })
    const phase = value => {
      try { loginOptions.onPhase?.(value) } catch { /* observer errors never alter authentication */ }
    }
    const sleep = delay => new Promise(resolve => globalThis.setTimeout(resolve, delay))
    const assertActive = () => {
      if (operation.cancelled || loginOptions.signal?.aborted)
        throw safeWeb3AuthAvmError({ code: 'PLAYER_CANCELLED' })
      if (localAttemptSequence !== loginAttemptSequence)
        throw safeWeb3AuthAvmError({ code: 'AUTH_SUPERSEDED' })
      if (Date.now() >= expiresAt)
        throw safeWeb3AuthAvmError({ code: 'ATTEMPT_EXPIRED' })
    }
    try {
      phase('opening_broker')
      const started = await rawRequest(qualificationMode
        ? '/v1/auth/web3auth-avm-email/qualification/start'
        : '/v1/auth/web3auth-avm-email/start', {
        method: 'POST',
        auth: false,
        body: {
          schemaVersion: qualificationMode
            ? 'blockmaker-web3auth-avm-qualification-start-request/v1'
            : 'blockmaker-web3auth-avm-start-request/v1',
          gameId,
          clientKind,
          ...(qualificationMode ? { permit: qualificationPermit } : {}),
        },
      })
      const relayUrl = (() => {
        try { return new URL(String(started?.relayUrl ?? '')) } catch { return null }
      })()
      attemptId = String(started?.attemptId ?? '')
      startSecret = String(started?.startSecret ?? '')
      expiresAt = Number(started?.expiresAt)
      qualification = qualificationMode ? started?.qualification : null
      const relayIsSafe = relayUrl
        && !relayUrl.username && !relayUrl.password
        && !relayUrl.search && !relayUrl.hash
        && relayUrl.pathname === '/web3auth-avm/relay'
        && relayUrl.origin !== pageOrigin
        && (relayUrl.protocol === 'https:'
          || (relayUrl.protocol === 'http:'
            && ['localhost', '127.0.0.1', '[::1]'].includes(relayUrl.hostname)))
      if (started?.success !== true
        || started?.schemaVersion !== (qualificationMode
          ? WEB3AUTH_AVM_QUALIFICATION_START_SCHEMA : WEB3AUTH_AVM_START_SCHEMA)
        || !WEB3AUTH_AVM_ATTEMPT_ID.test(attemptId)
        || !WEB3AUTH_AVM_START_SECRET.test(startSecret)
        || !Number.isSafeInteger(expiresAt) || expiresAt <= Date.now()
        || expiresAt > Date.now() + (qualificationMode ? 60 : 10) * 60_000 || !relayIsSafe
        || (qualificationMode && (!qualification
          || !/^[A-Za-z0-9_-]{24,80}$/.test(String(qualification.runId ?? ''))
          || !['first_login', 'returning_login', 'cancel', 'cleanup_failure', 'stale_binding']
            .includes(String(qualification.phase ?? ''))))) {
        throw safeWeb3AuthAvmError(null)
      }
      brokerOrigin = relayUrl.origin
      assertActive()

      messageHandler = event => {
        if (event.source !== popup || event.origin !== brokerOrigin
          || event.data?.attemptId !== attemptId) return
        if (event.data?.type === WEB3AUTH_AVM_RELAY_ACK && relayStarted) {
          brokerBound = true
          return
        }
        if (event.data?.type === WEB3AUTH_AVM_RELAY_READY && !relayStarted) {
          relayStarted = true
          relayStartedAt = Date.now()
          try {
            popup.postMessage({
              type: WEB3AUTH_AVM_RELAY_START,
              attemptId,
              startSecret,
            }, brokerOrigin)
          } catch {
            operation.cancelled = true
          }
        }
      }
      globalThis.addEventListener('message', messageHandler)
      relayUrl.hash = attemptId
      try { popup.location.replace(relayUrl.toString()) }
      catch { popup.location.href = relayUrl.toString() }
      phase('waiting_for_email')

      let delay = 350
      while (true) {
        assertActive()
        let completed
        try {
          completed = await rawRequest(qualificationMode
            ? '/v1/auth/web3auth-avm-email/qualification/complete'
            : '/v1/auth/web3auth-avm-email/complete', {
            method: 'POST',
            auth: false,
            timeoutMs: Math.min(timeoutMs, 8_000),
            body: {
              schemaVersion: qualificationMode
                ? 'blockmaker-web3auth-avm-qualification-complete-request/v1'
                : 'blockmaker-web3auth-avm-complete-request/v1',
              attemptId,
              startSecret,
            },
          })
        } catch (error) {
          const code = String(error?.code ?? '').trim().toUpperCase()
          const retryable = ['AUTH_PENDING', 'ATTEMPT_PENDING', 'RATE_LIMITED', 'NETWORK_ERROR', 'TIMEOUT'].includes(code)
            || Number(error?.status) >= 500
          if (!retryable) throw error
          const retryAfter = Number(error?.retryAfter)
          if (Number.isFinite(retryAfter) && retryAfter > 0)
            delay = Math.min(3_000, Math.max(delay, retryAfter * 1_000))
        }
        if (completed?.status === 'complete') {
          if (qualificationMode) {
            const result = {
              success: true,
              schemaVersion: WEB3AUTH_AVM_QUALIFICATION_RESULT_SCHEMA,
              status: 'complete',
              runId: completed.runId,
              phase: qualification.phase,
              qualificationSession: completed.qualificationSession,
              addressFingerprint: completed.addressFingerprint,
              expiresAt: completed.expiresAt,
              playerSessionIssued: false,
              authenticationOnly: true,
            }
            if (completed.success !== true
              || completed.schemaVersion !== WEB3AUTH_AVM_QUALIFICATION_COMPLETE_SCHEMA
              || completed.runId !== qualification.runId
              || !['first_login', 'returning_login'].includes(qualification.phase)
              || !/^[A-Za-z0-9_-]{43}$/.test(String(completed.qualificationSession ?? ''))
              || !/^[a-f0-9]{64}$/.test(String(completed.addressFingerprint ?? ''))
              || !Number.isSafeInteger(completed.expiresAt)
              || completed.expiresAt <= Date.now() || completed.expiresAt > expiresAt
              || completed.playerSessionIssued !== false
              || completed.authenticationOnly !== true
              || 'sessionToken' in completed || 'refreshToken' in completed
              || 'walletAddress' in completed) throw safeWeb3AuthAvmError(null)
            assertActive()
            phase('verifying')
            accepted = true
            phase('complete')
            try { popup.close() } catch {}
            return Object.freeze(result)
          }
          const printable = (value, maximum) => typeof value === 'string'
            && value.length > 0 && value.length <= maximum && /^[\x21-\x7e]+$/.test(value)
            && !/^sk_/i.test(value)
          const safe = {
            success: true,
            sessionToken: completed.sessionToken,
            refreshToken: completed.refreshToken,
            walletAddress: completed.walletAddress,
            ...(typeof completed.displayName === 'string' && completed.displayName.length <= 100
              ? { displayName: completed.displayName } : {}),
            accountKind: completed.accountKind,
            authProvider: completed.authProvider,
          }
          if (completed.success !== true || completed.schemaVersion !== WEB3AUTH_AVM_COMPLETE_SCHEMA
            || !printable(safe.sessionToken, 8_192) || !printable(safe.refreshToken, 1_024)
            || !/^[A-Z2-7]{58}$/.test(String(safe.walletAddress ?? ''))
            || safe.accountKind !== 'auth_only_email_wallet'
            || safe.authProvider !== 'web3auth_avm_email') throw safeWeb3AuthAvmError(null)
          assertActive()
          phase('verifying')
          const result = await acceptLogin(
            safe,
            localAttemptSequence,
            loginOptions[UNITY_WALLET_PACKAGE_LOGIN_TRACKER],
          )
          accepted = true
          phase('complete')
          try { popup.close() } catch {}
          return result
        }
        if (completed && (completed.success !== true
          || completed.schemaVersion !== (qualificationMode
            ? WEB3AUTH_AVM_QUALIFICATION_COMPLETE_SCHEMA : WEB3AUTH_AVM_COMPLETE_SCHEMA)
          || completed.status !== 'pending'
          || Number(completed.expiresAt) !== expiresAt)) throw safeWeb3AuthAvmError(null)
        // Check the completion endpoint before treating a just-closed popup as
        // cancellation; the broker closes itself immediately after verification.
        // Once /begin is acknowledged, a COOP-isolated run page can make this
        // stale WindowProxy report closed even while the secure popup is open.
        // Before that acknowledgement, a short delivery grace period lets a
        // genuinely closed/failed relay cancel promptly instead of waiting for
        // server expiry.
        if (!brokerBound && popup.closed
          && (!relayStarted || Date.now() - relayStartedAt >= 1_500))
          throw safeWeb3AuthAvmError({ code: 'PLAYER_CANCELLED' })
        await sleep(delay)
        delay = Math.min(3_000, Math.ceil(delay * 1.5))
      }
    } catch (error) {
      const rawCode = String(error?.code ?? '').trim().toUpperCase()
      const safeError = safeWeb3AuthAvmError(error)
      if (safeError.code === 'PROVIDER_CLEANUP_REQUIRED') latchWeb3AuthAvmCleanup()
      if (qualificationMode && qualification) {
        const expected = (qualification.phase === 'cancel'
          && ['PLAYER_CANCELLED', 'ATTEMPT_CANCELLED'].includes(rawCode))
          || (qualification.phase === 'cleanup_failure'
            && (rawCode === 'PROVIDER_CLEANUP_REQUIRED'
              || safeError.code === 'PROVIDER_CLEANUP_REQUIRED'))
          || (qualification.phase === 'stale_binding'
            && ['CONFIGURATION_CHANGED', 'QUALIFICATION_ATTEMPT_FAILED',
              'WEB3AUTH_AVM_CONFIGURATION_CHANGED'].includes(rawCode))
        if (expected) return Object.freeze({
          success: true,
          schemaVersion: WEB3AUTH_AVM_QUALIFICATION_RESULT_SCHEMA,
          status: 'expected_denial',
          runId: qualification.runId,
          phase: qualification.phase,
          playerSessionIssued: false,
          authenticationOnly: true,
        })
      }
      throw safeError
    } finally {
      if (messageHandler) globalThis.removeEventListener('message', messageHandler)
      loginOptions.signal?.removeEventListener?.('abort', onAbort)
      if (!accepted && attemptId && startSecret) {
        // Best effort. The server also expires attempts, and cancelling a
        // completion race must revoke any unclaimed refresh token it created.
        try {
          await rawRequest('/v1/auth/web3auth-avm-email/cancel', {
            method: 'POST', auth: false, timeoutMs: 1_500,
            body: {
              schemaVersion: 'blockmaker-web3auth-avm-cancel/v1',
              attemptId,
              startSecret,
            },
          })
        } catch {
          // Once the start secret reached the broker, a completion credential
          // may exist even if this page never received or accepted it. A lost
          // cancel acknowledgement cannot be treated as confirmed revocation.
          if (relayStarted || brokerBound) latchWeb3AuthAvmCleanup()
        }
      }
      try { popup.close() } catch {}
      attemptId = ''
      startSecret = ''
      brokerOrigin = ''
      if (activeWeb3AuthAvmEmail === operation) activeWeb3AuthAvmEmail = null
    }
  }

  const loginWithWeb3AuthAvmEmail = loginOptions =>
    loginWithWeb3AuthAvmEmailTransport(loginOptions, 'web')

  const qualifyWeb3AuthAvmEmail = qualificationOptions => {
    if (!qualificationOptions || typeof qualificationOptions !== 'object'
      || Array.isArray(qualificationOptions)
      || !/^[A-Za-z0-9_-]{43}$/.test(String(qualificationOptions.permit ?? ''))
      || (qualificationOptions.clientKind !== undefined
        && qualificationOptions.clientKind !== clientKind)) {
      throw new Error('qualifyWeb3AuthAvmEmail requires one valid permit bound to this exact Web or Unity WebGL client.')
    }
    return loginWithWeb3AuthAvmEmailTransport(
      qualificationOptions,
      clientKind,
      qualificationOptions.permit,
    )
  }

  const recordWeb3AuthAvmQualificationObservation = observation => {
    if (!observation || typeof observation !== 'object' || Array.isArray(observation)
      || !/^[A-Za-z0-9_-]{24,80}$/.test(String(observation.runId ?? ''))
      || !/^[A-Za-z0-9_-]{43}$/.test(String(observation.qualificationSession ?? ''))
      || !['mfa_recovery_observed', 'storage_surface_observed',
        'page_close_cleanup_observed', 'provider_endpoints_observed']
        .includes(String(observation.kind ?? ''))
      || !observation.evidence || typeof observation.evidence !== 'object'
      || Array.isArray(observation.evidence)) {
      throw new Error('The Web3Auth AVM qualification observation is invalid.')
    }
    return rawRequest('/v1/auth/web3auth-avm-email/qualification/observation', {
      method: 'POST',
      auth: false,
      body: {
        schemaVersion: 'blockmaker-web3auth-avm-qualification-observation/v1',
        runId: observation.runId,
        qualificationSession: observation.qualificationSession,
        kind: observation.kind,
        evidence: observation.evidence,
      },
    })
  }

  const installWeb3AuthAvmEmailUnityWebGlBridge = () => {
    if (web3AuthAvmCleanupLatched()) {
      throw safeWeb3AuthAvmError({ code: 'PROVIDER_CLEANUP_REQUIRED' })
    }
    const root = globalThis
    const bridgeGlobal = '__blockmakerWeb3AuthAvmEmailUnityWebGlV1'
    if (root[bridgeGlobal])
      throw safeWeb3AuthAvmError({ code: 'REQUEST_ALREADY_PENDING' })
    let active = null
    let disposed = false
    let operationSequence = 0
    const ownedRefreshTokens = new Set()
    if (isAuthenticationOnlyEmailSession(session)) {
      const persistedRefreshToken = String(session?.refreshToken ?? '')
      if (persistedRefreshToken.length < 1 || persistedRefreshToken.length > 1_024
        || !/^[\x21-\x7e]+$/.test(persistedRefreshToken)
        || /^sk_/i.test(persistedRefreshToken)) {
        latchWeb3AuthAvmCleanup()
        throw safeWeb3AuthAvmError({ code: 'PROVIDER_CLEANUP_REQUIRED' })
      }
      // This client restored the session from its game-scoped storage. Treat
      // that exact current email family as bridge-owned so Unity can sign out
      // safely after a page reload. Pera/Lute sessions are never adopted.
      ownedRefreshTokens.add(persistedRefreshToken)
    }
    const publicSession = result => Object.freeze({
      schemaVersion: 'blockmaker-unity-webgl-session/v1',
      sessionToken: result.sessionToken,
      refreshToken: result.refreshToken,
      walletAddress: result.walletAddress,
      accountKind: 'auth_only_email_wallet',
      authProvider: 'web3auth_avm_email',
    })
    const revokeOwnedSession = async acceptedSession => {
      const refreshToken = String(acceptedSession?.refreshToken ?? '')
      if (acceptedSession?.accountKind !== 'auth_only_email_wallet'
        || acceptedSession?.authProvider !== 'web3auth_avm_email'
        || refreshToken.length < 1 || refreshToken.length > 1_024
        || !/^[\x21-\x7e]+$/.test(refreshToken) || /^sk_/i.test(refreshToken)
        || !ownedRefreshTokens.has(refreshToken)) {
        latchWeb3AuthAvmCleanup()
        throw safeWeb3AuthAvmError({ code: 'PROVIDER_CLEANUP_REQUIRED' })
      }
      try {
        if (session?.refreshToken === refreshToken && isAuthenticationOnlyEmailSession(session)) {
          await logoutPlayer({ reportRevocationFailure: true })
        } else {
          // A newer Pera/Lute (or email) login owns the page session. Revoke
          // only the stale bridge result rather than clearing that newer login.
          await rawRequest('/v1/auth/logout', {
            method: 'POST',
            auth: false,
            body: { refreshToken, gameId },
          })
        }
        ownedRefreshTokens.delete(refreshToken)
      } catch {
        latchWeb3AuthAvmCleanup()
        throw safeWeb3AuthAvmError({ code: 'PROVIDER_CLEANUP_REQUIRED' })
      }
    }
    const begin = async () => {
      if (disposed) throw safeWeb3AuthAvmError({ code: 'PROVIDER_UNAVAILABLE' })
      if (active) throw safeWeb3AuthAvmError({ code: 'REQUEST_ALREADY_PENDING' })
      const sequence = ++operationSequence
      const controller = new AbortController()
      const operation = { controller, sequence }
      active = operation
      try {
        const result = await loginWithWeb3AuthAvmEmailTransport(
          { signal: controller.signal },
          'unity_webgl',
        )
        const acceptedSession = publicSession(result)
        ownedRefreshTokens.add(acceptedSession.refreshToken)
        if (disposed || active !== operation || controller.signal.aborted
          || sequence !== operationSequence) {
          try { await revokeOwnedSession(acceptedSession) } catch {}
          throw safeWeb3AuthAvmError({ code: 'AUTH_SUPERSEDED' })
        }
        return acceptedSession
      } finally {
        if (active === operation) active = null
      }
    }
    const cancel = () => {
      operationSequence += 1
      const pending = active
      active = null
      pending?.controller.abort()
    }
    const logout = async acceptedSession => {
      cancel()
      if (acceptedSession !== undefined) {
        await revokeOwnedSession(acceptedSession)
        return
      }
      for (const refreshToken of [...ownedRefreshTokens]) {
        await revokeOwnedSession({
          refreshToken,
          accountKind: 'auth_only_email_wallet',
          authProvider: 'web3auth_avm_email',
        })
      }
    }
    const facade = Object.freeze({ begin, cancel, logout })
    Object.defineProperty(root, bridgeGlobal, {
      value: facade,
      configurable: true,
      enumerable: false,
      writable: false,
    })
    return Object.freeze({
      schemaVersion: 'blockmaker-unity-webgl-bridge/v1',
      globalName: bridgeGlobal,
      cancel,
      logout,
      async dispose() {
        if (disposed) return
        disposed = true
        cancel()
        if (root[bridgeGlobal] === facade) {
          try { delete root[bridgeGlobal] } catch {}
        }
        await logout()
      },
    })
  }

  const loginWithMagicEmail = async (magic, email) => {
    const normalizedEmail = String(email ?? '').trim().toLowerCase()
    if (!normalizedEmail || !normalizedEmail.includes('@'))
      throw new Error('Enter a valid email address.')
    const login = magic?.auth?.loginWithEmailOTP
    if (typeof login !== 'function')
      throw new Error('loginWithMagicEmail requires a configured Magic browser instance with the Algorand extension.')
    await providerPolicyCheck(null)
    const attemptId = beginLoginAttempt()
    const didToken = await login.call(magic.auth, { email: normalizedEmail })
    if (typeof didToken !== 'string' || !didToken.trim())
      throw new BlockmakerError('Magic sign-in did not return a verifiable session.', { code: 'AUTH_INVALID' })
    return acceptLogin(await request('/v1/auth/magic/verify', {
      method: 'POST', auth: false, body: { didToken, email: normalizedEmail, gameId },
    }), attemptId)
  }

  // Magic email uses the shared xChain challenge path.
  const loginWithMagicEmailXChain = async (magic, email) => {
    const normalizedEmail = String(email ?? '').trim().toLowerCase()
    if (!normalizedEmail || !normalizedEmail.includes('@'))
      throw new Error('Enter a valid email address.')
    const login = magic?.auth?.loginWithEmailOTP
    const provider = magic?.rpcProvider
    if (typeof login !== 'function' || typeof provider?.request !== 'function')
      throw new Error('Magic email over xChain requires a configured Magic instance with auth and rpcProvider.request().')
    await providerPolicyCheck('magic_email')
    let didToken
    try { didToken = await login.call(magic.auth, { email: normalizedEmail }) }
    catch (error) { throw evmProviderFailure(error, 'Magic email', 'connect') }
    if (typeof didToken !== 'string' || !didToken.trim())
      throw new BlockmakerError('Magic email sign-in did not return a verifiable session.', { code: 'AUTH_INVALID' })
    return loginWithEvmWallet(provider, {
      walletName: 'Magic email',
      providerId: 'magic_email',
      providerPrechecked: true,
    })
  }

  const preparedAlgorandWalletLogins = new WeakSet()

  // Prepare the exact server-authored sign-in proof without opening a signing
  // prompt. Unity's Lute path uses this boundary to preserve a final, direct
  // player click for the wallet popup.
  const prepareAlgorandWalletLogin = async (wallet, algosdk, loginOptions = {}) => {
    const connect = wallet?.connect
    const signTransactions = wallet?.signTransactions
    const signTransaction = wallet?.signTransaction
    const rawWalletName = String(loginOptions.walletName ?? wallet?.metadata?.name ?? wallet?.name ?? 'Algorand wallet').trim()
    const walletName = rawWalletName && rawWalletName.length <= 48 ? rawWalletName : 'Algorand wallet'
    if (typeof connect !== 'function' || (typeof signTransactions !== 'function' && typeof signTransaction !== 'function'))
      throw new Error('loginWithAlgorandWallet requires a wallet adapter with connect() and transaction signing.')
    const decodeUnsignedTransaction = algosdk?.decodeUnsignedTransaction
    const encodeAddress = algosdk?.encodeAddress
    if (typeof decodeUnsignedTransaction !== 'function' || typeof encodeAddress !== 'function')
      throw new Error('loginWithAlgorandWallet requires the official algosdk package so the wallet receives an Algorand Transaction object.')
    const providerCandidate = String(
      loginOptions.providerId ?? wallet?.providerId ?? wallet?.metadata?.providerId ?? '',
    ).trim().toLowerCase()
    const providerId = loginOptions.providerPrechecked === true
      && WALLET_PROVIDER_IDS.has(providerCandidate)
      && providerCandidate !== 'web3auth_avm_email'
      ? providerCandidate
      : (await providerPolicyCheck(providerCandidate)).providerId
    const attemptId = beginLoginAttempt()

    const accountAddress = value => String(typeof value === 'string' ? value : value?.address ?? '').trim().toUpperCase()
    // TxnLab restores only a public address hint. Reconnect Web3Auth from this
    // explicit player action before trusting that hint or requesting a proof;
    // otherwise an expired identity session would not reopen until signing.
    const forceInteractiveConnect = providerId === 'txnlab_web3auth'
    let accounts
    if (loginOptions.connectImmediately === true) {
      // This call is deliberately made before the first promise hop when the
      // provider policy was already checked by the reviewed Unity host.
      const connection = connect.call(wallet)
      accounts = await connection
    } else {
      accounts = forceInteractiveConnect
        ? await connect.call(wallet)
        : Array.isArray(wallet?.accounts) ? wallet.accounts : []
    }
    if (!forceInteractiveConnect
      && (!Array.isArray(accounts) || accounts.length === 0)
      && typeof wallet?.reconnectSession === 'function') {
      try { accounts = await wallet.reconnectSession() }
      catch { accounts = [] }
    }
    if (!forceInteractiveConnect
      && (!Array.isArray(accounts) || accounts.length === 0)
      && typeof wallet?.resumeSession === 'function') {
      try {
        const resumed = await wallet.resumeSession()
        accounts = Array.isArray(resumed) ? resumed : Array.isArray(wallet?.accounts) ? wallet.accounts : []
      } catch { accounts = [] }
    }
    if (!forceInteractiveConnect && (!Array.isArray(accounts) || accounts.length === 0))
      accounts = await connect.call(wallet)
    const walletAddress = Array.isArray(accounts)
      ? accounts.map(accountAddress).find(isAlgorandAddressShape)
      : ''
    if (!walletAddress)
      throw new BlockmakerError(`No Algorand account was selected in ${walletName}.`, { code: 'WALLET_ACCOUNT_MISSING' })

    const challenge = await request('/v1/auth/wallet/challenge', {
      method: 'POST', auth: false,
      body: { walletAddress, chain: 'algorand', gameId, ...(providerId ? { providerId } : {}) },
    })
    if (!challenge?.nonce)
      throw new BlockmakerError(`Could not start secure ${walletName} sign-in.`, { code: 'AUTH_INVALID' })
    let proofNote
    if (challenge.challengeVersion === PLAYER_WALLET_AUTH_V3.challengeVersion) {
      proofNote = validatePlayerWalletAuthV3Challenge(challenge, {
        gameId,
        providerId,
        clientKind,
        walletAddress,
      })
    } else {
      if (challenge.challengeVersion !== undefined
        && challenge.challengeVersion !== 1 && challenge.challengeVersion !== 2) {
        throw new BlockmakerError(`Could not start secure ${walletName} sign-in.`, { code: 'AUTH_INVALID' })
      }
      proofNote = String(challenge.nonce)
    }

    const built = await request('/v1/transactions/build', {
      method: 'POST', auth: false,
      body: {
        type: 'payment', recipient: walletAddress, amount: 0,
        note: proofNote, walletAddress,
        gameId, providerId,
        ...(challenge.challengeVersion === 2 && (providerId === 'pera' || providerId === 'lute')
          ? { purpose: 'player-sign-in' } : {}),
      },
    })
    const unsigned = bytesFromBase64(built?.unsignedTxnBase64)
    let transaction
    try { transaction = decodeUnsignedTransaction.call(algosdk, unsigned) }
    catch { throw new BlockmakerError('Blockmaker returned an invalid wallet sign-in transaction.', { code: 'TX_INVALID' }) }
    if (!transaction)
      throw new BlockmakerError('Blockmaker returned an invalid wallet sign-in transaction.', { code: 'TX_INVALID' })

    // Verify the exact zero-ALGO nonce proof before the wallet opens.
    let sender = ''
    let receiver = ''
    let note = ''
    try {
      const addressString = value => {
        const direct = typeof value?.toString === 'function' ? String(value.toString()).trim() : ''
        return isAlgorandAddressShape(direct) ? direct : encodeAddress.call(algosdk, value?.publicKey)
      }
      sender = addressString(transaction.sender ?? transaction.from)
      receiver = addressString(transaction.payment?.receiver ?? transaction.to)
      note = new TextDecoder().decode(transaction.note ?? new Uint8Array())
    } catch {
      throw new BlockmakerError('Blockmaker returned an invalid wallet sign-in transaction.', { code: 'TX_INVALID' })
    }
    const amount = Number(transaction.payment?.amount ?? transaction.amount ?? 0)
    const fee = Number(transaction.fee)
    const transactionGenesisId = String(transaction.genesisID ?? transaction.genesisId ?? '')
    let transactionGenesisHash = ''
    try {
      const hash = transaction.genesisHash
      if (hash instanceof Uint8Array && hash.byteLength === 32) {
        let binary = ''
        for (const byte of hash) binary += String.fromCharCode(byte)
        transactionGenesisHash = globalThis.btoa(binary)
      }
    } catch { /* the v3 network check below fails closed */ }
    const strongNetworkMismatch = challenge.challengeVersion === PLAYER_WALLET_AUTH_V3.challengeVersion
      && (transactionGenesisId !== PLAYER_WALLET_AUTH_V3.genesisId
        || transactionGenesisHash !== PLAYER_WALLET_AUTH_V3.genesisHash
        || challenge.expiresAt <= Date.now())
    const unsafe = transaction.type !== 'pay'
      || sender !== walletAddress || receiver !== walletAddress
      || !Number.isFinite(amount) || amount !== 0
      || note !== proofNote
      || strongNetworkMismatch
      || !!transaction.rekeyTo || !!transaction.reKeyTo
      || !!transaction.payment?.closeRemainderTo || !!transaction.closeRemainderTo
      || !!transaction.lease?.byteLength
      || !!transaction.group?.byteLength
      || !Number.isSafeInteger(fee) || fee < 0 || fee > 1_000
    if (unsafe)
      throw new BlockmakerError('Blockmaker refused an unsafe wallet sign-in transaction. Nothing was signed.', { code: 'TX_UNSAFE' })

    const prepared = Object.freeze({
      wallet,
      algosdk,
      walletName,
      providerId,
      attemptId,
      walletAddress,
      accounts: Object.freeze([...new Set(accounts.map(accountAddress).filter(isAlgorandAddressShape))]),
      transaction,
      signTransactions,
      signTransaction,
      nonce: challenge.nonce,
      cleanupContext: loginOptions[UNITY_WALLET_PACKAGE_LOGIN_TRACKER] ?? null,
      onProgress: loginOptions.onProgress,
    })
    preparedAlgorandWalletLogins.add(prepared)
    return prepared
  }

  const completePreparedAlgorandWalletLogin = prepared => {
    if (!preparedAlgorandWalletLogins.has(prepared) || prepared.attemptId !== loginAttemptSequence) {
      return Promise.reject(new BlockmakerError('This wallet sign-in approval is no longer current.', {
        code: 'AUTH_SUPERSEDED',
      }))
    }
    preparedAlgorandWalletLogins.delete(prepared)
    let signing
    try {
      // Do not place an await, fetch, dynamic import or promise callback before
      // this call. No signer/auth-address hint is supplied, so Pera can choose
      // the current authorizer for a sender-present rekeyed account.
      signing = typeof prepared.signTransactions === 'function'
        ? prepared.signTransactions.call(prepared.wallet, [prepared.transaction], [0])
        : prepared.signTransaction.call(prepared.wallet, [[{ txn: prepared.transaction }]])
    } catch (error) {
      return Promise.reject(error)
    }
    return Promise.resolve(signing).then(async signed => {
      if (prepared.attemptId !== loginAttemptSequence)
        throw new BlockmakerError('This wallet sign-in was cancelled.', { code: 'AUTH_SUPERSEDED' })
      const signedTxn = Array.isArray(signed)
        ? signed.find(value => value instanceof Uint8Array && value.byteLength > 0)
        : null
      if (!signedTxn) {
        throw new BlockmakerError(`${prepared.walletName} sign-in was not approved. Please try again.`, {
          code: 'WALLET_SIGNATURE_MISSING',
        })
      }
      try { prepared.onProgress?.({ phase: 'verifying' }) } catch { /* presentation only */ }
      const result = await acceptLogin(await request('/v1/auth/wallet/verify', {
        method: 'POST', auth: false,
        body: {
          walletAddress: prepared.walletAddress,
          chain: 'algorand',
          signedTxn: base64FromBytes(signedTxn),
          nonce: prepared.nonce,
          gameId,
          ...(prepared.providerId ? { providerId: prepared.providerId } : {}),
        },
      }), prepared.attemptId, prepared.cleanupContext)
      // Remember only the selected public accounts and provider-neutral facade.
      rememberedAlgorandSigner = {
        wallet: prepared.wallet,
        algosdk: prepared.algosdk,
        walletAddress: prepared.walletAddress,
        providerId: prepared.providerId,
        accounts: prepared.accounts,
      }
      return result
    })
  }

  const discardPreparedAlgorandWalletLogin = prepared => {
    preparedAlgorandWalletLogins.delete(prepared)
  }

  // All Algorand adapters use one nonce/self-payment proof path.
  const loginWithAlgorandWallet = async (wallet, algosdk, loginOptions = {}) => {
    const prepared = await prepareAlgorandWalletLogin(wallet, algosdk, loginOptions)
    return completePreparedAlgorandWalletLogin(prepared)
  }

  // Backward-compatible provider-neutral shorthand.
  const loginWithPera = async (pera, algosdk) => {
    if (typeof pera?.connect !== 'function' || typeof pera?.signTransaction !== 'function')
      throw new Error('loginWithPera requires a configured PeraWalletConnect instance.')
    if (typeof algosdk?.decodeUnsignedTransaction !== 'function' || typeof algosdk?.encodeAddress !== 'function')
      throw new Error('loginWithPera requires the official algosdk package so Pera receives an Algorand Transaction object.')
    return loginWithAlgorandWallet(pera, algosdk, { walletName: 'Pera Wallet', providerId: 'pera' })
  }

  // EIP-1193 providers sign only the tenant-bound challenge.
  const evmProviderFailure = (error, walletName, phase) => {
    const providerCode = Number(error?.code)
    if (providerCode === -32002) {
      return new BlockmakerError(`A request is already waiting in ${walletName}. Open the wallet to continue or reject it, then try again.`, {
        code: 'WALLET_REQUEST_PENDING', details: error,
      })
    }
    if (phase === 'connect') {
      return new BlockmakerError(`${walletName} connection was not approved. Please try again.`, {
        code: providerCode === 4001 ? 'WALLET_REQUEST_REJECTED' : 'WALLET_CONNECTION_FAILED',
        details: error,
      })
    }
    return new BlockmakerError(`${walletName} sign-in was not approved. Please try again.`, {
      code: providerCode === 4001 ? 'WALLET_REQUEST_REJECTED' : 'WALLET_SIGNATURE_MISSING',
      details: error,
    })
  }

  const loginWithEvmWallet = async (wallet, loginOptions = {}) => {
    const providerRequest = wallet?.request
    const rawWalletName = String(loginOptions.walletName ?? 'EVM wallet').trim()
    const walletName = rawWalletName && rawWalletName.length <= 48 ? rawWalletName : 'EVM wallet'
    if (typeof providerRequest !== 'function')
      throw new Error('loginWithEvmWallet requires an injected EIP-1193 provider with request().')
    const providerId = String(loginOptions.providerId ?? 'xchain').trim().toLowerCase()
    if (loginOptions.providerPrechecked !== true) await providerPolicyCheck(providerId)
    const attemptId = beginLoginAttempt()

    let accounts
    try { accounts = await providerRequest.call(wallet, { method: 'eth_requestAccounts' }) }
    catch (error) { throw evmProviderFailure(error, walletName, 'connect') }
    const requestedEvmAddress = Array.isArray(accounts)
      ? accounts.map(value => String(value ?? '').trim()).find(isEvmAddressShape)
      : ''
    if (!requestedEvmAddress)
      throw new BlockmakerError(`No EVM account was selected in ${walletName}.`, { code: 'WALLET_ACCOUNT_MISSING' })

    const challenge = await request('/v1/auth/wallet/challenge', {
      method: 'POST', auth: false,
      body: { chain: 'evm', evmAddress: requestedEvmAddress, gameId, providerId },
    })
    const walletAddress = String(challenge?.walletAddress ?? '').trim().toUpperCase()
    const evmAddress = String(challenge?.evmAddress ?? '').trim()
    const message = String(challenge?.message ?? '')
    const nonce = String(challenge?.nonce ?? '')
    const expiresAt = Number(challenge?.expiresAt)
    const now = Date.now()
    let expires = ''
    try { if (Number.isFinite(expiresAt)) expires = new Date(expiresAt).toISOString() }
    catch { /* an invalid server timestamp must fail closed below */ }
    const expectedMessage = `Blockmaker player sign-in\ndeveloper: ${gameId}\nprovider: ${providerId}\nalgorand: ${walletAddress}\nevm: ${evmAddress}\nnonce: ${nonce}\nexpires: ${expires}`
    const challengeIsValid = challenge?.challengeVersion === 2
      && challenge?.purpose === 'player-sign-in'
      && challenge?.chain === 'evm'
      && challenge?.gameId === gameId
      && challenge?.providerId === providerId
      && isAlgorandAddressShape(walletAddress)
      && isEvmAddressShape(evmAddress)
      && evmAddress === evmAddress.toLowerCase()
      && evmAddress.toLowerCase() === requestedEvmAddress.toLowerCase()
      && /^[A-Za-z0-9_-]{32}$/.test(nonce)
      && Number.isFinite(expiresAt)
      && expiresAt > now
      && expiresAt <= now + 10 * 60_000
      && message.length <= 1_000
      && message === expectedMessage
    if (!challengeIsValid)
      throw new BlockmakerError('Blockmaker returned an invalid EVM sign-in challenge. Nothing was signed.', { code: 'AUTH_INVALID' })

    let signature
    try {
      signature = await providerRequest.call(wallet, {
        method: 'personal_sign',
        params: [message, evmAddress],
      })
    } catch (error) { throw evmProviderFailure(error, walletName, 'sign') }
    const signedMessage = String(signature ?? '').trim()
    if (!/^0x[0-9a-fA-F]{130}$/.test(signedMessage))
      throw new BlockmakerError(`${walletName} did not return a valid sign-in signature.`, { code: 'WALLET_SIGNATURE_MISSING' })

    const verified = await request('/v1/auth/wallet/verify', {
      method: 'POST', auth: false,
      body: { walletAddress, chain: 'evm', evmAddress, signature: signedMessage, nonce, gameId, providerId },
    })
    const verifiedWalletAddress = String(verified?.walletAddress ?? '').trim()
    const explicitAccountKind = verified?.accountKind
    if (verifiedWalletAddress !== walletAddress
      || (explicitAccountKind !== undefined && explicitAccountKind !== null && explicitAccountKind !== 'evm_wallet'))
      throw new BlockmakerError('Blockmaker returned an invalid EVM sign-in result. The player session was not saved.', { code: 'AUTH_INVALID' })
    return acceptLogin(verified, attemptId)
  }

  const normalizeAdapterAccounts = values => Array.isArray(values)
    ? [...new Set(values.map(value => String(typeof value === 'string' ? value : value?.address ?? '').trim().toUpperCase())
      .filter(isAlgorandAddressShape))].slice(0, 10)
    : []

  const peraWalletAdapter = (pera, adapterOptions = {}) => {
    if (typeof pera?.connect !== 'function' || typeof pera?.signTransaction !== 'function')
      throw new Error('wallets.pera requires a PeraWalletConnect instance.')
    const requestedGenesisId = String(adapterOptions.genesisId ?? '').trim()
    if (requestedGenesisId && !/^[A-Za-z0-9][A-Za-z0-9._-]{2,63}$/.test(requestedGenesisId))
      throw new Error("wallets.pera genesisId must be exact, such as 'mainnet-v1.0' or 'testnet-v1.0'.")
    const peraChainGenesisIds = Object.freeze({
      416001: 'mainnet-v1.0',
      416002: 'testnet-v1.0',
      416003: 'betanet-v1.0',
    })
    const connectorGenesisId = peraChainGenesisIds[Number(pera.chainId)] ?? ''
    if (requestedGenesisId && connectorGenesisId && requestedGenesisId !== connectorGenesisId)
      throw new Error(`wallets.pera was configured for ${connectorGenesisId}, not ${requestedGenesisId}.`)
    // chainId 4160 is Pera's explicit all-networks mode. In that mode the
    // caller-provided genesis binding remains authoritative and every prepared
    // transaction is still decoded and checked before Pera opens.
    const genesisId = requestedGenesisId || connectorGenesisId
    let accounts = []
    let connecting = null
    let signing = false
    const connect = async (resume = false) => {
      if (connecting) return connecting
      connecting = (async () => {
        const values = resume && typeof pera.reconnectSession === 'function'
          ? await pera.reconnectSession()
          : await pera.connect()
        accounts = normalizeAdapterAccounts(values)
        return [...accounts]
      })()
      try { return await connecting }
      finally { connecting = null }
    }
    return Object.freeze({
      name: 'Pera Wallet',
      providerId: 'pera',
      ...(genesisId ? { networkGenesisId: genesisId } : {}),
      metadata: Object.freeze({
        name: 'Pera Wallet', providerId: 'pera',
        ...(genesisId ? { networkGenesisId: genesisId } : {}),
      }),
      get accounts() { return [...accounts] },
      connect: () => connect(false),
      resumeSession: () => connect(true),
      disconnect: async () => {
        accounts = []
        if (typeof pera.disconnect === 'function') await pera.disconnect()
      },
      signTransaction: async groups => {
        if (signing)
          throw new BlockmakerError('A request is already waiting in Pera Wallet. Open the wallet to continue or reject it, then try again.', { code: 'WALLET_REQUEST_PENDING' })
        signing = true
        try { return await pera.signTransaction(groups) }
        finally { signing = false }
      },
    })
  }

  const luteWalletAdapter = (lute, adapterOptions = {}) => {
    if (typeof lute?.connect !== 'function' || typeof lute?.signTxns !== 'function')
      throw new Error('wallets.lute requires a LuteConnect instance.')
    const genesisId = String(adapterOptions.genesisId ?? '').trim()
    if (!/^[A-Za-z0-9][A-Za-z0-9._-]{2,63}$/.test(genesisId))
      throw new Error("wallets.lute requires the exact Algorand genesisId, such as 'mainnet-v1.0'.")
    const accountStorageKey = `blockmaker:${gameId}:wallet:lute:${genesisId}:accounts`
    let accounts = []
    let connecting = null
    let signing = false
    try { accounts = normalizeAdapterAccounts(JSON.parse(storage.getItem(accountStorageKey) || '[]')) }
    catch {
      accounts = []
      try { storage.removeItem(accountStorageKey) } catch { /* storage may be unavailable */ }
    }
    const saveAccounts = values => {
      accounts = normalizeAdapterAccounts(values)
      try {
        if (accounts.length) storage.setItem(accountStorageKey, JSON.stringify(accounts))
        else storage.removeItem(accountStorageKey)
      } catch { /* public-address caching is optional */ }
      return [...accounts]
    }
    const connect = async () => {
      if (connecting) return connecting
      connecting = Promise.resolve(lute.connect(genesisId)).then(saveAccounts)
      try { return await connecting }
      catch (error) {
        const message = String(error?.message ?? '')
        if (/pop.?up|window.{0,24}block|block.{0,24}window/i.test(message)) {
          throw new BlockmakerError('Your browser blocked the Lute Wallet window. Allow pop-ups for this game, then try again.', {
            code: 'PROVIDER_UNAVAILABLE',
          })
        }
        throw error
      }
      finally { connecting = null }
    }
    return Object.freeze({
      name: 'Lute Wallet',
      providerId: 'lute',
      networkGenesisId: genesisId,
      metadata: Object.freeze({ name: 'Lute Wallet', providerId: 'lute', networkGenesisId: genesisId }),
      get accounts() { return [...accounts] },
      connect,
      // Lute intentionally has no background reconnect API. Remembering only
      // public addresses avoids an unnecessary popup; the next proof still has
      // to be approved by Lute and verified by Blockmaker.
      resumeSession: async () => [...accounts],
      disconnect: async () => {
        saveAccounts([])
        if (typeof lute.disconnect === 'function') await lute.disconnect()
      },
      signTransactions: async (transactions, indexesToSign = []) => {
        if (!Array.isArray(transactions) || transactions.length < 1 || transactions.length > 50)
          throw new Error('Lute can sign at most 50 reviewed Algorand transactions at once.')
        if (signing)
          throw new BlockmakerError('A request is already waiting in Lute Wallet. Open the wallet to continue or reject it, then try again.', { code: 'WALLET_REQUEST_PENDING' })
        const requested = new Set(Array.isArray(indexesToSign) ? indexesToSign : [])
        if ([...requested].some(index => !Number.isInteger(index) || index < 0 || index >= transactions.length))
          throw new Error('Lute received an invalid transaction signing index.')
        const walletTransactions = transactions.map((transaction, index) => {
          if (typeof transaction?.toByte !== 'function')
            throw new BlockmakerError('Lute could not encode the reviewed Algorand transaction.', { code: 'TX_INVALID' })
          const entry = { txn: base64FromBytes(transaction.toByte()) }
          if (requested.size && !requested.has(index)) entry.signers = []
          return entry
        })
        signing = true
        try { return await lute.signTxns(walletTransactions) }
        finally { signing = false }
      },
    })
  }

  // Normalize official clients; tenant policy remains authoritative.
  const algorandWalletSetup = (setupOptions = {}) => {
    if (!setupOptions || typeof setupOptions !== 'object' || Array.isArray(setupOptions))
      throw new Error('wallets.algorand requires an options object.')
    const algosdk = setupOptions.algosdk
    if (typeof algosdk?.decodeUnsignedTransaction !== 'function'
      || typeof algosdk?.encodeAddress !== 'function')
      throw new Error('wallets.algorand requires the official algosdk package.')
    const algorandWallets = []
    if (setupOptions.pera) {
      algorandWallets.push(Object.freeze({
        id: 'pera', providerId: 'pera', name: 'Pera Wallet',
        wallet: peraWalletAdapter(setupOptions.pera, { genesisId: setupOptions.genesisId }), featured: true,
      }))
    }
    if (setupOptions.lute) {
      algorandWallets.push(Object.freeze({
        id: 'lute', providerId: 'lute', name: 'Lute Wallet',
        wallet: luteWalletAdapter(setupOptions.lute, { genesisId: setupOptions.genesisId }),
      }))
    }
    if (setupOptions.txnLabWeb3Auth) {
      const wallet = setupOptions.txnLabWeb3Auth
      if (typeof wallet?.connect !== 'function'
        || (typeof wallet?.signTransactions !== 'function'
          && typeof wallet?.signTransaction !== 'function')) {
        throw new Error('wallets.algorand txnLabWeb3Auth must be a connected TxnLab-compatible Algorand wallet adapter.')
      }
      algorandWallets.push(Object.freeze({
        id: 'txnlab-web3auth', providerId: 'txnlab_web3auth', name: 'Email or Google',
        wallet, featured: true,
      }))
    }
    if (!algorandWallets.length)
      throw new Error('wallets.algorand needs at least one Pera, Lute, or TxnLab Web3Auth client.')
    return Object.freeze({
      algorandWallets: Object.freeze(algorandWallets),
      algosdk,
      ...(setupOptions.txnLabWeb3Auth
        ? { txnLabWeb3Auth: setupOptions.txnLabWeb3Auth }
        : {}),
    })
  }

  const gameProfileGet = () => request('/v1/game-profile')
  const universalUsernameInput = value => {
    const username = String(value ?? '').trim()
    if (!username) throw new Error('A username is required.')
    if (username.length > 20) throw new Error('Username is too long.')
    return username
  }
  const checkUniversalUsername = username => request('/v1/profile/username/check', {
    method: 'POST', body: { username: universalUsernameInput(username) },
  })
  const prepareUniversalUsername = username => request('/v1/profile/username/claim/prepare', {
    method: 'POST', body: { username: universalUsernameInput(username) },
  })
  const getUniversalUsernames = () => request('/v1/profile/username/names')
  const setPrimaryUniversalUsername = username => request('/v1/profile/username/primary', {
    method: 'POST', body: { username: universalUsernameInput(username) },
  })
  const setGameUsername = async () => {
    throw new BlockmakerError(
      'Usernames are universal and owner-approved on Algorand. Use profile.registerUniversalUsername() or profile.open() so the player can review and approve the exact price.',
      { code: 'UNIVERSAL_USERNAME_REGISTRATION_REQUIRED' },
    )
  }
  const clearGameUsername = async () => {
    throw new BlockmakerError(
      'A game cannot remove a player’s universal username. The player can choose another name as their primary identity.',
      { code: 'UNIVERSAL_USERNAME_GLOBAL' },
    )
  }
  // Backwards-compatible read/check names. They now use the one universal
  // identity registry; no game-local username can be created through the SDK.
  const checkGameUsername = checkUniversalUsername
  const getGameProfiles = walletAddresses => {
    if (!Array.isArray(walletAddresses))
      throw new Error('profile.getGameProfiles requires an array of Algorand wallet addresses.')
    const unique = [...new Set(walletAddresses.map(value => String(value ?? '').trim().toUpperCase()))]
    if (unique.length > 100 || unique.some(address => !isAlgorandAddressShape(address)))
      throw new Error('profile.getGameProfiles accepts up to 100 valid Algorand wallet addresses.')
    return request('/v1/game-profile/batch', { method: 'POST', body: { walletAddresses: unique } })
  }
  const searchNfts = (search = '', options = {}) => {
    const cursor = String(options?.cursor ?? '').trim()
    if (cursor.length > 512) throw new Error('profile.searchNfts received an invalid result cursor. Start the search again.')
    if (options?.limit !== undefined && (!Number.isSafeInteger(options.limit) || options.limit < 1 || options.limit > 50))
      throw new Error('profile.searchNfts limit must be a whole number from 1 to 50.')
    return request('/v1/profile/wallet/nfts', {
      method: 'POST',
      body: {
        search: String(search ?? '').trim(),
        ...(cursor ? { cursor } : {}),
        ...(options?.limit !== undefined ? { limit: Number(options.limit) } : {}),
      },
      timeoutMs: 35_000,
    })
  }
  const validNftAssetId = value => {
    const assetId = Number(value)
    if (!Number.isSafeInteger(assetId) || assetId <= 0)
      throw new Error('A positive NFT asset ID is required.')
    return assetId
  }
  const previewNft = assetId => request('/v1/profile/nft/image', {
    method: 'POST', body: { assetId: validNftAssetId(assetId) },
  })
  const setGameProfileNft = assetId => request('/v1/game-profile/pfp', {
    method: 'PUT', body: { assetId: validNftAssetId(assetId) },
  })
  const clearGameProfileNft = () => request('/v1/game-profile/pfp', { method: 'DELETE' })
  const refreshGameProfileNft = () => request('/v1/game-profile/pfp/refresh', { method: 'POST' })

  const verifyEmail = async (email, otp) => {
    const attemptId = beginLoginAttempt()
    return acceptLogin(await request('/v1/auth/email/verify', {
      method: 'POST', auth: false, body: { email, otp, gameId },
    }), attemptId)
  }
  const verifyMagic = async (didToken, email) => {
    const attemptId = beginLoginAttempt()
    return acceptLogin(await request('/v1/auth/magic/verify', {
      method: 'POST', auth: false, body: { didToken, email, gameId },
    }), attemptId)
  }
  const walletVerify = async input => {
    const attemptId = beginLoginAttempt()
    return acceptLogin(await request('/v1/auth/wallet/verify', {
      method: 'POST', auth: false, body: { ...input, gameId },
    }), attemptId)
  }

  const preflight = async () => {
    const config = await request('/v1/integrations/config', { auth: false })
    const methods = Object.entries(config.auth ?? {}).filter(([, available]) => available === true).map(([method]) => method)
    const checks = [
      { id: 'game', required: true, ok: config.game?.id === gameId, message: config.game?.id === gameId ? `Connected to ${config.game.name}.` : 'The returned game does not match this gameId.' },
      { id: 'player-auth', required: true, ok: methods.length > 0, message: methods.length > 0 ? `Player login available: ${methods.join(', ')}.` : 'No player login method is configured on this deployment.' },
      {
        id: 'browser-origin', required: config.browserOriginAllowed !== null,
        ok: config.browserOriginAllowed !== false,
        message: config.browserOriginAllowed === null
          ? 'Browser origin could not be checked in this environment.'
          : config.browserOriginAllowed
            ? 'This exact browser origin is allowed.'
            : (config.browserOrigin?.action || 'Add this exact browser origin in Blockmaker → Connect game → Live game address.'),
      },
    ]
    return { ok: checks.filter(check => check.required).every(check => check.ok), checks, config }
  }

  const evaluateGates = gateKeys => {
    if (!Array.isArray(gateKeys) || gateKeys.length === 0)
      throw new Error('gating.evaluate requires an array of one or more in-game gate keys.')
    return request('/v1/integrations/gating/evaluate', { method: 'POST', body: { gateKeys } })
  }

  const evaluateGatesV2 = (gateKeys, options = {}) => {
    if (!Array.isArray(gateKeys) || gateKeys.length === 0)
      throw new Error('gating.evaluateV2 requires an array of one or more in-game gate keys.')
    const protectedGateKeys = Array.isArray(options.protectedGateKeys) ? options.protectedGateKeys : []
    return request('/v1/integrations/gating/evaluate-v2', {
      method: 'POST',
      body: { gateKeys, protectedGateKeys },
    })
  }

  const checkGateV2 = async (gateKey, options = {}) => {
    if (typeof gateKey !== 'string' || !gateKey.trim())
      throw new Error('gating.checkV2 requires one in-game gate key.')
    const protectedGateKeys = options.protected === false ? [] : [gateKey]
    const result = await evaluateGatesV2([gateKey], { protectedGateKeys })
    return result.gates[0]
  }

  const createOnrampSession = async (options = {}) => {
    const checkout = await request('/v1/onramp/session', {
      method: 'POST',
      body: {
        ...(options.fiatCurrency ? { fiatCurrency: options.fiatCurrency } : {}),
        ...(options.fiatAmount !== undefined ? { fiatAmount: options.fiatAmount } : {}),
      },
    })
    let widget
    try { widget = new URL(String(checkout.widgetUrl ?? '')) }
    catch { throw new BlockmakerError('Blockmaker returned an invalid secure checkout address.', { code: 'ONRAMP_URL_INVALID' }) }
    const allowedHost = widget.hostname === 'global.transak.com' || widget.hostname === 'global-stg.transak.com'
    if (widget.protocol !== 'https:' || !allowedHost || widget.port || widget.username || widget.password)
      throw new BlockmakerError('Blockmaker returned an invalid secure checkout address.', { code: 'ONRAMP_URL_INVALID' })
    const orderId = String(checkout.orderId ?? '').trim()
    if (!/^[A-Za-z0-9_-]{1,128}$/.test(orderId))
      throw new BlockmakerError('Blockmaker returned an invalid purchase reference.', { code: 'ONRAMP_ORDER_INVALID' })
    storeLastOnrampOrder(orderId)
    return { ...checkout, widgetUrl: widget.toString() }
  }

  const terminalOnrampStatuses = new Set(['COMPLETED', 'FAILED', 'CANCELLED', 'REFUNDED', 'EXPIRED'])
  const trackOnrampOrder = (orderId, options = {}) => {
    const id = String(orderId ?? '').trim()
    if (!id) throw new Error('onramp.trackOrder requires an orderId.')
    const intervalMs = Math.min(15_000, Math.max(2_500, Number(options.intervalMs) || 3_000))
    const timeoutMs = Math.min(30 * 60_000, Math.max(30_000, Number(options.timeoutMs) || 15 * 60_000))
    const startedAt = Date.now()
    let stopped = false
    let timer = null
    let polling = false
    let pollAgain = false
    let lastStatus = ''
    let timeoutReported = false

    const stop = () => {
      stopped = true
      if (timer !== null) clearTimeout(timer)
      timer = null
    }
    const schedule = () => {
      if (stopped) return
      if (Date.now() - startedAt >= timeoutMs) {
        stop()
        if (!timeoutReported) {
          timeoutReported = true
          try {
            options.onTrackingError?.(new BlockmakerError(
              'We could not confirm this purchase yet. Check your wallet or purchase history before trying again.',
              { code: 'ONRAMP_TRACKING_TIMEOUT' },
            ))
          } catch { /* consumer callback */ }
        }
        return
      }
      if (timer !== null) clearTimeout(timer)
      timer = setTimeout(poll, intervalMs)
    }
    const poll = async () => {
      if (stopped) return null
      if (polling) { pollAgain = true; return null }
      polling = true
      if (timer !== null) clearTimeout(timer)
      timer = null
      try {
        const result = await request(`/v1/onramp/orders/${encodeURIComponent(id)}`)
        const order = result?.order
        const status = String(order?.status ?? '').toUpperCase()
        if (status && status !== lastStatus) {
          lastStatus = status
          try { options.onOrderUpdate?.(order) } catch { /* consumer callbacks cannot stop verification */ }
        }
        if (terminalOnrampStatuses.has(status)) {
          stop()
          if (lastOnrampOrderId === id) storeLastOnrampOrder(null)
          try { options.onOrderFinal?.(order) } catch { /* consumer callback */ }
          if (status === 'COMPLETED') {
            try { options.onFundingComplete?.(order) } catch { /* consumer callback */ }
          }
          return order
        }
      } catch (error) {
        try { options.onTrackingError?.(error) } catch { /* consumer callback */ }
      } finally {
        polling = false
      }
      if (pollAgain) { pollAgain = false; return poll() }
      schedule()
      return null
    }
    void poll()
    return Object.freeze({ stop, pollNow: poll })
  }

  const isolateDialogBackground = (doc, overlay) => {
    const states = []
    for (const element of Array.from(doc.body?.children ?? [])) {
      if (element === overlay) continue
      states.push({
        element,
        inert: 'inert' in element ? element.inert : undefined,
        ariaHidden: element.getAttribute?.('aria-hidden'),
      })
      try { if ('inert' in element) element.inert = true } catch { /* older WebViews */ }
      try { element.setAttribute?.('aria-hidden', 'true') } catch { /* non-HTMLElement child */ }
    }
    return () => {
      for (const state of states) {
        try { if (state.inert !== undefined) state.element.inert = state.inert } catch { /* element removed */ }
        try {
          if (state.ariaHidden === null) state.element.removeAttribute?.('aria-hidden')
          else state.element.setAttribute?.('aria-hidden', state.ariaHidden)
        } catch { /* element removed */ }
      }
    }
  }

  const beginFullscreenExit = doc => {
    const element = doc.fullscreenElement || doc.webkitFullscreenElement || null
    if (!element) return { element: null, ready: Promise.resolve() }
    let result
    try {
      const exit = doc.exitFullscreen || doc.webkitExitFullscreen
      result = exit?.call(doc)
    } catch { /* caller still gets the modal and a visible close control */ }
    return { element, ready: Promise.resolve(result).catch(() => {}) }
  }

  let checkoutOpen = false
  let onboardingOpen = false
  let fundingGuideOpen = false
  let accountDialogOpen = false
  let profileDialogOpen = false
  let marketplaceDialogOpen = false

  const applyUiAppearance = (overlay, appearance = {}) => {
    const scheme = ['dark', 'light', 'system'].includes(appearance?.scheme) ? appearance.scheme : 'dark'
    overlay.setAttribute('data-bm-scheme', scheme)
    const classes = String(appearance?.className ?? '').split(/\s+/)
      .filter(value => /^[A-Za-z_][A-Za-z0-9_-]{0,63}$/.test(value)).slice(0, 8)
    if (classes.length) overlay.classList.add(...classes)
    for (const [key, property] of Object.entries(UI_TOKEN_PROPERTIES)) {
      const value = safeUiToken(key, appearance?.tokens?.[key])
      if (value) overlay.style.setProperty(property, value)
    }
  }

  const createUiDialog = ({ kind, title: initialTitle, description: initialDescription, appearance, onClose }) => {
    const doc = globalThis.document
    if (!doc?.body?.appendChild)
      throw new BlockmakerError(`${kind}.open() requires a browser.`, { code: 'BROWSER_REQUIRED' })
    if (doc.querySelector?.('[data-blockmaker-dialog="account"], [data-blockmaker-dialog="profile"], [data-blockmaker-dialog="marketplace"]'))
      throw new BlockmakerError('A game account window is already open.', { code: 'BLOCKMAKER_DIALOG_ACTIVE' })

    const priorFocus = doc.activeElement
    const overlay = doc.createElement('div')
    overlay.className = 'bm-ui-overlay'
    overlay.setAttribute('data-blockmaker-dialog', kind)
    overlay.setAttribute('data-bm-part', 'overlay')
    applyUiAppearance(overlay, appearance)

    const style = doc.createElement('style')
    const nonce = String(appearance?.styleNonce ?? '').trim()
    if (nonce) style.setAttribute('nonce', nonce)
    style.textContent = BLOCKMAKER_UI_CSS

    const panel = doc.createElement('section')
    panel.className = 'bm-ui-panel'
    panel.setAttribute('data-bm-dialog-kind', kind)
    panel.setAttribute('data-bm-part', 'panel')
    panel.setAttribute('role', 'dialog')
    panel.setAttribute('aria-modal', 'true')
    const titleId = `bm-${kind}-title-${gameId}`
    const descriptionId = `bm-${kind}-description-${gameId}`
    panel.setAttribute('aria-labelledby', titleId)
    panel.setAttribute('aria-describedby', descriptionId)

    const header = doc.createElement('header')
    header.className = 'bm-ui-header'
    header.setAttribute('data-bm-part', 'header')
    const brand = doc.createElement('p')
    brand.className = 'bm-ui-brand'
    brand.textContent = kind === 'marketplace' ? 'Marketplace' : 'Player account'
    const title = doc.createElement('h2')
    title.className = 'bm-ui-title'
    title.id = titleId
    title.setAttribute('data-bm-part', 'title')
    title.textContent = String(initialTitle ?? '')
    const description = doc.createElement('p')
    description.className = 'bm-ui-description'
    description.id = descriptionId
    description.setAttribute('data-bm-part', 'description')
    description.textContent = String(initialDescription ?? '')
    const closeButton = doc.createElement('button')
    closeButton.type = 'button'
    closeButton.className = 'bm-ui-close'
    closeButton.setAttribute('data-bm-part', 'close')
    closeButton.setAttribute('aria-label', `Close ${kind}`)
    closeButton.textContent = '×'
    header.append(brand, title, description, closeButton)

    const scroll = doc.createElement('div')
    scroll.className = 'bm-ui-scroll'
    scroll.setAttribute('data-bm-part', 'scroll')
    const status = doc.createElement('div')
    status.className = 'bm-ui-status'
    status.setAttribute('data-bm-part', 'status')
    status.setAttribute('role', 'status')
    status.setAttribute('aria-live', 'polite')
    status.setAttribute('aria-atomic', 'true')
    const content = doc.createElement('div')
    content.setAttribute('data-bm-part', 'content')
    scroll.append(status, content)
    panel.append(header, scroll)
    overlay.append(style, panel)
    doc.body.appendChild(overlay)

    const restoreBackground = isolateDialogBackground(doc, overlay)
    const previousOverflow = doc.body.style.overflow
    doc.body.style.overflow = 'hidden'
    let closed = false
    let busy = false

    const focusable = () => Array.from(panel.querySelectorAll('button:not([disabled]),input:not([disabled]),a[href]'))
    const close = reason => {
      if (closed) return
      closed = true
      doc.removeEventListener('keydown', onKeydown)
      overlay.remove()
      restoreBackground()
      doc.body.style.overflow = previousOverflow
      try { priorFocus?.focus?.({ preventScroll: true }) } catch { /* opener may no longer exist */ }
      try { onClose?.(reason) } catch { /* consumer callback */ }
    }
    const onKeydown = event => {
      if (event.key === 'Escape') { event.preventDefault(); close('dismissed'); return }
      if (event.key !== 'Tab') return
      const items = focusable()
      if (!items.length) return
      const first = items[0]
      const last = items[items.length - 1]
      if (event.shiftKey && doc.activeElement === first) { event.preventDefault(); last.focus() }
      else if (!event.shiftKey && doc.activeElement === last) { event.preventDefault(); first.focus() }
    }
    doc.addEventListener('keydown', onKeydown)
    closeButton.addEventListener('click', () => close('dismissed'))
    overlay.addEventListener('click', event => { if (event.target === overlay) close('dismissed') })

    const setState = value => {
      panel.setAttribute('data-bm-state', String(value ?? ''))
      panel.setAttribute('data-bm-part-state', String(value ?? ''))
    }
    const setStatus = (message = '', tone = 'info') => {
      const text = String(message ?? '').trim()
      status.textContent = text
      status.style.display = text ? 'block' : 'none'
      status.setAttribute('data-bm-tone', tone)
      status.setAttribute('role', tone === 'error' ? 'alert' : 'status')
      status.setAttribute('aria-live', tone === 'error' ? 'assertive' : 'polite')
    }
    const setBusy = (value, message = '') => {
      busy = !!value
      panel.setAttribute('aria-busy', busy ? 'true' : 'false')
      for (const control of content.querySelectorAll('[data-bm-disable-while-busy]')) {
        if (busy) {
          if (control.disabled) control.setAttribute('data-bm-disabled-before-busy', 'true')
          control.disabled = true
        } else {
          control.disabled = control.getAttribute('data-bm-disabled-before-busy') === 'true'
          control.removeAttribute('data-bm-disabled-before-busy')
        }
      }
      if (message) setStatus(message, 'info')
    }
    const setProviderActive = value => {
      overlay.style.visibility = value ? 'hidden' : 'visible'
      overlay.setAttribute('aria-hidden', value ? 'true' : 'false')
    }
    const replaceContent = (...nodes) => content.replaceChildren(...nodes)
    const setTitle = value => { title.textContent = String(value ?? '') }
    const setDescription = value => { description.textContent = String(value ?? '') }
    const focusFirst = () => { try { (focusable()[0] ?? closeButton).focus({ preventScroll: true }) } catch { /* older WebViews */ } }
    closeButton.focus?.({ preventScroll: true })

    return {
      doc, overlay, panel, content, closeButton,
      close, isClosed: () => closed, isBusy: () => busy,
      setState, setStatus, setBusy, setProviderActive, replaceContent,
      setTitle, setDescription, focusFirst,
    }
  }

  const uiButton = (doc, label, variant = 'default', full = false) => {
    const button = doc.createElement('button')
    button.type = 'button'
    button.className = 'bm-ui-button'
    button.setAttribute('data-bm-variant', variant)
    button.setAttribute('data-bm-full', full ? 'true' : 'false')
    button.setAttribute('data-bm-disable-while-busy', '')
    button.textContent = label
    return button
  }

  // Create the overlay in the click stack before awaiting its provider URL.
  const openOnramp = async (options = {}) => {
    if (!session?.sessionToken)
      throw new BlockmakerError('Sign in before adding ALGO.', { status: 401, code: 'AUTH_MISSING' })
    const doc = globalThis.document
    if (!doc?.body?.appendChild)
      throw new BlockmakerError('onramp.open() requires a browser or Unity WebGL page.', { code: 'BROWSER_REQUIRED' })
    if (checkoutOpen || doc.querySelector?.('[data-blockmaker-dialog="checkout"]'))
      throw new BlockmakerError('Secure checkout is already open.', { code: 'ONRAMP_SESSION_ACTIVE' })
    checkoutOpen = true

    const fullscreen = beginFullscreenExit(doc)

    const priorFocus = doc.activeElement
    const overlay = doc.createElement('div')
    overlay.setAttribute('data-blockmaker-dialog', 'checkout')
    overlay.setAttribute('role', 'dialog')
    overlay.setAttribute('aria-modal', 'true')
    overlay.setAttribute('aria-label', 'Add ALGO with card')
    overlay.style.cssText = 'position:fixed;inset:0;z-index:2147483647;display:flex;width:100%;max-width:100vw;min-width:0;align-items:center;justify-content:center;overflow:hidden;padding:max(12px,env(safe-area-inset-top)) max(12px,env(safe-area-inset-right)) max(12px,env(safe-area-inset-bottom)) max(12px,env(safe-area-inset-left));background:rgba(5,7,12,.82);backdrop-filter:blur(8px);-webkit-backdrop-filter:blur(8px);box-sizing:border-box;'
    const panel = doc.createElement('div')
    panel.style.cssText = 'position:relative;flex:0 1 520px;width:100%;max-width:520px;min-width:0;height:calc(100vh - 24px);height:min(820px,calc(100dvh - 24px));min-height:0;overflow:hidden;border-radius:20px;background:#10131a;box-shadow:0 24px 80px rgba(0,0,0,.55);box-sizing:border-box;'
    const status = doc.createElement('div')
    status.setAttribute('role', 'status')
    status.style.cssText = 'position:absolute;inset:0;display:grid;place-items:center;padding:32px;color:#fff;font:600 16px/1.4 system-ui,sans-serif;text-align:center;'
    status.textContent = 'Preparing secure checkout…'
    const frame = doc.createElement('iframe')
    frame.title = 'Secure ALGO checkout'
    frame.setAttribute('allow', 'camera; microphone; payment; clipboard-write')
    frame.setAttribute('referrerpolicy', 'strict-origin-when-cross-origin')
    frame.style.cssText = 'position:absolute;inset:0;width:100%;height:100%;border:0;background:#fff;opacity:0;transition:opacity .16s ease;'
    const closeButton = doc.createElement('button')
    closeButton.type = 'button'
    closeButton.setAttribute('aria-label', 'Close checkout')
    closeButton.textContent = '×'
    closeButton.style.cssText = 'position:absolute;z-index:2;top:10px;right:10px;width:46px;height:46px;border:0;border-radius:999px;background:rgba(15,17,22,.92);color:#fff;font:400 30px/42px system-ui,sans-serif;cursor:pointer;box-shadow:0 2px 12px rgba(0,0,0,.25);'
    panel.append(status, frame, closeButton)
    overlay.append(panel)
    doc.body.appendChild(overlay)
    const restoreBackground = isolateDialogBackground(doc, overlay)
    const previousOverflow = doc.body.style.overflow
    doc.body.style.overflow = 'hidden'

    let closed = false
    let tracker = null
    let providerOrigin = ''
    const close = (restoreFullscreen = true) => {
      if (closed) return
      closed = true
      checkoutOpen = false
      doc.removeEventListener('keydown', onKeydown)
      globalThis.removeEventListener?.('message', onProviderMessage)
      overlay.remove()
      restoreBackground()
      doc.body.style.overflow = previousOverflow
      try { priorFocus?.focus?.({ preventScroll: true }) } catch { /* element may no longer exist */ }
      const requestFullscreen = fullscreen.element && (fullscreen.element.requestFullscreen || fullscreen.element.webkitRequestFullscreen)
      if (restoreFullscreen && requestFullscreen) {
        try {
          const result = requestFullscreen.call(fullscreen.element)
          result?.catch?.(() => {})
        } catch { /* restoration may require a direct user gesture */ }
      }
      options.onClose?.()
    }
    const focusable = () => Array.from(panel.querySelectorAll('button:not([disabled]),iframe'))
    const onKeydown = event => {
      if (event.key === 'Escape') return close()
      if (event.key !== 'Tab') return
      const items = focusable()
      if (!items.length) return
      const first = items[0]
      const last = items[items.length - 1]
      if (event.shiftKey && doc.activeElement === first) { event.preventDefault(); last.focus() }
      else if (!event.shiftKey && doc.activeElement === last) { event.preventDefault(); first.focus() }
    }
    const onProviderMessage = event => {
      if (!providerOrigin || event.origin !== providerOrigin || event.source !== frame.contentWindow) return
      const eventId = String(event.data?.event_id ?? '')
      if (eventId.startsWith('TRANSAK_')) void tracker?.pollNow?.()
      if (eventId === 'TRANSAK_WIDGET_CLOSE') close(false)
    }
    doc.addEventListener('keydown', onKeydown)
    globalThis.addEventListener?.('message', onProviderMessage)
    closeButton.addEventListener('click', close)
    overlay.addEventListener('click', event => { if (event.target === overlay) close() })
    try { closeButton.focus({ preventScroll: true }) } catch { closeButton.focus?.() }
    try {
      await fullscreen.ready
      const checkout = await createOnrampSession(options)
      if (closed) return { ...checkout, close }
      providerOrigin = new URL(checkout.widgetUrl).origin
      if (options.onOrderUpdate || options.onOrderFinal || options.onFundingComplete || options.onTrackingError)
        tracker = trackOnrampOrder(checkout.orderId, options)
      frame.addEventListener('load', () => {
        if (closed) return
        frame.style.opacity = '1'
        status.style.display = 'none'
      }, { once: true })
      frame.src = checkout.widgetUrl
      return { ...checkout, close, stopTracking: () => tracker?.stop?.(), pollNow: () => tracker?.pollNow?.() }
    } catch (error) {
      if (!closed) {
        const message = doc.createElement('p')
        message.textContent = playerFacingError(error, 'Could not open secure checkout. Please try again.')
        message.style.cssText = 'max-width:34ch;margin:0;color:#fff;'
        const backButton = doc.createElement('button')
        backButton.type = 'button'
        backButton.textContent = 'Back'
        backButton.style.cssText = 'min-width:140px;min-height:48px;margin-top:18px;padding:12px 20px;border:0;border-radius:12px;background:#a38cff;color:#0d1017;font:800 15px/1 system-ui,sans-serif;cursor:pointer;'
        backButton.addEventListener('click', () => close(false))
        status.replaceChildren(message, backButton)
        status.style.alignContent = 'center'
        status.style.padding = '72px 32px 32px'
        backButton.focus?.({ preventScroll: true })
      }
      throw error
    }
  }

  // Read-only provider-neutral funding help; it never creates an order.
  const openFundingGuide = (options = {}, exactFundingSession = null) => {
    const fundingSession = exactFundingSession ?? session
    if (!fundingSession?.sessionToken)
      throw new BlockmakerError('Sign in before opening wallet funding help.', { status: 401, code: 'AUTH_MISSING' })
    if (isAuthenticationOnlyEmailSession(fundingSession))
      throw new BlockmakerError(
        'This email sign-in account is for authentication only. Connect Pera or Lute for funding and on-chain features.',
        { status: 403, code: 'PLAYER_ECONOMIC_CAPABILITY_DENIED' },
      )
    const doc = globalThis.document
    if (!doc?.body?.appendChild)
      throw new BlockmakerError('funding.open() requires a browser or Unity WebGL page.', { code: 'BROWSER_REQUIRED' })
    if (fundingGuideOpen || doc.querySelector?.('[data-blockmaker-dialog="funding-guide"]'))
      throw new BlockmakerError('Wallet funding help is already open.', { code: 'FUNDING_GUIDE_ACTIVE' })
    fundingGuideOpen = true
    const fullscreen = beginFullscreenExit(doc)

    const priorFocus = doc.activeElement
    const overlay = doc.createElement('div')
    overlay.setAttribute('data-blockmaker-dialog', 'funding-guide')
    overlay.setAttribute('role', 'dialog')
    overlay.setAttribute('aria-modal', 'true')
    overlay.setAttribute('aria-labelledby', `bm-funding-title-${gameId}`)
    overlay.style.cssText = 'position:fixed;inset:0;z-index:2147483647;display:flex;width:100%;max-width:100vw;min-width:0;align-items:center;justify-content:center;overflow:hidden;padding:max(14px,env(safe-area-inset-top)) max(14px,env(safe-area-inset-right)) max(14px,env(safe-area-inset-bottom)) max(14px,env(safe-area-inset-left));background:rgba(5,7,12,.86);backdrop-filter:blur(10px);-webkit-backdrop-filter:blur(10px);box-sizing:border-box;color:#f8fafc;font-family:Inter,ui-sans-serif,system-ui,-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif;'
    const panel = doc.createElement('section')
    panel.style.cssText = 'position:relative;flex:0 1 620px;width:100%;max-width:620px;min-width:0;max-height:calc(100vh - 28px);max-height:calc(100dvh - 28px);overflow:auto;overscroll-behavior:contain;border:1px solid rgba(255,255,255,.12);border-radius:24px;background:linear-gradient(180deg,#181c27 0%,#0d1017 100%);box-shadow:0 28px 90px rgba(0,0,0,.64);box-sizing:border-box;-webkit-overflow-scrolling:touch;'
    const content = doc.createElement('div')
    content.style.cssText = 'min-width:0;padding:clamp(24px,6vw,38px);box-sizing:border-box;'
    const closeButton = doc.createElement('button')
    closeButton.type = 'button'
    closeButton.setAttribute('aria-label', 'Close wallet funding help')
    closeButton.textContent = '×'
    closeButton.style.cssText = 'position:absolute;z-index:2;top:12px;right:12px;width:46px;height:46px;border:0;border-radius:999px;background:rgba(255,255,255,.08);color:#fff;font:400 29px/42px system-ui,sans-serif;cursor:pointer;'
    const eyebrow = doc.createElement('div')
    eyebrow.textContent = 'WALLET · GET ALGO'
    eyebrow.style.cssText = 'margin:0 48px 12px 0;color:#a996ff;font-size:12px;font-weight:850;letter-spacing:.12em;'
    const title = doc.createElement('h2')
    title.id = `bm-funding-title-${gameId}`
    title.textContent = String(options.title ?? 'Get ALGO, step by step')
    title.style.cssText = 'margin:0 46px 10px 0;color:#fff;font-size:clamp(27px,7vw,38px);line-height:1.06;letter-spacing:-.035em;'
    const intro = doc.createElement('p')
    intro.textContent = 'Choose the route that matches how you signed in. Adding funds is optional unless this game tells you otherwise.'
    intro.style.cssText = 'margin:0 0 22px;color:#b8bfce;font-size:15px;line-height:1.55;'
    const status = doc.createElement('div')
    status.setAttribute('role', 'status')
    status.setAttribute('aria-live', 'polite')
    status.setAttribute('aria-atomic', 'true')
    status.style.cssText = 'margin:0 0 14px;padding:12px 14px;border-radius:12px;background:rgba(157,140,255,.10);color:#d8d2ff;font-size:13px;line-height:1.5;'
    status.textContent = 'Checking your game wallet…'
    const body = doc.createElement('div')
    content.append(eyebrow, title, intro, status, body)
    panel.append(closeButton, content)
    overlay.append(panel)
    doc.body.appendChild(overlay)
    const restoreBackground = isolateDialogBackground(doc, overlay)
    const previousOverflow = doc.body.style.overflow
    doc.body.style.overflow = 'hidden'

    let closed = false
    let loading = false
    let latest = null
    const buttonStyle = 'min-height:48px;padding:12px 16px;border-radius:12px;font:800 14px/1.25 inherit;cursor:pointer;box-sizing:border-box;text-align:center;text-decoration:none;'
    const setStatus = (message, error = false) => {
      status.style.display = message ? 'block' : 'none'
      status.style.background = error ? 'rgba(255,92,113,.12)' : 'rgba(157,140,255,.10)'
      status.style.color = error ? '#ffb7c1' : '#d8d2ff'
      status.textContent = message
    }
    const close = () => {
      if (closed) return
      closed = true
      fundingGuideOpen = false
      doc.removeEventListener('keydown', onKeydown)
      overlay.remove()
      restoreBackground()
      doc.body.style.overflow = previousOverflow
      try { priorFocus?.focus?.({ preventScroll: true }) } catch { /* caller may have removed it */ }
      try { options.onClose?.() } catch { /* consumer callback */ }
    }
    const focusable = () => Array.from(panel.querySelectorAll('button:not([disabled]),input:not([disabled]),a[href]'))
    const onKeydown = event => {
      if (event.key === 'Escape') return close()
      if (event.key !== 'Tab') return
      const items = focusable()
      if (!items.length) return
      const first = items[0]
      const last = items[items.length - 1]
      if (event.shiftKey && doc.activeElement === first) { event.preventDefault(); last.focus() }
      else if (!event.shiftKey && doc.activeElement === last) { event.preventDefault(); first.focus() }
    }
    doc.addEventListener('keydown', onKeydown)
    closeButton.addEventListener('click', close)
    overlay.addEventListener('click', event => { if (event.target === overlay) close() })

    const makeLink = (label, href, primary = false) => {
      const link = doc.createElement('a')
      link.href = href
      link.target = '_blank'
      link.rel = 'noopener noreferrer'
      link.textContent = label
      link.style.cssText = `${buttonStyle}display:inline-flex;align-items:center;justify-content:center;border:${primary ? '1px solid #b3a2ff' : '1px solid rgba(255,255,255,.16)'};background:${primary ? '#a996ff' : 'rgba(255,255,255,.06)'};color:${primary ? '#0c0f16' : '#fff'};`
      return link
    }
    const makeSteps = (heading, copy, steps, highlighted = false) => {
      const card = doc.createElement('section')
      card.style.cssText = `margin-top:14px;padding:17px;border:1px solid ${highlighted ? 'rgba(169,150,255,.52)' : 'rgba(255,255,255,.11)'};border-radius:16px;background:${highlighted ? 'rgba(139,109,255,.10)' : 'rgba(255,255,255,.035)'};`
      const cardHeading = doc.createElement('h3')
      cardHeading.textContent = heading
      cardHeading.style.cssText = 'margin:0;color:#fff;font-size:16px;line-height:1.3;'
      const cardCopy = doc.createElement('p')
      cardCopy.textContent = copy
      cardCopy.style.cssText = 'margin:7px 0 0;color:#aeb6c6;font-size:13px;line-height:1.55;'
      const list = doc.createElement('ol')
      list.style.cssText = 'display:grid;gap:9px;margin:14px 0 0;padding:0;list-style:none;counter-reset:bm-funding-step;'
      for (const step of steps) {
        const item = doc.createElement('li')
        item.style.cssText = 'position:relative;min-height:26px;padding-left:36px;color:#d8dde7;font-size:13px;line-height:1.5;counter-increment:bm-funding-step;'
        const number = doc.createElement('span')
        number.setAttribute('aria-hidden', 'true')
        number.textContent = String(list.children.length + 1)
        number.style.cssText = 'position:absolute;left:0;top:0;width:25px;height:25px;display:grid;place-items:center;border-radius:999px;background:rgba(169,150,255,.16);color:#cfc4ff;font-size:11px;font-weight:850;'
        const text = doc.createElement('span')
        text.textContent = step
        item.append(number, text)
        list.append(item)
      }
      card.append(cardHeading, cardCopy, list)
      return card
    }

    const render = data => {
      latest = data
      body.replaceChildren()
      setStatus('')
      const accountKind = String(data?.accountKind ?? 'other')
      const walletProvider = ['pera', 'lute', 'txnlab_web3auth'].includes(data?.walletProvider)
        ? String(data.walletProvider)
        : null
      const walletAddress = String(data?.walletAddress ?? fundingSession?.walletAddress ?? '')
      if (!isAlgorandAddressShape(walletAddress))
        throw new BlockmakerError('The wallet service returned an invalid game wallet. Please sign in again.', { code: 'AUTH_INVALID' })
      const gameName = String(data?.game?.name ?? 'this game')
      const balance = formatAlgoBalance(data?.algoBalanceMicroalgo)

      const summary = doc.createElement('section')
      summary.style.cssText = 'padding:15px;border:1px solid rgba(255,255,255,.11);border-radius:16px;background:rgba(255,255,255,.045);'
      const summaryTop = doc.createElement('div')
      summaryTop.style.cssText = 'display:flex;align-items:flex-start;justify-content:space-between;gap:14px;flex-wrap:wrap;'
      const labelWrap = doc.createElement('div')
      const walletLabel = doc.createElement('strong')
      walletLabel.textContent = walletProvider === 'pera'
        ? 'Pera wallet'
        : walletProvider === 'lute'
          ? 'Lute wallet'
          : walletProvider === 'txnlab_web3auth'
            ? 'Email or Google wallet'
            : accountKind === 'email_wallet'
              ? 'Email game wallet'
              : accountKind === 'evm_wallet'
                ? 'EVM-linked game wallet'
                : accountKind === 'algorand_wallet'
                  ? 'Connected Algorand wallet'
                  : 'Game wallet'
      walletLabel.style.cssText = 'display:block;color:#fff;font-size:14px;'
      const walletHint = doc.createElement('span')
      walletHint.textContent = `Use this exact Algorand MainNet address for ${gameName}.`
      walletHint.style.cssText = 'display:block;margin-top:3px;color:#9099aa;font-size:12px;line-height:1.4;'
      labelWrap.append(walletLabel, walletHint)
      const balanceWrap = doc.createElement('div')
      balanceWrap.style.cssText = 'text-align:right;'
      const balanceLabel = doc.createElement('span')
      balanceLabel.textContent = 'Current balance'
      balanceLabel.style.cssText = 'display:block;color:#9099aa;font-size:11px;'
      const balanceValue = doc.createElement('strong')
      balanceValue.textContent = balance ?? 'Unavailable'
      balanceValue.style.cssText = 'display:block;margin-top:2px;color:#fff;font-size:14px;'
      balanceWrap.append(balanceLabel, balanceValue)
      summaryTop.append(labelWrap, balanceWrap)
      const addressRow = doc.createElement('div')
      addressRow.style.cssText = 'display:flex;align-items:stretch;gap:8px;margin-top:13px;flex-wrap:wrap;'
      const addressInput = doc.createElement('input')
      addressInput.readOnly = true
      addressInput.value = walletAddress
      addressInput.setAttribute('aria-label', 'Your game wallet address')
      addressInput.spellcheck = false
      addressInput.style.cssText = 'flex:1 1 330px;min-width:0;height:46px;padding:0 12px;border:1px solid rgba(255,255,255,.13);border-radius:11px;background:#090c12;color:#e9ecf2;font:650 12px/1 ui-monospace,SFMono-Regular,Menlo,monospace;box-sizing:border-box;'
      const copyButton = doc.createElement('button')
      copyButton.type = 'button'
      copyButton.textContent = 'Copy address'
      copyButton.style.cssText = `${buttonStyle}border:1px solid rgba(255,255,255,.16);background:rgba(255,255,255,.07);color:#fff;`
      copyButton.addEventListener('click', async () => {
        try {
          if (!globalThis.navigator?.clipboard?.writeText) throw new Error('Clipboard unavailable')
          await globalThis.navigator.clipboard.writeText(walletAddress)
          copyButton.textContent = 'Address copied ✓'
          setStatus('Wallet address copied. Compare its first and last characters before sending.')
        } catch {
          addressInput.focus()
          addressInput.select?.()
          setStatus('Your browser blocked automatic copying. The full address is selected—copy it manually.', true)
        }
      })
      addressRow.append(addressInput, copyButton)
      if (walletProvider !== 'pera') {
        const peraAddressLink = doc.createElement('a')
        peraAddressLink.href = `perawallet://${walletAddress}`
        peraAddressLink.textContent = 'Open in Pera'
        peraAddressLink.setAttribute('aria-label', 'Open this game wallet address in Pera on your phone')
        peraAddressLink.style.cssText = `${buttonStyle}display:inline-flex;align-items:center;justify-content:center;border:1px solid rgba(169,150,255,.48);background:rgba(169,150,255,.10);color:#e5e0ff;`
        addressRow.append(peraAddressLink)
      }
      summary.append(summaryTop, addressRow)
      body.append(summary)

      if (walletProvider === 'pera') {
        body.append(makeSteps(
          'Fund this account in Pera',
          'Pera Fund shows third-party purchase options inside Pera. The game does not handle that payment.',
          [
            'Open Pera Wallet and select the same Algorand account you use for this game.',
            'Tap Fund (called Buy/Sell in some older versions), then choose ALGO.',
            'Choose a card, bank, or crypto option if one is offered in your region. Review the named provider, fees, limits, and final ALGO amount.',
            'Complete any provider identity check, then return here and refresh your balance.',
          ],
          true,
        ))
      } else {
        const accountCopy = walletProvider === 'lute'
          ? 'This game wallet is connected through Lute. A Pera wallet is separate, so fund Pera first and send ALGO to the exact Lute address above.'
          : walletProvider === 'txnlab_web3auth'
            ? 'Your Email or Google sign-in is a real Algorand wallet. A Pera wallet is separate, so fund Pera first and send ALGO to the exact address above.'
          : accountKind === 'email_wallet'
          ? 'Your email sign-in already has an Algorand game wallet. A new Pera wallet is separate, so keep using email for this game and send ALGO from Pera to the exact address above.'
          : accountKind === 'evm_wallet'
            ? 'Keep signing into the game with your EVM wallet. Pera is used only to buy and send ALGO to the linked Algorand game address above.'
            : 'If you create a Pera wallet, it will be separate from this game wallet. Fund Pera first, then send ALGO to the exact address above.'
        body.append(makeSteps(
          'Beginner route: use Pera, then send to this wallet',
          accountCopy,
          [
            'Install Pera only from its official website or app-store listing and create a self-custody wallet. Pera does not require an email account to create it.',
            'Back up the recovery phrase privately. Never put it into this game, a message, or a support form.',
            'In Pera, tap Fund and buy ALGO using an option available in your region. Review all provider fees and the final amount.',
            'In Pera, choose Send → ALGO. On your phone, use Open in Pera above—or paste the address—and compare its first and last characters.',
            'Send a small test first. Refresh here and confirm it arrived before sending more.',
          ],
          true,
        ))
      }

      body.append(makeSteps(
        'Already use a trusted exchange?',
        'You can buy ALGO there and withdraw it to the game wallet above. This usually requires an exchange account and identity checks.',
        [
          'Choose ALGO and the Algorand MainNet network—not Ethereum, Base, or another chain.',
          'Paste the exact game wallet address shown above. Never type a long address by hand.',
          'Check withdrawal fees and minimums, then send a small test first.',
          'Return to the game and refresh the balance before doing anything else.',
        ],
      ))

      const actions = doc.createElement('div')
      actions.style.cssText = 'display:flex;gap:9px;flex-wrap:wrap;margin-top:16px;'
      if (walletProvider === 'pera') {
        actions.append(
          makeLink('Open official Pera site ↗', OFFICIAL_FUNDING_LINKS.peraWebsite, true),
          makeLink('Pera funding options ↗', OFFICIAL_FUNDING_LINKS.peraFundingHelp),
        )
      } else {
        actions.append(
          makeLink('Get Pera Wallet ↗', OFFICIAL_FUNDING_LINKS.peraWebsite, true),
          makeLink('Wallet setup guide ↗', OFFICIAL_FUNDING_LINKS.peraCreateWallet),
          makeLink('Pera funding options ↗', OFFICIAL_FUNDING_LINKS.peraFundingHelp),
        )
      }
      body.append(actions)

      const safety = doc.createElement('aside')
      safety.style.cssText = 'margin-top:16px;padding:14px 15px;border:1px solid rgba(255,196,92,.26);border-radius:14px;background:rgba(255,196,92,.07);color:#d8d0bd;font-size:12px;line-height:1.58;'
      const safetyTitle = doc.createElement('strong')
      safetyTitle.textContent = 'Before money moves'
      safetyTitle.style.cssText = 'display:block;margin-bottom:4px;color:#ffe2a8;font-size:13px;'
      const safetyText = doc.createElement('span')
      safetyText.textContent = 'Crypto prices can change and blockchain transfers are normally irreversible. Card availability, identity checks, quotes, minimums, and fees come from Pera’s third-party providers and vary by country. The game cannot reverse or support their purchase.'
      const safetyLink = doc.createElement('a')
      safetyLink.href = OFFICIAL_FUNDING_LINKS.peraRecoverySafety
      safetyLink.target = '_blank'
      safetyLink.rel = 'noopener noreferrer'
      safetyLink.textContent = 'Read Pera’s recovery-word safety guide ↗'
      safetyLink.style.cssText = 'display:block;margin-top:7px;color:#ffe2a8;text-underline-offset:3px;'
      safety.append(safetyTitle, safetyText, safetyLink)
      body.append(safety)

      const footer = doc.createElement('div')
      footer.style.cssText = 'display:flex;gap:10px;align-items:center;justify-content:flex-end;flex-wrap:wrap;margin-top:18px;padding-top:18px;border-top:1px solid rgba(255,255,255,.09);'
      const refreshButton = doc.createElement('button')
      refreshButton.type = 'button'
      refreshButton.textContent = 'Refresh balance'
      refreshButton.style.cssText = `${buttonStyle}border:1px solid rgba(255,255,255,.16);background:rgba(255,255,255,.06);color:#fff;`
      refreshButton.addEventListener('click', () => { void load(true) })
      const doneButton = doc.createElement('button')
      doneButton.type = 'button'
      doneButton.textContent = 'Done'
      doneButton.style.cssText = `${buttonStyle}min-width:110px;border:1px solid #b3a2ff;background:#a996ff;color:#0c0f16;`
      doneButton.addEventListener('click', close)
      footer.append(refreshButton, doneButton)
      body.append(footer)
      try { options.onBalanceRefresh?.({ walletAddress, algoBalanceMicroalgo: data?.algoBalanceMicroalgo ?? null, balanceStatus: data?.balanceStatus ?? 'unavailable' }) } catch { /* consumer callback */ }
    }

    const load = async (refreshing = false) => {
      if (loading || closed) return latest
      loading = true
      panel.setAttribute('aria-busy', 'true')
      setStatus(refreshing ? 'Refreshing your verified wallet balance…' : 'Checking your game wallet…')
      try {
        await fullscreen.ready
        if (closed) return null
        const result = exactFundingSession
          ? await rawRequest('/v1/funding-guide/me', {}, exactFundingSession.sessionToken)
          : await request('/v1/funding-guide/me')
        if (!closed) render(result)
        return result
      } catch (error) {
        if (!closed) {
          if (!latest) {
            body.replaceChildren()
            const errorCopy = doc.createElement('p')
            errorCopy.textContent = playerFacingError(error, 'Wallet funding help is unavailable right now. You can close this window and keep playing.')
            errorCopy.style.cssText = 'margin:0;color:#d5d9e2;font-size:14px;line-height:1.6;'
            body.append(errorCopy)
          }
          setStatus(playerFacingError(error, refreshing ? 'Could not refresh the balance. Your previous wallet details are still shown.' : 'Wallet funding help is unavailable right now.'), true)
          try { options.onError?.(error) } catch { /* consumer callback */ }
        }
        return null
      } finally {
        loading = false
        panel.setAttribute('aria-busy', 'false')
      }
    }

    closeButton.focus?.({ preventScroll: true })
    void load(false)
    return Object.freeze({ close, refresh: () => load(true) })
  }

  const normalizedAccountWallets = options => {
    if (options.algorandWallets !== undefined && !Array.isArray(options.algorandWallets))
      throw new Error('account.open algorandWallets must be an array of named wallet adapters.')
    if (options.evmWallets !== undefined && !Array.isArray(options.evmWallets))
      throw new Error('account.open evmWallets must be an array of named EIP-1193 providers.')
    if ((options.algorandWallets?.length ?? 0) > 6 || (options.evmWallets?.length ?? 0) > 6)
      throw new Error('account.open supports at most six wallets per chain.')

    const usedIds = new Set()
    const makeId = (entry, name, chain, index) => {
      const raw = String(entry?.id ?? '').trim().toLowerCase()
      const generated = name.toLowerCase().replace(/[^a-z0-9_-]+/g, '-').replace(/^-+|-+$/g, '')
      const baseId = (raw || generated || `${chain}-${index + 1}`).slice(0, 48)
      let id = baseId
      for (let suffix = 2; usedIds.has(id); suffix++) id = `${baseId.slice(0, 44)}-${suffix}`
      usedIds.add(id)
      return id
    }
    const configuredAlgorandWallets = [...(options.algorandWallets ?? [])]
    if (options.txnLabWeb3Auth !== undefined) {
      const existingIndex = configuredAlgorandWallets.findIndex(entry =>
        entry?.wallet === options.txnLabWeb3Auth)
      if (existingIndex >= 0) {
        configuredAlgorandWallets[existingIndex] = {
          ...configuredAlgorandWallets[existingIndex],
          providerId: 'txnlab_web3auth',
        }
      } else {
        configuredAlgorandWallets.unshift({
          id: 'txnlab-web3auth',
          providerId: 'txnlab_web3auth',
          name: 'Email or Google',
          wallet: options.txnLabWeb3Auth,
          featured: true,
        })
      }
    }
    if (configuredAlgorandWallets.length > 6)
      throw new Error('account.open supports at most six wallets per chain.')
    const algorand = configuredAlgorandWallets.map((entry, index) => {
      const wallet = entry?.wallet
      const name = String(entry?.name ?? wallet?.metadata?.name ?? wallet?.name ?? '').trim()
      if (!name || name.length > 48 || typeof wallet?.connect !== 'function'
        || (typeof wallet?.signTransactions !== 'function' && typeof wallet?.signTransaction !== 'function'))
        throw new Error(`account.open Algorand wallet ${index + 1} needs a short name and compatible adapter.`)
      const id = makeId(entry, name, 'algorand', index)
      const providerCandidate = String(entry?.providerId ?? wallet?.providerId ?? wallet?.metadata?.providerId ?? id).trim().toLowerCase()
      return {
        id, name, wallet, chain: 'algorand',
        providerId: WALLET_PROVIDER_IDS.has(providerCandidate) ? providerCandidate : null,
        featured: entry?.featured === true,
        iconUrl: safeImageUrl(entry?.iconUrl ?? wallet?.metadata?.icon),
      }
    })
    if (options.pera && !algorand.some(entry => entry.wallet === options.pera)) {
      const name = 'Pera Wallet'
      algorand.unshift({
        id: makeId({ id: 'pera' }, name, 'algorand', 0), name, wallet: options.pera,
        chain: 'algorand', providerId: 'pera', featured: true, iconUrl: safeImageUrl(options.pera?.metadata?.icon),
      })
    }
    if (algorand.length && (typeof options.algosdk?.decodeUnsignedTransaction !== 'function'
      || typeof options.algosdk?.encodeAddress !== 'function'))
      throw new Error('account.open requires the official algosdk package when Algorand wallet login is enabled.')

    const evm = (options.evmWallets ?? []).map((entry, index) => {
      const name = String(entry?.name ?? '').trim()
      if (!name || name.length > 48 || typeof entry?.wallet?.request !== 'function')
        throw new Error(`account.open EVM wallet ${index + 1} needs a short name and an EIP-1193 provider.`)
      return {
        id: makeId(entry, name, 'evm', index), name, wallet: entry.wallet, chain: 'evm',
        providerId: 'xchain',
        featured: entry?.featured === true, iconUrl: safeImageUrl(entry?.iconUrl),
      }
    })
    return { algorand, evm, all: [...algorand, ...evm] }
  }

  const availableWalletProviders = async (options = {}) => {
    const config = await request('/v1/integrations/config', { auth: false })
    const providers = Array.isArray(config?.walletProviders) ? config.walletProviders : []
    return providers
      .filter(provider => options.includeUnavailable === true || provider?.enabled === true)
      .map(provider => Object.freeze({ ...provider }))
  }

  const simulatedWalletAdapter = (options = {}) => {
    const loopback = value => ['localhost', '127.0.0.1', '::1', '[::1]'].includes(String(value ?? '').toLowerCase())
    const apiHost = new URL(baseUrl).hostname
    const pageHost = globalThis.location?.hostname
    if (!loopback(apiHost) || !loopback(pageHost)) {
      throw new BlockmakerError('The simulated wallet is available only when both the game and Blockmaker use loopback addresses.', {
        code: 'DEVELOPMENT_ADAPTER_FORBIDDEN',
      })
    }
    const outcome = String(options.outcome ?? 'cancelled')
    if (!['cancelled', 'pending', 'rejected', 'missing_account'].includes(outcome))
      throw new Error('Simulated wallet outcome must be cancelled, pending, rejected or missing_account.')
    const address = String(options.address ?? 'A'.repeat(58)).trim().toUpperCase()
    const wait = () => new Promise(() => {})
    const fail = () => {
      if (outcome === 'pending') return wait()
      if (outcome === 'missing_account') return Promise.resolve([])
      throw new BlockmakerError(
        outcome === 'cancelled' ? 'The simulated player cancelled.' : 'The simulated wallet rejected the request.',
        { code: outcome === 'cancelled' ? 'WALLET_REQUEST_REJECTED' : 'WALLET_CONNECTION_FAILED' },
      )
    }
    return Object.freeze({
      name: String(options.name ?? 'Simulated wallet'),
      accounts: outcome === 'missing_account' ? [] : [address],
      connect: fail,
      resumeSession: fail,
      signTransactions: fail,
    })
  }

  // Unity presentation reuses the same prepared nonce proof, provider policy,
  // verification and login-attempt cleanup as the browser account dialog.
  // It owns no DOM dialog and never exits or re-enters fullscreen.
  const openUnityPeraAccount = options => {
    if (accountDialogOpen)
      throw new BlockmakerError('Player account is already open.', { code: 'ACCOUNT_DIALOG_ACTIVE' })
    const providerId = options.providerId ?? 'pera'
    const entry = options.algorandWallets.find(entry => entry.providerId === providerId)
    const wallet = entry?.wallet
    if (typeof wallet?.beginUnityPresentation !== 'function')
      throw new BlockmakerError('This package cannot present the selected wallet inside Unity.', { code: 'PRESENTATION_UNAVAILABLE' })
    accountDialogOpen = true
    let closed = false
    let prepared = null
    let phase = 'connecting'
    let cancellation = null
    let cancellationFailure = null
    const tracker = options[UNITY_WALLET_PACKAGE_LOGIN_TRACKER]
    const progress = event => {
      if (closed || (event.phase === 'qr' && (phase === 'approval' || phase === 'verifying'))) return
      phase = event.phase
      try { options.onProgress?.(Object.freeze({ ...event, providerId })) } catch { /* presentation only */ }
    }
    let presentation
    try { presentation = wallet.beginUnityPresentation(progress) }
    catch (error) { accountDialogOpen = false; throw error }
    const cancelled = () => new BlockmakerError('The player cancelled wallet sign-in.', { code: 'PLAYER_CANCELLED' })
    const close = () => {
      if (closed) return
      closed = true
      if (prepared) discardPreparedAlgorandWalletLogin(prepared)
      // Keep provider cleanup in the same attempt. A late proof is rejected by
      // the existing login-attempt guard, including server-family revocation.
      cancellation = Promise.resolve().then(() => presentation.cancel()).catch(error => {
        cancellationFailure = error
      })
      options.onClose?.()
    }
    progress({ phase: providerId === 'txnlab_web3auth' ? 'provider_auth' : 'connecting' })
    void (async () => {
      let failure = null
      try {
        tracker.start()
        prepared = await prepareAlgorandWalletLogin(wallet, options.algosdk, {
          walletName: entry.name, providerId,
          onProgress: progress,
          [UNITY_WALLET_PACKAGE_LOGIN_TRACKER]: tracker,
        })
        if (closed) throw cancelled()
        progress({ phase: 'approval' })
        const result = await completePreparedAlgorandWalletLogin(prepared)
        tracker.accepted(result)
        if (closed) throw cancelled()
        options.onComplete?.(result)
      } catch (error) {
        failure = error
        if (!closed) {
          if (providerId === 'txnlab_web3auth') {
            try { await presentation.cancel() } catch (cleanupError) { error = cleanupError; failure = cleanupError }
          }
          const failure = normalizedWalletError(error)
          options.onError?.(new BlockmakerError(failure.message, { code: failure.code, details: error }))
        }
      } finally {
        if (closed) {
          await cancellation
          // A cancellation may precede a slow provider/configuration response.
          // Close the connector again after that exact attempt has settled.
          try { await presentation.cancel() } catch (error) { cancellationFailure = error }
          if (cancellationFailure) failure = new BlockmakerError(
            'Wallet cleanup could not be confirmed.', { code: 'PROVIDER_CLEANUP_REQUIRED' })
        }
        tracker.settle(failure)
        presentation.dispose()
        closed = true
        accountDialogOpen = false
      }
    })()
    return Object.freeze({ close })
  }

  const openAccount = (options = {}) => {
    const doc = globalThis.document
    if (!doc?.body?.appendChild)
      throw new BlockmakerError('account.open() requires a browser.', { code: 'BROWSER_REQUIRED' })
    if (accountDialogOpen)
      throw new BlockmakerError('Player account is already open.', { code: 'ACCOUNT_DIALOG_ACTIVE' })
    const wallets = normalizedAccountWallets(options)
    accountDialogOpen = true
    let completed = false
    let navigating = false
    let config = null
    let emailWalletAbort = null
    let preparedWalletLoginCancel = null
    const packageLoginTracker = options[UNITY_WALLET_PACKAGE_LOGIN_TRACKER]
    const trackedPackageLogin = async action => {
      if (!packageLoginTracker) return action()
      packageLoginTracker.start()
      let failure = null
      try {
        const result = await action()
        packageLoginTracker.accepted(result)
        return result
      }
      catch (error) { failure = error; throw error }
      finally { packageLoginTracker.settle(failure) }
    }
    const fullscreen = beginFullscreenExit(doc)
    let dialog
    try {
      dialog = createUiDialog({
        kind: 'account',
        title: options.title ?? 'Sign in to play',
        description: 'Checking the secure sign-in choices available for this game.',
        appearance: options.appearance,
        onClose: () => {
          emailWalletAbort?.abort()
          emailWalletAbort = null
          preparedWalletLoginCancel?.()
          preparedWalletLoginCancel = null
          accountDialogOpen = false
          if (!completed && !navigating) options.onClose?.()
        },
      })
    } catch (error) {
      accountDialogOpen = false
      throw error
    }
    const { content } = dialog
    dialog.setState('loading')
    dialog.setStatus('Checking the sign-in options for this game…', 'info')

    const emitError = error => {
      try { options.onError?.(error) } catch { /* consumer callback */ }
    }
    const complete = () => {
      const result = session ? { ...session } : null
      completed = true
      dialog.close('completed')
      if (result) options.onComplete?.(result)
    }
    const policyLinks = () => {
      const policies = doc.createElement('div')
      policies.className = 'bm-ui-policies'
      policies.setAttribute('data-bm-part', 'policies')
      for (const [label, value] of [['Terms', options.termsUrl], ['Privacy', options.privacyUrl]]) {
        const href = safeExternalPageUrl(value)
        if (!href) continue
        const link = doc.createElement('a')
        link.className = 'bm-ui-link'
        link.href = href
        link.target = '_blank'
        link.rel = 'noopener noreferrer'
        link.textContent = label
        policies.append(link)
      }
      return policies
    }

    const renderConnected = () => {
      if (dialog.isClosed() || !session) return
      dialog.setBusy(false)
      dialog.setState('connected')
      dialog.setTitle('Your player account')
      dialog.setDescription('You’re signed in to this game. You can continue, edit your game profile, or sign out.')
      dialog.setStatus('', 'info')
      const card = doc.createElement('section')
      card.className = 'bm-ui-card'
      card.setAttribute('data-bm-part', 'account-summary')
      const name = doc.createElement('strong')
      name.className = 'bm-ui-card-title'
      name.textContent = session.displayName || 'Signed-in player'
      const address = doc.createElement('span')
      address.className = 'bm-ui-address'
      if (isAuthenticationOnlyEmailSession(session)) {
        address.title = ''
        address.textContent = 'Authentication-only email account. Do not send ALGO or assets here; connect Pera or Lute for on-chain features.'
      } else {
        address.title = session.walletAddress
        address.textContent = session.walletAddress
      }
      card.append(name, address)

      const actions = doc.createElement('div')
      actions.className = 'bm-ui-stack'
      actions.style.marginTop = '14px'
      const continueButton = uiButton(doc, 'Continue to game', 'primary', true)
      continueButton.setAttribute('data-bm-part', 'continue')
      continueButton.addEventListener('click', complete)
      const editButton = uiButton(doc, 'Edit game profile', 'default', true)
      editButton.setAttribute('data-bm-part', 'edit-profile')
      editButton.addEventListener('click', () => {
        navigating = true
        dialog.close('navigate')
        try {
          openProfile({ appearance: options.appearance, onError: options.onError })
        } catch (error) { emitError(error) }
      })
      const signOutButton = uiButton(doc, 'Sign out', 'quiet', true)
      signOutButton.setAttribute('data-bm-part', 'sign-out')
      signOutButton.addEventListener('click', async () => {
        if (dialog.isBusy()) return
        dialog.setBusy(true, 'Signing out…')
        let logoutError = null
        try { await logoutPlayer() }
        catch (error) {
          logoutError = error
          emitError(error)
        }
        dialog.setBusy(false)
        if (!config) {
          try { config = await request('/v1/integrations/config', { auth: false }) }
          catch (error) {
            dialog.setStatus(playerFacingError(error, 'Could not reload sign-in. Close this window and try again.'), 'error')
            emitError(error)
            return
          }
        }
        renderLogin()
        if (logoutError && !dialog.isClosed()) {
          dialog.setStatus(playerFacingError(
            logoutError,
            'Sign-out could not be confirmed. Reload this page before reconnecting your wallet.',
          ), 'error')
        }
      })
      actions.append(continueButton, editButton, signOutButton)
      dialog.replaceContent(card, actions)
      continueButton.focus?.({ preventScroll: true })
    }

    const finishConnected = () => {
      if (options[UNITY_WALLET_PACKAGE_COMPLETE_AFTER_LOGIN] === true) complete()
      else renderConnected()
    }

    const awaitDirectWalletApproval = (prepared, walletName) => new Promise((resolve, reject) => {
      let settled = false
      const settle = (handler, value) => {
        if (settled) return
        settled = true
        if (preparedWalletLoginCancel === cancelPrepared) preparedWalletLoginCancel = null
        handler(value)
      }
      const cancelPrepared = () => {
        discardPreparedAlgorandWalletLogin(prepared)
        settle(reject, new BlockmakerError(
          `${walletName} sign-in was cancelled before approval.`,
          { code: 'PLAYER_CANCELLED' },
        ))
      }
      preparedWalletLoginCancel?.()
      preparedWalletLoginCancel = cancelPrepared
      // The connection popup has closed. Restore the game dialog so the next
      // approval is an actual visible click, not a hidden/inert control.
      dialog.setProviderActive(false)
      dialog.setBusy(false)
      dialog.setState('wallet-approval')
      dialog.setTitle(`Approve with ${walletName}`)
      dialog.setDescription(
        'Your sign-in proof has been checked. Use the button below to open the wallet from this click; it signs only a zero-ALGO self-payment and does not submit it.',
      )
      dialog.setStatus('', 'info')
      const actions = doc.createElement('div')
      actions.className = 'bm-ui-stack'
      const approveButton = uiButton(doc, `Open ${walletName}`, 'primary', true)
      approveButton.setAttribute('data-bm-part', 'wallet-direct-sign')
      approveButton.addEventListener('click', event => {
        event.preventDefault()
        if (settled || dialog.isBusy()) return
        dialog.setBusy(true, `Waiting for ${walletName} approval…`)
        dialog.setProviderActive(true)
        let launched
        try {
          // This is intentionally the first provider operation in the final
          // click stack. Lute may synchronously open a popup here.
          launched = completePreparedAlgorandWalletLogin(prepared)
        } catch (error) {
          settle(reject, error)
          return
        }
        Promise.resolve(launched).then(
          result => settle(resolve, result),
          error => settle(reject, error),
        )
      })
      const cancelButton = uiButton(doc, 'Cancel', 'quiet', true)
      cancelButton.setAttribute('data-bm-part', 'wallet-direct-sign-cancel')
      cancelButton.addEventListener('click', event => {
        event.preventDefault()
        cancelPrepared()
      })
      actions.append(approveButton, cancelButton)
      dialog.replaceContent(actions)
      approveButton.focus?.({ preventScroll: true })
    })

    const renderEmailCode = email => {
      if (dialog.isClosed()) return
      dialog.setBusy(false)
      dialog.setState('email-code')
      dialog.setTitle('Check your email')
      dialog.setDescription(`Enter the code sent to ${email}. It expires after a short time.`)
      dialog.setStatus('', 'info')
      const form = doc.createElement('form')
      form.noValidate = true
      form.className = 'bm-ui-stack'
      const label = doc.createElement('label')
      label.className = 'bm-ui-field'
      label.htmlFor = `bm-account-code-${gameId}`
      label.textContent = 'Sign-in code'
      const code = doc.createElement('input')
      code.className = 'bm-ui-input'
      code.id = label.htmlFor
      code.type = 'text'
      code.inputMode = 'numeric'
      code.autocomplete = 'one-time-code'
      code.maxLength = 12
      code.required = true
      code.setAttribute('data-bm-part', 'email-code')
      code.setAttribute('data-bm-disable-while-busy', '')
      const verifyButton = uiButton(doc, 'Verify and continue', 'primary', true)
      verifyButton.type = 'submit'
      const changeButton = uiButton(doc, 'Use a different email', 'quiet', true)
      changeButton.addEventListener('click', () => renderLogin(email))
      form.append(label, code, verifyButton, changeButton)
      form.addEventListener('submit', async event => {
        event.preventDefault()
        if (dialog.isBusy()) return
        const otp = code.value.trim()
        if (!otp) { dialog.setStatus('Enter the code from your email.', 'error'); code.focus(); return }
        dialog.setBusy(true, 'Verifying your code…')
        try {
          await trackedPackageLogin(() => verifyEmail(email, otp))
          if (!dialog.isClosed()) finishConnected()
        } catch (error) {
          if (!dialog.isClosed()) {
            dialog.setBusy(false)
            dialog.setStatus(playerFacingError(error, 'That code could not be verified. Check it and try again.'), 'error')
            code.focus?.({ preventScroll: true })
          }
          emitError(error)
        }
      })
      dialog.replaceContent(form)
      code.focus?.({ preventScroll: true })
    }

    const chooseFeatured = availableWallets => {
      const requested = Array.isArray(options.featuredWalletIds)
        ? options.featuredWalletIds.map(value => String(value).trim().toLowerCase()).slice(0, 3)
        : []
      let featured = requested.length
        ? requested.map(id => availableWallets.find(wallet => wallet.id === id)).filter(Boolean)
        : availableWallets.filter(wallet => wallet.featured).slice(0, 3)
      if (!featured.length) {
        const firstAlgorand = availableWallets.find(wallet => wallet.chain === 'algorand')
        const firstEvm = availableWallets.find(wallet => wallet.chain === 'evm')
        featured = [firstAlgorand, firstEvm].filter(Boolean)
      }
      if (!featured.length && availableWallets[0]) featured = [availableWallets[0]]
      const ids = new Set(featured.map(wallet => wallet.id))
      return { featured, more: availableWallets.filter(wallet => !ids.has(wallet.id)) }
    }

    const renderLogin = preferredEmail => {
      if (dialog.isClosed() || !config) return
      dialog.setBusy(false)
      dialog.setState('sign-in')
      dialog.setTitle(options.title ?? 'Sign in to play')
      const auth = config.auth ?? {}
      const txnLabEmailWallet = wallets.algorand.find(wallet =>
        wallet.providerId === 'txnlab_web3auth') ?? null
      const txnLabEmailAvailable = auth.txnLabWeb3Auth === true && txnLabEmailWallet !== null
      const emailChoice = options.email === undefined ? 'auto' : options.email
      const emailMode = emailChoice === false ? null
        : emailChoice === 'txnlab_web3auth' ? (txnLabEmailAvailable ? 'txnlab_web3auth' : null)
          : emailChoice === 'web3auth_avm' ? (auth.web3AuthAvmEmail ? 'web3auth_avm' : null)
          : emailChoice === 'blockmaker' ? (auth.email ? 'blockmaker' : null)
          : emailChoice === 'magic_xchain' ? (auth.magicXchain && options.magicXchain ? 'magic_xchain' : null)
          : emailChoice === 'magic' ? (auth.magic && options.magic ? 'magic' : null)
            : txnLabEmailAvailable ? 'txnlab_web3auth'
              : auth.web3AuthAvmEmail ? 'web3auth_avm'
                : auth.magicXchain && options.magicXchain ? 'magic_xchain'
                : auth.magic && options.magic ? 'magic'
                : auth.email ? 'blockmaker' : null
      dialog.setDescription(
        emailMode === 'txnlab_web3auth'
          ? 'Continue with email or Google to create or restore your Algorand wallet, or connect a wallet you already use.'
          : emailMode
            ? 'Use email for the simplest setup, or connect a wallet you already use.'
            : 'Connect a wallet to create your secure game account.',
      )
      dialog.setStatus('', 'info')
      const providerCatalog = Array.isArray(config.walletProviders) ? config.walletProviders : []
      const allowUnlistedAdapters = config.walletProviderPolicy == null
        || config.walletProviderPolicy.allowUnlistedAdapters === true
      const availableWallets = wallets.all
        .filter(wallet => wallet.chain === 'algorand' ? auth.algorandWallet === true : auth.evmWallet === true)
        .filter(wallet => {
          const provider = providerCatalog.find(item => item?.id === wallet.providerId)
          // Unknown custom adapters remain supported for backwards compatibility.
          // Known compatibility adapters also remain injectable by existing games,
          // but are never advertised by wallets.available() for new integrations.
          return provider?.enabled === true
            || (allowUnlistedAdapters && (!provider || provider.status === 'legacy_compatibility'))
        })
        .map(wallet => {
          const provider = providerCatalog.find(item => item?.id === wallet.providerId)
          return { ...wallet, featured: wallet.featured || provider?.featured === true }
        })
        // The embedded wallet is already the primary email/Google action. Showing the
        // same adapter again under "Or use a wallet" is confusing and can open
        // two concurrent provider requests from one account dialog.
        .filter(wallet => emailMode !== 'txnlab_web3auth'
          || wallet.providerId !== 'txnlab_web3auth')
      const nodes = []

      if (emailMode === 'txnlab_web3auth') {
        const emailStack = doc.createElement('div')
        emailStack.className = 'bm-ui-stack'
        emailStack.setAttribute('data-bm-part', 'email-wallet')
        const emailButton = uiButton(doc, 'Continue with Email or Google', 'primary', true)
        emailButton.setAttribute('data-bm-part', 'txnlab-email-wallet-submit')
        const note = doc.createElement('p')
        note.className = 'bm-ui-help'
        note.append(
          doc.createTextNode('TxnLab uses email passwordless or Google through MetaMask Embedded Wallets/Web3Auth to create or restore a real Algorand wallet in this browser. It can approve normal transactions. Blockmaker’s API receives its public address and signed transactions, never its private key. See the provider’s '),
        )
        const providerPrivacy = doc.createElement('a')
        providerPrivacy.href = 'https://legal.consensys.io/metamask/privacy-policy/'
        providerPrivacy.target = '_blank'
        providerPrivacy.rel = 'noopener noreferrer'
        providerPrivacy.textContent = 'privacy policy'
        const providerTerms = doc.createElement('a')
        providerTerms.href = 'https://legal.consensys.io/metamask/terms-of-use/'
        providerTerms.target = '_blank'
        providerTerms.rel = 'noopener noreferrer'
        providerTerms.textContent = 'terms of use'
        note.append(providerPrivacy, doc.createTextNode(' and '), providerTerms, doc.createTextNode('.'))
        emailButton.addEventListener('click', event => {
          event.preventDefault()
          if (dialog.isBusy() || !txnLabEmailWallet) return
          dialog.setBusy(true, 'Opening Email or Google…')
          dialog.setProviderActive(true)
          void trackedPackageLogin(() => loginWithAlgorandWallet(
            txnLabEmailWallet.wallet,
            options.algosdk,
            { walletName: 'Email or Google', providerId: 'txnlab_web3auth', providerPrechecked: true },
          ))
            .then(() => {
              if (!dialog.isClosed()) {
                dialog.setProviderActive(false)
                finishConnected()
              }
            })
            .catch(error => {
              if (!dialog.isClosed()) {
                dialog.setProviderActive(false)
                dialog.setBusy(false)
                const publicError = normalizedWalletError(error)
                dialog.setStatus(
                  publicError.code === 'PLAYER_CANCELLED'
                    ? publicError.message
                    : playerFacingError(error, 'Email or Google sign-in did not finish. Please try again.'),
                  publicError.code === 'PLAYER_CANCELLED' ? 'info' : 'error',
                )
                emailButton.focus?.({ preventScroll: true })
              }
              emitError(error)
            })
        })
        emailStack.append(emailButton, note)
        nodes.push(emailStack)
      } else if (emailMode === 'web3auth_avm') {
        const emailStack = doc.createElement('div')
        emailStack.className = 'bm-ui-stack'
        emailStack.setAttribute('data-bm-part', 'email-wallet')
        const emailButton = uiButton(doc, 'Continue with email', 'primary', true)
        emailButton.setAttribute('data-bm-part', 'email-wallet-submit')
        const note = doc.createElement('p')
        note.className = 'bm-ui-help'
        note.append(
          doc.createTextNode('MetaMask Embedded Wallets/Web3Auth is a third-party processor. Blockmaker-served broker code handles your email in memory during sign-in; the Blockmaker API and game do not receive it. The provider may use its own cookies, storage, and telemetry. See the provider’s '),
        )
        const providerPrivacy = doc.createElement('a')
        providerPrivacy.href = 'https://legal.consensys.io/metamask/privacy-policy/'
        providerPrivacy.target = '_blank'
        providerPrivacy.rel = 'noopener noreferrer'
        providerPrivacy.textContent = 'privacy policy'
        const providerTerms = doc.createElement('a')
        providerTerms.href = 'https://legal.consensys.io/metamask/terms-of-use/'
        providerTerms.target = '_blank'
        providerTerms.rel = 'noopener noreferrer'
        providerTerms.textContent = 'terms of use'
        note.append(providerPrivacy, doc.createTextNode(' and '), providerTerms, doc.createTextNode('.'))
        emailButton.addEventListener('click', event => {
          event.preventDefault()
          if (dialog.isBusy()) return
          dialog.setBusy(true, 'Opening secure email sign-in…')
          dialog.setProviderActive(true)
          const controller = new AbortController()
          emailWalletAbort = controller
          // Start synchronously inside this click. Waiting before the call can
          // consume browser user activation and cause a safe popup rejection.
          void trackedPackageLogin(() => loginWithWeb3AuthAvmEmail({
            signal: controller.signal,
            [UNITY_WALLET_PACKAGE_LOGIN_TRACKER]: packageLoginTracker,
          }))
            .then(() => {
              if (emailWalletAbort === controller) emailWalletAbort = null
              if (!dialog.isClosed()) {
                dialog.setProviderActive(false)
                finishConnected()
              }
            })
            .catch(error => {
              if (emailWalletAbort === controller) emailWalletAbort = null
              if (!dialog.isClosed()) {
                dialog.setProviderActive(false)
                dialog.setBusy(false)
                const publicError = normalizedWalletError(error)
                dialog.setStatus(publicError.message,
                  publicError.code === 'PLAYER_CANCELLED' ? 'info' : 'error')
                emailButton.focus?.({ preventScroll: true })
              }
              emitError(error)
            })
        })
        emailStack.append(emailButton, note)
        nodes.push(emailStack)
      } else if (emailMode) {
        const form = doc.createElement('form')
        form.noValidate = true
        form.className = 'bm-ui-stack'
        form.setAttribute('data-bm-part', 'email-form')
        const label = doc.createElement('label')
        label.className = 'bm-ui-field'
        label.htmlFor = `bm-account-email-${gameId}`
        label.textContent = 'Email address'
        const email = doc.createElement('input')
        email.className = 'bm-ui-input'
        email.id = label.htmlFor
        email.type = 'email'
        email.inputMode = 'email'
        email.autocomplete = 'email'
        email.placeholder = 'you@example.com'
        email.required = true
        email.value = String(preferredEmail ?? '')
        email.setAttribute('data-bm-part', 'email-input')
        email.setAttribute('data-bm-disable-while-busy', '')
        const emailButton = uiButton(doc, 'Continue with email', 'primary', true)
        emailButton.type = 'submit'
        emailButton.setAttribute('data-bm-part', 'email-submit')
        form.append(label, email, emailButton)
        form.addEventListener('submit', async event => {
          event.preventDefault()
          if (dialog.isBusy()) return
          const normalizedEmail = email.value.trim().toLowerCase()
          if (!normalizedEmail || !normalizedEmail.includes('@')) {
            dialog.setStatus('Enter a valid email address.', 'error')
            email.focus()
            return
          }
          dialog.setBusy(true, emailMode === 'magic' || emailMode === 'magic_xchain' ? 'Opening secure email sign-in…' : 'Sending your sign-in code…')
          try {
            if (emailMode === 'magic' || emailMode === 'magic_xchain') {
              dialog.setProviderActive(true)
              await fullscreen.ready
              if (emailMode === 'magic_xchain') await loginWithMagicEmailXChain(options.magicXchain, normalizedEmail)
              else await loginWithMagicEmail(options.magic, normalizedEmail)
              if (!dialog.isClosed()) { dialog.setProviderActive(false); finishConnected() }
            } else {
              await request('/v1/auth/email/request', { method: 'POST', auth: false, body: { email: normalizedEmail, gameId } })
              if (!dialog.isClosed()) renderEmailCode(normalizedEmail)
            }
          } catch (error) {
            if (!dialog.isClosed()) {
              dialog.setProviderActive(false)
              dialog.setBusy(false)
              const publicError = normalizedWalletError(error)
              dialog.setStatus(
                publicError.code === 'PLAYER_CANCELLED'
                  ? publicError.message
                  : playerFacingError(error, 'Email sign-in did not finish. Please try again.'),
                publicError.code === 'PLAYER_CANCELLED' ? 'info' : 'error',
              )
              email.focus?.({ preventScroll: true })
            }
            emitError(error)
          }
        })
        nodes.push(form)
      }

      if (emailMode && availableWallets.length) {
        const divider = doc.createElement('div')
        divider.className = 'bm-ui-divider'
        divider.setAttribute('aria-hidden', 'true')
        divider.textContent = 'Or use a wallet'
        nodes.push(divider)
      }
      if (availableWallets.length) {
        const { featured, more } = chooseFeatured(availableWallets)
        const makeWalletButton = wallet => {
          const button = uiButton(doc, '', 'default', true)
          button.classList.add('bm-ui-wallet')
          button.setAttribute('data-bm-part', 'wallet-button')
          button.setAttribute('data-bm-wallet-id', wallet.id)
          button.setAttribute('data-bm-chain', wallet.chain)
          const mark = doc.createElement('span')
          mark.className = 'bm-ui-wallet-mark'
          mark.setAttribute('aria-hidden', 'true')
          if (wallet.iconUrl) {
            const icon = doc.createElement('img')
            icon.src = wallet.iconUrl
            icon.alt = ''
            icon.referrerPolicy = 'no-referrer'
            icon.addEventListener('error', () => {
              icon.remove()
              mark.textContent = wallet.name.split(/\s+/).map(word => word[0]).join('').slice(0, 2).toUpperCase()
            }, { once: true })
            mark.append(icon)
          } else {
            mark.textContent = wallet.name.split(/\s+/).map(word => word[0]).join('').slice(0, 2).toUpperCase()
          }
          const walletName = doc.createElement('span')
          walletName.className = 'bm-ui-wallet-name'
          walletName.textContent = wallet.name
          const chain = doc.createElement('span')
          chain.className = 'bm-ui-chain'
          chain.textContent = wallet.chain === 'evm' ? 'EVM' : 'Algorand'
          button.append(mark, walletName, chain)
          button.addEventListener('click', async () => {
            if (dialog.isBusy()) return
            const stagedLute = options[UNITY_WALLET_PACKAGE_STAGED_LUTE_LOGIN] === true
              && wallet.chain === 'algorand' && wallet.providerId === 'lute'
            dialog.setBusy(true, `Waiting for ${wallet.name} approval…`)
            dialog.setProviderActive(true)
            try {
              // renderLogin runs only after fullscreen.ready. Do not insert a
              // fresh promise hop before a staged provider connection.
              if (!stagedLute) await fullscreen.ready
              if (wallet.chain === 'evm') await trackedPackageLogin(() => loginWithEvmWallet(wallet.wallet, {
                walletName: wallet.name,
                providerId: wallet.providerId ?? 'xchain',
              }))
              else if (stagedLute) {
                await trackedPackageLogin(async () => {
                  const prepared = await prepareAlgorandWalletLogin(wallet.wallet, options.algosdk, {
                    walletName: wallet.name,
                    providerId: 'lute',
                    providerPrechecked: true,
                    connectImmediately: true,
                    [UNITY_WALLET_PACKAGE_LOGIN_TRACKER]: packageLoginTracker,
                  })
                  return awaitDirectWalletApproval(prepared, wallet.name)
                })
              } else {
                await trackedPackageLogin(() => loginWithAlgorandWallet(wallet.wallet, options.algosdk, {
                  walletName: wallet.name,
                  ...(wallet.providerId ? { providerId: wallet.providerId } : {}),
                  [UNITY_WALLET_PACKAGE_LOGIN_TRACKER]: packageLoginTracker,
                }))
              }
              if (!dialog.isClosed()) { dialog.setProviderActive(false); finishConnected() }
            } catch (error) {
              if (!dialog.isClosed()) {
                dialog.setProviderActive(false)
                dialog.setBusy(false)
                // A cancelled/rejected staged proof was consumed or discarded.
                // Return to fresh provider controls instead of leaving its
                // settled approval button on screen.
                if (stagedLute) renderLogin()
                const publicError = normalizedWalletError(error)
                dialog.setStatus(
                  publicError.code === 'PLAYER_CANCELLED'
                    ? publicError.message
                    : playerFacingError(error, `${wallet.name} sign-in did not finish. Please try again.`),
                  publicError.code === 'PLAYER_CANCELLED' ? 'info' : 'error',
                )
                button.focus?.({ preventScroll: true })
              }
              emitError(error)
            }
          })
          return button
        }
        const featuredGrid = doc.createElement('div')
        featuredGrid.className = 'bm-ui-wallet-grid'
        featuredGrid.setAttribute('data-bm-part', 'featured-wallets')
        featuredGrid.setAttribute('aria-label', 'Featured wallets')
        featuredGrid.append(...featured.map(makeWalletButton))
        nodes.push(featuredGrid)
        if (more.length) {
          const toggle = uiButton(doc, String(options.moreWalletsLabel ?? `More wallets (${more.length})`), 'quiet', true)
          toggle.classList.add('bm-ui-more-toggle')
          toggle.setAttribute('data-bm-part', 'more-wallets-toggle')
          toggle.setAttribute('aria-expanded', 'false')
          const moreId = `bm-more-wallets-${gameId}`
          toggle.setAttribute('aria-controls', moreId)
          const moreList = doc.createElement('div')
          moreList.id = moreId
          moreList.className = 'bm-ui-more'
          moreList.setAttribute('data-bm-part', 'more-wallets')
          moreList.hidden = true
          moreList.append(...more.map(makeWalletButton))
          toggle.addEventListener('click', () => {
            const expanded = toggle.getAttribute('aria-expanded') !== 'true'
            toggle.setAttribute('aria-expanded', expanded ? 'true' : 'false')
            moreList.hidden = !expanded
          })
          nodes.push(toggle, moreList)
        }
        const walletHelp = doc.createElement('p')
        walletHelp.className = 'bm-ui-help'
        walletHelp.textContent = 'Signing in never sends ALGO or assets. A real transaction always opens a separate wallet review. Blockmaker never asks for recovery words or private keys.'
        nodes.push(walletHelp)
      }
      if (options.allowGuest === true) {
        const divider = doc.createElement('div')
        divider.className = 'bm-ui-divider'
        divider.setAttribute('aria-hidden', 'true')
        divider.textContent = 'Or play without an account'
        const guestButton = uiButton(doc, String(options.guestLabel ?? 'Continue as guest'), 'quiet', true)
        guestButton.setAttribute('data-bm-part', 'continue-as-guest')
        guestButton.addEventListener('click', () => {
          if (dialog.isBusy()) return
          completed = true
          dialog.close('guest')
          try { options.onGuest?.() } catch { /* consumer callback */ }
        })
        nodes.push(divider, guestButton)
      }
      const policies = policyLinks()
      if (policies.childNodes.length) nodes.push(policies)
      if (!emailMode && !availableWallets.length) {
        const empty = doc.createElement('section')
        empty.className = 'bm-ui-card'
        const heading = doc.createElement('strong')
        heading.className = 'bm-ui-card-title'
        heading.textContent = 'Sign-in is not available yet'
        const copy = doc.createElement('p')
        copy.className = 'bm-ui-card-copy'
        copy.textContent = 'This game’s secure sign-in choices are still being set up. Close this window and try again later.'
        empty.append(heading, copy)
        nodes.push(empty)
      }
      dialog.replaceContent(...nodes)
      dialog.focusFirst()
    }

    void fullscreen.ready.then(async () => {
      if (dialog.isClosed()) return
      if (session?.sessionToken) {
        try {
          await getPlayerSession()
              if (!dialog.isClosed()) { finishConnected(); return }
        } catch (error) {
          if (error instanceof BlockmakerError
            && (error.status === 401 || error.code === 'PROVIDER_NOT_ENABLED')) storeSession(null)
          else emitError(error)
        }
      }
      try {
        config = await request('/v1/integrations/config', { auth: false })
        if (dialog.isClosed()) return
        if (config.browserOriginAllowed === false) {
          dialog.setState('configuration-error')
          dialog.setTitle('This web address is not connected')
          dialog.setDescription('The game owner needs to allow this exact website in Blockmaker before players can sign in.')
          dialog.replaceContent()
          dialog.setStatus(config.browserOrigin?.action || 'Add this exact website under Blockmaker → Connect game → Live game address.', 'error')
          return
        }
        renderLogin()
      } catch (error) {
        if (!dialog.isClosed()) {
          dialog.setState('error')
          dialog.setStatus(playerFacingError(error, 'Could not load sign-in. Check your connection and try again.'), 'error')
        }
        emitError(error)
      }
    })
    return Object.freeze({ close: () => dialog.close('dismissed') })
  }

  const openProfile = (options = {}) => {
    const doc = globalThis.document
    if (!doc?.body?.appendChild)
      throw new BlockmakerError('profile.open() requires a browser.', { code: 'BROWSER_REQUIRED' })
    if (!session?.sessionToken)
      throw new BlockmakerError('Sign in before editing your game profile.', { status: 401, code: 'AUTH_MISSING' })
    if (profileDialogOpen)
      throw new BlockmakerError('Game profile is already open.', { code: 'PROFILE_DIALOG_ACTIVE' })

    profileDialogOpen = true
    const fullscreen = beginFullscreenExit(doc)
    let dialog
    try {
      dialog = createUiDialog({
        kind: 'profile',
        title: options.title ?? 'Your player profile',
        description: 'Your universal username follows you across Blockmaker games. Each game can still use a different NFT profile picture.',
        appearance: options.appearance,
        onClose: () => {
          profileDialogOpen = false
          try { options.onClose?.() } catch { /* consumer callback */ }
        },
      })
    } catch (error) {
      profileDialogOpen = false
      throw error
    }

    let latest = null
    let loadSequence = 0
    const emitError = error => {
      try { options.onError?.(error) } catch { /* consumer callback */ }
    }
    const emitSave = profile => {
      try { options.onSave?.(profile) } catch { /* consumer callback */ }
    }
    const profileFrom = response => response?.profile && typeof response.profile === 'object' ? response.profile : null
    const shortAddress = value => {
      const address = String(value ?? '')
      return address.length > 18 ? `${address.slice(0, 9)}…${address.slice(-7)}` : address
    }
    const avatar = (profile, size = 'large') => {
      const holder = doc.createElement('div')
      holder.className = 'bm-ui-avatar'
      holder.setAttribute('data-bm-part', size === 'large' ? 'profile-avatar' : 'nft-preview-image')
      const imageUrl = safeImageUrl(profile?.profileImageUrl)
      if (imageUrl) {
        const image = doc.createElement('img')
        image.src = imageUrl
        image.alt = `${profile?.displayName || 'Player'} profile picture`
        image.referrerPolicy = 'no-referrer'
        image.addEventListener('error', () => {
          image.remove()
          holder.textContent = String(profile?.displayName || '?').trim().slice(0, 1).toUpperCase() || '?'
        }, { once: true })
        holder.append(image)
      } else {
        holder.textContent = String(profile?.displayName || '?').trim().slice(0, 1).toUpperCase() || '?'
      }
      return holder
    }

    const render = response => {
      if (dialog.isClosed()) return
      const profile = profileFrom(response)
      if (!profile) {
        dialog.setState('error')
        dialog.setStatus('Blockmaker returned an invalid profile. Close this window and try again.', 'error')
        return
      }
      latest = response
      dialog.setBusy(false)
      dialog.setState('ready')
      dialog.setStatus('', 'info')

      const summary = doc.createElement('section')
      summary.className = 'bm-ui-profile-head'
      summary.setAttribute('data-bm-part', 'profile-summary')
      const identity = doc.createElement('div')
      const displayName = doc.createElement('strong')
      displayName.className = 'bm-ui-card-title'
      displayName.textContent = String(profile.displayName || 'Player')
      const address = doc.createElement('span')
      address.className = 'bm-ui-address'
      address.title = String(profile.walletAddress ?? response.walletAddress ?? '')
      address.textContent = shortAddress(address.title)
      identity.append(displayName, address)
      summary.append(avatar(profile), identity)

      const usernameSection = doc.createElement('section')
      usernameSection.className = 'bm-ui-section'
      usernameSection.setAttribute('data-bm-part', 'username-section')
      const usernameTitle = doc.createElement('h3')
      usernameTitle.className = 'bm-ui-section-title'
      usernameTitle.textContent = 'Universal username'
      const usernameCopy = doc.createElement('p')
      usernameCopy.className = 'bm-ui-section-copy'
      usernameCopy.textContent = 'One username follows this wallet across every game that uses Blockmaker. Registering a new name requires an Algorand wallet payment that the player reviews first.'
      usernameSection.append(usernameTitle, usernameCopy)
      if (profile.username) {
        const currentName = doc.createElement('section')
        currentName.className = 'bm-ui-card'
        currentName.setAttribute('data-bm-part', 'username-current')
        const currentLabel = doc.createElement('span')
        currentLabel.className = 'bm-ui-help'
        currentLabel.textContent = 'Your current name everywhere'
        const currentValue = doc.createElement('strong')
        currentValue.className = 'bm-ui-card-title'
        currentValue.textContent = String(profile.username)
        currentName.append(currentLabel, currentValue)
        usernameSection.append(currentName)
      }
      const ownedNames = Array.isArray(response?.universalNames)
        ? response.universalNames.filter(value => value && typeof value.username === 'string')
        : []
      if (ownedNames.length > 1) {
        const choices = doc.createElement('div')
        choices.className = 'bm-ui-stack'
        choices.setAttribute('data-bm-part', 'username-choices')
        const choiceHelp = doc.createElement('p')
        choiceHelp.className = 'bm-ui-help'
        choiceHelp.textContent = 'You own more than one name. Choose which one games display.'
        choices.append(choiceHelp)
        for (const entry of ownedNames) {
          if (entry.username === profile.username) continue
          const choose = uiButton(doc, `Use ${entry.username} everywhere`, 'default', true)
          choose.setAttribute('data-bm-part', 'username-primary')
          choose.addEventListener('click', async () => {
            if (dialog.isBusy()) return
            dialog.setBusy(true, `Choosing ${entry.username}…`)
            try {
              const updated = await setPrimaryUniversalUsername(entry.username)
              emitSave(updated.profile)
              await load(false)
              if (!dialog.isClosed()) dialog.setStatus(`${entry.username} is now your name in every Blockmaker game.`, 'success')
            } catch (error) {
              if (!dialog.isClosed()) {
                dialog.setBusy(false)
                dialog.setStatus(playerFacingError(error, 'Could not choose that name. Nothing changed.'), 'error')
              }
              emitError(error)
            }
          })
          choices.append(choose)
        }
        usernameSection.append(choices)
      }
      // Paid registration fails closed unless readiness is exact.
      const registrationState = String(response?.usernameRegistration?.state ?? 'unavailable')
      if (registrationState === 'ready') {
      const usernameForm = doc.createElement('form')
      usernameForm.className = 'bm-ui-stack'
      usernameForm.noValidate = true
      const usernameLabel = doc.createElement('label')
      usernameLabel.className = 'bm-ui-field'
      usernameLabel.htmlFor = `bm-profile-username-${gameId}`
      usernameLabel.textContent = profile.username ? 'Register another name' : 'Choose your username'
      const usernameRow = doc.createElement('div')
      usernameRow.className = 'bm-ui-row'
      const usernameInput = doc.createElement('input')
      usernameInput.className = 'bm-ui-input'
      usernameInput.id = usernameLabel.htmlFor
      usernameInput.type = 'text'
      usernameInput.autocomplete = 'username'
      usernameInput.autocapitalize = 'none'
      usernameInput.spellcheck = false
      usernameInput.minLength = 3
      usernameInput.maxLength = 20
      usernameInput.pattern = '[a-z0-9_-]{3,20}'
      usernameInput.placeholder = 'player_name'
      usernameInput.value = ''
      usernameInput.setAttribute('data-bm-part', 'username-input')
      usernameInput.setAttribute('data-bm-disable-while-busy', '')
      const saveUsername = uiButton(doc, 'Check availability', 'primary')
      saveUsername.type = 'submit'
      saveUsername.setAttribute('data-bm-part', 'username-save')
      usernameRow.append(usernameInput, saveUsername)
      const usernameRules = doc.createElement('p')
      usernameRules.className = 'bm-ui-help'
      usernameRules.textContent = 'Use 3 to 20 lowercase letters, numbers, hyphens or underscores. A hyphen or underscore cannot be first or last.'
      const usernameReview = doc.createElement('div')
      usernameReview.setAttribute('data-bm-part', 'username-review')
      usernameForm.append(usernameLabel, usernameRow, usernameRules, usernameReview)
      usernameInput.addEventListener('input', () => usernameReview.replaceChildren())
      usernameForm.addEventListener('submit', async event => {
        event.preventDefault()
        if (dialog.isBusy()) return
        const username = usernameInput.value.trim().toLowerCase()
        if (!/^[a-z0-9_-]{3,20}$/.test(username) || /^[-_]|[-_]$/.test(username)) {
          dialog.setStatus('Use 3–20 letters, numbers, hyphens, or underscores, without one at either end.', 'error')
          usernameInput.focus()
          return
        }
        dialog.setBusy(true, 'Checking that username…')
        try {
          const checked = await checkUniversalUsername(username)
          if (!checked?.available) {
            if (!dialog.isClosed()) {
              dialog.setBusy(false)
              dialog.setStatus(checked?.reason || 'That username is not available. Try another one.', 'error')
              usernameInput.focus?.({ preventScroll: true })
            }
            return
          }
          const quote = checked.quote
          const registrationPrice = formatAlgoBalance(Number(quote?.registrationPriceMicroalgo))
          const amountDue = formatAlgoBalance(Number(quote?.amountDueMicroalgo))
          const storage = formatAlgoBalance(Number(quote?.storageDepositMicroalgo))
          const fees = formatAlgoBalance(Number(quote?.networkFeeMicroalgo))
          if (!registrationPrice || !amountDue || !storage || !fees)
            throw new BlockmakerError('Blockmaker returned an invalid username quote. Nothing was signed.', { code: 'TX_INVALID' })
          dialog.setBusy(false)
          dialog.setStatus('', 'info')
          const review = doc.createElement('section')
          review.className = 'bm-ui-card'
          review.setAttribute('data-bm-part', 'username-price-review')
          const heading = doc.createElement('strong')
          heading.className = 'bm-ui-card-title'
          heading.textContent = `${checked.username} is available`
          const copy = doc.createElement('p')
          copy.className = 'bm-ui-card-copy'
          copy.textContent = `Registration is ${registrationPrice}. The one-time Algorand storage deposit is ${storage} and is returned if you later release this new name. Network fees are ${fees}. The exact total shown by the wallet is ${amountDue}.`
          const split = doc.createElement('details')
          split.className = 'bm-ui-card'
          split.setAttribute('data-bm-part', 'username-revenue-split')
          const splitSummary = doc.createElement('summary')
          splitSummary.textContent = 'Where the registration price goes'
          const splitRows = [
            ['50% to this game', quote?.gameRevenueAddress],
            ['25% to Blockmaker', quote?.beneficiaryAAddress],
            ['25% to Blockmaker', quote?.beneficiaryBAddress],
          ]
          split.append(splitSummary)
          for (const [label, recipient] of splitRows) {
            const row = doc.createElement('p')
            row.className = 'bm-ui-help'
            row.textContent = `${label}: ${shortAddress(recipient)}`
            row.title = String(recipient ?? '')
            split.append(row)
          }
          const register = uiButton(doc, `Register for ${amountDue}`, 'primary', true)
          register.type = 'button'
          register.setAttribute('data-bm-part', 'username-register')
          register.addEventListener('click', async () => {
            if (dialog.isBusy()) return
            dialog.setBusy(true, 'Preparing the exact Algorand payment…')
            try {
              const prepared = await prepareUniversalUsername(checked.username || username)
              const quotedFields = value => JSON.stringify([
                value?.registrationNumber,
                value?.registrationPriceMicroalgo,
                value?.storageDepositMicroalgo,
                value?.networkFeeMicroalgo,
                value?.amountDueMicroalgo,
                value?.gameShareMicroalgo,
                value?.beneficiaryAShareMicroalgo,
                value?.beneficiaryBShareMicroalgo,
                value?.gameRevenueAddress,
                value?.beneficiaryAAddress,
                value?.beneficiaryBAddress,
              ])
              if (quotedFields(prepared?.quote) !== quotedFields(quote)) {
                dialog.setBusy(false)
                usernameReview.replaceChildren()
                dialog.setStatus('The exact price or income address changed before signing. Nothing was submitted. Check the name again to review the new total.', 'info')
                usernameInput.focus?.({ preventScroll: true })
                return
              }
              const updated = await signAndCompleteUniversalUsername(prepared, {
                algosdk: options.algosdk,
                wallet: options.wallet,
                signTransactions: options.signTransactions,
              })
              if (!dialog.isClosed()) {
                emitSave(updated.profile)
                await load(false)
                if (!dialog.isClosed()) dialog.setStatus('Your universal username is confirmed and ready in every Blockmaker game.', 'success')
              }
            } catch (error) {
              if (!dialog.isClosed()) {
                dialog.setBusy(false)
                dialog.setStatus(playerFacingError(error, 'Could not register that username. No replacement payment was prepared.'), 'error')
                register.focus?.({ preventScroll: true })
              }
              emitError(error)
            }
          })
          review.append(heading, copy, split, register)
          usernameReview.replaceChildren(review)
          register.focus?.({ preventScroll: true })
        } catch (error) {
          if (!dialog.isClosed()) {
            dialog.setBusy(false)
            dialog.setStatus(playerFacingError(error, 'Could not check that username. Please try again.'), 'error')
            usernameInput.focus?.({ preventScroll: true })
          }
          emitError(error)
        }
      })
      usernameSection.append(usernameForm)
      } else {
        const readiness = doc.createElement('section')
        readiness.className = 'bm-ui-card'
        readiness.setAttribute('data-bm-part', 'username-readiness')
        const readinessTitle = doc.createElement('strong')
        readinessTitle.className = 'bm-ui-card-title'
        readinessTitle.textContent = registrationState === 'paused'
          ? 'New names are paused for now'
          : registrationState === 'owner_setup_required'
            ? 'New names are coming soon'
            : 'New names are temporarily unavailable'
        const readinessCopy = doc.createElement('p')
        readinessCopy.className = 'bm-ui-card-copy'
        readinessCopy.textContent = String(response?.usernameRegistration?.reason
          || 'You can still play, use an existing Blockmaker username, and choose an owned NFT profile picture.')
        readiness.append(readinessTitle, readinessCopy)
        usernameSection.append(readiness)
      }

      const nftSection = doc.createElement('section')
      nftSection.className = 'bm-ui-section'
      nftSection.setAttribute('data-bm-part', 'nft-section')
      const nftTitle = doc.createElement('h3')
      nftTitle.className = 'bm-ui-section-title'
      nftTitle.textContent = 'Profile picture'
      const nftCopy = doc.createElement('p')
      nftCopy.className = 'bm-ui-section-copy'
      nftCopy.textContent = profile.profilePicStatus === 'not_owned'
        ? `NFT asset #${profile.profilePicAssetId} is no longer held by this wallet, so its public picture is hidden. Choose another NFT whenever you like.`
        : profile.profilePicStatus === 'verification_delayed'
          ? `Using NFT asset #${profile.profilePicAssetId}. Its latest ownership or artwork check was delayed, so Blockmaker has kept the last verified picture.`
          : profile.profilePicStatus === 'refreshing'
            ? `Using NFT asset #${profile.profilePicAssetId}. Blockmaker is quietly checking its current ownership and artwork.`
            : profile.profilePicAssetId
              ? `Using NFT asset #${profile.profilePicAssetId}. Blockmaker periodically checks ownership and current ARC-19 artwork.`
              : 'Search the eligible NFTs in your connected game wallet. Nothing is transferred or approved.'
      const nftForm = doc.createElement('form')
      nftForm.className = 'bm-ui-row'
      nftForm.noValidate = true
      const nftLabel = doc.createElement('label')
      nftLabel.className = 'bm-ui-sr'
      nftLabel.htmlFor = `bm-profile-nft-search-${gameId}`
      nftLabel.textContent = 'NFT name or asset ID'
      const nftInput = doc.createElement('input')
      nftInput.className = 'bm-ui-input'
      nftInput.id = nftLabel.htmlFor
      nftInput.type = 'search'
      nftInput.maxLength = 100
      nftInput.placeholder = 'NFT name or asset ID'
      nftInput.setAttribute('data-bm-part', 'nft-search-input')
      nftInput.setAttribute('data-bm-disable-while-busy', '')
      const searchButton = uiButton(doc, 'Search', 'default')
      searchButton.type = 'submit'
      searchButton.setAttribute('data-bm-part', 'nft-search')
      nftForm.append(nftLabel, nftInput, searchButton)
      const nftResults = doc.createElement('div')
      nftResults.setAttribute('data-bm-part', 'nft-results')
      nftResults.setAttribute('aria-live', 'polite')
      let nftGrid = null
      let nftPreview = null
      let activeNftSearch = ''
      let nextNftCursor = null
      const renderedNftIds = new Set()

      const removeNftSearchFooter = () => {
        for (const node of nftResults.querySelectorAll('[data-bm-nft-footer]')) node.remove()
      }

      const appendNftAsset = asset => {
        const assetId = Number(asset?.assetId)
        if (!Number.isSafeInteger(assetId) || assetId <= 0 || renderedNftIds.has(assetId)) return
        renderedNftIds.add(assetId)
        const eligible = asset?.eligible !== false && String(asset?.eligibility ?? 'eligible') === 'eligible'
        const button = uiButton(doc, '', 'default', true)
        button.classList.add('bm-ui-nft')
        button.setAttribute('data-bm-part', 'nft-result')
        button.setAttribute('data-bm-asset-id', String(assetId))
        const name = doc.createElement('strong')
        name.textContent = String(asset?.name || `Asset #${assetId}`).slice(0, 120)
        const meta = doc.createElement('small')
        meta.textContent = `${String(asset?.unitName || 'NFT').slice(0, 32)} · #${assetId}`
        button.append(name, meta)
        if (!eligible) {
          const reason = doc.createElement('small')
          reason.className = 'bm-ui-nft-reason'
          reason.textContent = String(asset?.eligibilityReason || 'This asset cannot be used as a profile picture.').slice(0, 180)
          button.append(reason)
          button.disabled = true
          button.setAttribute('aria-disabled', 'true')
          button.title = reason.textContent
          nftGrid?.append(button)
          return
        }
        button.addEventListener('click', async () => {
          if (dialog.isBusy()) return
          dialog.setBusy(true, `Loading ${name.textContent}…`)
          try {
            const preview = await previewNft(assetId)
            if (dialog.isClosed()) return
            dialog.setBusy(false)
            const previewUrl = safeImageUrl(preview?.imageUrl)
            if (!previewUrl) throw new BlockmakerError('That NFT image is not available from a secure web address.', { code: 'NFT_IMAGE_INVALID' })
            const card = doc.createElement('section')
            card.className = 'bm-ui-preview bm-ui-card'
            card.setAttribute('data-bm-part', 'nft-preview')
            const image = doc.createElement('img')
            image.src = previewUrl
            image.alt = `${name.textContent} preview`
            image.referrerPolicy = 'no-referrer'
            const detail = doc.createElement('div')
            const heading = doc.createElement('strong')
            heading.className = 'bm-ui-card-title'
            heading.textContent = name.textContent
            const copy = doc.createElement('p')
            copy.className = 'bm-ui-card-copy'
            copy.textContent = `Asset #${assetId}. Blockmaker will check current ownership and NFT eligibility again before saving.`
            const choose = uiButton(doc, 'Use this NFT', 'primary', true)
            choose.setAttribute('data-bm-part', 'nft-use')
            choose.addEventListener('click', async () => {
              if (dialog.isBusy()) return
              dialog.setBusy(true, 'Verifying ownership and saving your picture…')
              try {
                const updated = await setGameProfileNft(assetId)
                if (!dialog.isClosed()) {
                  emitSave(updated.profile)
                  await load(false)
                  if (!dialog.isClosed()) dialog.setStatus('Shared profile picture saved.', 'success')
                }
              } catch (error) {
                if (!dialog.isClosed()) {
                  dialog.setBusy(false)
                  dialog.setStatus(playerFacingError(error, 'Could not use that NFT. Check ownership and try again.'), 'error')
                }
                emitError(error)
              }
            })
            detail.append(heading, copy, choose)
            card.append(image, detail)
            nftPreview?.remove()
            nftPreview = card
            nftResults.append(card)
            dialog.setStatus('', 'info')
            choose.focus?.({ preventScroll: true })
          } catch (error) {
            if (!dialog.isClosed()) {
              dialog.setBusy(false)
              dialog.setStatus(playerFacingError(error, 'Could not preview that NFT. Try another one.'), 'error')
            }
            emitError(error)
          }
        })
        nftGrid?.append(button)
      }

      const loadNftPage = async ({ append = false, cursor = null } = {}) => {
        if (dialog.isBusy()) return
        dialog.setBusy(true, append ? 'Loading more eligible NFTs…' : 'Searching your wallet. The first search can take up to 30 seconds…')
        if (!append) {
          nftResults.replaceChildren()
          nftGrid = null
          nftPreview = null
          nextNftCursor = null
          renderedNftIds.clear()
        }
        removeNftSearchFooter()
        try {
          const result = await searchNfts(activeNftSearch, { cursor, limit: 24 })
          if (dialog.isClosed()) return
          dialog.setBusy(false)
          const assets = Array.isArray(result?.assets) ? result.assets : []
          const stillIndexing = !!result?.index?.status && result.index.status !== 'ready'
          if (!nftGrid && assets.length) {
            nftGrid = doc.createElement('div')
            nftGrid.className = 'bm-ui-nft-grid'
            nftGrid.setAttribute('aria-label', 'NFT search results')
            nftResults.append(nftGrid)
          }
          for (const asset of assets) appendNftAsset(asset)
          nextNftCursor = typeof result?.nextCursor === 'string' && result.nextCursor ? result.nextCursor : null

          if (!renderedNftIds.size) {
            const empty = doc.createElement('p')
            empty.className = 'bm-ui-help'
            empty.setAttribute('data-bm-nft-footer', '')
            empty.textContent = stillIndexing
              ? 'No matches yet. Blockmaker is still checking the rest of your wallet, so try this search again shortly.'
              : 'No matching NFTs were found in this wallet. Try a name or asset ID.'
            nftResults.append(empty)
          }
          if (nextNftCursor) {
            const moreRow = doc.createElement('div')
            moreRow.className = 'bm-ui-nft-more'
            moreRow.setAttribute('data-bm-nft-footer', '')
            const more = uiButton(doc, 'Load more', 'default')
            more.setAttribute('data-bm-part', 'nft-load-more')
            more.addEventListener('click', () => { void loadNftPage({ append: true, cursor: nextNftCursor }) })
            moreRow.append(more)
            nftResults.append(moreRow)
          }
          if (stillIndexing) {
            const progress = doc.createElement('p')
            progress.className = 'bm-ui-help'
            const indexed = Number(result?.index?.indexed)
            const total = Number(result?.index?.total)
            const count = Number.isFinite(indexed) && Number.isFinite(total) && total > 0
              ? ` (${Math.min(indexed, total)} of ${total} checked)`
              : ''
            progress.textContent = `These are partial results${count}. Blockmaker is still checking the rest of your wallet.`
            progress.setAttribute('data-bm-part', 'nft-index-progress')
            progress.setAttribute('data-bm-nft-footer', '')
            nftResults.append(progress)
          }
          dialog.setStatus(append ? `${assets.length} more results loaded.` : '', append ? 'success' : 'info')
        } catch (error) {
          if (!dialog.isClosed()) {
            dialog.setBusy(false)
            dialog.setStatus(playerFacingError(error, append ? 'Could not load more NFTs. You can safely try again.' : 'Could not search this wallet right now. Please try again.'), 'error')
            if (append && cursor) {
              const retryRow = doc.createElement('div')
              retryRow.className = 'bm-ui-nft-more'
              retryRow.setAttribute('data-bm-nft-footer', '')
              const retry = uiButton(doc, 'Try loading more again', 'default')
              retry.setAttribute('data-bm-part', 'nft-load-more-retry')
              retry.addEventListener('click', () => { void loadNftPage({ append: true, cursor }) })
              retryRow.append(retry)
              nftResults.append(retryRow)
            }
          }
          emitError(error)
        }
      }

      nftForm.addEventListener('submit', event => {
        event.preventDefault()
        activeNftSearch = nftInput.value.trim()
        void loadNftPage()
      })
      nftSection.append(nftTitle, nftCopy, nftForm, nftResults)
      if (profile.profilePicAssetId) {
        const refreshNft = uiButton(doc, 'Refresh ownership and artwork', 'quiet')
        refreshNft.setAttribute('data-bm-part', 'nft-refresh')
        refreshNft.style.marginTop = '10px'
        refreshNft.addEventListener('click', async () => {
          if (dialog.isBusy()) return
          dialog.setBusy(true, 'Checking current ownership and artwork…')
          try {
            const updated = await refreshGameProfileNft()
            if (!dialog.isClosed()) {
              render(updated)
              dialog.setStatus('Refresh started. You can close this window; Blockmaker will finish in the background.', 'success')
            }
          } catch (error) {
            if (!dialog.isClosed()) {
              dialog.setBusy(false)
              dialog.setStatus(playerFacingError(error, 'Could not start that refresh. Your current picture has not changed.'), 'error')
            }
            emitError(error)
          }
        })
        const clearNft = uiButton(doc, 'Remove profile picture', 'quiet')
        clearNft.setAttribute('data-bm-part', 'nft-clear')
        clearNft.style.marginTop = '10px'
        clearNft.addEventListener('click', async () => {
          if (dialog.isBusy()) return
          dialog.setBusy(true, 'Removing your profile picture…')
          try {
            const updated = await clearGameProfileNft()
            if (!dialog.isClosed()) {
              emitSave(updated.profile)
              render(updated)
              dialog.setStatus('Shared profile picture removed.', 'success')
            }
          } catch (error) {
            if (!dialog.isClosed()) {
              dialog.setBusy(false)
              dialog.setStatus(playerFacingError(error, 'Could not remove your profile picture. Please try again.'), 'error')
            }
            emitError(error)
          }
        })
        nftSection.append(refreshNft, clearNft)
      }

      const done = uiButton(doc, 'Done', 'primary', true)
      done.setAttribute('data-bm-part', 'profile-done')
      done.addEventListener('click', () => dialog.close('completed'))
      dialog.replaceContent(summary, usernameSection, nftSection, done)
    }

    const load = async (announce = true) => {
      const loadId = ++loadSequence
      if (dialog.isClosed()) return null
      dialog.setState('loading')
      dialog.setBusy(true, announce ? 'Loading your game profile…' : '')
      try {
        await fullscreen.ready
        const [profileResponse, namesResponse] = await Promise.all([
          gameProfileGet(),
          getUniversalUsernames().catch(() => ({ names: [] })),
        ])
        const response = {
          ...profileResponse,
          universalNames: Array.isArray(namesResponse?.names) ? namesResponse.names : [],
        }
        if (!dialog.isClosed() && loadId === loadSequence) render(response)
        return loadId === loadSequence ? response : latest
      } catch (error) {
        if (!dialog.isClosed() && loadId === loadSequence) {
          dialog.setBusy(false)
          dialog.setState('error')
          dialog.setStatus(playerFacingError(error, 'Could not load your game profile. Check your connection and try again.'), 'error')
          const retry = uiButton(doc, 'Try again', 'primary', true)
          retry.setAttribute('data-bm-part', 'profile-retry')
          retry.addEventListener('click', () => { void load(true) })
          dialog.replaceContent(retry)
        }
        emitError(error)
        return null
      }
    }

    void load(true)
    return Object.freeze({
      close: () => dialog.close('dismissed'),
      refresh: () => load(true),
    })
  }

  // Providers own OTP/approval; funding help uses fixed official resources.
  const openOnboarding = (options = {}) => {
    const doc = globalThis.document
    if (!doc?.body?.appendChild)
      throw new BlockmakerError('onboarding.open() requires a browser.', { code: 'BROWSER_REQUIRED' })
    if (options.algorandWallets !== undefined && !Array.isArray(options.algorandWallets))
      throw new Error('onboarding.open algorandWallets must be an array of named wallet adapters.')
    if ((options.algorandWallets?.length ?? 0) > 6)
      throw new Error('onboarding.open supports at most six wallet options.')
    const algorandWallets = (options.algorandWallets ?? []).map((entry, index) => {
      const wallet = entry?.wallet
      const name = String(entry?.name ?? wallet?.metadata?.name ?? '').trim()
      const requestedId = String(entry?.id ?? '').trim().toLowerCase()
      const generatedId = name.toLowerCase().replace(/[^a-z0-9_-]+/g, '-').replace(/^-+|-+$/g, '')
      const id = (requestedId || generatedId || `wallet-${index + 1}`).slice(0, 48)
      if (!name || name.length > 48 || typeof wallet?.connect !== 'function'
        || (typeof wallet?.signTransactions !== 'function' && typeof wallet?.signTransaction !== 'function'))
        throw new Error(`onboarding.open wallet option ${index + 1} needs a short name and a compatible wallet adapter.`)
      return { id, name, wallet }
    })
    if (options.pera && !algorandWallets.some(entry => entry.wallet === options.pera))
      algorandWallets.unshift({ id: 'pera', name: 'Pera Wallet', wallet: options.pera })
    if (algorandWallets.length && (typeof options.algosdk?.decodeUnsignedTransaction !== 'function'
      || typeof options.algosdk?.encodeAddress !== 'function'))
      throw new Error('onboarding.open requires the official algosdk package when Algorand wallet login is enabled.')
    if (!options.magic && !algorandWallets.length && !session)
      throw new Error('onboarding.open requires Magic, an Algorand wallet, or an existing Blockmaker player session.')
    if (onboardingOpen || doc.querySelector?.('[data-blockmaker-dialog="onboarding"]'))
      throw new BlockmakerError('Player sign-in is already open.', { code: 'ONBOARDING_ALREADY_OPEN' })
    onboardingOpen = true
    const fullscreen = beginFullscreenExit(doc)

    const priorFocus = doc.activeElement
    const overlay = doc.createElement('div')
    overlay.setAttribute('data-blockmaker-dialog', 'onboarding')
    overlay.setAttribute('role', 'dialog')
    overlay.setAttribute('aria-modal', 'true')
    overlay.setAttribute('aria-labelledby', `bm-onboarding-title-${gameId}`)
    overlay.style.cssText = 'position:fixed;inset:0;z-index:2147483646;display:flex;width:100%;max-width:100vw;min-width:0;align-items:center;justify-content:center;overflow:hidden;padding:max(14px,env(safe-area-inset-top)) max(14px,env(safe-area-inset-right)) max(14px,env(safe-area-inset-bottom)) max(14px,env(safe-area-inset-left));background:rgba(5,7,12,.84);backdrop-filter:blur(10px);-webkit-backdrop-filter:blur(10px);box-sizing:border-box;color:#f8fafc;font-family:Inter,ui-sans-serif,system-ui,-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif;'
    const panel = doc.createElement('section')
    panel.style.cssText = 'position:relative;flex:0 1 500px;width:100%;max-width:500px;min-width:0;max-height:calc(100vh - 28px);max-height:calc(100dvh - 28px);overflow:auto;overscroll-behavior:contain;border:1px solid rgba(255,255,255,.11);border-radius:24px;background:linear-gradient(180deg,#171b25 0%,#0e1118 100%);box-shadow:0 28px 90px rgba(0,0,0,.62);box-sizing:border-box;-webkit-overflow-scrolling:touch;'
    const content = doc.createElement('div')
    content.style.cssText = 'min-width:0;padding:clamp(24px,6vw,38px);box-sizing:border-box;'
    const closeButton = doc.createElement('button')
    closeButton.type = 'button'
    closeButton.setAttribute('aria-label', 'Close sign in')
    closeButton.textContent = '×'
    closeButton.style.cssText = 'position:absolute;z-index:2;top:12px;right:12px;width:46px;height:46px;border:0;border-radius:999px;background:rgba(255,255,255,.08);color:#fff;font:400 29px/42px system-ui,sans-serif;cursor:pointer;'
    const eyebrow = doc.createElement('div')
    eyebrow.textContent = 'BLOCKMAKER PLAYER ACCOUNT'
    eyebrow.style.cssText = 'margin:0 48px 12px 0;color:#9d8cff;font-size:12px;font-weight:800;letter-spacing:.12em;overflow-wrap:anywhere;'
    const title = doc.createElement('h2')
    title.id = `bm-onboarding-title-${gameId}`
    title.textContent = String(options.title ?? 'Start playing')
    title.style.cssText = 'margin:0 46px 10px 0;color:#fff;font-size:clamp(28px,8vw,38px);line-height:1.05;letter-spacing:-.035em;'
    const intro = doc.createElement('p')
    intro.textContent = options.magic
      ? 'Enter your email. We’ll send a one-time code and set up your game account—no crypto knowledge needed.'
      : 'Choose your Algorand wallet to start playing. Blockmaker validates a zero-ALGO self-payment proof and never broadcasts it.'
    intro.style.cssText = 'margin:0 0 24px;color:#b8bfce;font-size:15px;line-height:1.55;'
    const body = doc.createElement('div')
    const status = doc.createElement('div')
    status.setAttribute('role', 'status')
    status.setAttribute('aria-live', 'polite')
    status.setAttribute('aria-atomic', 'true')
    status.style.cssText = 'display:none;margin:0 0 14px;padding:12px 14px;border-radius:12px;background:rgba(157,140,255,.10);color:#d8d2ff;font-size:14px;line-height:1.45;'
    content.append(eyebrow, title, intro, status, body)
    panel.append(closeButton, content)
    overlay.append(panel)
    doc.body.appendChild(overlay)
    const restoreBackground = isolateDialogBackground(doc, overlay)
    const previousOverflow = doc.body.style.overflow
    doc.body.style.overflow = 'hidden'

    let closed = false
    let completed = false
    let busy = false
    const buttonStyle = 'width:100%;min-height:52px;padding:13px 18px;border:0;border-radius:13px;font:800 15px/1.2 inherit;cursor:pointer;box-sizing:border-box;transition:transform .12s ease,opacity .12s ease;'
    const setStatus = (message, error = false) => {
      status.style.display = message ? 'block' : 'none'
      status.style.background = error ? 'rgba(255,92,113,.12)' : 'rgba(157,140,255,.10)'
      status.style.color = error ? '#ffb7c1' : '#d8d2ff'
      status.textContent = message
    }
    const setBusy = (value, message = '') => {
      busy = value
      for (const control of body.querySelectorAll('button,input')) control.disabled = value
      closeButton.disabled = value
      closeButton.style.opacity = value ? '.42' : '1'
      panel.setAttribute('aria-busy', value ? 'true' : 'false')
      if (message) setStatus(message)
    }
    const setProviderActive = value => {
      // Yield the overlay stacking context to provider approval UI.
      overlay.style.visibility = value ? 'hidden' : 'visible'
    }
    const close = (didComplete = false) => {
      if (closed) return
      closed = true
      onboardingOpen = false
      completed = didComplete
      doc.removeEventListener('keydown', onKeydown)
      overlay.remove()
      restoreBackground()
      doc.body.style.overflow = previousOverflow
      try { priorFocus?.focus?.({ preventScroll: true }) } catch { /* caller may have removed it */ }
      if (!completed) options.onClose?.()
    }
    const focusable = () => Array.from(panel.querySelectorAll('button:not([disabled]),input:not([disabled]),a[href]'))
    const onKeydown = event => {
      if (event.key === 'Escape' && !busy) return close()
      if (event.key !== 'Tab') return
      const items = focusable()
      if (!items.length) return
      const first = items[0]
      const last = items[items.length - 1]
      if (event.shiftKey && doc.activeElement === first) { event.preventDefault(); last.focus() }
      else if (!event.shiftKey && doc.activeElement === last) { event.preventDefault(); first.focus() }
    }
    doc.addEventListener('keydown', onKeydown)
    closeButton.addEventListener('click', () => { if (!busy) close() })
    overlay.addEventListener('click', event => { if (event.target === overlay && !busy) close() })

    const renderReady = async authResult => {
      setBusy(false)
      const readySession = session ? { ...session } : authResult
      const authenticationOnly = isAuthenticationOnlyEmailSession(readySession)
      title.textContent = 'You’re ready to play'
      intro.textContent = authenticationOnly
        ? 'Your email account is ready for gameplay. Connect Pera or Lute separately for funding or on-chain features.'
        : 'Your game account and wallet are ready. Adding ALGO is optional unless the game asks for it.'
      body.replaceChildren()
      setStatus('')

      const wallet = doc.createElement('details')
      wallet.style.cssText = 'margin:0 0 16px;padding:13px 15px;border:1px solid rgba(255,255,255,.10);border-radius:14px;background:rgba(255,255,255,.045);'
      const walletLabel = doc.createElement('summary')
      walletLabel.textContent = authenticationOnly ? 'Account details' : 'Wallet details'
      walletLabel.style.cssText = 'cursor:pointer;color:#d7dbe5;font-size:13px;font-weight:750;'
      const walletAddress = doc.createElement('div')
      const address = String(readySession?.walletAddress ?? '')
      walletAddress.textContent = authenticationOnly
        ? 'Authentication-only email account. Do not send ALGO or assets to this sign-in account.'
        : address.length > 18 ? `${address.slice(0, 9)}…${address.slice(-7)}` : address
      walletAddress.title = authenticationOnly ? '' : address
      walletAddress.style.cssText = 'overflow:hidden;margin-top:10px;color:#fff;font:700 13px/1.3 ui-monospace,SFMono-Regular,Menlo,monospace;text-overflow:ellipsis;white-space:nowrap;'
      wallet.append(walletLabel, walletAddress)

      const continueButton = doc.createElement('button')
      continueButton.type = 'button'
      continueButton.textContent = 'Continue to game'
      continueButton.style.cssText = `${buttonStyle}background:#a38cff;color:#0d1017;`
      continueButton.addEventListener('click', () => {
        const result = session ? { ...session } : readySession
        close(true)
        options.onComplete?.(result)
      })
      body.append(wallet, continueButton)

      if (authenticationOnly) {
        const fundingCard = doc.createElement('section')
        fundingCard.style.cssText = 'margin:0 0 12px;padding:15px;border:1px solid rgba(157,140,255,.32);border-radius:14px;background:rgba(157,140,255,.08);'
        const fundingTitle = doc.createElement('strong')
        fundingTitle.textContent = 'On-chain features need Pera or Lute'
        fundingTitle.style.cssText = 'display:block;color:#fff;font-size:14px;'
        const fundingCopy = doc.createElement('p')
        fundingCopy.textContent = 'This email sign-in is for authentication only. Connect a supported Algorand wallet before funding, paying, receiving assets, or using marketplace features.'
        fundingCopy.style.cssText = 'margin:5px 0 0;color:#b8bfce;font-size:12px;line-height:1.5;'
        fundingCard.append(fundingTitle, fundingCopy)
        body.insertBefore(fundingCard, continueButton)
      } else if (options.showFundingHelp !== false) {
        void request('/v1/funding-guide/config', { auth: false }).then(config => {
          if (!closed && config?.available) {
            const fundingCard = doc.createElement('section')
            fundingCard.style.cssText = 'margin:0 0 12px;padding:15px;border:1px solid rgba(157,140,255,.32);border-radius:14px;background:rgba(157,140,255,.08);'
            const fundingTitle = doc.createElement('strong')
            fundingTitle.textContent = 'Need ALGO later?'
            fundingTitle.style.cssText = 'display:block;color:#fff;font-size:14px;'
            const fundingCopy = doc.createElement('p')
            fundingCopy.textContent = 'See beginner steps for Pera or an exchange. Blockmaker never asks for card details or recovery words.'
            fundingCopy.style.cssText = 'margin:5px 0 11px;color:#b8bfce;font-size:12px;line-height:1.5;'
            const fundingButton = doc.createElement('button')
            fundingButton.type = 'button'
            fundingButton.textContent = 'How to get ALGO'
            fundingButton.style.cssText = `${buttonStyle}min-height:46px;border:1px solid rgba(255,255,255,.16);background:rgba(255,255,255,.07);color:#fff;`
            fundingButton.addEventListener('click', () => {
              const result = session ? { ...session } : readySession
              close(true)
              try {
                openFundingGuide({
                  onClose: () => options.onComplete?.(result),
                  onError: options.onError,
                })
              } catch (error) {
                try { options.onError?.(error) } catch { /* consumer callback */ }
                options.onComplete?.(result)
              }
            })
            fundingCard.append(fundingTitle, fundingCopy, fundingButton)
            body.insertBefore(fundingCard, continueButton)
          }
        }).catch(() => { /* optional funding help must never delay or fail player sign-in */ })
      }
      continueButton.focus?.({ preventScroll: true })
    }

    const renderLogin = () => {
      body.replaceChildren()
      title.textContent = String(options.title ?? 'Start playing')
      intro.textContent = options.magic
        ? 'Enter your email. We’ll send a one-time code and set up your game account—no crypto knowledge needed.'
        : 'Choose your Algorand wallet to start playing. Blockmaker validates a zero-ALGO self-payment proof and never broadcasts it.'
      const form = doc.createElement('form')
      form.noValidate = true
      const label = doc.createElement('label')
      label.textContent = 'Email address'
      label.htmlFor = `bm-onboarding-email-${gameId}`
      label.style.cssText = 'display:block;margin-bottom:8px;color:#f2f4f8;font-size:14px;font-weight:750;'
      const email = doc.createElement('input')
      email.id = `bm-onboarding-email-${gameId}`
      email.type = 'email'
      email.inputMode = 'email'
      email.autocomplete = 'email'
      email.placeholder = 'you@example.com'
      email.required = true
      email.style.cssText = 'width:100%;height:52px;margin:0 0 12px;padding:0 15px;border:1px solid rgba(255,255,255,.17);border-radius:13px;outline:2px solid transparent;outline-offset:2px;background:#0b0e14;color:#fff;font:500 16px/1 system-ui,sans-serif;box-sizing:border-box;'
      email.addEventListener('focus', () => { email.style.outlineColor = '#a38cff'; email.style.borderColor = '#a38cff' })
      email.addEventListener('blur', () => { email.style.outlineColor = 'transparent'; email.style.borderColor = 'rgba(255,255,255,.17)' })
      const emailButton = doc.createElement('button')
      emailButton.type = 'submit'
      emailButton.textContent = 'Email me a sign-in code'
      emailButton.style.cssText = `${buttonStyle}background:#a38cff;color:#0d1017;`
      form.append(label, email, emailButton)
      form.addEventListener('submit', async event => {
        event.preventDefault()
        if (busy) return
        setBusy(true, 'Check your email for the secure sign-in code…')
        setProviderActive(true)
        try {
          await fullscreen.ready
          const result = await loginWithMagicEmail(options.magic, email.value)
          if (!closed) { setProviderActive(false); await renderReady(result) }
        } catch (error) {
          if (!closed) { setProviderActive(false); setBusy(false); setStatus(playerFacingError(error, 'Email sign-in did not finish. Please try again.'), true); email.focus() }
          options.onError?.(error)
        }
      })
      if (options.magic) body.append(form)

      if (options.magic && algorandWallets.length) {
        const divider = doc.createElement('div')
        divider.setAttribute('aria-hidden', 'true')
        divider.style.cssText = 'display:flex;align-items:center;gap:12px;margin:18px 0;color:#737d90;font-size:11px;font-weight:800;letter-spacing:.12em;'
        const leftLine = doc.createElement('span')
        const dividerText = doc.createElement('span')
        const rightLine = doc.createElement('span')
        leftLine.style.cssText = rightLine.style.cssText = 'height:1px;flex:1;background:rgba(255,255,255,.10);'
        dividerText.textContent = 'OR'
        divider.append(leftLine, dividerText, rightLine)
        body.append(divider)
      }
      if (algorandWallets.length) {
        const walletList = doc.createElement('div')
        walletList.setAttribute('aria-label', 'Choose an Algorand wallet')
        walletList.style.cssText = 'display:grid;gap:8px;'
        for (const entry of algorandWallets) {
          const walletButton = doc.createElement('button')
          walletButton.type = 'button'
          walletButton.textContent = `Continue with ${entry.name}`
          walletButton.style.cssText = `${buttonStyle}border:1px solid rgba(255,255,255,.16);background:rgba(255,255,255,.06);color:#fff;`
          walletButton.addEventListener('click', async () => {
            if (busy) return
            setBusy(true, `Approve the secure sign-in request in ${entry.name}…`)
            setProviderActive(true)
            try {
              await fullscreen.ready
              const result = await loginWithAlgorandWallet(entry.wallet, options.algosdk, { walletName: entry.name })
              if (!closed) { setProviderActive(false); await renderReady(result) }
            } catch (error) {
              if (!closed) { setProviderActive(false); setBusy(false); setStatus(playerFacingError(error, `${entry.name} sign-in did not finish. Please try again.`), true); walletButton.focus?.({ preventScroll: true }) }
              options.onError?.(error)
            }
          })
          walletList.append(walletButton)
        }
        const walletHelp = doc.createElement('p')
        walletHelp.textContent = 'Your wallet will show 0 ALGO going back to your own address. Blockmaker checks the transaction and never broadcasts it.'
        walletHelp.style.cssText = 'margin:9px 4px 0;color:#929bad;font-size:12px;line-height:1.45;text-align:center;'
        body.append(walletList, walletHelp)

        if (algorandWallets.some(entry => entry.id.toLowerCase() === 'pera' || /pera/i.test(entry.name))) {
          const peraLink = doc.createElement('a')
          peraLink.href = OFFICIAL_FUNDING_LINKS.peraWebsite
          peraLink.target = '_blank'
          peraLink.rel = 'noopener noreferrer'
          peraLink.textContent = 'New to wallets? Learn about Pera Wallet'
          peraLink.style.cssText = 'display:block;margin:14px auto 0;color:#abb3c3;font-size:13px;line-height:1.4;text-align:center;text-decoration:underline;text-underline-offset:3px;'
          body.append(peraLink)
        }
      }
      if (options.termsUrl || options.privacyUrl) {
        const policies = doc.createElement('div')
        policies.style.cssText = 'display:flex;justify-content:center;gap:14px;flex-wrap:wrap;margin:16px 0 0;font-size:12px;'
        for (const [label, value] of [['Terms', options.termsUrl], ['Privacy', options.privacyUrl]]) {
          const href = safeExternalPageUrl(value)
          if (!href) continue
          const link = doc.createElement('a')
          link.href = href
          link.target = '_blank'
          link.rel = 'noopener noreferrer'
          link.textContent = label
          link.style.cssText = 'color:#929bad;text-underline-offset:3px;'
          policies.append(link)
        }
        body.append(policies)
      }
      setStatus('')
      ;(options.magic ? email : focusable()[0])?.focus?.({ preventScroll: true })
    }

    if (session) {
      setBusy(true, 'Restoring your account…')
      void fullscreen.ready.then(async () => {
        try {
          await request('/v1/auth/session')
          if (!closed) await renderReady(session)
        } catch (error) {
          storeSession(null)
          if (!closed) {
            setBusy(false)
            if (options.magic || algorandWallets.length) {
              renderLogin()
              setStatus('Your previous sign-in expired. Please sign in again.', true)
            } else {
              title.textContent = 'Sign-in expired'
              intro.textContent = 'Close this window and start sign-in again from the game.'
              body.replaceChildren()
              setStatus('Your saved player session is no longer valid.', true)
              const returnButton = doc.createElement('button')
              returnButton.type = 'button'
              returnButton.textContent = 'Return to game'
              returnButton.style.cssText = `${buttonStyle}background:#a38cff;color:#0d1017;`
              returnButton.addEventListener('click', () => close())
              body.append(returnButton)
              returnButton.focus?.({ preventScroll: true })
            }
          }
          options.onError?.(error)
        }
      })
    } else {
      void fullscreen.ready.then(() => { if (!closed) renderLogin() })
    }
    return Object.freeze({ close: () => close(false) })
  }

  const marketplaceConfig = async () => {
    const result = await request('/v1/game-marketplace/config', { auth: false })
    return result.marketplace
  }

  const marketplaceListings = async (filters = {}) => {
    const query = new URLSearchParams()
    for (const key of ['collection', 'q', 'seller', 'sort']) {
      const value = filters?.[key]
      if (value !== undefined && value !== null && String(value).trim()) query.set(key, String(value).trim())
    }
    return request(`/v1/game-marketplace/listings${query.size ? `?${query}` : ''}`, { auth: false })
  }

  const marketplaceBuild = async (action, input = {}) => {
    if (!session?.sessionToken)
      throw new BlockmakerError(`Sign in before you ${action === 'buy' ? 'buy an NFT' : action === 'list' ? 'list an NFT' : 'change a listing'}.`, { status: 401, code: 'AUTH_MISSING' })
    const body = action === 'list'
      ? { assetId: input.assetId, priceMicroalgo: input.priceMicroalgo }
      : { assetId: input.assetId }
    const [built, config] = await Promise.all([
      request(`/v1/game-marketplace/${action}/build`, { method: 'POST', body }),
      marketplaceConfig(),
    ])
    return { ...built, marketplace: config }
  }

  const shopInfo = () => request('/v1/pack-shop/info')
  const shopInfoV1 = async () => normalizeShopInfoV1(await shopInfo())

  const shopRecoveryCommitId = (value, method) => {
    const commitId = String(value ?? '').trim()
    if (!commitId || commitId.length > 128)
      throw new Error(`shop.${method} requires the existing Store commit ID.`)
    return commitId
  }

  const requireShopRecoverySession = method => {
    if (!session?.sessionToken || !isAlgorandAddressShape(session.walletAddress))
      throw new BlockmakerError(
        `Sign in with the Store wallet before calling shop.${method}.`,
        { status: 401, code: 'AUTH_MISSING' },
      )
  }

  // Caller-driven, JWT-scoped recovery reads; no wallet or payment side effects.
  const openShopCommits = () => {
    requireShopRecoverySession('openCommits')
    return request('/v1/pack-shop/commits')
  }

  const recoverableShopProtocolResponse = async (operation, pendingRequest) => {
    try { return await pendingRequest }
    catch (error) {
      if (!(error instanceof BlockmakerError)) throw error
      const details = error.details
      if (!details || typeof details !== 'object' || Array.isArray(details)
        || details.success !== false || details.code !== error.code
        || typeof details.error !== 'string' || !details.error.trim()) throw error

      // These exact Store envelopes are durable protocol states, not failures.
      if (operation === 'confirm'
        && error.status === 202
        && details.code === 'SHOP_CONFIRMATION_PENDING') return details
      if (operation === 'reveal'
        && error.status === 202
        && details.code === 'SHOP_CONFIRMATION_PENDING') return details
      if (operation === 'reveal'
        && error.status === 200
        && details.code === 'SHOP_ASSET_OPT_IN_REQUIRED'
        && details.status === 'awaiting_opt_in'
        && Array.isArray(details.assetIds)
        && Array.isArray(details.items)
        && Array.isArray(details.deliveredParts)) return details
      if (operation === 'reveal'
        && error.status === 200
        && details.status === 'blocked'
        && Array.isArray(details.items)
        && Array.isArray(details.deliveredParts)) return details
      throw error
    }
  }

  const confirmShopCommit = (commitIdInput, txIdInput) => {
    requireShopRecoverySession('confirm')
    const commitId = shopRecoveryCommitId(commitIdInput, 'confirm')
    const txId = txIdInput === undefined || txIdInput === null
      ? ''
      : String(txIdInput).trim()
    if (txIdInput !== undefined && txIdInput !== null && !txId)
      throw new Error('shop.confirm txId must be the existing payment transaction ID when supplied.')
    return recoverableShopProtocolResponse('confirm', request('/v1/pack-shop/confirm-commit', {
      method: 'POST',
      body: { commitId, ...(txId ? { txId } : {}) },
      timeoutMs: 35_000,
    }))
  }

  const revealShopCommit = commitIdInput => {
    requireShopRecoverySession('reveal')
    const commitId = shopRecoveryCommitId(commitIdInput, 'reveal')
    return recoverableShopProtocolResponse('reveal', request('/v1/pack-shop/reveal', {
      method: 'POST',
      body: { commitId },
      timeoutMs: 35_000,
    }))
  }

  const SHOP_WATCH_RETRY_DEFAULT_MS = 3_000
  const SHOP_WATCH_RETRY_MIN_MS = 2_000
  const SHOP_WATCH_RETRY_MAX_MS = 30_000
  const shopWatchRetryMs = value => {
    const seconds = Number(value)
    const requested = Number.isFinite(seconds) && seconds > 0
      ? Math.ceil(seconds * 1_000)
      : SHOP_WATCH_RETRY_DEFAULT_MS
    return Math.max(SHOP_WATCH_RETRY_MIN_MS, Math.min(SHOP_WATCH_RETRY_MAX_MS, requested))
  }
  const shopWatchAbortError = () => {
    if (typeof globalThis.DOMException === 'function')
      return new globalThis.DOMException('The Store delivery watch was aborted.', 'AbortError')
    const error = new Error('The Store delivery watch was aborted.')
    error.name = 'AbortError'
    return error
  }
  const requireShopWatchSignal = signal => {
    if (signal === undefined) return null
    if (!signal || typeof signal !== 'object' || typeof signal.aborted !== 'boolean'
      || typeof signal.addEventListener !== 'function' || typeof signal.removeEventListener !== 'function')
      throw new Error('shop.watchDelivery options.signal must be an AbortSignal.')
    return signal
  }
  const shopWatchThrowIfAborted = signal => {
    if (signal?.aborted) throw shopWatchAbortError()
  }
  const shopWatchAwait = (pending, signal) => {
    if (!signal) return Promise.resolve(pending)
    if (signal.aborted) return Promise.reject(shopWatchAbortError())
    return new Promise((resolve, reject) => {
      let settled = false
      const cleanup = () => signal.removeEventListener('abort', onAbort)
      const onAbort = () => {
        if (settled) return
        settled = true
        cleanup()
        reject(shopWatchAbortError())
      }
      signal.addEventListener('abort', onAbort, { once: true })
      Promise.resolve(pending).then(
        value => {
          if (settled) return
          settled = true
          cleanup()
          resolve(value)
        },
        error => {
          if (settled) return
          settled = true
          cleanup()
          reject(error)
        },
      )
    })
  }
  const shopWatchDelay = (delayMs, signal) => {
    if (signal?.aborted) return Promise.reject(shopWatchAbortError())
    return new Promise((resolve, reject) => {
      let timer
      const cleanup = () => {
        if (timer !== undefined) clearTimeout(timer)
        signal?.removeEventListener('abort', onAbort)
      }
      const onAbort = () => {
        cleanup()
        reject(shopWatchAbortError())
      }
      signal?.addEventListener('abort', onAbort, { once: true })
      timer = setTimeout(() => {
        cleanup()
        resolve()
      }, delayMs)
    })
  }
  const invalidShopRecoveryResponse = (message, details) => new BlockmakerError(
    `Blockmaker returned an invalid Store recovery response: ${message}`,
    { code: 'SHOP_RECOVERY_RESPONSE_INVALID', details },
  )
  const transientShopRecoveryError = error => error instanceof BlockmakerError && (
    [408, 425, 429, 502, 504].includes(error.status)
    || (error.code === 'REQUEST_FAILED' && (error.status === 0 || error.status >= 500))
    || ['NETWORK_ERROR', 'TIMEOUT', 'SHOP_SERVICE_UNAVAILABLE',
      'SHOP_CONFIRMATION_UNAVAILABLE'].includes(error.code)
  )

  /** Reconcile an existing order; stop at an explicit asset opt-in boundary. */
  const watchShopDelivery = async (commitIdInput, watchOptions = {}) => {
    requireShopRecoverySession('watchDelivery')
    const commitId = shopRecoveryCommitId(commitIdInput, 'watchDelivery')
    if (!watchOptions || typeof watchOptions !== 'object' || Array.isArray(watchOptions))
      throw new Error('shop.watchDelivery options must be an object when supplied.')
    const signal = requireShopWatchSignal(watchOptions.signal)
    if (watchOptions.onProgress !== undefined && typeof watchOptions.onProgress !== 'function')
      throw new Error('shop.watchDelivery options.onProgress must be a function when supplied.')
    const onProgress = watchOptions.onProgress
    const notify = progress => {
      if (!onProgress) return
      try { onProgress(Object.freeze(progress)) }
      catch { /* observer errors must not alter payment reconciliation */ }
    }
    const readWithRetry = async (stage, action) => {
      while (true) {
        shopWatchThrowIfAborted(signal)
        try { return await shopWatchAwait(action(), signal) }
        catch (error) {
          if (!transientShopRecoveryError(error)) throw error
          const retryAfterMs = shopWatchRetryMs(error.retryAfter)
          notify({
            commitId,
            status: 'temporarily_unavailable',
            stage,
            retryAfterSeconds: retryAfterMs / 1_000,
            code: error.code,
            message: error.message,
          })
          await shopWatchDelay(retryAfterMs, signal)
        }
      }
    }
    shopWatchThrowIfAborted(signal)

    const open = await readWithRetry('open_commits', openShopCommits)
    if (!open || typeof open !== 'object' || Array.isArray(open) || open.success !== true
      || !Array.isArray(open.commits) || !Array.isArray(open.unpaidIntents))
      throw invalidShopRecoveryResponse('openCommits did not return the expected arrays.', open)
    const paidMatches = open.commits.filter(item => item?.commitId === commitId)
    const unpaidMatches = open.unpaidIntents.filter(item => item?.commitId === commitId)
    if (paidMatches.length > 1 || unpaidMatches.length > 1 || (paidMatches.length && unpaidMatches.length))
      throw invalidShopRecoveryResponse('the commit was returned more than once.', open)
    if (unpaidMatches.length === 1) {
      throw new BlockmakerError(
        'This Store intent has no observed signature or submission attempt. Nothing needs delivery; start a new purchase when ready.',
        { status: 409, code: 'SHOP_UNSIGNED_INTENT', details: unpaidMatches[0] },
      )
    }

    const existing = paidMatches[0] ?? null
    if (existing && typeof existing.status !== 'string')
      throw invalidShopRecoveryResponse('the open commit status is missing.', existing)
    let expectedTxId = existing?.expectedTxId ?? null
    if (expectedTxId !== null && !/^[A-Z2-7]{52}$/.test(expectedTxId))
      throw invalidShopRecoveryResponse('the existing payment transaction ID is invalid.', existing)
    let needsConfirmation = existing?.status === 'pending'

    while (true) {
      shopWatchThrowIfAborted(signal)
      if (needsConfirmation) {
        const confirmation = await readWithRetry(
          'confirm',
          () => confirmShopCommit(commitId, expectedTxId),
        )
        if (confirmation?.success === false && confirmation.code === 'SHOP_CONFIRMATION_PENDING') {
          const retryAfterMs = shopWatchRetryMs(confirmation.retryAfterSeconds)
          notify({
            commitId,
            status: 'confirmation_pending',
            retryAfterSeconds: retryAfterMs / 1_000,
            response: confirmation,
          })
          await shopWatchDelay(retryAfterMs, signal)
          continue
        }
        if (!confirmation || confirmation.success !== true || confirmation.commitId !== commitId
          || !/^[A-Z2-7]{52}$/.test(String(confirmation.txId ?? ''))
          || (expectedTxId !== null && confirmation.txId !== expectedTxId))
          throw invalidShopRecoveryResponse('confirm did not return the exact existing transaction.', confirmation)
        expectedTxId = confirmation.txId
        needsConfirmation = false
      }

      const reveal = await readWithRetry('reveal', () => revealShopCommit(commitId))
      if (reveal?.success === false && reveal.code === 'SHOP_CONFIRMATION_PENDING') {
        const retryAfterMs = shopWatchRetryMs(reveal.retryAfterSeconds)
        notify({
          commitId,
          status: 'confirmation_pending',
          retryAfterSeconds: retryAfterMs / 1_000,
          response: reveal,
        })
        needsConfirmation = true
        await shopWatchDelay(retryAfterMs, signal)
        continue
      }
      if (reveal?.success === true && reveal.status === 'distributed') {
        if (!Array.isArray(reveal.items) || !Array.isArray(reveal.parts))
          throw invalidShopRecoveryResponse('distributed delivery is missing its item arrays.', reveal)
        return reveal
      }
      if (reveal?.success === true
        && (reveal.status === 'delivering' || reveal.status === 'confirming_delivery')) {
        if (!Array.isArray(reveal.items) || !Array.isArray(reveal.parts)
          || !Array.isArray(reveal.pendingAssetIds))
          throw invalidShopRecoveryResponse('delivery progress is missing its item arrays.', reveal)
        const retryAfterMs = shopWatchRetryMs(reveal.retryAfterSeconds)
        notify({
          commitId,
          status: reveal.status,
          retryAfterSeconds: retryAfterMs / 1_000,
          response: reveal,
        })
        await shopWatchDelay(retryAfterMs, signal)
        continue
      }
      if (reveal?.success === false && reveal.status === 'awaiting_opt_in'
        && reveal.code === 'SHOP_ASSET_OPT_IN_REQUIRED'
        && Array.isArray(reveal.assetIds) && Array.isArray(reveal.items)
        && Array.isArray(reveal.deliveredParts)) return reveal
      if (reveal?.success === false && reveal.status === 'blocked'
        && Array.isArray(reveal.items) && Array.isArray(reveal.deliveredParts)) return reveal
      throw invalidShopRecoveryResponse('reveal returned an unknown state.', reveal)
    }
  }

  const shopSigningWallet = (signingOptions, algosdk, network) => {
    if (!session?.sessionToken || !isAlgorandAddressShape(session.walletAddress))
      throw new BlockmakerError('Sign in with the paying Algorand wallet before continuing in the Shop.', { status: 401, code: 'AUTH_MISSING' })
    const walletAddress = String(session.walletAddress).trim().toUpperCase()
    const remembered = rememberedAlgorandSigner?.walletAddress === walletAddress
      ? rememberedAlgorandSigner : null
    const wallet = signingOptions.wallet ?? remembered?.wallet ?? null
    const accounts = () => normalizeAdapterAccounts([
      ...(Array.isArray(wallet?.accounts) ? wallet.accounts : []),
      ...(wallet === remembered?.wallet ? remembered.accounts ?? [] : []),
    ])
    const assertMatch = () => {
      if (String(session?.walletAddress ?? '').trim().toUpperCase() !== walletAddress
        || !accounts().includes(walletAddress))
        throw new BlockmakerError('The connected wallet or Blockmaker player does not match this Shop player.', { code: 'AUTH_MISMATCH' })
    }
    assertMatch()
    const genesisId = String(wallet?.networkGenesisId ?? wallet?.metadata?.networkGenesisId ?? '').trim()
    if (genesisId && genesisId !== SHOP_ALGORAND_NETWORK_IDENTITIES[network].genesisId)
      throw new BlockmakerError('The connected wallet is on a different Algorand network from this Shop.', { code: 'SHOP_NETWORK_INVALID' })
    const providerId = String(wallet?.providerId ?? wallet?.metadata?.providerId ?? '').trim().toLowerCase()
    const peraChainId = network === 'mainnet' ? 416001 : 416002
    if (providerId === 'pera' && wallet?.chainId !== undefined
      && Number(wallet.chainId) !== peraChainId && Number(wallet.chainId) !== 4160)
      throw new BlockmakerError('Pera Wallet is on a different Algorand network from this Shop.', { code: 'SHOP_NETWORK_INVALID' })
    const usePera = typeof wallet?.signTransaction === 'function'
      && (providerId === 'pera' || typeof wallet?.signTransactions !== 'function')
    if (!usePera && typeof wallet?.signTransactions !== 'function')
      throw new BlockmakerError('The connected wallet cannot sign this Shop wallet action.', { code: 'PROVIDER_UNAVAILABLE' })
    return { walletAddress, wallet, usePera, assertMatch }
  }

  const normalizeShopSignedTransactions = (values, expectedBytes, algosdk) => {
    if (!Array.isArray(values) || values.length !== expectedBytes.length)
      throw new BlockmakerError('Your wallet did not sign every reviewed Shop transaction.', { code: 'WALLET_SIGNATURE_MISSING' })
    return Object.freeze(values.map((value, index) => {
      let encoded
      let bytes
      if (value instanceof Uint8Array) {
        bytes = value
        encoded = base64FromBytes(value)
      } else if (typeof value === 'string' && value && value.length <= 200_000) {
        encoded = value
        try {
          bytes = bytesFromBase64(value)
          if (base64FromBytes(bytes) !== value) throw new Error('non-canonical base64')
        } catch {
          throw new BlockmakerError('Your wallet returned an invalid signed Shop transaction.', { code: 'WALLET_SIGNATURE_MISSING' })
        }
      } else throw new BlockmakerError('Your wallet did not sign every reviewed Shop transaction.', { code: 'WALLET_SIGNATURE_MISSING' })
      let envelope
      try { envelope = algosdk.decodeSignedTransaction(bytes) }
      catch { throw new BlockmakerError('Your wallet returned an invalid signed Shop transaction.', { code: 'WALLET_SIGNATURE_MISSING' }) }
      const signer = envelope?.sgnr
      const signerKey = signer instanceof Uint8Array ? signer : signer?.publicKey
      if (!(envelope?.sig instanceof Uint8Array) || envelope.sig.byteLength !== 64
        || !sameBytes(envelope?.txn?.toByte?.(), expectedBytes[index])
        || shopMeaningful(envelope?.msig) || shopMeaningful(envelope?.lsig)
        || (signer != null && (!(signerKey instanceof Uint8Array) || signerKey.byteLength !== 32)))
        throw new BlockmakerError('Your wallet did not sign the exact reviewed Shop transactions in order.', { code: 'WALLET_SIGNATURE_MISSING' })
      return encoded
    }))
  }

  const prepareShopPurchase = async (input = {}, signingOptions = {}) => {
    if (!session?.sessionToken || !isAlgorandAddressShape(session.walletAddress))
      throw new BlockmakerError('Sign in with the paying Algorand wallet before preparing a Shop purchase.', { status: 401, code: 'AUTH_MISSING' })
    if (!input || typeof input !== 'object' || Array.isArray(input))
      throw new Error('shop.prepare requires packs and an independently pinned expectedPolicy.')
    const packs = Number(input.packs)
    if (!Number.isSafeInteger(packs) || packs < 1 || packs > 100)
      throw new Error('shop.prepare packs must be a positive whole number.')
    const algosdk = signingOptions.algosdk ?? rememberedAlgorandSigner?.algosdk ?? options.algosdk
    if (typeof algosdk?.decodeUnsignedTransaction !== 'function'
      || typeof algosdk?.decodeSignedTransaction !== 'function'
      || typeof algosdk?.encodeAddress !== 'function'
      || typeof algosdk?.decodeAddress !== 'function')
      throw new Error('shop.prepare requires the official algosdk package.')
    const policy = normalizeShopPinnedPolicy(input.expectedPolicy, algosdk)
    const { walletAddress, wallet, usePera, assertMatch } = shopSigningWallet(signingOptions, algosdk, policy.network)

    const response = await request('/v1/pack-shop/commit', {
      method: 'POST',
      body: {
        walletAddress,
        packs,
        supportedPaymentPlanVersions: [3],
        expectedStoreRevenuePolicyHash: policy.policyHash,
      },
      timeoutMs: 35_000,
    })
    const verified = validateShopDirectSplit(response, { packs }, policy, algosdk, walletAddress)
    let signedTxnsBase64 = null
    let completed = null
    let active = null
    let terminalError = null
    let state = 'ready'

    const assertSessionAndWalletStillMatch = () => {
      assertMatch()
    }
    const submit = signed => {
      if (String(session?.walletAddress ?? '').trim().toUpperCase() !== walletAddress)
        throw new BlockmakerError('The Blockmaker player changed after Shop signing. Restore the original player before retrying these exact signed bytes.', { code: 'AUTH_MISMATCH' })
      return request('/v1/pack-shop/submit', {
        method: 'POST',
        body: { commitId: verified.commitId, walletAddress, signedTxnsBase64: [...signed] },
        timeoutMs: 35_000,
      }).then(result => {
        if (result?.success !== true || result.commitId !== verified.commitId
          || result.txId !== verified.expectedTxIds[0]
          || typeof result.alreadySubmitted !== 'boolean'
          || typeof result.confirmationPending !== 'boolean')
          throw shopUnsafe('Blockmaker returned a submit receipt for a different Shop payment.')
        return Object.freeze({
          success: true,
          commitId: verified.commitId,
          txId: verified.expectedTxIds[0],
          alreadySubmitted: result.alreadySubmitted,
          confirmationPending: result.confirmationPending,
        })
      })
    }
    const launch = () => {
      if (completed) return Promise.resolve(completed)
      if (terminalError) return Promise.reject(terminalError)
      if (active) return active
      let operation
      if (signedTxnsBase64) {
        state = 'submitting'
        try { operation = submit(signedTxnsBase64) }
        catch (error) { operation = Promise.reject(error) }
      } else {
        assertSessionAndWalletStillMatch()
        state = 'signing'
        let walletResult
        try {
          // Synchronous launch with no signer hints supports Ed25519 rekeying.
          walletResult = usePera
            ? wallet.signTransaction([verified.transactions.map(transaction => ({ txn: transaction }))])
            : wallet.signTransactions(
                verified.transactions,
                verified.transactions.map((_, index) => index),
              )
        } catch (error) {
          state = 'ready'
          return Promise.reject(error)
        }
        operation = Promise.resolve(walletResult).then(values => {
          signedTxnsBase64 = normalizeShopSignedTransactions(values, verified.unsignedBytes, algosdk)
          state = 'submitting'
          return submit(signedTxnsBase64)
        })
      }
      const tracked = Promise.resolve(operation).then(
        result => {
          completed = result
          state = 'submitted'
          if (active === tracked) active = null
          return result
        },
        error => {
          const terminal = signedTxnsBase64 && error instanceof BlockmakerError
            && ['SHOP_PAYMENT_EXPIRED', 'SHOP_PURCHASE_NOT_FOUND', 'SHOP_PAYMENT_CHANGED'].includes(error.code)
          if (terminal) terminalError = error
          state = terminal ? 'terminal' : signedTxnsBase64 ? 'signed_retryable' : 'ready'
          if (active === tracked) active = null
          throw error
        },
      )
      active = tracked
      return tracked
    }
    return Object.freeze({
      commitId: verified.commitId,
      review: verified.review,
      get state() { return state },
      get signedForRetry() { return signedTxnsBase64 !== null && terminalError === null && completed === null },
      launch,
      retry: launch,
    })
  }

  const addressFromTransaction = (algosdk, value) => {
    const direct = typeof value?.toString === 'function' ? String(value.toString()).trim().toUpperCase() : ''
    if (isAlgorandAddressShape(direct)) return direct
    if (value?.publicKey && typeof algosdk?.encodeAddress === 'function')
      return String(algosdk.encodeAddress(value.publicKey)).trim().toUpperCase()
    return ''
  }

  const transactionAmount = transaction => Number(
    transaction?.payment?.amount ?? transaction?.assetTransfer?.amount ?? transaction?.amount ?? 0,
  )

  const transactionReceiver = (algosdk, transaction) => addressFromTransaction(
    algosdk,
    transaction?.payment?.receiver ?? transaction?.assetTransfer?.receiver ?? transaction?.to,
  )

  const applicationSelector = transaction => {
    const value = transaction?.applicationCall?.appArgs?.[0] ?? transaction?.appArgs?.[0]
    return value instanceof Uint8Array
      ? Array.from(value).map(byte => byte.toString(16).padStart(2, '0')).join('')
      : ''
  }

  const applicationArgumentUint64 = (transaction, index) => {
    const value = transaction?.applicationCall?.appArgs?.[index] ?? transaction?.appArgs?.[index]
    if (!(value instanceof Uint8Array) || value.byteLength !== 8) return null
    const decoded = new DataView(value.buffer, value.byteOffset, value.byteLength).getBigUint64(0)
    return decoded <= BigInt(Number.MAX_SAFE_INTEGER) ? Number(decoded) : null
  }

  const applicationIndex = transaction => Number(transaction?.applicationCall?.appIndex ?? transaction?.appIndex ?? 0)
  const applicationOnComplete = transaction => Number(transaction?.applicationCall?.onComplete ?? transaction?.appOnComplete ?? 0)
  const assetIndex = transaction => Number(transaction?.assetTransfer?.assetIndex ?? transaction?.assetIndex ?? 0)

  const sameBytes = (left, right) => left instanceof Uint8Array && right instanceof Uint8Array
    && left.byteLength === right.byteLength
    && left.every((byte, index) => byte === right[index])

  const shopUnsafe = message => new BlockmakerError(
    `${message} Nothing was signed.`,
    { code: 'TX_UNSAFE' },
  )

  const recomputeShopTransactionGroup = (algosdk, transactions) => {
    if (typeof algosdk?.computeGroupID !== 'function')
      throw shopUnsafe('The Algorand SDK cannot verify the Shop atomic group.')
    const submittedGroups = transactions.map(transaction => transaction?.group)
    try {
      for (const transaction of transactions) transaction.group = undefined
      const computed = algosdk.computeGroupID(transactions)
      if (!(computed instanceof Uint8Array) || computed.byteLength !== 32)
        throw new Error('invalid computed group')
      return computed
    } catch (error) {
      if (error instanceof BlockmakerError) throw error
      throw shopUnsafe('Blockmaker returned a Shop atomic group that could not be verified.')
    } finally {
      // The same decoded objects are handed to the wallet after validation.
      // Restore their exact submitted group fields even when computation fails.
      transactions.forEach((transaction, index) => {
        transaction.group = submittedGroups[index]
      })
    }
  }

  const shopExactUint = (value, label) => {
    let normalized
    if (typeof value === 'bigint') normalized = value
    else if (typeof value === 'number' && Number.isSafeInteger(value)) normalized = BigInt(value)
    else if (typeof value === 'string' && /^(?:0|[1-9][0-9]*)$/.test(value)) normalized = BigInt(value)
    else throw shopUnsafe(`Blockmaker returned an invalid ${label}.`)
    if (normalized < 0n || normalized > 18_446_744_073_709_551_615n)
      throw shopUnsafe(`Blockmaker returned an invalid ${label}.`)
    return normalized
  }

  const shopAddress = (value, algosdk, label) => {
    const address = String(value ?? '').trim().toUpperCase()
    if (!isAlgorandAddressShape(address) || typeof algosdk?.decodeAddress !== 'function')
      throw shopUnsafe(`The ${label} is not a complete Algorand address.`)
    try {
      const decoded = algosdk.decodeAddress(address)?.publicKey
      if (!(decoded instanceof Uint8Array) || decoded.byteLength !== 32
        || String(algosdk.encodeAddress(decoded)).trim().toUpperCase() !== address)
        throw new Error('invalid address')
    } catch { throw shopUnsafe(`The ${label} is not a complete Algorand address.`) }
    return address
  }

  const normalizeShopPinnedPolicy = (value, algosdk) => {
    if (!value || typeof value !== 'object' || Array.isArray(value)
      || value.paymentPlanVersion !== 3)
      throw new Error('shop.prepare expectedPolicy must be an independently pinned payment plan 3 policy.')
    const network = String(value.network ?? '')
    if (!Object.hasOwn(SHOP_ALGORAND_NETWORK_IDENTITIES, network))
      throw new Error('shop.prepare expectedPolicy.network must be mainnet or testnet.')
    const policyHash = String(value.policyHash ?? '').trim()
    if (!/^[a-f0-9]{64}$/.test(policyHash))
      throw new Error('shop.prepare expectedPolicy.policyHash must be the exact lowercase 64-character digest.')
    if (!Array.isArray(value.destinations) || value.destinations.length < 2 || value.destinations.length > 4)
      throw new Error('shop.prepare expectedPolicy.destinations must contain the exact ordered 2–4 recipients.')
    const ids = new Set()
    const addresses = new Set()
    const destinations = value.destinations.map((raw, ordinal) => {
      const destinationId = String(raw?.destinationId ?? '').trim()
      if (!/^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$/.test(destinationId) || ids.has(destinationId))
        throw new Error('shop.prepare expectedPolicy destination IDs must be unique stable identifiers.')
      const address = shopAddress(raw?.address, algosdk, `pinned revenue recipient ${ordinal + 1}`)
      if (addresses.has(address))
        throw new Error('shop.prepare expectedPolicy recipient addresses must be unique.')
      const bps = Number(raw?.bps)
      if (!Number.isInteger(bps) || bps < 1 || bps > 10_000)
        throw new Error('shop.prepare expectedPolicy recipient BPS must be whole numbers from 1 to 10,000.')
      if (typeof raw?.isRemainder !== 'boolean')
        throw new Error('shop.prepare expectedPolicy must explicitly identify the rounding-remainder recipient.')
      const label = raw?.label === undefined ? undefined : String(raw.label).trim()
      if (label !== undefined && (!label || label.length > 80 || /[\u0000-\u001f\u007f-\u009f]/u.test(label)))
        throw new Error('shop.prepare expectedPolicy recipient labels must be short plain text.')
      ids.add(destinationId)
      addresses.add(address)
      return Object.freeze({ ordinal, destinationId, address, bps, isRemainder: raw.isRemainder, label })
    })
    if (destinations.reduce((total, destination) => total + destination.bps, 0) !== 10_000)
      throw new Error('shop.prepare expectedPolicy recipient BPS must total exactly 10,000.')
    const remainderDestinationId = String(value.remainderDestinationId ?? '').trim()
    const remainder = destinations.filter(destination => destination.isRemainder)
    if (remainder.length !== 1 || remainder[0].destinationId !== remainderDestinationId)
      throw new Error('shop.prepare expectedPolicy remainder destination does not match its ordered recipients.')
    return Object.freeze({
      paymentPlanVersion: 3,
      network,
      policyHash,
      destinations: Object.freeze(destinations),
      remainderDestinationId,
    })
  }

  const shopRawBase32 = bytes => {
    const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
    let accumulator = 0
    let bits = 0
    let output = ''
    for (const byte of bytes) {
      accumulator = (accumulator << 8) | byte
      bits += 8
      while (bits >= 5) {
        bits -= 5
        output += alphabet[(accumulator >> bits) & 31]
        accumulator &= (1 << bits) - 1
      }
    }
    if (bits > 0) output += alphabet[(accumulator << (5 - bits)) & 31]
    return output
  }

  const shopCanonicalUnsignedBytes = value => {
    try {
      const bytes = bytesFromBase64(value)
      if (base64FromBytes(bytes) !== value) throw new Error('non-canonical base64')
      return bytes
    } catch { throw shopUnsafe('Blockmaker returned an invalid Shop payment transaction.') }
  }

  const shopTransactionAmount = transaction => shopExactUint(
    transaction?.payment?.amount ?? transaction?.amount,
    'Shop payment amount',
  )

  const shopTransactionRound = (transaction, first) => {
    const round = shopExactUint(
      first
        ? transaction?.firstValid ?? transaction?.firstRound
        : transaction?.lastValid ?? transaction?.lastRound,
      first ? 'Shop first-valid round' : 'Shop last-valid round',
    )
    if (round < 1n || round > BigInt(Number.MAX_SAFE_INTEGER))
      throw shopUnsafe('Blockmaker returned an invalid Shop validity window.')
    return Number(round)
  }

  const shopMeaningful = value => value !== undefined && value !== null && value !== false
    && value !== 0 && value !== 0n && value !== ''
    && (!(value instanceof Uint8Array) || value.byteLength > 0)
    && (!Array.isArray(value) || value.length > 0)

  const assertNoShopTransactionExtras = transaction => {
    const allowed = new Set([
      'name', 'tag', 'type', 'sender', 'from', 'payment', 'to', 'amount',
      'fee', 'flatFee', 'firstValid', 'firstRound', 'lastValid', 'lastRound',
      'genesisID', 'genesisId', 'genesisHash', 'note', 'lease', 'group',
    ])
    for (const [key, value] of Object.entries(transaction ?? {})) {
      if (!allowed.has(key) && shopMeaningful(value))
        throw shopUnsafe('Blockmaker added an unexpected field to the Shop payment.')
    }
    if (transaction?.payment && typeof transaction.payment === 'object') {
      const paymentAllowed = new Set(['receiver', 'amount', 'closeRemainderTo'])
      for (const [key, value] of Object.entries(transaction.payment)) {
        if (!paymentAllowed.has(key) && shopMeaningful(value))
          throw shopUnsafe('Blockmaker added an unexpected payment field to the Shop payment.')
      }
    }
    const lease = transaction?.lease
    if ((lease instanceof Uint8Array && lease.byteLength > 0) || (!(lease instanceof Uint8Array) && shopMeaningful(lease))
      || shopMeaningful(transaction?.rekeyTo) || shopMeaningful(transaction?.reKeyTo)
      || shopMeaningful(transaction?.payment?.closeRemainderTo)
      || shopMeaningful(transaction?.closeRemainderTo))
      throw shopUnsafe('Blockmaker returned a Shop payment with account-control fields.')
  }

  const validateShopDirectSplit = (response, input, policy, algosdk, walletAddress) => {
    if (!response || typeof response !== 'object' || response.success !== true
      || response.paymentPlanVersion !== 3 || response.network !== policy.network
      || response.storeRevenuePolicyHash !== policy.policyHash)
      throw shopUnsafe('Blockmaker returned a Shop payment for a different revenue policy.')
    const commitId = String(response.commitId ?? '')
    if (!/^[A-Za-z0-9][A-Za-z0-9:._-]{0,127}$/.test(commitId))
      throw shopUnsafe('Blockmaker returned an invalid Shop purchase identifier.')
    if (Number(response.packs) !== input.packs || response.paymentAssetId !== 0
      || response.paymentAssetLabel !== 'ALGO' || response.paymentAssetDecimals !== 6)
      throw shopUnsafe('Blockmaker returned different Shop purchase terms.')
    const recipients = response.revenueRecipients
    const encoded = response.unsignedTxnsBase64
    if (!Array.isArray(recipients) || recipients.length !== policy.destinations.length
      || !Array.isArray(encoded) || encoded.length !== policy.destinations.length
      || !Array.isArray(response.unsignedTxns) || response.unsignedTxns.length !== encoded.length
      || response.unsignedTxns.some((value, index) => value !== encoded[index])
      || Number(response.transactionCount) !== encoded.length)
      throw shopUnsafe('Blockmaker returned an incomplete Shop payment group.')
    const expectedTxIds = response.expectedTransactionIds
    if (!Array.isArray(expectedTxIds) || expectedTxIds.length !== encoded.length
      || expectedTxIds.some(value => !/^[A-Z2-7]{52}$/.test(String(value))))
      throw shopUnsafe('Blockmaker returned invalid Shop transaction identifiers.')
    const total = shopExactUint(response.amountAtomic, 'Shop total')
    if (total < 1n || shopExactUint(response.amountMicro, 'Shop total') !== total)
      throw shopUnsafe('Blockmaker returned inconsistent Shop totals.')
    const fee = shopExactUint(response.networkFeeMicroalgo, 'Shop network fee')
    if (fee !== BigInt(encoded.length * 1_000))
      throw shopUnsafe('Blockmaker returned an unexpected Shop network fee.')
    const firstValidRound = Number(response.firstValidRound)
    const lastValidRound = Number(response.lastValidRound)
    if (!Number.isSafeInteger(firstValidRound) || !Number.isSafeInteger(lastValidRound)
      || firstValidRound < 1 || lastValidRound < firstValidRound
      || lastValidRound - firstValidRound > 40)
      throw shopUnsafe('Blockmaker returned an unsafe Shop validity window.')
    if (!Number.isSafeInteger(Number(response.storeRevenuePolicyRevision))
      || Number(response.storeRevenuePolicyRevision) < 1
      || !/^[A-Z2-7]{52}$/.test(String(response.paymentGroupId ?? ''))
      || !/^[a-f0-9]{64}$/.test(String(response.paymentGroupCommitment ?? '')))
      throw shopUnsafe('Blockmaker returned an invalid Shop payment receipt.')
    const signing = response.signingContract
    if (signing?.walletRequestCount !== 1 || signing?.completeAtomicGroupRequired !== true
      || !Array.isArray(signing?.signerTypes) || signing.signerTypes.length !== 1
      || signing.signerTypes[0] !== 'ed25519' || signing.rekeyedEd25519Supported !== true
      || signing.falcon1024Supported !== false || signing.omitSignerHintsWhenWalletKnowsSender !== true)
      throw shopUnsafe('Blockmaker returned an unsupported Shop signing contract.')

    let transactions
    const unsignedBytes = encoded.map(shopCanonicalUnsignedBytes)
    try { transactions = unsignedBytes.map(bytes => algosdk.decodeUnsignedTransaction(bytes)) }
    catch { throw shopUnsafe('Blockmaker returned an invalid Shop payment transaction.') }
    const expectedNetwork = SHOP_ALGORAND_NETWORK_IDENTITIES[policy.network]
    const group = transactions[0]?.group
    if (!(group instanceof Uint8Array) || group.byteLength !== 32
      || shopRawBase32(group) !== response.paymentGroupId
      || transactions.some(transaction => !(transaction?.group instanceof Uint8Array)
        || !sameBytes(group, transaction.group)))
      throw shopUnsafe('Blockmaker returned a broken Shop atomic group.')
    if (!sameBytes(group, recomputeShopTransactionGroup(algosdk, transactions)))
      throw shopUnsafe('Blockmaker returned a Shop atomic group with the wrong consensus digest.')

    let allocated = 0n
    const reviewRecipients = transactions.map((transaction, ordinal) => {
      assertNoShopTransactionExtras(transaction)
      const pinned = policy.destinations[ordinal]
      const projected = recipients[ordinal]
      const sender = addressFromTransaction(algosdk, transaction?.sender ?? transaction?.from)
      const receiver = transactionReceiver(algosdk, transaction)
      const amount = shopTransactionAmount(transaction)
      const expectedBaseAmount = total * BigInt(pinned.bps) / 10_000n
      const baseAllocated = policy.destinations.reduce(
        (sum, destination) => sum + total * BigInt(destination.bps) / 10_000n,
        0n,
      )
      const expectedAmount = expectedBaseAmount
        + (pinned.isRemainder ? total - baseAllocated : 0n)
      const genesisHash = transaction?.genesisHash instanceof Uint8Array
        ? base64FromBytes(transaction.genesisHash)
        : ''
      const note = new TextEncoder().encode(`pack:${commitId}:split:${policy.policyHash}:${ordinal}`)
      let txId = ''
      let encodedAgain
      try {
        txId = String(transaction?.txID?.() ?? '')
        encodedAgain = transaction?.toByte?.()
      } catch { throw shopUnsafe('Blockmaker returned an invalid Shop transaction identifier.') }
      if (transaction?.type !== 'pay' || sender !== walletAddress || receiver !== pinned.address
        || policy.destinations.some(destination => destination.address === walletAddress)
        || amount !== expectedAmount || amount < 1n || shopExactUint(transaction?.fee, 'Shop transaction fee') !== 1_000n
        || shopTransactionRound(transaction, true) !== firstValidRound
        || shopTransactionRound(transaction, false) !== lastValidRound
        || String(transaction?.genesisID ?? transaction?.genesisId ?? '') !== expectedNetwork.genesisId
        || genesisHash !== expectedNetwork.genesisHashBase64
        || !(transaction?.note instanceof Uint8Array) || !sameBytes(transaction.note, note)
        || !(encodedAgain instanceof Uint8Array) || !sameBytes(encodedAgain, unsignedBytes[ordinal])
        || txId !== expectedTxIds[ordinal]
        || Number(projected?.ordinal) !== ordinal
        || String(projected?.destinationId ?? '') !== pinned.destinationId
        || String(projected?.address ?? '').trim().toUpperCase() !== pinned.address
        || Number(projected?.bps) !== pinned.bps
        || projected?.isRemainder !== pinned.isRemainder
        || shopExactUint(projected?.amountMicroalgo, 'Shop recipient amount') !== amount
        || String(projected?.expectedTxId ?? '') !== txId
        || (pinned.label !== undefined && String(projected?.label ?? '') !== pinned.label))
        throw shopUnsafe('Blockmaker changed an exact Shop revenue payment.')
      allocated += amount
      return Object.freeze({
        ordinal,
        destinationId: pinned.destinationId,
        label: pinned.label ?? pinned.destinationId,
        address: pinned.address,
        bps: pinned.bps,
        isRemainder: pinned.isRemainder,
        amountMicroalgo: amount.toString(),
        expectedTxId: txId,
      })
    })
    if (allocated !== total || response.expectedTxId !== expectedTxIds[0])
      throw shopUnsafe('Blockmaker returned an inconsistent Shop payment total.')
    return Object.freeze({
      commitId,
      transactions: Object.freeze(transactions),
      unsignedBytes: Object.freeze(unsignedBytes),
      expectedTxIds: Object.freeze([...expectedTxIds]),
      review: Object.freeze({
        paymentPlanVersion: 3,
        network: policy.network,
        policyRevision: Number(response.storeRevenuePolicyRevision),
        policyHash: policy.policyHash,
        packs: input.packs,
        amountMicroalgo: total.toString(),
        networkFeeMicroalgo: fee.toString(),
        totalDebitMicroalgo: (total + fee).toString(),
        firstValidRound,
        lastValidRound,
        paymentGroupId: String(response.paymentGroupId),
        recipients: Object.freeze(reviewRecipients),
        expectedTransactionIds: Object.freeze([...expectedTxIds]),
        replayed: response.replayed === true,
      }),
    })
  }

  const prepareShopAssetAcceptance = async (input = {}, signingOptions = {}) => {
    if (!input || typeof input !== 'object' || Array.isArray(input))
      throw new Error('shop.prepareAssetAcceptance requires the awaiting_opt_in result.')
    const commitId = shopRecoveryCommitId(input.commitId, 'prepareAssetAcceptance')
    const required = input.required
    if (!required || required.success !== false || required.status !== 'awaiting_opt_in'
      || required.code !== 'SHOP_ASSET_OPT_IN_REQUIRED' || !Array.isArray(required.assetIds)
      || !Array.isArray(required.items) || !Array.isArray(required.deliveredParts))
      throw new Error('shop.prepareAssetAcceptance requires the exact awaiting_opt_in result from watchDelivery().')
    const assetIds = [...required.assetIds]
    if (assetIds.length < 1 || assetIds.length > 50
      || assetIds.some(value => !Number.isSafeInteger(value) || value < 1)
      || new Set(assetIds).size !== assetIds.length)
      throw new Error('shop.prepareAssetAcceptance requires 1–50 unique positive asset IDs.')
    const network = String(input.expectedNetwork ?? '')
    if (!Object.hasOwn(SHOP_ALGORAND_NETWORK_IDENTITIES, network))
      throw new Error('shop.prepareAssetAcceptance expectedNetwork must be independently pinned as mainnet or testnet.')
    const algosdk = signingOptions.algosdk ?? rememberedAlgorandSigner?.algosdk ?? options.algosdk
    if (typeof algosdk?.decodeUnsignedTransaction !== 'function'
      || typeof algosdk?.decodeSignedTransaction !== 'function'
      || typeof algosdk?.computeGroupID !== 'function' || typeof algosdk?.encodeAddress !== 'function')
      throw new Error('shop.prepareAssetAcceptance requires the official algosdk package.')
    const signer = shopSigningWallet(signingOptions, algosdk, network)
    let watchOptions = signingOptions.watchDelivery
    if (watchOptions === true) watchOptions = {}
    if (watchOptions !== undefined && watchOptions !== false
      && (!watchOptions || typeof watchOptions !== 'object' || Array.isArray(watchOptions)))
      throw new Error('shop.prepareAssetAcceptance watchDelivery must be true, false, or watch options.')
    const watchSignal = watchOptions && watchOptions !== false
      ? requireShopWatchSignal(watchOptions.signal) : null
    if (watchOptions && watchOptions !== false && watchOptions.onProgress !== undefined
      && typeof watchOptions.onProgress !== 'function')
      throw new Error('shop.prepareAssetAcceptance watchDelivery.onProgress must be a function.')

    const idChunks = []
    for (let index = 0; index < assetIds.length; index += 16) idChunks.push(assetIds.slice(index, index + 16))
    const responses = await Promise.all(idChunks.map(ids => request('/v1/transactions/build-optin-group', {
      method: 'POST', body: { assetIds: ids }, timeoutMs: 35_000,
    })))
    const expectedNetwork = SHOP_ALGORAND_NETWORK_IDENTITIES[network]
    const chunks = responses.map((response, chunkIndex) => {
      const ids = idChunks[chunkIndex]
      if (!response || response.success !== true || response.txType !== 'asset_optin_group'
        || String(response.from ?? '').trim().toUpperCase() !== signer.walletAddress
        || !Array.isArray(response.assetIds) || response.assetIds.length !== ids.length
        || response.assetIds.some((value, index) => value !== ids[index])
        || !Array.isArray(response.unsignedTxnsBase64)
        || response.unsignedTxnsBase64.length !== ids.length)
        throw shopUnsafe('Blockmaker returned a different Store asset-acceptance group.')
      const unsignedBytes = response.unsignedTxnsBase64.map(shopCanonicalUnsignedBytes)
      let transactions
      try { transactions = unsignedBytes.map(value => algosdk.decodeUnsignedTransaction(value)) }
      catch { throw shopUnsafe('Blockmaker returned an invalid Store asset-acceptance transaction.') }
      const group = transactions[0]?.group
      if (!(group instanceof Uint8Array) || group.byteLength !== 32
        || transactions.some(transaction => !sameBytes(group, transaction?.group))
        || !sameBytes(group, recomputeShopTransactionGroup(algosdk, transactions)))
        throw shopUnsafe('Blockmaker returned a broken Store asset-acceptance group.')
      let firstValidRound
      let lastValidRound
      const expectedTransactionIds = transactions.map((transaction, index) => {
        const transfer = transaction?.assetTransfer
        const allowed = ['name', 'tag', 'type', 'sender', 'from', 'assetTransfer', 'to', 'amount',
          'assetIndex', 'fee', 'flatFee', 'firstValid', 'firstRound', 'lastValid', 'lastRound',
          'genesisID', 'genesisId', 'genesisHash', 'note', 'lease', 'group', 'rekeyTo', 'reKeyTo',
          'closeRemainderTo', 'assetSender', 'revocationTarget']
        if (Object.entries(transaction ?? {}).some(([key, value]) => !allowed.includes(key) && shopMeaningful(value))
          || (transfer && Object.entries(transfer).some(([key, value]) =>
            !['assetIndex', 'amount', 'assetSender', 'receiver', 'closeRemainderTo'].includes(key)
              && shopMeaningful(value))))
          throw shopUnsafe('Blockmaker added an unexpected field to Store asset acceptance.')
        const first = shopTransactionRound(transaction, true)
        const last = shopTransactionRound(transaction, false)
        if (index === 0) { firstValidRound = first; lastValidRound = last }
        const genesisHash = transaction?.genesisHash instanceof Uint8Array
          ? base64FromBytes(transaction.genesisHash) : ''
        let txId = ''
        let encodedAgain
        try { txId = String(transaction?.txID?.() ?? ''); encodedAgain = transaction?.toByte?.() }
        catch { /* rejected below */ }
        if (transaction?.type !== 'axfer'
          || addressFromTransaction(algosdk, transaction?.sender ?? transaction?.from) !== signer.walletAddress
          || transactionReceiver(algosdk, transaction) !== signer.walletAddress
          || transactionAmount(transaction) !== 0 || assetIndex(transaction) !== ids[index]
          || shopExactUint(transaction?.fee, 'Store asset-acceptance fee') !== 1_000n
          || first !== firstValidRound || last !== lastValidRound || first < 1 || last < first || last - first > 1_000
          || String(transaction?.genesisID ?? transaction?.genesisId ?? '') !== expectedNetwork.genesisId
          || genesisHash !== expectedNetwork.genesisHashBase64
          || shopMeaningful(transaction?.note) || shopMeaningful(transaction?.lease)
          || shopMeaningful(transaction?.rekeyTo) || shopMeaningful(transaction?.reKeyTo)
          || shopMeaningful(transfer?.closeRemainderTo) || shopMeaningful(transaction?.closeRemainderTo)
          || shopMeaningful(transfer?.assetSender) || shopMeaningful(transaction?.assetSender)
          || shopMeaningful(transaction?.revocationTarget)
          || !(encodedAgain instanceof Uint8Array) || !sameBytes(encodedAgain, unsignedBytes[index])
          || !/^[A-Z2-7]{52}$/.test(txId))
          throw shopUnsafe('Blockmaker changed an exact Store asset acceptance.')
        return txId
      })
      return Object.freeze({
        assetIds: Object.freeze([...ids]), transactions: Object.freeze(transactions),
        unsignedBytes: Object.freeze(unsignedBytes),
        expectedTransactionIds: Object.freeze(expectedTransactionIds),
        groupId: shopRawBase32(group), firstValidRound, lastValidRound,
      })
    })
    const transactions = Object.freeze(chunks.flatMap(chunk => chunk.transactions))
    const expectedBytes = Object.freeze(chunks.flatMap(chunk => chunk.unsignedBytes))
    let signed = null
    const submitted = new Set()
    const receipts = []
    let active = null
    let completed = null
    let terminalError = null
    let state = 'ready'

    const submit = async () => {
      if (String(session?.walletAddress ?? '').trim().toUpperCase() !== signer.walletAddress)
        throw new BlockmakerError('Restore the original Shop player before retrying these exact signed bytes.', { code: 'AUTH_MISMATCH' })
      for (let index = 0, offset = 0; index < chunks.length; offset += chunks[index].transactions.length, index++) {
        if (submitted.has(index)) continue
        const chunk = chunks[index]
        const values = signed.slice(offset, offset + chunk.transactions.length)
        const result = await request('/v1/transactions/submit', {
          method: 'POST', body: { signedTxnsBase64: [...values] }, timeoutMs: 35_000,
        })
        if (result?.success !== true || result.txId !== chunk.expectedTransactionIds[0]
          || (result.txIds !== undefined && (!Array.isArray(result.txIds)
            || result.txIds.length !== chunk.expectedTransactionIds.length
            || result.txIds.some((value, item) => value !== chunk.expectedTransactionIds[item])))
          || (result.alreadyConfirmed !== undefined && typeof result.alreadyConfirmed !== 'boolean'))
          throw shopUnsafe('Blockmaker returned a submit receipt for a different Store asset acceptance.')
        submitted.add(index)
        receipts[index] = Object.freeze({
          groupId: chunk.groupId, txId: chunk.expectedTransactionIds[0],
          alreadyConfirmed: result.alreadyConfirmed === true,
        })
      }
      const receipt = {
        success: true, commitId, network, walletAddress: signer.walletAddress,
        assetIds: Object.freeze([...assetIds]), receipts: Object.freeze([...receipts]),
      }
      if (watchOptions !== undefined && watchOptions !== false) {
        state = 'watching'
        const accepted = new Set(assetIds)
        while (true) {
          const delivery = await watchShopDelivery(commitId, watchOptions)
          if (delivery.status !== 'awaiting_opt_in' || delivery.assetIds.length === 0
            || delivery.assetIds.some(value => !accepted.has(value)))
            return Object.freeze({ ...receipt, delivery })
          await shopWatchDelay(shopWatchRetryMs(delivery.retryAfterSeconds), watchSignal)
        }
      }
      return Object.freeze(receipt)
    }
    const launch = () => {
      if (completed) return Promise.resolve(completed)
      if (terminalError) return Promise.reject(terminalError)
      if (active) return active
      let operation
      if (signed) {
        state = submitted.size === chunks.length ? 'watching' : 'submitting'
        operation = submit()
      } else {
        signer.assertMatch()
        state = 'signing'
        let walletResult
        try {
          walletResult = signer.usePera
            ? signer.wallet.signTransaction(chunks.map(chunk => chunk.transactions.map(txn => ({ txn }))))
            : signer.wallet.signTransactions(transactions, transactions.map((_, index) => index))
        } catch (error) { state = 'ready'; return Promise.reject(error) }
        operation = Promise.resolve(walletResult).then(values => {
          signed = normalizeShopSignedTransactions(values, expectedBytes, algosdk)
          state = 'submitting'
          return submit()
        })
      }
      const tracked = Promise.resolve(operation).then(result => {
        completed = result
        state = 'complete'
        if (active === tracked) active = null
        return result
      }, error => {
        const terminal = signed && error instanceof BlockmakerError && error.code === 'TX_EXPIRED'
        if (terminal) terminalError = error
        state = terminal ? 'terminal' : signed
          ? submitted.size === chunks.length ? 'submitted' : 'signed_retryable' : 'ready'
        if (active === tracked) active = null
        throw error
      })
      active = tracked
      return tracked
    }
    return Object.freeze({
      commitId,
      review: Object.freeze({
        network, walletAddress: signer.walletAddress, assetIds: Object.freeze([...assetIds]),
        networkFeeMicroalgo: String(assetIds.length * 1_000), chunks: Object.freeze(chunks.map(chunk => Object.freeze({
          assetIds: chunk.assetIds, groupId: chunk.groupId,
          firstValidRound: chunk.firstValidRound, lastValidRound: chunk.lastValidRound,
          expectedTransactionIds: chunk.expectedTransactionIds,
        }))),
      }),
      get state() { return state },
      get signedForRetry() { return signed !== null && submitted.size < chunks.length && terminalError === null },
      launch, retry: launch,
    })
  }

  const concatBytes = (...values) => {
    const size = values.reduce((sum, value) => sum + value.byteLength, 0)
    const output = new Uint8Array(size)
    let offset = 0
    for (const value of values) { output.set(value, offset); offset += value.byteLength }
    return output
  }

  const nftMintingOperationId = value => {
    const operationId = String(value ?? '').trim()
    if (!/^[A-Za-z0-9][A-Za-z0-9:._-]{0,127}$/.test(operationId))
      throw new Error('nftMinting requires a valid operation ID returned by Blockmaker.')
    return operationId
  }

  const nftMintingConfig = () => request('/v1/nft-minting/config', { auth: false })

  const prepareNftMint = input => {
    if (!session?.sessionToken)
      throw new BlockmakerError('Sign in with Pera or Lute before preparing a TestNet NFT.', { status: 401, code: 'AUTH_MISSING' })
    if (!input || typeof input !== 'object' || Array.isArray(input))
      throw new Error('nftMinting.prepare requires an input object.')
    const idempotencyKey = String(input.idempotencyKey ?? '').trim()
    if (!/^[A-Za-z0-9][A-Za-z0-9:._-]{7,127}$/.test(idempotencyKey))
      throw new Error('nftMinting.prepare idempotencyKey must be a stable 8-128 character value.')
    const expectedConfigRevision = String(input.expectedConfigRevision ?? '').trim()
    if (!/^[1-9][0-9]{0,8}$/.test(expectedConfigRevision))
      throw new Error('nftMinting.prepare expectedConfigRevision must come from nftMinting.readiness().')
    const assetName = String(input.assetName ?? '')
    const unitName = String(input.unitName ?? '')
    const metadataUrl = String(input.metadataUrl ?? '')
    const metadataBytesBase64 = String(input.metadataBytesBase64 ?? '').trim()
    if (!assetName.trim() || new TextEncoder().encode(assetName.trim()).byteLength > 32)
      throw new Error('nftMinting.prepare assetName must contain 1-32 UTF-8 bytes.')
    if (!unitName.trim() || new TextEncoder().encode(unitName.trim()).byteLength > 8)
      throw new Error('nftMinting.prepare unitName must contain 1-8 UTF-8 bytes.')
    let parsedMetadataUrl
    try { parsedMetadataUrl = new URL(metadataUrl) } catch { /* rejected below */ }
    if (!metadataUrl || new TextEncoder().encode(metadataUrl).byteLength > 96 || !metadataUrl.endsWith('#arc3')
      || !['ipfs:', 'https:'].includes(parsedMetadataUrl?.protocol)
      || parsedMetadataUrl?.username || parsedMetadataUrl?.password)
      throw new Error('nftMinting.prepare metadataUrl must be an ARC-3 URL of at most 96 UTF-8 bytes ending in #arc3.')
    if (!metadataBytesBase64 || metadataBytesBase64.length > 87_384 || metadataBytesBase64.length % 4 !== 0
      || !/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(metadataBytesBase64))
      throw new Error('nftMinting.prepare metadataBytesBase64 must contain bounded canonical base64 metadata.')
    const metadataSha256 = input.metadataSha256 == null ? undefined : String(input.metadataSha256).trim().toLowerCase()
    if (metadataSha256 !== undefined && !/^[a-f0-9]{64}$/.test(metadataSha256))
      throw new Error('nftMinting.prepare metadataSha256 must be one lowercase SHA-256 digest.')
    return request('/v1/nft-minting/operations', {
      method: 'POST',
      body: {
        idempotencyKey,
        expectedConfigRevision,
        assetName: assetName.trim(),
        unitName: unitName.trim(),
        metadataUrl,
        metadataBytesBase64,
        ...(metadataSha256 === undefined ? {} : { metadataSha256 }),
      },
      timeoutMs: 35_000,
    })
  }

  const nftMintingStatus = operationId => request(
    `/v1/nft-minting/operations/${encodeURIComponent(nftMintingOperationId(operationId))}`,
  )

  const reconcileNftMint = operationId => request(
    `/v1/nft-minting/operations/${encodeURIComponent(nftMintingOperationId(operationId))}/reconcile`,
    { method: 'POST', timeoutMs: 35_000 },
  )

  const mintUintString = value => {
    if (typeof value === 'bigint' && value >= 0n) return value.toString()
    if (typeof value === 'number' && Number.isSafeInteger(value) && value >= 0) return String(value)
    if (typeof value === 'string' && /^(?:0|[1-9][0-9]*)$/.test(value)) return value
    return null
  }

  const mintAssetField = (transaction, nestedName, legacyName) =>
    transaction?.assetConfig?.[nestedName] ?? transaction?.[legacyName]

  const validateNftMintTransaction = (prepared, algosdk) => {
    const operation = prepared?.operation
    const walletAddress = String(session?.walletAddress ?? '').trim().toUpperCase()
    if (!isAlgorandAddressShape(walletAddress))
      throw new BlockmakerError('Your Blockmaker session has no valid Algorand wallet.', { code: 'AUTH_INVALID' })
    if (!operation || typeof operation !== 'object' || operation.status !== 'ready_for_wallet'
      || operation.network !== 'testnet' || operation.preset !== 'immutable_arc3'
      || nftMintingOperationId(operation.operationId) !== operation.operationId
      || String(operation.creator ?? '').trim().toUpperCase() !== walletAddress
      || !String(operation.configRevision ?? '').trim()
      || !Array.isArray(operation.standards) || operation.standards.length < 1
      || operation.standards[0] !== 'ARC-3'
      || operation.standards.some(value => !['ARC-3', 'ARC-16'].includes(value))
      || operation.safety?.testnetOnly !== true
      || operation.safety?.exactWalletSignatureRequired !== true
      || operation.safety?.simulationPassed !== true
      || operation.safety?.managedSigner !== false
      || operation.safety?.deliveryIncluded !== false
      || operation.safety?.activationIncluded !== false)
      throw new BlockmakerError('Blockmaker returned an invalid TestNet mint operation. Nothing was signed.', { code: 'MINT_OPERATION_INVALID' })
    if ((session.accountKind && session.accountKind !== 'algorand_wallet')
      || (session.authProvider && !['pera', 'lute'].includes(String(session.authProvider).toLowerCase())))
      throw new BlockmakerError('Sign in with Pera or Lute before minting a TestNet NFT.', { code: 'PROVIDER_NOT_ENABLED' })
    if (typeof algosdk?.decodeUnsignedTransaction !== 'function' || typeof algosdk?.encodeAddress !== 'function')
      throw new Error('NFT mint signing requires the official algosdk package.')
    const transactionPlan = operation.transaction
    const receipt = operation.receipt
    const unsignedTxnBase64 = String(transactionPlan?.unsignedTxnBase64 ?? '')
    if (!unsignedTxnBase64 || !receipt || typeof receipt !== 'object')
      throw new BlockmakerError('Blockmaker returned an incomplete TestNet mint operation. Nothing was signed.', { code: 'MINT_OPERATION_INVALID' })
    let unsignedBytes
    let transaction
    try {
      unsignedBytes = bytesFromBase64(unsignedTxnBase64)
      transaction = algosdk.decodeUnsignedTransaction(unsignedBytes)
    } catch {
      throw new BlockmakerError('Blockmaker returned an invalid TestNet mint transaction. Nothing was signed.', { code: 'TX_INVALID' })
    }
    const sender = addressFromTransaction(algosdk, transaction?.sender ?? transaction?.from)
    const firstValidRound = mintUintString(transaction?.firstValid ?? transaction?.firstRound)
    const lastValidRound = mintUintString(transaction?.lastValid ?? transaction?.lastRound)
    const total = mintUintString(mintAssetField(transaction, 'total', 'assetTotal'))
    const decimals = Number(mintAssetField(transaction, 'decimals', 'assetDecimals') ?? 0)
    const defaultFrozen = mintAssetField(transaction, 'defaultFrozen', 'assetDefaultFrozen') === true
    const assetName = String(mintAssetField(transaction, 'assetName', 'assetName')
      ?? mintAssetField(transaction, 'name', 'assetName') ?? '')
    const unitName = String(mintAssetField(transaction, 'unitName', 'assetUnitName') ?? '')
    const metadataUrl = String(mintAssetField(transaction, 'assetURL', 'assetURL') ?? '')
    const metadataHashValue = mintAssetField(transaction, 'assetMetadataHash', 'assetMetadataHash')
    const metadataSha256 = metadataHashValue instanceof Uint8Array
      ? Array.from(metadataHashValue).map(byte => byte.toString(16).padStart(2, '0')).join('')
      : ''
    const genesisHash = transaction?.genesisHash instanceof Uint8Array
      ? base64FromBytes(transaction.genesisHash)
      : ''
    const note = transaction?.note instanceof Uint8Array
      ? new TextDecoder().decode(transaction.note)
      : ''
    const expectedTxId = String(transactionPlan.expectedTxId ?? '')
    const decodedTxId = typeof transaction?.txID === 'function' ? String(transaction.txID()) : ''
    const encodedAgain = typeof transaction?.toByte === 'function' ? transaction.toByte() : null
    const unsafeAuthority = ['manager', 'reserve', 'freeze', 'clawback']
      .some(name => mintAssetField(transaction, name, `asset${name[0].toUpperCase()}${name.slice(1)}`))
    const assetIndex = mintUintString(mintAssetField(transaction, 'assetIndex', 'assetIndex') ?? 0)
    const receiptSafe = receipt.assetName === assetName && receipt.unitName === unitName
      && receipt.total === '1' && receipt.decimals === 0 && receipt.defaultFrozen === false
      && receipt.metadataUrl === metadataUrl && receipt.metadataSha256 === metadataSha256
      && receipt.manager === null && receipt.reserve === null && receipt.freeze === null && receipt.clawback === null
      && receipt.networkFeeMicroalgo === 1_000
      && receipt.permanentCreatorMinimumBalanceIncreaseMicroalgo === 100_000
      && receipt.recoverableMinimumBalance === false
      && receipt.rekeyTo === null && receipt.closeRemainderTo === null && receipt.assetCloseTo === null
    const validRoundWindow = firstValidRound !== null && lastValidRound !== null
      && BigInt(lastValidRound) >= BigInt(firstValidRound)
      && BigInt(lastValidRound) - BigInt(firstValidRound) <= 50n
    const unsafe = transaction?.type !== 'acfg' || sender !== walletAddress
      || String(transaction?.genesisID ?? transaction?.genesisId ?? '') !== 'testnet-v1.0'
      || genesisHash !== 'SGO1GKSzyE7IEPItTxCByw9x8FmnrCDexi9/cOUJOiI='
      || Number(transaction?.fee) !== 1_000
      || !validRoundWindow
      || firstValidRound !== mintUintString(transactionPlan.firstValidRound)
      || lastValidRound !== mintUintString(transactionPlan.lastValidRound)
      || total !== '1' || decimals !== 0 || defaultFrozen
      || assetIndex !== '0' || unsafeAuthority
      || transaction?.group?.byteLength || transaction?.lease?.byteLength
      || transaction?.rekeyTo || transaction?.reKeyTo
      || transaction?.closeRemainderTo || transaction?.assetCloseTo
      || note !== `Blockmaker mint:${operation.operationId}`
      || !/^[A-Z2-7]{52}$/.test(expectedTxId) || decodedTxId !== expectedTxId
      || !(encodedAgain instanceof Uint8Array) || !sameBytes(encodedAgain, unsignedBytes)
      || !receiptSafe
    if (unsafe)
      throw new BlockmakerError('Blockmaker refused an unsafe or altered TestNet NFT mint. Nothing was signed.', { code: 'TX_UNSAFE' })
    const signingIntentExpiresAt = Number(operation.signingIntentExpiresAt)
    const now = Date.now()
    if (typeof operation.signingIntent !== 'string' || !operation.signingIntent
      || !Number.isFinite(signingIntentExpiresAt) || signingIntentExpiresAt <= now
      || signingIntentExpiresAt > now + 10 * 60 * 1_000)
      throw new BlockmakerError('This TestNet mint review has expired. Prepare it again before signing.', { code: 'TX_INTENT_EXPIRED' })
    return { operation, transaction }
  }

  const validateUniversalUsernameTransactions = (built, algosdk, acknowledgedAddress = session?.walletAddress) => {
    const walletAddress = String(acknowledgedAddress ?? '').trim().toUpperCase()
    if (!isAlgorandAddressShape(walletAddress))
      throw new BlockmakerError('Your Blockmaker session has no valid Algorand wallet.', { code: 'AUTH_INVALID' })
    if (typeof algosdk?.decodeUnsignedTransaction !== 'function'
      || typeof algosdk?.encodeAddress !== 'function'
      || typeof algosdk?.decodeAddress !== 'function'
      || typeof algosdk?.getApplicationAddress !== 'function')
      throw new Error('Universal username signing requires the official algosdk package.')

    const encoded = built?.unsignedTxnsBase64
    const quote = built?.quote
    const username = String(built?.username ?? '')
    const appId = Number(built?.appId)
    const gameKeyHex = String(built?.gameKeyHex ?? '').toLowerCase()
    if (!Array.isArray(encoded) || encoded.length !== 2 || !/^[a-z0-9][a-z0-9_-]{2,19}$/.test(username)
      || !Number.isSafeInteger(appId) || appId <= 0 || !/^[0-9a-f]{64}$/.test(gameKeyHex)
      || !quote || typeof built?.reservationId !== 'string' || !built.reservationId)
      throw new BlockmakerError('Blockmaker returned an invalid universal username request.', { code: 'TX_INVALID' })

    let transactions
    try { transactions = encoded.map(value => algosdk.decodeUnsignedTransaction(bytesFromBase64(value))) }
    catch { throw new BlockmakerError('Blockmaker returned an invalid universal username request.', { code: 'TX_INVALID' }) }
    const [payment, application] = transactions
    const appAddress = String(algosdk.getApplicationAddress(appId)).trim().toUpperCase()
    const gameRevenueAddress = String(quote.gameRevenueAddress ?? '').trim().toUpperCase()
    const beneficiaryA = String(quote.beneficiaryAAddress ?? '').trim().toUpperCase()
    const beneficiaryB = String(quote.beneficiaryBAddress ?? '').trim().toUpperCase()
    const paymentReceiverAddress = String(quote.paymentReceiverAddress ?? '').trim().toUpperCase()
    const registrationPrice = Number(quote.registrationPriceMicroalgo)
    const storageDeposit = Number(quote.storageDepositMicroalgo)
    const networkFee = Number(quote.networkFeeMicroalgo)
    const due = Number(quote.amountDueMicroalgo)
    const gameShare = Number(quote.gameShareMicroalgo)
    const beneficiaryAShare = Number(quote.beneficiaryAShareMicroalgo)
    const beneficiaryBShare = Number(quote.beneficiaryBShareMicroalgo)
    const validNumbers = [registrationPrice, storageDeposit, networkFee, due, gameShare, beneficiaryAShare, beneficiaryBShare]
      .every(value => Number.isSafeInteger(value) && value >= 0)
    if (!validNumbers || networkFee !== 10_000 || due !== registrationPrice + storageDeposit + networkFee
      || gameShare !== Math.floor(registrationPrice / 2)
      || beneficiaryAShare !== Math.floor(registrationPrice / 4)
      || beneficiaryBShare !== registrationPrice - gameShare - beneficiaryAShare
      || (quote.purpose !== undefined && quote.purpose !== 'universal_username_registration')
      || (quote.paymentReceiverAddress !== undefined && paymentReceiverAddress !== appAddress)
      || (quote.validUntil !== undefined && Number(quote.validUntil) !== Number(built.validUntil))
      || beneficiaryA !== UNIVERSAL_USERNAME_BENEFICIARY_A
      || beneficiaryB !== UNIVERSAL_USERNAME_BENEFICIARY_B
      || !isAlgorandAddressShape(gameRevenueAddress))
      throw new BlockmakerError('Blockmaker returned an invalid username price or revenue split. Nothing was signed.', { code: 'TX_UNSAFE' })

    const group = payment?.group
    const grouped = group instanceof Uint8Array && group.byteLength === 32
      && application?.group instanceof Uint8Array && sameBytes(group, application.group)
    const commonUnsafe = transaction => addressFromTransaction(algosdk, transaction?.sender ?? transaction?.from) !== walletAddress
      || transaction?.rekeyTo || transaction?.reKeyTo
      || transaction?.payment?.closeRemainderTo || transaction?.closeRemainderTo
      || transaction?.assetTransfer?.closeRemainderTo || transaction?.assetCloseTo
      || transaction?.assetTransfer?.assetSender || transaction?.assetTransfer?.revocationTarget
      || transaction?.assetSender || transaction?.assetRevocationTarget
      || (transaction?.note instanceof Uint8Array && transaction.note.byteLength > 0)
      || (transaction?.lease instanceof Uint8Array && transaction.lease.byteLength > 0)
    if (!grouped || commonUnsafe(payment) || commonUnsafe(application)
      || payment?.type !== 'pay' || Number(payment?.fee) !== 1_000
      || transactionReceiver(algosdk, payment) !== appAddress
      || transactionAmount(payment) !== registrationPrice + storageDeposit
      || application?.type !== 'appl' || Number(application?.fee) !== 9_000
      || applicationIndex(application) !== appId || applicationOnComplete(application) !== 0
      || applicationSelector(application) !== UNIVERSAL_USERNAME_METHOD)
      throw new BlockmakerError('Blockmaker refused an unexpected universal username payment. Nothing was signed.', { code: 'TX_UNSAFE' })

    const args = application?.applicationCall?.appArgs ?? application?.appArgs
    const accounts = application?.applicationCall?.accounts ?? application?.appAccounts
    const boxes = application?.applicationCall?.boxes ?? application?.boxes
    const usernameBytes = new TextEncoder().encode(username)
    const encodedUsername = concatBytes(new Uint8Array([usernameBytes.byteLength >> 8, usernameBytes.byteLength & 255]), usernameBytes)
    const gameKey = Uint8Array.from(gameKeyHex.match(/../g).map(value => Number.parseInt(value, 16)))
    const walletKey = algosdk.decodeAddress(walletAddress)?.publicKey
    if (!(walletKey instanceof Uint8Array) || walletKey.byteLength !== 32)
      throw new BlockmakerError('Your Blockmaker session has an invalid Algorand wallet.', { code: 'AUTH_INVALID' })
    const expectedAccounts = [gameRevenueAddress, beneficiaryA, beneficiaryB]
    const actualAccounts = Array.isArray(accounts)
      ? accounts.map(value => addressFromTransaction(algosdk, value))
      : []
    const expectedBoxes = [
      concatBytes(new TextEncoder().encode('u:'), usernameBytes),
      concatBytes(new TextEncoder().encode('w:'), walletKey),
      concatBytes(new TextEncoder().encode('g:'), gameKey),
    ]
    const expectedRevenueKey = algosdk.decodeAddress(gameRevenueAddress)?.publicKey
    const argsValid = Array.isArray(args) && args.length === 6
      && applicationSelector(application) === UNIVERSAL_USERNAME_METHOD
      && sameBytes(args[1], encodedUsername)
      && sameBytes(args[2], gameKey)
      && expectedRevenueKey instanceof Uint8Array && expectedRevenueKey.byteLength === 32
      && sameBytes(args[3], expectedRevenueKey)
      && applicationArgumentUint64(application, 4) === Number(built.validUntil)
      && args[5] instanceof Uint8Array && args[5].byteLength === 64
    const boxesValid = Array.isArray(boxes) && boxes.length === expectedBoxes.length
      && boxes.every((box, index) => Number(box?.appIndex ?? 0) === 0 && sameBytes(box?.name, expectedBoxes[index]))
    if (!argsValid || actualAccounts.length !== expectedAccounts.length
      || actualAccounts.some((address, index) => address !== expectedAccounts[index]) || !boxesValid)
      throw new BlockmakerError('Blockmaker refused altered universal username settings. Nothing was signed.', { code: 'TX_UNSAFE' })
    const validUntil = Number(built.validUntil)
    const now = Math.floor(Date.now() / 1_000)
    if (!Number.isSafeInteger(validUntil) || validUntil <= now || validUntil > now + 6 * 60)
      throw new BlockmakerError('This username wallet request has expired. Start again.', { code: 'TX_INTENT_EXPIRED' })
    return transactions
  }

  const validateMarketplaceTransactions = (built, algosdk) => {
    if (!session?.walletAddress || !isAlgorandAddressShape(session.walletAddress))
      throw new BlockmakerError('Your Blockmaker session has no valid Algorand wallet.', { code: 'AUTH_INVALID' })
    if (typeof algosdk?.decodeUnsignedTransaction !== 'function' || typeof algosdk?.encodeAddress !== 'function' || typeof algosdk?.getApplicationAddress !== 'function')
      throw new Error('Marketplace signing requires the official algosdk package.')
    const encoded = built?.unsignedTxnsBase64
    if (!Array.isArray(encoded) || encoded.length < 1 || encoded.length > 3)
      throw new BlockmakerError('Blockmaker returned an invalid marketplace transaction group.', { code: 'TX_INVALID' })
    const transactions = encoded.map(value => algosdk.decodeUnsignedTransaction(bytesFromBase64(value)))
    const walletAddress = session.walletAddress.toUpperCase()
    const appId = Number(built?.marketplace?.contract?.appId ?? 0)
    if (!Number.isSafeInteger(appId) || appId <= 0)
      throw new BlockmakerError('This marketplace contract is not configured.', { code: 'MARKETPLACE_NOT_LIVE' })
    const configuredMaxPrice = Number(built?.marketplace?.maxPriceMicroalgo)
    if (!Number.isSafeInteger(configuredMaxPrice) || configuredMaxPrice < 1 || configuredMaxPrice > 100_000_000)
      throw new BlockmakerError('Blockmaker returned an invalid marketplace purchase limit.', { code: 'TX_INVALID' })
    const appAddress = String(algosdk.getApplicationAddress(appId)).toUpperCase()
    for (const transaction of transactions) {
      const sender = addressFromTransaction(algosdk, transaction?.sender ?? transaction?.from)
      const fee = Number(transaction?.fee ?? 0)
      if (sender !== walletAddress || !Number.isSafeInteger(fee) || fee < 0 || fee > 5_000
        || transaction?.rekeyTo || transaction?.reKeyTo
        || transaction?.payment?.closeRemainderTo || transaction?.closeRemainderTo
        || transaction?.assetTransfer?.closeRemainderTo || transaction?.assetCloseTo
        || transaction?.assetTransfer?.assetSender || transaction?.assetTransfer?.revocationTarget
        || transaction?.assetSender || transaction?.assetRevocationTarget)
        throw new BlockmakerError('Blockmaker refused an unsafe marketplace transaction. Nothing was signed.', { code: 'TX_UNSAFE' })
    }
    const grouped = transactions.length > 1
    if (grouped) {
      const group = transactions[0]?.group
      const sameGroup = group instanceof Uint8Array && group.byteLength > 0
        && transactions.every(transaction => transaction.group instanceof Uint8Array
          && transaction.group.byteLength === group.byteLength
          && transaction.group.every((byte, index) => byte === group[index]))
      if (!sameGroup) throw new BlockmakerError('Blockmaker returned an ungrouped marketplace action. Nothing was signed.', { code: 'TX_UNSAFE' })
    }

    const action = String(built?.action ?? '')
    const assetId = Number(built?.assetId ?? 0)
    if (!Number.isSafeInteger(assetId) || assetId <= 0)
      throw new BlockmakerError('Blockmaker returned an invalid marketplace NFT.', { code: 'TX_INVALID' })
    const expectCall = (transaction, selector, expectedFee) => transaction?.type === 'appl'
      && applicationIndex(transaction) === appId && applicationOnComplete(transaction) === 0
      && applicationSelector(transaction) === selector && Number(transaction?.fee ?? 0) === expectedFee

    if (action === 'list') {
      const priceMicroalgo = Number(built?.priceMicroalgo)
      const contractGameId = Number(built?.marketplace?.contract?.gameId)
      if (transactions.length !== 3
        || !Number.isSafeInteger(priceMicroalgo) || priceMicroalgo <= 0 || priceMicroalgo > configuredMaxPrice
        || !Number.isSafeInteger(contractGameId) || contractGameId <= 0
        || transactions[0]?.type !== 'pay' || Number(transactions[0]?.fee ?? 0) !== 1_000
        || transactionReceiver(algosdk, transactions[0]) !== appAddress || transactionAmount(transactions[0]) !== 134_900
        || !expectCall(transactions[1], '541e0f67', 2_000)
        || applicationArgumentUint64(transactions[1], 1) !== priceMicroalgo
        || applicationArgumentUint64(transactions[1], 2) !== contractGameId
        || transactions[2]?.type !== 'axfer' || transactionReceiver(algosdk, transactions[2]) !== appAddress
        || Number(transactions[2]?.fee ?? 0) !== 1_000
        || transactionAmount(transactions[2]) !== 1 || assetIndex(transactions[2]) !== assetId)
        throw new BlockmakerError('Blockmaker refused an unexpected NFT listing transaction. Nothing was signed.', { code: 'TX_UNSAFE' })
    } else if (action === 'buy') {
      if (transactions.length !== 2 && transactions.length !== 3)
        throw new BlockmakerError('Blockmaker returned an invalid NFT purchase group.', { code: 'TX_INVALID' })
      const paymentIndex = transactions.length - 2
      const priceMicroalgo = Number(built?.priceMicroalgo)
      if (!Number.isSafeInteger(priceMicroalgo) || priceMicroalgo <= 0 || priceMicroalgo > configuredMaxPrice)
        throw new BlockmakerError('Blockmaker returned a purchase above this game’s marketplace limit. Nothing was signed.', { code: 'TX_UNSAFE' })
      if (paymentIndex === 1 && (transactions[0]?.type !== 'axfer'
        || transactionReceiver(algosdk, transactions[0]) !== walletAddress || transactionAmount(transactions[0]) !== 0
        || Number(transactions[0]?.fee ?? 0) !== 1_000 || assetIndex(transactions[0]) !== assetId))
        throw new BlockmakerError('Blockmaker refused an unsafe NFT opt-in. Nothing was signed.', { code: 'TX_UNSAFE' })
      if (transactions[paymentIndex]?.type !== 'pay' || Number(transactions[paymentIndex]?.fee ?? 0) !== 1_000
        || transactionReceiver(algosdk, transactions[paymentIndex]) !== appAddress
        || transactionAmount(transactions[paymentIndex]) !== priceMicroalgo
        || !expectCall(transactions[paymentIndex + 1], '790bf59f', 5_000)
        || applicationArgumentUint64(transactions[paymentIndex + 1], 1) !== assetId)
        throw new BlockmakerError('Blockmaker refused an unexpected NFT purchase transaction. Nothing was signed.', { code: 'TX_UNSAFE' })
    } else if (action === 'cancel') {
      if (transactions.length !== 1 || !expectCall(transactions[0], '772b39ba', 3_000)
        || applicationArgumentUint64(transactions[0], 1) !== assetId)
        throw new BlockmakerError('Blockmaker refused an unexpected listing cancellation. Nothing was signed.', { code: 'TX_UNSAFE' })
    } else {
      throw new BlockmakerError('Blockmaker returned an unknown marketplace action.', { code: 'TX_INVALID' })
    }
    const decodedNetworkFee = transactions.reduce((sum, transaction) => sum + Number(transaction?.fee ?? 0), 0)
    if (built?.networkFeeMicroalgo !== undefined && Number(built.networkFeeMicroalgo) !== decodedNetworkFee)
      throw new BlockmakerError('Blockmaker returned a marketplace fee summary that does not match the transaction. Nothing was signed.', { code: 'TX_UNSAFE' })
    return transactions
  }

  const normalizeSignedTransactions = values => {
    if (!Array.isArray(values)) return []
    return values.map(value => {
      if (value instanceof Uint8Array) return base64FromBytes(value)
      if (typeof value === 'string' && value.length > 0 && value.length <= 200_000) {
        bytesFromBase64(value)
        return value
      }
      return null
    }).filter(Boolean)
  }

  const submitSignedNftMint = (prepared, signedTxn) => {
    const operation = prepared?.operation
    const operationId = nftMintingOperationId(operation?.operationId)
    const signed = normalizeSignedTransactions([signedTxn])
    if (signed.length !== 1)
      throw new BlockmakerError('Your wallet did not sign the TestNet NFT transaction.', { code: 'WALLET_SIGNATURE_MISSING' })
    if (typeof operation?.signingIntent !== 'string' || !operation.signingIntent)
      throw new BlockmakerError('That TestNet NFT review has no valid signing intent.', { code: 'MINT_OPERATION_INVALID' })
    return request(`/v1/nft-minting/operations/${encodeURIComponent(operationId)}/submit`, {
      method: 'POST',
      body: { signedTxnBase64: signed[0], signingIntent: operation.signingIntent },
      timeoutMs: 35_000,
    })
  }

  const signAndSubmitNftMint = async (prepared, signingOptions = {}) => {
    const wallet = signingOptions.wallet
    const algosdk = signingOptions.algosdk
    const providerId = String(wallet?.providerId ?? wallet?.metadata?.providerId ?? '').trim().toLowerCase()
    if (!['pera', 'lute'].includes(providerId))
      throw new BlockmakerError('Use an explicit Blockmaker Pera or Lute TestNet wallet adapter for NFT minting.', { code: 'PROVIDER_NOT_ENABLED' })
    const walletGenesisId = String(wallet?.networkGenesisId ?? wallet?.metadata?.networkGenesisId ?? '').trim()
    if (walletGenesisId !== 'testnet-v1.0')
      throw new BlockmakerError(`Reconnect ${providerId === 'pera' ? 'Pera' : 'Lute'} Wallet for Algorand TestNet before minting.`, { code: 'MINT_NETWORK_INVALID' })
    const { operation, transaction } = validateNftMintTransaction(prepared, algosdk)
    const accountAddress = value => String(typeof value === 'string' ? value : value?.address ?? '').trim().toUpperCase()
    let accounts = Array.isArray(wallet?.accounts) ? wallet.accounts : []
    if (!accounts.length && typeof wallet?.resumeSession === 'function') {
      try {
        const resumed = await wallet.resumeSession()
        accounts = Array.isArray(resumed) ? resumed : Array.isArray(wallet?.accounts) ? wallet.accounts : []
      } catch { accounts = [] }
    }
    if (!accounts.length && typeof wallet?.connect === 'function') accounts = await wallet.connect()
    if (!Array.isArray(accounts) || !accounts.map(accountAddress).includes(operation.creator))
      throw new BlockmakerError('That wallet did not include the TestNet creator account for this mint.', { code: 'WALLET_ACCOUNT_MISSING' })
    let signed
    if (typeof wallet?.signTransactions === 'function') {
      signed = await wallet.signTransactions([transaction], [0])
    } else if (typeof wallet?.signTransaction === 'function') {
      // Leave signers unset so an ARC-1 wallet can use the current authorized
      // signer of a normally rekeyed creator. The server checks TestNet auth-addr.
      signed = await wallet.signTransaction([[{ txn: transaction }]])
    } else {
      throw new BlockmakerError('That Pera or Lute adapter cannot sign Algorand transactions.', { code: 'PROVIDER_UNAVAILABLE' })
    }
    const normalized = normalizeSignedTransactions(signed)
    if (normalized.length !== 1)
      throw new BlockmakerError('Your wallet did not sign the TestNet NFT transaction.', { code: 'WALLET_SIGNATURE_MISSING' })
    return submitSignedNftMint(prepared, normalized[0])
  }

  const completeUniversalUsername = (prepared, signedTxnsBase64) => {
    if (!prepared || typeof prepared !== 'object')
      throw new Error('profile.completeUniversalUsername requires the exact result from prepareUniversalUsername().')
    const signed = normalizeSignedTransactions(signedTxnsBase64)
    if (signed.length !== 2)
      throw new BlockmakerError('Your wallet did not sign both universal username transactions.', { code: 'WALLET_SIGNATURE_MISSING' })
    return request('/v1/profile/username/claim/complete', {
      method: 'POST',
      body: {
        reservationId: prepared.reservationId,
        signedTxnsBase64: signed,
        signingIntent: prepared.signingIntent,
      },
      timeoutMs: 35_000,
    })
  }

  const signAndCompleteUniversalUsername = async (prepared, signingOptions = {}) => {
    const signerSdk = signingOptions.algosdk ?? rememberedAlgorandSigner?.algosdk ?? options.algosdk
    const transactions = validateUniversalUsernameTransactions(prepared, signerSdk)
    let signedTxnsBase64 = []
    const customSign = signingOptions.signTransactions ?? options.signTransactions
    if (typeof customSign === 'function') {
      signedTxnsBase64 = normalizeSignedTransactions(await customSign(transactions, [0, 1], {
        action: 'username.register',
        walletAddress: session.walletAddress,
        username: prepared.username,
        amountDueMicroalgo: prepared.quote.amountDueMicroalgo,
        unsignedTxnsBase64: [...prepared.unsignedTxnsBase64],
      }))
    } else {
      const wallet = signingOptions.wallet
        ?? (rememberedAlgorandSigner?.walletAddress === session?.walletAddress ? rememberedAlgorandSigner.wallet : null)
      if (typeof wallet?.signTransactions === 'function') {
        signedTxnsBase64 = normalizeSignedTransactions(await wallet.signTransactions(transactions, [0, 1]))
      } else if (typeof wallet?.signTransaction === 'function') {
        signedTxnsBase64 = normalizeSignedTransactions(await wallet.signTransaction([
          transactions.map(transaction => ({ txn: transaction, signers: [session.walletAddress] })),
        ]))
      } else if (prepared.signingIntent) {
        const signed = await request('/v1/auth/sign', {
          method: 'POST',
          body: {
            unsignedTxnsBase64: prepared.unsignedTxnsBase64,
            signingIntent: prepared.signingIntent,
            gameId,
          },
        })
        signedTxnsBase64 = Array.isArray(signed?.signedTxnsBase64)
          ? signed.signedTxnsBase64
          : signed?.signedTxnBase64 ? [signed.signedTxnBase64] : []
      }
    }
    if (signedTxnsBase64.length !== transactions.length)
      throw new BlockmakerError(
        'Reconnect the Algorand wallet for this player, then approve both username transactions. EVM sign-in by itself cannot approve an Algorand payment.',
        { code: 'WALLET_SIGNATURE_MISSING' },
      )
    return completeUniversalUsername(prepared, signedTxnsBase64)
  }

  const registerUniversalUsername = async (username, signingOptions = {}) => {
    if (!session?.sessionToken)
      throw new BlockmakerError('Sign in before registering a universal username.', { status: 401, code: 'AUTH_MISSING' })
    const prepared = await prepareUniversalUsername(username)
    return signAndCompleteUniversalUsername(prepared, signingOptions)
  }

  const signAndSubmitMarketplace = async (built, signingOptions = {}) => {
    const signerSdk = signingOptions.algosdk ?? rememberedAlgorandSigner?.algosdk ?? options.algosdk
    const transactions = validateMarketplaceTransactions(built, signerSdk)
    let signedTxnsBase64 = []
    const customSign = signingOptions.signTransactions ?? options.signTransactions
    if (typeof customSign === 'function') {
      signedTxnsBase64 = normalizeSignedTransactions(await customSign(transactions, transactions.map((_, index) => index), {
        action: built.action,
        walletAddress: session.walletAddress,
        unsignedTxnsBase64: [...built.unsignedTxnsBase64],
      }))
    } else {
      const wallet = signingOptions.wallet ?? (rememberedAlgorandSigner?.walletAddress === session.walletAddress ? rememberedAlgorandSigner.wallet : null)
      if (typeof wallet?.signTransactions === 'function') {
        signedTxnsBase64 = normalizeSignedTransactions(await wallet.signTransactions(transactions, transactions.map((_, index) => index)))
      } else if (typeof wallet?.signTransaction === 'function') {
        signedTxnsBase64 = normalizeSignedTransactions(await wallet.signTransaction([
          transactions.map(transaction => ({ txn: transaction, signers: [session.walletAddress] })),
        ]))
      } else if (built.signingIntent) {
        const signed = await request('/v1/auth/sign', {
          method: 'POST',
          body: { unsignedTxnsBase64: built.unsignedTxnsBase64, signingIntent: built.signingIntent, gameId },
        })
        signedTxnsBase64 = Array.isArray(signed?.signedTxnsBase64)
          ? signed.signedTxnsBase64
          : signed?.signedTxnBase64 ? [signed.signedTxnBase64] : []
      }
    }
    if (signedTxnsBase64.length !== transactions.length)
      throw new BlockmakerError('Your wallet did not sign every transaction in this marketplace action.', { code: 'WALLET_SIGNATURE_MISSING' })
    const submitted = await request('/v1/game-marketplace/submit', {
      method: 'POST',
      body: { signedTxnsBase64, signingIntent: built.signingIntent },
    })
    try {
      const confirmed = await request(`/v1/game-marketplace/confirm/${encodeURIComponent(submitted.txId)}`)
      return { ...submitted, ...confirmed, submitted: true, confirmed: true, action: built.action, assetId: built.assetId }
    } catch (confirmationError) {
      return { ...submitted, submitted: true, confirmed: false, action: built.action, assetId: built.assetId, confirmationError }
    }
  }

  const magicMarketplaceSigner = magic => {
    if (typeof magic?.algorand?.signGroupTransactionV2 !== 'function')
      throw new Error('Magic marketplace signing requires @magic-ext/algorand with signGroupTransactionV2().')
    return async transactions => {
      const requestGroup = transactions.map(transaction => {
        if (typeof transaction?.toByte !== 'function')
          throw new BlockmakerError('Magic could not encode the reviewed Algorand transaction.', { code: 'TX_INVALID' })
        return { txn: base64FromBytes(transaction.toByte()) }
      })
      const values = await magic.algorand.signGroupTransactionV2(requestGroup)
      if (!Array.isArray(values)) return []
      return values.map(value => value?.blob ?? value)
    }
  }

  const xchainMarketplaceSigner = ({ sdk, evmAddress, signMessage } = {}) => {
    const normalizedEvm = String(evmAddress ?? '').trim().toLowerCase()
    if (typeof sdk?.getAddress !== 'function' || typeof sdk?.signTxn !== 'function'
      || !isEvmAddressShape(normalizedEvm) || typeof signMessage !== 'function')
      throw new Error('xChain marketplace signing requires AlgoXEvmSdk, the connected EVM address, and a signMessage callback.')
    return async transactions => {
      const derived = String(await sdk.getAddress({ evmAddress: normalizedEvm })).trim().toUpperCase()
      if (!session?.walletAddress || derived !== session.walletAddress.toUpperCase())
        throw new BlockmakerError('The connected EVM wallet does not match this Blockmaker player session. Sign in again before trading.', { code: 'AUTH_MISMATCH' })
      return sdk.signTxn({ evmAddress: normalizedEvm, txns: transactions, signMessage })
    }
  }

  const magicXchainMarketplaceSigner = ({ magic, sdk } = {}) => {
    const provider = magic?.rpcProvider
    if (typeof provider?.request !== 'function' || typeof sdk?.getAddress !== 'function' || typeof sdk?.signTxn !== 'function')
      throw new Error('Magic xChain marketplace signing requires a configured Magic instance and AlgoXEvmSdk.')
    return async transactions => {
      let accounts
      try { accounts = await provider.request({ method: 'eth_accounts' }) }
      catch (error) { throw evmProviderFailure(error, 'Magic email', 'connect') }
      let evmAddress = Array.isArray(accounts)
        ? accounts.map(value => String(value ?? '').trim().toLowerCase()).find(isEvmAddressShape)
        : ''
      if (!evmAddress) {
        try { accounts = await provider.request({ method: 'eth_requestAccounts' }) }
        catch (error) { throw evmProviderFailure(error, 'Magic email', 'connect') }
        evmAddress = Array.isArray(accounts)
          ? accounts.map(value => String(value ?? '').trim().toLowerCase()).find(isEvmAddressShape)
          : ''
      }
      if (!evmAddress)
        throw new BlockmakerError('Magic email did not return its EVM wallet. Sign in again and retry.', { code: 'WALLET_ACCOUNT_MISSING' })
      return xchainMarketplaceSigner({
        sdk,
        evmAddress,
        signMessage: async typedData => {
          let signature
          try {
            signature = await provider.request({
              method: 'eth_signTypedData_v4',
              params: [evmAddress, JSON.stringify(typedData)],
            })
          } catch (error) { throw evmProviderFailure(error, 'Magic email', 'sign') }
          const normalized = String(signature ?? '').trim()
          if (!/^0x[0-9a-fA-F]{130}$/.test(normalized))
            throw new BlockmakerError('Magic email did not return a valid xChain signature.', { code: 'WALLET_SIGNATURE_MISSING' })
          return normalized
        },
      })(transactions)
    }
  }

  const runMarketplaceAction = async (action, input, signingOptions) => {
    const built = await marketplaceBuild(action, input)
    return signAndSubmitMarketplace(built, signingOptions)
  }

  const openMarketplace = (openOptions = {}) => {
    const doc = globalThis.document
    if (!doc?.body?.appendChild)
      throw new BlockmakerError('marketplace.open() requires a browser.', { code: 'BROWSER_REQUIRED' })
    if (marketplaceDialogOpen)
      throw new BlockmakerError('The marketplace is already open.', { code: 'MARKETPLACE_DIALOG_ACTIVE' })
    marketplaceDialogOpen = true
    let dialog
    try {
      dialog = createUiDialog({
        kind: 'marketplace',
        title: openOptions.title ?? 'NFT marketplace',
        description: 'Browse, buy and sell game NFTs with your own wallet.',
        appearance: openOptions.appearance,
        onClose: reason => {
          marketplaceDialogOpen = false
          try { openOptions.onClose?.(reason) } catch { /* consumer callback */ }
        },
      })
    } catch (error) {
      marketplaceDialogOpen = false
      throw error
    }
    const { content, doc: modalDoc, close, setStatus, setBusy, setDescription, focusFirst } = dialog
    let currentTab = 'browse'
    let latestConfig = null
    let latestListings = []
    let latestInventory = []
    let query = ''
    let collection = ''
    let sort = 'newest'
    let loadSequence = 0

    const signingOptions = {
      algosdk: openOptions.algosdk,
      wallet: openOptions.wallet,
      signTransactions: openOptions.signTransactions,
    }
    const money = microalgo => `${(Number(microalgo) / 1_000_000).toLocaleString(undefined, { maximumFractionDigits: 6 })} ALGO`
    const shortWallet = wallet => wallet ? `${wallet.slice(0, 6)}…${wallet.slice(-4)}` : ''

    const artFor = asset => {
      const art = modalDoc.createElement('div')
      art.className = 'bm-market-art'
      const src = safeImageUrl(asset?.imageUrl)
      if (src) {
        const image = modalDoc.createElement('img')
        image.src = src
        image.alt = String(asset?.name ?? 'NFT')
        image.loading = 'lazy'
        image.referrerPolicy = 'no-referrer'
        image.addEventListener('error', () => { image.remove(); art.textContent = `NFT #${asset?.assetId ?? ''}` })
        art.append(image)
      } else art.textContent = `NFT #${asset?.assetId ?? ''}`
      return art
    }

    const facts = rows => {
      const list = modalDoc.createElement('div')
      list.className = 'bm-market-facts'
      for (const [label, value] of rows) {
        const row = modalDoc.createElement('div')
        row.className = 'bm-market-fact'
        const left = modalDoc.createElement('span')
        left.textContent = label
        const right = modalDoc.createElement('strong')
        right.textContent = value
        row.append(left, right)
        list.append(row)
      }
      return list
    }

    const empty = (title, copy) => {
      const node = modalDoc.createElement('div')
      node.className = 'bm-market-empty'
      const strong = modalDoc.createElement('strong')
      strong.textContent = title
      strong.style.cssText = 'display:block;margin-bottom:5px;color:var(--bm-text);font-size:15px;'
      const text = modalDoc.createElement('span')
      text.textContent = copy
      node.append(strong, text)
      return node
    }

    const requireSignIn = () => {
      if (session?.sessionToken) return true
      if (typeof openOptions.onRequireSignIn === 'function') {
        close('sign-in-required')
        try { openOptions.onRequireSignIn() } catch { /* consumer callback */ }
      } else setStatus('Sign in to this game before buying or selling an NFT.', 'error')
      return false
    }

    const showResult = (result, verb) => {
      const panel = modalDoc.createElement('div')
      panel.className = 'bm-market-empty'
      const title = modalDoc.createElement('strong')
      title.style.cssText = 'display:block;margin-bottom:7px;color:var(--bm-text);font-size:18px;'
      title.textContent = result.confirmed ? `${verb} confirmed` : `${verb} submitted`
      const copy = modalDoc.createElement('p')
      copy.style.cssText = 'margin:0;color:var(--bm-muted);'
      copy.textContent = result.confirmed
        ? 'Algorand confirmed the transaction. The marketplace will refresh now.'
        : 'The transaction reached Algorand but confirmation is taking longer than usual. It is safe to close this window and check again shortly.'
      const tx = modalDoc.createElement('code')
      tx.className = 'bm-ui-address'
      tx.textContent = result.txId
      const done = uiButton(modalDoc, 'Back to marketplace', 'primary')
      done.style.marginTop = '16px'
      done.addEventListener('click', () => { void load(currentTab) })
      panel.append(title, copy, tx, done)
      content.replaceChildren(panel)
      setStatus('', 'info')
      try { openOptions.onActionComplete?.(result) } catch { /* consumer callback */ }
    }

    const runPreparedAction = async (built, verb) => {
      setBusy(true, `Opening your wallet for ${verb.toLowerCase()}…`)
      try {
        const result = await signAndSubmitMarketplace(built, signingOptions)
        showResult(result, verb)
      } catch (error) {
        setBusy(false)
        setStatus(playerFacingError(error, `The NFT could not be ${verb.toLowerCase()}. Please try again.`), 'error')
        try { openOptions.onError?.(error) } catch { /* consumer callback */ }
      }
    }

    const confirmation = (asset, action, priceMicroalgo, built, networkFeeMicroalgo) => {
      const wrap = modalDoc.createElement('div')
      wrap.className = 'bm-market-confirm'
      wrap.append(artFor(asset))
      const details = modalDoc.createElement('div')
      const name = modalDoc.createElement('h3')
      name.textContent = asset.name || `NFT #${asset.assetId}`
      name.style.cssText = 'margin:0;color:var(--bm-text);font-size:22px;line-height:1.15;'
      const description = modalDoc.createElement('p')
      description.style.cssText = 'margin:7px 0 0;color:var(--bm-muted);font-size:13px;line-height:1.5;'
      description.textContent = action === 'buy'
        ? 'The price and NFT move together in one atomic Algorand transaction.'
        : action === 'list'
          ? 'The game-owned contract holds this NFT only while it is listed. Your refundable escrow deposit comes back when it sells or you cancel.'
          : 'Cancelling returns the NFT and refundable escrow deposit to this wallet.'
      const rows = action === 'buy'
        ? [
            ['NFT price', money(priceMicroalgo)],
            ['Marketplace fee', 'Included in the price'],
            ['Algorand network fee', money(networkFeeMicroalgo)],
            ['Total from this wallet', money(priceMicroalgo + networkFeeMicroalgo)],
            ...(built?.optInIncluded ? [['NFT opt-in', 'Included in this transaction']] : []),
            ['NFT', `Asset ${asset.assetId}`],
          ]
        : action === 'list'
          ? [
              ['List for', money(priceMicroalgo)],
              ['Refundable escrow deposit', money(134_900)],
              ['Algorand network fee', money(networkFeeMicroalgo)],
              ['Due from this wallet now', money(134_900 + networkFeeMicroalgo)],
              ['Marketplace fee after sale', `${latestConfig?.feePercent ?? 0}%`],
            ]
          : [
              ['Returns to', shortWallet(session?.walletAddress)],
              ['NFT', `Asset ${asset.assetId}`],
              ['Algorand network fee', money(networkFeeMicroalgo)],
              ['Sale price charged', 'None'],
            ]
      const buttons = modalDoc.createElement('div')
      buttons.className = 'bm-ui-row'
      const back = uiButton(modalDoc, 'Back', 'quiet')
      back.addEventListener('click', () => { void load(currentTab) })
      const approve = uiButton(modalDoc, action === 'buy' ? `Buy for ${money(priceMicroalgo)}` : action === 'list' ? 'Approve listing' : 'Cancel listing', action === 'cancel' ? 'danger' : 'primary')
      approve.addEventListener('click', () => { void runPreparedAction(built, action === 'buy' ? 'Purchase' : action === 'list' ? 'Listing' : 'Cancellation') })
      buttons.append(back, approve)
      details.append(name, description, facts(rows), buttons)
      wrap.append(details)
      content.replaceChildren(wrap)
      setDescription(action === 'buy' ? 'Review the exact purchase before your wallet opens.' : action === 'list' ? 'Review the price and refundable deposit before signing.' : 'Review what will return to your wallet.')
      setStatus('', 'info')
      focusFirst()
    }

    const prepareReview = async (asset, action, priceMicroalgo = 0) => {
      setBusy(true, 'Preparing the exact wallet review…')
      try {
        const built = await marketplaceBuild(action, { assetId: asset.assetId, priceMicroalgo })
        const signerSdk = signingOptions.algosdk ?? rememberedAlgorandSigner?.algosdk ?? options.algosdk
        const transactions = validateMarketplaceTransactions(built, signerSdk)
        const networkFeeMicroalgo = transactions.reduce((sum, transaction) => sum + Number(transaction?.fee ?? 0), 0)
        if (!Number.isSafeInteger(networkFeeMicroalgo) || networkFeeMicroalgo < 1 || networkFeeMicroalgo > 20_000)
          throw new BlockmakerError('Blockmaker returned an unexpected marketplace network fee. Nothing was signed.', { code: 'TX_UNSAFE' })
        setBusy(false)
        confirmation(asset, action, Number(built.priceMicroalgo ?? priceMicroalgo), built, networkFeeMicroalgo)
      } catch (error) {
        setBusy(false)
        setStatus(playerFacingError(error, 'The marketplace review could not be prepared. Please try again.'), 'error')
        try { openOptions.onError?.(error) } catch { /* consumer callback */ }
      }
    }

    const parseAlgo = value => {
      const text = String(value ?? '').trim()
      if (!/^(?:0|[1-9]\d*)(?:\.\d{1,6})?$/.test(text)) return null
      const [whole, fraction = ''] = text.split('.')
      const micro = Number(whole) * 1_000_000 + Number(fraction.padEnd(6, '0'))
      return Number.isSafeInteger(micro) && micro > 0 ? micro : null
    }

    const showListForm = asset => {
      const wrap = modalDoc.createElement('div')
      wrap.className = 'bm-market-confirm'
      wrap.append(artFor(asset))
      const details = modalDoc.createElement('div')
      const name = modalDoc.createElement('h3')
      name.textContent = asset.name || `NFT #${asset.assetId}`
      name.style.cssText = 'margin:0 0 14px;color:var(--bm-text);font-size:22px;'
      const label = modalDoc.createElement('label')
      label.className = 'bm-ui-field'
      label.textContent = 'Listing price in ALGO'
      const input = modalDoc.createElement('input')
      input.className = 'bm-ui-input'
      input.inputMode = 'decimal'
      input.placeholder = '10'
      input.autocomplete = 'off'
      const help = modalDoc.createElement('p')
      help.className = 'bm-ui-help'
      help.textContent = `Choose up to ${latestConfig?.maxPriceAlgo ?? 100} ALGO. Listing also places a refundable 0.1349 ALGO escrow deposit.`
      const buttons = modalDoc.createElement('div')
      buttons.className = 'bm-ui-row'
      buttons.style.marginTop = '16px'
      const back = uiButton(modalDoc, 'Back', 'quiet')
      back.addEventListener('click', () => { void load('sell') })
      const review = uiButton(modalDoc, 'Review listing', 'primary')
      review.addEventListener('click', () => {
        const micro = parseAlgo(input.value)
        if (!micro || micro > Number(latestConfig?.maxPriceMicroalgo ?? 100_000_000)) {
          setStatus(`Enter a price from 0.000001 to ${latestConfig?.maxPriceAlgo ?? 100} ALGO.`, 'error')
          input.focus()
          return
        }
        void prepareReview(asset, 'list', micro)
      })
      buttons.append(back, review)
      details.append(name, label, input, help, buttons)
      wrap.append(details)
      content.replaceChildren(wrap)
      setDescription('Choose a clear ALGO price for this NFT.')
      setStatus('', 'info')
      input.focus?.({ preventScroll: true })
    }

    const cardFor = (asset, mode) => {
      const card = modalDoc.createElement('article')
      card.className = 'bm-market-item'
      card.append(artFor(asset))
      const info = modalDoc.createElement('div')
      info.className = 'bm-market-info'
      const name = modalDoc.createElement('h3')
      name.className = 'bm-market-name'
      name.textContent = asset.name || `NFT #${asset.assetId}`
      name.title = name.textContent
      const meta = modalDoc.createElement('div')
      meta.className = 'bm-market-meta'
      const collectionName = modalDoc.createElement('span')
      collectionName.textContent = asset.collectionName || `Asset ${asset.assetId}`
      const unit = modalDoc.createElement('span')
      unit.textContent = asset.unitName || `#${asset.assetId}`
      meta.append(collectionName, unit)
      const action = uiButton(modalDoc, '', mode === 'cancel' ? 'danger' : 'primary', true)
      if (mode === 'buy') {
        const price = modalDoc.createElement('div')
        price.className = 'bm-market-price'
        price.textContent = money(asset.priceMicroalgo)
        const tradingOpen = latestConfig?.status === 'active'
        action.textContent = tradingOpen ? 'Review purchase' : latestConfig?.status === 'paused' ? 'Marketplace paused' : 'Trading unavailable'
        action.disabled = !tradingOpen
        action.addEventListener('click', () => { if (requireSignIn()) void prepareReview(asset, 'buy', asset.priceMicroalgo) })
        info.append(name, meta, price, action)
      } else if (mode === 'list') {
        const tradingOpen = latestConfig?.status === 'active'
        action.textContent = asset.listed ? 'Already listed' : tradingOpen ? 'List this NFT' : latestConfig?.status === 'paused' ? 'Marketplace paused' : 'Trading unavailable'
        action.disabled = !!asset.listed || !tradingOpen
        action.addEventListener('click', () => showListForm(asset))
        info.append(name, meta, action)
      } else {
        const price = modalDoc.createElement('div')
        price.className = 'bm-market-price'
        price.textContent = money(asset.priceMicroalgo)
        action.textContent = 'Cancel listing'
        action.addEventListener('click', () => { void prepareReview(asset, 'cancel', 0) })
        info.append(name, meta, price, action)
      }
      card.append(info)
      return card
    }

    const tabs = () => {
      const nav = modalDoc.createElement('div')
      nav.className = 'bm-market-tabs'
      nav.setAttribute('role', 'tablist')
      for (const [id, label] of [['browse', 'Browse'], ['sell', 'Sell'], ['mine', 'My listings']]) {
        const button = uiButton(modalDoc, label, 'quiet')
        button.setAttribute('role', 'tab')
        button.setAttribute('aria-selected', currentTab === id ? 'true' : 'false')
        button.addEventListener('click', () => { if (id === 'browse' || requireSignIn()) void load(id) })
        nav.append(button)
      }
      return nav
    }

    const renderBrowse = () => {
      const nodes = [tabs()]
      const toolbar = modalDoc.createElement('div')
      toolbar.className = 'bm-market-toolbar'
      const search = modalDoc.createElement('input')
      search.className = 'bm-ui-input'
      search.type = 'search'
      search.value = query
      search.placeholder = 'Search NFTs or asset ID'
      search.setAttribute('aria-label', 'Search marketplace')
      const collectionSelect = modalDoc.createElement('select')
      collectionSelect.className = 'bm-market-select'
      collectionSelect.setAttribute('aria-label', 'Filter by collection')
      const all = modalDoc.createElement('option')
      all.value = ''
      all.textContent = 'All collections'
      collectionSelect.append(all)
      for (const item of latestConfig?.collections ?? []) {
        const option = modalDoc.createElement('option')
        option.value = item.id
        option.textContent = item.name
        collectionSelect.append(option)
      }
      collectionSelect.value = collection
      const sortSelect = modalDoc.createElement('select')
      sortSelect.className = 'bm-market-select'
      sortSelect.setAttribute('aria-label', 'Sort marketplace')
      for (const [value, label] of [['newest', 'Newest'], ['price_asc', 'Price: low to high'], ['price_desc', 'Price: high to low']]) {
        const option = modalDoc.createElement('option')
        option.value = value
        option.textContent = label
        sortSelect.append(option)
      }
      sortSelect.value = sort
      let searchTimer
      search.addEventListener('input', () => {
        query = search.value
        clearTimeout(searchTimer)
        searchTimer = setTimeout(() => { void load('browse') }, 250)
      })
      collectionSelect.addEventListener('change', () => { collection = collectionSelect.value; void load('browse') })
      sortSelect.addEventListener('change', () => { sort = sortSelect.value; void load('browse') })
      toolbar.append(search, collectionSelect, sortSelect)
      nodes.push(toolbar)
      if (!latestListings.length) nodes.push(empty('No listings yet', 'This marketplace is ready. The first player listing will appear here.'))
      else {
        const grid = modalDoc.createElement('div')
        grid.className = 'bm-market-grid'
        for (const listing of latestListings) grid.append(cardFor(listing, 'buy'))
        nodes.push(grid)
      }
      content.replaceChildren(...nodes)
      focusFirst()
    }

    const renderSell = () => {
      const nodes = [tabs()]
      if (latestConfig?.status !== 'active') nodes.push(empty(
        latestConfig?.status === 'paused' ? 'Marketplace paused' : 'Trading unavailable',
        'New listings and purchases are unavailable. Existing sellers can still use My listings to cancel and recover their NFTs.',
      ))
      else if (!latestInventory.length) nodes.push(empty('No eligible NFTs found', 'Only one-of-one NFTs from the game’s approved collections appear here.'))
      else {
        const grid = modalDoc.createElement('div')
        grid.className = 'bm-market-grid'
        for (const asset of latestInventory) grid.append(cardFor(asset, 'list'))
        nodes.push(grid)
      }
      content.replaceChildren(...nodes)
      focusFirst()
    }

    const renderMine = () => {
      const mine = latestListings.filter(item => item.seller === session?.walletAddress)
      const nodes = [tabs()]
      if (!mine.length) nodes.push(empty('No active listings', 'NFTs you list from this wallet will appear here, with a clear cancel option.'))
      else {
        const grid = modalDoc.createElement('div')
        grid.className = 'bm-market-grid'
        for (const listing of mine) grid.append(cardFor(listing, 'cancel'))
        nodes.push(grid)
      }
      content.replaceChildren(...nodes)
      focusFirst()
    }

    const load = async (tab = currentTab) => {
      const sequence = ++loadSequence
      currentTab = tab
      setBusy(true, tab === 'sell' ? 'Checking your NFTs…' : 'Loading marketplace…')
      try {
        const filters = { q: query, collection, sort }
        const [listingResult, config, inventoryResult] = await Promise.all([
          marketplaceListings(filters),
          marketplaceConfig(),
          tab === 'sell' && session?.sessionToken ? request('/v1/game-marketplace/inventory') : Promise.resolve(null),
        ])
        if (sequence !== loadSequence || dialog.isClosed()) return null
        latestConfig = config
        latestListings = listingResult.listings ?? []
        latestInventory = inventoryResult?.assets ?? latestInventory
        setDescription(`${config.displayName ?? 'NFT marketplace'} · Prices in ALGO · Player-wallet signing`)
        setBusy(false)
        setStatus(
          config.status === 'paused'
            ? 'The game owner paused new listings and purchases. Sellers can still cancel.'
            : config.status !== 'active'
              ? 'A marketplace safety check needs attention. New trades are unavailable, but sellers can still cancel.'
              : '',
          config.status === 'active' ? 'info' : 'error',
        )
        if (tab === 'sell') renderSell()
        else if (tab === 'mine') renderMine()
        else renderBrowse()
        return listingResult
      } catch (error) {
        if (sequence !== loadSequence || dialog.isClosed()) return null
        setBusy(false)
        content.replaceChildren(empty('Marketplace unavailable', playerFacingError(error, 'The marketplace could not load. Please try again.')))
        setStatus('', 'info')
        try { openOptions.onError?.(error) } catch { /* consumer callback */ }
        return null
      }
    }

    const ready = load('browse')
    return Object.freeze({
      close: () => close('closed'),
      refresh: () => load(currentTab),
      ready,
    })
  }

  let unityWebGlWalletPackageController = null
  const installUnityWebGlWalletPackageController = controllerOptions => {
    if (clientKind !== 'unity_webgl')
      throw new Error('unityWebGl.walletPackage() requires a client created with clientKind: unity_webgl.')
    walletPackageExactKeys(controllerOptions, [
      'network', 'genesisId', 'genesisHashBase64', 'hostRuntimeUrl',
      'loadAlgorandWalletSetup', 'restoredSession', 'runtimeProfile',
    ], 'The Unity WebGL wallet-package options')
    if (controllerOptions.network !== PLAYER_WALLET_AUTH_V3.network
      || controllerOptions.genesisId !== PLAYER_WALLET_AUTH_V3.genesisId
      || controllerOptions.genesisHashBase64 !== PLAYER_WALLET_AUTH_V3.genesisHash
      || typeof controllerOptions.hostRuntimeUrl !== 'string'
      || !UNITY_WEBGL_RUNTIME_PROFILE_PROVIDER_IDS[controllerOptions.runtimeProfile]
      || typeof controllerOptions.loadAlgorandWalletSetup !== 'function') {
      throw new Error('The Unity WebGL wallet package requires the exact Algorand MainNet contract.')
    }
    if (unityWebGlWalletPackageController)
      throw new BlockmakerError('The Unity WebGL wallet-package controller is already installed.', {
        code: 'REQUEST_ALREADY_PENDING',
      })

    let openSequence = 0
    let activeOpen = null
    let pendingHandoff = null
    let cleanupOperationsInFlight = 0
    let fundingHandle = null
    let evidenceState = null
    let pendingHandoffPageHide = null
    let acknowledgedUnitySigner = null
    let preparedTransactionGroup = null

    const normalizedUnityWalletSetup = value => {
      const expected = UNITY_WEBGL_RUNTIME_PROFILE_PROVIDER_IDS[controllerOptions.runtimeProfile]
      if (!value || typeof value !== 'object' || !Array.isArray(value.algorandWallets)
        || value.algorandWallets.length !== expected.length
        || typeof value.algosdk?.decodeUnsignedTransaction !== 'function'
        || typeof value.algosdk?.encodeUnsignedTransaction !== 'function'
        || typeof value.algosdk?.decodeSignedTransaction !== 'function'
        || typeof value.algosdk?.computeGroupID !== 'function'
        || typeof value.algosdk?.encodeAddress !== 'function'
        || typeof value.disconnectAll !== 'function') {
        throw new Error('The Unity WebGL host did not supply the exact reviewed TxnLab wallet setup.')
      }
      const byProvider = new Map()
      for (const entry of value.algorandWallets) {
        const providerId = String(entry?.providerId ?? '').trim().toLowerCase()
        const wallet = entry?.wallet
        if (!expected.includes(providerId) || byProvider.has(providerId)
          || wallet?.providerId !== providerId
          || typeof wallet?.connect !== 'function'
          || typeof wallet?.resumeSession !== 'function'
          || typeof wallet?.disconnect !== 'function'
          || typeof wallet?.signTransactions !== 'function'
          || wallet?.networkGenesisId !== PLAYER_WALLET_AUTH_V3.genesisId) {
          throw new Error('The Unity WebGL host supplied an invalid TxnLab wallet facade.')
        }
        byProvider.set(providerId, Object.freeze({ ...entry, providerId, wallet }))
      }
      if (expected.some(providerId => !byProvider.has(providerId)))
        throw new Error('The Unity WebGL host did not supply every provider in the selected runtime profile exactly once.')
      return Object.freeze({
        algorandWallets: Object.freeze(expected.map(providerId => byProvider.get(providerId))),
        algosdk: value.algosdk,
        disconnectAll: value.disconnectAll,
        byProvider,
      })
    }

    const walletSetup = normalizedUnityWalletSetup(controllerOptions.loadAlgorandWalletSetup())

    const beginUnityWalletCleanup = () => { cleanupOperationsInFlight += 1 }
    const confirmUnityWalletCleanup = () => {
      cleanupOperationsInFlight = Math.max(0, cleanupOperationsInFlight - 1)
    }
    const requireUnityWalletCleanupIdle = () => {
      if (unityWalletPackageCleanupLatched())
        throw new BlockmakerError('A prior wallet-package cleanup could not be confirmed. Reload is required.', {
          code: 'PROVIDER_CLEANUP_REQUIRED',
        })
      if (cleanupOperationsInFlight > 0)
        throw new BlockmakerError('A Unity WebGL wallet cleanup is still being confirmed.', {
          code: 'REQUEST_ALREADY_PENDING',
        })
    }

    const packageSession = value => {
      walletPackageExactKeys(value, [
        'sessionToken', 'refreshToken', 'walletAddress', 'accountKind', 'authProvider',
      ], 'The Unity WebGL wallet-package session')
      const accountKind = String(value.accountKind ?? '')
      const authProvider = String(value.authProvider ?? '')
      const supported = accountKind === 'algorand_wallet'
        && walletSetup.byProvider.has(authProvider)
      if (!supported
        || typeof value.sessionToken !== 'string' || value.sessionToken.length < 1
        || value.sessionToken.length > 8_192 || !/^[\x21-\x7e]+$/.test(value.sessionToken)
        || typeof value.refreshToken !== 'string' || value.refreshToken.length < 1
        || value.refreshToken.length > 1_024 || !/^[\x21-\x7e]+$/.test(value.refreshToken)
        || /^sk_/i.test(value.sessionToken) || /^sk_/i.test(value.refreshToken)
        || !isAlgorandAddressShape(value.walletAddress)) {
        throw new BlockmakerError('The Unity WebGL wallet package received an invalid player session.', {
          code: 'AUTH_INVALID',
        })
      }
      return Object.freeze({
        sessionToken: value.sessionToken,
        refreshToken: value.refreshToken,
        walletAddress: String(value.walletAddress),
        accountKind,
        authProvider,
      })
    }

    const packagePublicSession = value => {
      if (value === null) return null
      walletPackageExactKeys(value, ['walletAddress', 'accountKind', 'authProvider'],
        'The restored Unity WebGL wallet-package session')
      const walletAddress = String(value.walletAddress ?? '').trim().toUpperCase()
      const accountKind = String(value.accountKind ?? '')
      const authProvider = String(value.authProvider ?? '')
      if (!isAlgorandAddressShape(walletAddress) || accountKind !== 'algorand_wallet'
        || !walletSetup.byProvider.has(authProvider)) {
        throw new BlockmakerError('The restored Unity WebGL wallet session is invalid.', {
          code: 'SESSION_CONFLICT',
        })
      }
      return Object.freeze({ walletAddress, accountKind, authProvider })
    }

    const publicWalletAccounts = wallet => Object.freeze([...new Set(
      (Array.isArray(wallet?.accounts) ? wallet.accounts : [])
        .map(value => String(typeof value === 'string' ? value : value?.address ?? '').trim().toUpperCase())
        .filter(isAlgorandAddressShape),
    )])

    const restoredSession = packagePublicSession(controllerOptions.restoredSession)
    if (restoredSession) {
      const entry = walletSetup.byProvider.get(restoredSession.authProvider)
      const accounts = publicWalletAccounts(entry.wallet)
      if (accounts.includes(restoredSession.walletAddress)) {
        acknowledgedUnitySigner = Object.freeze({
          wallet: entry.wallet,
          algosdk: walletSetup.algosdk,
          walletAddress: restoredSession.walletAddress,
          providerId: restoredSession.authProvider,
          accounts,
        })
      }
    }

    const operationAcceptedSession = operation => {
      return operation?.acceptedSession ?? null
    }

    const removeOpenPageHide = operation => {
      if (!operation?.onPageHide) return
      try { globalThis.removeEventListener?.('pagehide', operation.onPageHide) } catch { /* non-browser host */ }
      operation.onPageHide = null
    }

    const clearPendingHandoffPageHide = () => {
      if (!pendingHandoffPageHide) return
      try { globalThis.removeEventListener?.('pagehide', pendingHandoffPageHide) } catch { /* non-browser host */ }
      pendingHandoffPageHide = null
    }

    const armPendingHandoffPageHide = handoff => {
      clearPendingHandoffPageHide()
      pendingHandoffPageHide = () => {
        if (pendingHandoff !== handoff) return
        pendingHandoff = null
        clearPendingHandoffPageHide()
        if (session?.refreshToken === handoff.refreshToken) storeSession(null)
        if (rememberedAlgorandSigner?.walletAddress === handoff.walletAddress)
          rememberedAlgorandSigner = null
        // A navigation cannot synchronously prove server revocation. Keep the
        // request alive, but permanently fail this page lifecycle closed.
        latchUnityWalletPackageCleanup()
        if (handoff.authProvider === 'web3auth_avm_email') latchWeb3AuthAvmCleanup()
        beginUnityWalletCleanup()
        void rawRequest('/v1/auth/logout', {
          method: 'POST', auth: false, keepalive: true,
          body: { refreshToken: handoff.refreshToken, gameId },
        }).catch(() => { /* the page-lifetime latch already records uncertainty */ })
      }
      try { globalThis.addEventListener?.('pagehide', pendingHandoffPageHide, { once: true }) }
      catch { /* non-browser host */ }
    }

    const revokeOperationSession = (operation, target, rejection, keepalive = true) => {
      if (!target || operation.cleanupRefreshToken === target.refreshToken) return false
      operation.cleanupRefreshToken = target.refreshToken
      beginUnityWalletCleanup()
      void rawRequest('/v1/auth/logout', {
        method: 'POST', auth: false, keepalive,
        body: { refreshToken: target.refreshToken, gameId },
      })
        .then(() => {
          if (session?.refreshToken === target.refreshToken) storeSession(null)
          if (rememberedAlgorandSigner?.walletAddress === target.walletAddress)
            rememberedAlgorandSigner = null
          confirmUnityWalletCleanup()
          operation.reject(rejection)
        })
        .catch(error => {
          if (session?.refreshToken === target.refreshToken) storeSession(null)
          latchUnityWalletPackageCleanup()
          if (target.authProvider === 'web3auth_avm_email') latchWeb3AuthAvmCleanup()
          operation.reject(new BlockmakerError(
            'The accepted wallet session could not be safely closed. Reload is required.',
            { code: 'PROVIDER_CLEANUP_REQUIRED', details: error },
          ))
        })
      return true
    }

    const terminateOpenBeforeHandoff = (operation, rejection, closeHandle = false, keepalive = false) => {
      if (!operation || operation.terminal) return
      operation.terminal = true
      if (activeOpen === operation) activeOpen = null
      openSequence += 1
      invalidateLoginAttempts()
      removeOpenPageHide(operation)
      if (closeHandle) {
        try { operation.handle?.close?.() } catch { /* the exact accepted family is still handled below */ }
      }
      if (revokeOperationSession(operation, operationAcceptedSession(operation), rejection, keepalive)) return
      if (operation.loginInFlight) {
        operation.cancelRejection = rejection
        if (!operation.loginCleanupReserved) {
          operation.loginCleanupReserved = true
          beginUnityWalletCleanup()
        }
        return
      }
      operation.reject(rejection)
    }

    const closeActiveAccount = () => {
      const operation = activeOpen
      if (!operation) return
      terminateOpenBeforeHandoff(operation, new BlockmakerError('The player cancelled wallet sign-in.', {
        code: 'PLAYER_CANCELLED',
      }), true)
    }

    const openAccountForUnity = (openOptions = {}) => {
      const presented = openOptions.presentation === 'unity'
      if (presented) {
        walletPackageExactKeys(openOptions, ['presentation', 'onProgress', ...('providerId' in openOptions ? ['providerId'] : [])], 'The Unity presentation request')
        const selected = openOptions.providerId ?? 'pera'
        if (!['pera', 'txnlab_web3auth'].includes(selected) || !walletSetup.byProvider.has(selected))
          throw new BlockmakerError('That wallet is not enabled for Unity presentation.', { code: 'PROVIDER_NOT_ENABLED' })
        if (typeof openOptions.onProgress !== 'function')
          throw new BlockmakerError('Unity presentation requires a progress receiver.', { code: 'PRESENTATION_UNAVAILABLE' })
      } else walletPackageExactKeys(openOptions, [], 'The Unity account request')
      requireUnityWalletCleanupIdle()
      if (activeOpen || pendingHandoff)
        throw new BlockmakerError('A Unity WebGL account request is already pending.', {
          code: 'REQUEST_ALREADY_PENDING',
        })
      const setup = walletSetup
      const sequence = ++openSequence
      let resolveOperation
      let rejectOperation
      const promise = new Promise((resolve, reject) => {
        resolveOperation = resolve
        rejectOperation = reject
      })
      const operation = {
        sequence,
        handle: null,
        resolve: resolveOperation,
        reject: rejectOperation,
        acceptedSession: null,
        cleanupRefreshToken: '',
        cancelRejection: null,
        loginCleanupReserved: false,
        loginInFlight: false,
        pageHideStarted: false,
        terminal: false,
        onPageHide: null,
      }
      const loginTracker = Object.freeze({
        get keepalive() { return operation.pageHideStarted === true },
        start: () => {
          if (operation.terminal)
            throw new BlockmakerError('This Unity WebGL wallet request was already cancelled.', {
              code: 'AUTH_SUPERSEDED',
            })
          operation.loginInFlight = true
        },
        accepted: value => {
          const refreshToken = typeof value?.refreshToken === 'string'
            && value.refreshToken.length >= 1 && value.refreshToken.length <= 1_024
            && /^[\x21-\x7e]+$/.test(value.refreshToken)
            && !/^sk_/i.test(value.refreshToken)
            ? value.refreshToken
            : ''
          if (!refreshToken) return
          operation.acceptedSession = Object.freeze({
            sessionToken: typeof value?.sessionToken === 'string' ? value.sessionToken : '',
            refreshToken,
            walletAddress: isAlgorandAddressShape(value?.walletAddress)
              ? String(value.walletAddress) : '',
            accountKind: String(value?.accountKind ?? ''),
            authProvider: String(value?.authProvider ?? ''),
          })
        },
        settle: failure => {
          operation.loginInFlight = false
          if (!operation.loginCleanupReserved) return
          const rejection = operation.cancelRejection
            ?? new BlockmakerError('The player cancelled wallet sign-in.', { code: 'PLAYER_CANCELLED' })
          if (failure?.code === 'PROVIDER_CLEANUP_REQUIRED') {
            latchUnityWalletPackageCleanup()
            operation.reject(new BlockmakerError(
              'The cancelled wallet session could not be safely closed. Reload is required.',
              { code: 'PROVIDER_CLEANUP_REQUIRED', details: failure },
            ))
            return
          }
          operation.loginCleanupReserved = false
          confirmUnityWalletCleanup()
          operation.reject(rejection)
        },
      })
      operation.onPageHide = () => {
        operation.pageHideStarted = true
        terminateOpenBeforeHandoff(operation, new BlockmakerError(
          'The game page closed before Unity accepted the wallet session.',
          { code: 'PLAYER_CANCELLED' },
        ), false, true)
      }
      try { globalThis.addEventListener?.('pagehide', operation.onPageHide) } catch { /* non-browser host */ }
      activeOpen = operation
      try {
        operation.handle = (presented ? openUnityPeraAccount : openAccount)({
          providerId: openOptions.providerId,
          onProgress: openOptions.onProgress,
          onError: presented ? error => terminateOpenBeforeHandoff(operation, error) : undefined,
          [UNITY_WALLET_PACKAGE_COMPLETE_AFTER_LOGIN]: true,
          [UNITY_WALLET_PACKAGE_LOGIN_TRACKER]: loginTracker,
          algorandWallets: setup.algorandWallets,
          algosdk: setup.algosdk,
          email: setup.byProvider.has('txnlab_web3auth') ? 'txnlab_web3auth' : false,
          featuredWalletIds: ['pera', 'lute'],
          [UNITY_WALLET_PACKAGE_STAGED_LUTE_LOGIN]: true,
          onComplete: result => {
            let handoff
            try {
              handoff = packageSession({
                sessionToken: result?.sessionToken,
                refreshToken: result?.refreshToken,
                walletAddress: result?.walletAddress,
                accountKind: result?.accountKind,
                authProvider: result?.authProvider,
              })
            } catch (error) {
              operation.terminal = true
              removeOpenPageHide(operation)
              beginUnityWalletCleanup()
              if (activeOpen === operation) activeOpen = null
              const refreshToken = typeof result?.refreshToken === 'string'
                && result.refreshToken.length >= 1 && result.refreshToken.length <= 1_024
                && /^[\x21-\x7e]+$/.test(result.refreshToken)
                && !/^sk_/i.test(result.refreshToken)
                ? result.refreshToken
                : ''
              void (async () => {
                let revoked = false
                if (refreshToken) {
                  try {
                    await rawRequest('/v1/auth/logout', {
                      method: 'POST', auth: false, keepalive: true,
                      body: { refreshToken, gameId },
                    })
                    revoked = true
                  } catch { /* fail closed below */ }
                }
                if ((refreshToken && session?.refreshToken === refreshToken)
                  || (typeof result?.sessionToken === 'string'
                    && session?.sessionToken === result.sessionToken)) storeSession(null)
                if (!revoked) {
                  latchUnityWalletPackageCleanup()
                  rejectOperation(new BlockmakerError(
                    'The accepted wallet session could not be safely handed to Unity. Reload is required.',
                    { code: 'PROVIDER_CLEANUP_REQUIRED', details: error },
                  ))
                  return
                }
                confirmUnityWalletCleanup()
                rejectOperation(error)
              })()
              return
            }
            if (cleanupOperationsInFlight > 0 || activeOpen !== operation || sequence !== openSequence) {
              operation.terminal = true
              removeOpenPageHide(operation)
              beginUnityWalletCleanup()
              void rawRequest('/v1/auth/logout', {
                method: 'POST', auth: false, keepalive: true,
                body: { refreshToken: handoff.refreshToken, gameId },
              })
                .then(() => { confirmUnityWalletCleanup() })
                .catch(() => {
                  latchUnityWalletPackageCleanup()
                  if (handoff.authProvider === 'web3auth_avm_email') latchWeb3AuthAvmCleanup()
                })
              rejectOperation(new BlockmakerError('A newer Unity WebGL operation replaced this sign-in.', {
                code: 'AUTH_SUPERSEDED',
              }))
              return
            }
            operation.terminal = true
            activeOpen = null
            pendingHandoff = handoff
            removeOpenPageHide(operation)
            armPendingHandoffPageHide(handoff)
            resolveOperation(handoff)
          },
          onClose: () => {
            terminateOpenBeforeHandoff(operation, new BlockmakerError('The player cancelled wallet sign-in.', {
              code: 'PLAYER_CANCELLED',
            }))
          },
        })
      } catch (error) {
        terminateOpenBeforeHandoff(operation, error)
      }
      return promise
    }

    const acknowledgeUnityHandoff = handoff => {
      requireUnityWalletCleanupIdle()
      if (!pendingHandoff || handoff !== pendingHandoff)
        throw new BlockmakerError('The Unity WebGL session handoff is no longer current.', { code: 'SESSION_CONFLICT' })
      if (session?.refreshToken !== handoff.refreshToken
        || session?.sessionToken !== handoff.sessionToken) {
        throw new BlockmakerError('The Unity WebGL session changed before handoff.', { code: 'SESSION_CONFLICT' })
      }
      const signer = rememberedAlgorandSigner
      if (!signer || signer.walletAddress !== handoff.walletAddress
        || signer.providerId !== handoff.authProvider
        || !walletSetup.byProvider.has(handoff.authProvider)
        || walletSetup.byProvider.get(handoff.authProvider).wallet !== signer.wallet
        || !publicWalletAccounts(signer.wallet).includes(handoff.walletAddress)) {
        throw new BlockmakerError('The Unity WebGL wallet signer changed before handoff.', {
          code: 'SESSION_CONFLICT',
        })
      }
      // Ownership crosses once: Unity receives the refresh family, while the
      // browser SDK discards its copy without revoking the server family.
      clearPendingHandoffPageHide()
      pendingHandoff = null
      invalidateLoginAttempts()
      storeSession(null)
      acknowledgedUnitySigner = Object.freeze({
        wallet: signer.wallet,
        algosdk: signer.algosdk,
        walletAddress: signer.walletAddress,
        providerId: signer.providerId,
        accounts: publicWalletAccounts(signer.wallet),
      })
      rememberedAlgorandSigner = null
    }

    const rejectUnityHandoff = async handoff => {
      requireUnityWalletCleanupIdle()
      if (!pendingHandoff || handoff !== pendingHandoff)
        throw new BlockmakerError('The Unity WebGL session handoff is no longer current.', { code: 'SESSION_CONFLICT' })
      beginUnityWalletCleanup()
      try {
        await rawRequest('/v1/auth/logout', {
          method: 'POST', auth: false, keepalive: true,
          body: { refreshToken: handoff.refreshToken, gameId },
        })
      } catch (error) {
        if (session?.refreshToken === handoff.refreshToken) storeSession(null)
        if (pendingHandoff?.refreshToken === handoff.refreshToken) pendingHandoff = null
        clearPendingHandoffPageHide()
        if (rememberedAlgorandSigner?.walletAddress === handoff.walletAddress)
          rememberedAlgorandSigner = null
        latchUnityWalletPackageCleanup()
        if (handoff.authProvider === 'web3auth_avm_email') latchWeb3AuthAvmCleanup()
        throw error
      }
      confirmUnityWalletCleanup()
      if (session?.refreshToken === handoff.refreshToken) storeSession(null)
      if (rememberedAlgorandSigner?.walletAddress === handoff.walletAddress)
        rememberedAlgorandSigner = null
      pendingHandoff = null
      clearPendingHandoffPageHide()
    }

    const logoutUnitySession = async value => {
      requireUnityWalletCleanupIdle()
      const target = value == null ? null : packageSession(value)
      if (!target && (activeOpen || pendingHandoff || session || acknowledgedUnitySigner || preparedTransactionGroup))
        throw new BlockmakerError('Finish cancelling the current request before disconnecting.', { code: 'REQUEST_ALREADY_PENDING' })
      beginUnityWalletCleanup()
      try {
        if (target) await rawRequest('/v1/auth/logout', {
          method: 'POST', auth: false, keepalive: true,
          body: { refreshToken: target.refreshToken, gameId },
        })
        await walletSetup.disconnectAll()
      } catch (error) {
        if (session?.refreshToken === target?.refreshToken) storeSession(null)
        if (pendingHandoff?.refreshToken === target?.refreshToken) {
          pendingHandoff = null
          clearPendingHandoffPageHide()
        }
        if (rememberedAlgorandSigner?.walletAddress === target?.walletAddress)
          rememberedAlgorandSigner = null
        latchUnityWalletPackageCleanup()
        throw error
      }
      confirmUnityWalletCleanup()
      invalidateLoginAttempts()
      if (session?.refreshToken === target?.refreshToken) storeSession(null)
      if (pendingHandoff?.refreshToken === target?.refreshToken) {
        pendingHandoff = null
        clearPendingHandoffPageHide()
      }
      if (rememberedAlgorandSigner?.walletAddress === target?.walletAddress)
        rememberedAlgorandSigner = null
      if (acknowledgedUnitySigner?.walletAddress === target?.walletAddress)
        acknowledgedUnitySigner = null
      if (preparedTransactionGroup) {
        if (preparedTransactionGroup.state === 'signing') preparedTransactionGroup.state = 'abandoned'
        else preparedTransactionGroup = null
      }
      return Object.freeze({ confirmed: true })
    }

    const transactionGroupFailure = (message, code = 'TRANSACTION_GROUP_INVALID') =>
      new BlockmakerError(message, { code })

    const exactUnitySigner = input => {
      walletPackageExactKeys(input, ['address', 'providerId', 'unsignedTransactionsBase64'],
        'The Unity transaction-group request')
      const address = String(input.address ?? '').trim().toUpperCase()
      const providerId = String(input.providerId ?? '').trim().toLowerCase()
      const binding = acknowledgedUnitySigner
      if (!isAlgorandAddressShape(address) || !walletSetup.byProvider.has(providerId)
        || !binding || binding.walletAddress !== address || binding.providerId !== providerId
        || walletSetup.byProvider.get(providerId).wallet !== binding.wallet
        || binding.wallet?.networkGenesisId !== PLAYER_WALLET_AUTH_V3.genesisId
        || !publicWalletAccounts(binding.wallet).includes(address)) {
        throw new BlockmakerError('The prepared transaction group does not match the acknowledged Unity wallet.', {
          code: 'SESSION_CONFLICT',
        })
      }
      return binding
    }

    const canonicalUnityUnsignedTransaction = (value, algosdk, address) => {
      if (typeof value !== 'string' || value.length < 1 || value.length > 10_000)
        throw transactionGroupFailure('The transaction group contains an invalid encoded transaction.')
      let bytes
      let transaction
      try {
        bytes = bytesFromBase64(value)
        if (base64FromBytes(bytes) !== value || bytes.byteLength < 1 || bytes.byteLength > 7_500)
          throw new Error('non-canonical transaction')
        transaction = algosdk.decodeUnsignedTransaction(bytes)
        const encoded = algosdk.encodeUnsignedTransaction(transaction)
        if (!(encoded instanceof Uint8Array) || !sameBytes(encoded, bytes))
          throw new Error('non-canonical transaction')
      } catch {
        throw transactionGroupFailure('The transaction group contains an invalid Algorand transaction.')
      }
      const type = String(transaction?.type ?? '')
      const sender = addressFromTransaction(algosdk, transaction?.sender ?? transaction?.from)
      const genesisId = String(transaction?.genesisID ?? transaction?.genesisId ?? '')
      const genesisHash = transaction?.genesisHash instanceof Uint8Array
        ? base64FromBytes(transaction.genesisHash) : ''
      const assetTransfer = transaction?.assetTransfer
      const accountControl = shopMeaningful(transaction?.rekeyTo)
        || shopMeaningful(transaction?.reKeyTo)
        || shopMeaningful(transaction?.payment?.closeRemainderTo)
        || shopMeaningful(transaction?.closeRemainderTo)
        || shopMeaningful(assetTransfer?.closeRemainderTo)
        || shopMeaningful(assetTransfer?.assetSender)
        || shopMeaningful(transaction?.assetSender)
      // Admit immutable creation, never existing-asset changes or asset controls.
      const creation = transaction?.assetConfig
      const immutableCreation = type === 'acfg' && creation?.assetIndex === 0n
        && creation.total > 0n && creation.decimals <= 19 && creation.defaultFrozen === false
        && !['manager', 'reserve', 'freeze', 'clawback'].some(field => shopMeaningful(creation[field]))
      if ((!['pay', 'axfer', 'appl'].includes(type) && !immutableCreation) || sender !== address
        || genesisId !== PLAYER_WALLET_AUTH_V3.genesisId
        || genesisHash !== PLAYER_WALLET_AUTH_V3.genesisHash || accountControl) {
        throw transactionGroupFailure('The transaction group is outside the reviewed MainNet signing boundary.')
      }
      return Object.freeze({ bytes: new Uint8Array(bytes), transaction })
    }

    const validateUnityTransactionGroup = (values, algosdk, address) => {
      if (!Array.isArray(values) || values.length < 1 || values.length > 16)
        throw transactionGroupFailure('A transaction group must contain from one to sixteen transactions.')
      const entries = values.map(value => canonicalUnityUnsignedTransaction(value, algosdk, address))
      const submittedGroups = entries.map(entry => entry.transaction?.group)
      if (entries.length === 1) {
        if (shopMeaningful(submittedGroups[0]))
          throw transactionGroupFailure('A one-transaction request must be ungrouped.')
      } else {
        const expected = submittedGroups[0]
        if (!(expected instanceof Uint8Array) || expected.byteLength !== 32
          || submittedGroups.some(group => !(group instanceof Uint8Array) || !sameBytes(group, expected))) {
          throw transactionGroupFailure('The atomic transaction group identifier is invalid.')
        }
        let computed
        try {
          for (const entry of entries) entry.transaction.group = undefined
          computed = algosdk.computeGroupID(entries.map(entry => entry.transaction))
        } catch {
          throw transactionGroupFailure('The atomic transaction group could not be recomputed.')
        } finally {
          entries.forEach((entry, index) => { entry.transaction.group = submittedGroups[index] })
        }
        if (!(computed instanceof Uint8Array) || computed.byteLength !== 32 || !sameBytes(computed, expected))
          throw transactionGroupFailure('The atomic transaction group identifier does not match its members.')
      }
      return Object.freeze(entries.map(entry => Object.freeze({
        bytes: new Uint8Array(entry.bytes),
      })))
    }

    const prepareUnityUniversalUsername = input => {
      walletPackageExactKeys(input, ['address', 'providerId', 'unsignedTransactionsBase64', 'prepared'],
        'The Unity universal username request')
      const group = {
        address: input.address, providerId: input.providerId,
        unsignedTransactionsBase64: input.unsignedTransactionsBase64,
      }
      const binding = exactUnitySigner(group)
      const prepared = JSON.parse(JSON.stringify(input.prepared))
      if (!Array.isArray(prepared?.unsignedTxnsBase64)
        || prepared.unsignedTxnsBase64.length !== 2
        || !Array.isArray(group.unsignedTransactionsBase64)
        || group.unsignedTransactionsBase64.length !== 2
        || prepared.unsignedTxnsBase64.some((value, index) => value !== group.unsignedTransactionsBase64[index]))
        throw transactionGroupFailure('The username plan does not match the exact reviewed group.')
      validateUniversalUsernameTransactions(prepared, binding.algosdk, binding.walletAddress)
      // The existing group verifier additionally checks MainNet, canonical
      // bytes, group identity and the acknowledged sender before any signing.
      prepareUnityTransactionGroup(group)
    }

    const prepareUnityTransactionGroup = input => {
      requireUnityWalletCleanupIdle()
      if (activeOpen || pendingHandoff || fundingHandle || preparedTransactionGroup)
        throw new BlockmakerError('Another Unity WebGL wallet-package operation is active.', {
          code: 'REQUEST_ALREADY_PENDING',
        })
      const binding = exactUnitySigner(input)
      const entries = validateUnityTransactionGroup(
        input.unsignedTransactionsBase64,
        binding.algosdk,
        binding.walletAddress,
      )
      preparedTransactionGroup = {
        state: 'prepared',
        binding,
        address: binding.walletAddress,
        providerId: binding.providerId,
        entries,
      }
    }

    const validateUnitySignedGroup = async (values, operation) => {
      if (!Array.isArray(values) || values.length !== operation.entries.length)
        throw transactionGroupFailure('The wallet did not sign every prepared transaction.', 'WALLET_SIGNATURE_INVALID')
      const verified = await Promise.all(values.map(async (value, index) => {
        if (!(value instanceof Uint8Array) || value.byteLength < 1 || value.byteLength > 10_000)
          throw transactionGroupFailure('The wallet returned an invalid signed transaction.', 'WALLET_SIGNATURE_INVALID')
        const bytes = new Uint8Array(value)
        let envelope
        try { envelope = operation.binding.algosdk.decodeSignedTransaction(bytes) }
        catch {
          throw transactionGroupFailure('The wallet returned an invalid signed transaction.', 'WALLET_SIGNATURE_INVALID')
        }
        const signer = envelope?.sgnr
        const signerKey = signer instanceof Uint8Array ? signer : signer?.publicKey
        const sender = envelope?.txn?.sender ?? envelope?.txn?.from
        const senderKey = sender instanceof Uint8Array ? sender : sender?.publicKey
        const verificationKey = signer == null ? senderKey : signerKey
        let signingBytes
        try { signingBytes = envelope?.txn?.bytesToSign?.() }
        catch { signingBytes = null }
        if (!(envelope?.sig instanceof Uint8Array) || envelope.sig.byteLength !== 64
          || shopMeaningful(envelope?.msig) || shopMeaningful(envelope?.lsig)
          || !sameBytes(envelope?.txn?.toByte?.(), operation.entries[index].bytes)
          || !(verificationKey instanceof Uint8Array) || verificationKey.byteLength !== 32
          || !(signingBytes instanceof Uint8Array)) {
          throw transactionGroupFailure(
            'The wallet did not sign the exact prepared transactions in order.',
            'WALLET_SIGNATURE_INVALID',
          )
        }
        let validSignature = false
        try {
          const subtle = globalThis.crypto?.subtle
          if (!subtle || typeof subtle.importKey !== 'function' || typeof subtle.verify !== 'function')
            throw new Error('Ed25519 verification unavailable')
          const publicKey = await subtle.importKey(
            'raw', new Uint8Array(verificationKey), { name: 'Ed25519' }, false, ['verify'],
          )
          validSignature = await subtle.verify(
            { name: 'Ed25519' }, publicKey, envelope.sig, signingBytes,
          )
        } catch { validSignature = false }
        if (!validSignature) {
          throw transactionGroupFailure(
            'The wallet returned an invalid signature for the exact prepared transaction.',
            'WALLET_SIGNATURE_INVALID',
          )
        }
        return bytes
      }))
      return Object.freeze(verified)
    }

    const signPreparedUnityTransactionGroup = () => {
      requireUnityWalletCleanupIdle()
      const operation = preparedTransactionGroup
      if (!operation || operation.state !== 'prepared')
        throw transactionGroupFailure('There is no prepared transaction group to sign.')
      exactUnitySigner({
        address: operation.address,
        providerId: operation.providerId,
        unsignedTransactionsBase64: operation.entries.map(entry => base64FromBytes(entry.bytes)),
      })
      const transactions = operation.entries.map(entry =>
        operation.binding.algosdk.decodeUnsignedTransaction(new Uint8Array(entry.bytes)))
      operation.state = 'signing'
      let launched
      try {
        // This public facade call is intentionally synchronous in the caller's
        // final click. No fetch, await, signer hint or provider object precedes it.
        launched = operation.binding.wallet.signTransactions(
          transactions,
          transactions.map((_, index) => index),
        )
      } catch (error) {
        preparedTransactionGroup = null
        throw error
      }
      return Promise.resolve(launched).then(async values => {
        if (operation.state === 'abandoned')
          throw new BlockmakerError('The transaction signing request was cancelled.', { code: 'PLAYER_CANCELLED' })
        const verified = await validateUnitySignedGroup(values, operation)
        if (operation.state === 'abandoned')
          throw new BlockmakerError('The transaction signing request was cancelled.', { code: 'PLAYER_CANCELLED' })
        return verified
      }).finally(() => {
        if (preparedTransactionGroup === operation) preparedTransactionGroup = null
      })
    }

    const cancelPreparedUnityTransactionGroup = () => {
      const operation = preparedTransactionGroup
      if (!operation) return
      if (operation.state === 'signing') operation.state = 'abandoned'
      else if (preparedTransactionGroup === operation) preparedTransactionGroup = null
    }

    const openUnityFunding = input => {
      walletPackageExactKeys(input, ['accessToken', 'accountKind', 'authProvider'], 'The Unity funding request')
      if (input.accountKind !== 'algorand_wallet'
        || !walletSetup.byProvider.has(input.authProvider)
        || typeof input.accessToken !== 'string' || input.accessToken.length < 1
        || input.accessToken.length > 8_192 || !/^[\x21-\x7e]+$/.test(input.accessToken)
        || /^sk_/i.test(input.accessToken)) {
        throw new BlockmakerError('Funding help requires a current TxnLab Algorand wallet session.', {
          code: 'PLAYER_ECONOMIC_CAPABILITY_DENIED',
        })
      }
      if (fundingHandle)
        throw new BlockmakerError('Wallet funding help is already open.', { code: 'FUNDING_GUIDE_ACTIVE' })
      fundingHandle = openFundingGuide({ onClose: () => { fundingHandle = null } }, {
        sessionToken: input.accessToken,
        accountKind: input.accountKind,
        authProvider: input.authProvider,
      })
    }

    const closeUnityFunding = () => {
      const handle = fundingHandle
      fundingHandle = null
      handle?.close?.()
    }

    const captureUnityDeploymentEvidence = envelopeValue => {
      const envelope = exactWalletPackageBridgeEnvelope(envelopeValue)
      if (envelope.schemaVersion !== 'blockmaker-unity-webgl-wallet-package/v2'
        || envelope.runtimeProfile !== controllerOptions.runtimeProfile) {
        walletPackageEvidenceError('The Unity bridge runtime profile does not match the installed controller.')
      }
      if (evidenceState)
        walletPackageEvidenceError('Unity WebGL deployment evidence was already captured for this page lifecycle.')
      const observedHostRuntimeUrl = exactSiblingUnityHostRuntimeUrl(controllerOptions.hostRuntimeUrl)
      evidenceState = beginEvidenceCapture({
        unityBridgeEnvelope: envelope,
        unityHostRuntimeUrl: observedHostRuntimeUrl,
      })
      evidenceState.lifecycleId = envelope.lifecycleId
    }

    const downloadUnityDeploymentEvidence = requestValue => {
      walletPackageExactKeys(requestValue, [
        'schemaVersion', 'lifecycleId', 'operationId', 'operationKind', 'phase',
      ], 'The Unity WebGL deployment-evidence download request')
      if (requestValue.schemaVersion !== 'blockmaker-unity-webgl-wallet-package/v2'
        || !evidenceState || requestValue.lifecycleId !== evidenceState.lifecycleId
        || !Number.isSafeInteger(requestValue.operationId) || requestValue.operationId < 1
        || requestValue.operationKind !== 'deployment_evidence' || requestValue.phase !== 'download'
        || evidenceState.status !== 2 || !evidenceState.materialized) {
        walletPackageEvidenceError('Unity WebGL deployment evidence is not ready to download.', 'PACKAGE_EVIDENCE_UNAVAILABLE')
      }
      downloadWalletPackageEvidence(evidenceState.materialized)
    }

    unityWebGlWalletPackageController = Object.freeze({
      openAccount: openAccountForUnity,
      captureDeploymentEvidence: captureUnityDeploymentEvidence,
      getDeploymentEvidenceStatus: () => evidenceStatus(evidenceState),
      downloadDeploymentEvidence: downloadUnityDeploymentEvidence,
      acknowledge: acknowledgeUnityHandoff,
      reject: rejectUnityHandoff,
      cancel: closeActiveAccount,
      logout: logoutUnitySession,
      prepareTransactionGroup: prepareUnityTransactionGroup,
      prepareUniversalUsername: prepareUnityUniversalUsername,
      signPreparedTransactionGroup: signPreparedUnityTransactionGroup,
      cancelPreparedTransactionGroup: cancelPreparedUnityTransactionGroup,
      openFunding: openUnityFunding,
      closeFunding: closeUnityFunding,
    })
    return unityWebGlWalletPackageController
  }

  // Explicit, server-timed gameplay tracking.
  let activeAnalyticsPromise = null
  const startAnalyticsSession = async (startOptions = {}) => {
    if (activeAnalyticsPromise) return activeAnalyticsPromise
    const pending = (async () => {
      const started = await request('/v1/analytics/session/start', { method: 'POST' })
      const id = String(started?.session?.id ?? '')
      if (!/^[a-f0-9]{48}$/.test(id))
        throw new BlockmakerError('Blockmaker returned an invalid play session.', { code: 'PLAY_SESSION_INVALID' })
      const intervalMs = Math.max(10_000, Number(started.heartbeatEverySeconds || 30) * 1000)
      const autoHeartbeat = startOptions.autoHeartbeat !== false
      let stopped = false
      let timer = null

      const reportError = error => {
        try { startOptions.onError?.(error) } catch { /* consumer callbacks cannot break tracking cleanup */ }
      }
      const heartbeat = async () => {
        if (stopped) return { success: true, alreadyEnded: true, session: started.session }
        return request(`/v1/analytics/session/${encodeURIComponent(id)}/heartbeat`, { method: 'POST' })
      }
      const onVisibility = () => {
        if (globalThis.document?.visibilityState === 'visible')
          void heartbeat().catch(reportError)
      }
      const cleanup = () => {
        if (timer !== null) clearInterval(timer)
        timer = null
        try { globalThis.document?.removeEventListener?.('visibilitychange', onVisibility) } catch { /* no DOM */ }
        try { globalThis.removeEventListener?.('pagehide', onPageHide) } catch { /* no DOM */ }
      }
      const end = async (keepalive = false) => {
        if (stopped) return { success: true, alreadyEnded: true, session: started.session }
        stopped = true
        cleanup()
        activeAnalyticsPromise = null
        return request(`/v1/analytics/session/${encodeURIComponent(id)}/end`, { method: 'POST', keepalive })
      }
      const onPageHide = () => { void end(true).catch(reportError) }

      if (autoHeartbeat) {
        timer = setInterval(() => {
          if (!globalThis.document || globalThis.document.visibilityState !== 'hidden')
            void heartbeat().catch(reportError)
        }, intervalMs)
        try { globalThis.document?.addEventListener?.('visibilitychange', onVisibility) } catch { /* no DOM */ }
        try { globalThis.addEventListener?.('pagehide', onPageHide, { once: true }) } catch { /* no DOM */ }
      }
      return Object.freeze({
        id,
        startedAt: Number(started.session.startedAt || 0),
        get stopped() { return stopped },
        heartbeat,
        stop: () => end(false),
      })
    })()
    activeAnalyticsPromise = pending
    try { return await pending }
    catch (error) {
      if (activeAnalyticsPromise === pending) activeAnalyticsPromise = null
      throw error
    }
  }

  return Object.freeze({
    gameId,
    baseUrl,
    clientKind,
    get session() { return session ? { ...session } : null },
    request,
    integration: {
      config: () => request('/v1/integrations/config', { auth: false }),
      manifest: () => request('/v1/integrations/manifest', { auth: false }),
      preflight,
    },
    walletPackage: {
      /** Build canonical Web deployment evidence from this exact loaded SDK. */
      captureDeploymentEvidence: captureBrowserWalletPackageEvidence,
      getDeploymentEvidenceStatus: () => clientKind === 'web'
        ? evidenceStatus(webEvidenceState)
        : evidenceStatus({ status: 10 }),
      /** Download the previously captured canonical Web deployment evidence. */
      downloadDeploymentEvidence: downloadBrowserWalletPackageEvidence,
    },
    auth: {
      requestEmail: email => request('/v1/auth/email/request', { method: 'POST', auth: false, body: { email, gameId } }),
      verifyEmail,
      verifyMagic,
      loginWithMagicEmail,
      loginWithMagicEmailXChain,
      loginWithAlgorandWallet,
      loginWithPera,
      loginWithEvmWallet,
      /** Isolated, authentication-only Algorand email wallet. Call directly from a click/tap. */
      loginWithWeb3AuthAvmEmail,
      /** Operator-only, permit-gated canary. Never installs a player session. */
      qualifyWeb3AuthAvmEmail,
      /** Attach one bounded self-observation to an active qualification session. */
      recordWeb3AuthAvmQualificationObservation,
      walletChallenge: input => request('/v1/auth/wallet/challenge', { method: 'POST', auth: false, body: { ...input, gameId } }),
      walletVerify,
      refresh,
      getSession: getPlayerSession,
      setSession: setSessionManually,
      logout: () => logoutPlayer({ reportRevocationFailure: true }),
    },
    wallets: {
      /** Tenant-enabled, safe display metadata from the public integration config. */
      available: availableWalletProviders,
      /** Stable, player-facing wallet error mapping without leaking provider internals. */
      normalizeError: normalizedWalletError,
      playerMessage: code => WALLET_ERROR_COPY[String(code ?? '').trim().toUpperCase()] ?? WALLET_ERROR_COPY.PROVIDER_UNAVAILABLE,
      /** Framework-neutral adapter for @perawallet/connect 1.6.x. */
      pera: peraWalletAdapter,
      /** Framework-neutral adapter for lute-connect 2.x. */
      lute: luteWalletAdapter,
      /** One safe account.open() setup for the recommended Pera/Lute/TxnLab Web3Auth clients. */
      algorand: algorandWalletSetup,
      /** Loopback-only failure simulator for automated wallet UX tests. */
      simulated: simulatedWalletAdapter,
    },
    unityWebGl: {
      /** Install the bounded session-only bridge consumed by the published Unity WebGL plug-in. */
      web3authAvmEmail: installWeb3AuthAvmEmailUnityWebGlBridge,
      /** Install the unified Pera, Lute and TxnLab Email/Google Algorand-wallet controller. */
      walletPackage: installUnityWebGlWalletPackageController,
    },
    profile: {
      get: () => request('/v1/profile'),
      getGameProfile: gameProfileGet,
      checkUniversalUsername,
      prepareUniversalUsername,
      completeUniversalUsername,
      registerUniversalUsername,
      signAndCompleteUniversalUsername,
      getUniversalUsernames,
      setPrimaryUniversalUsername,
      checkGameUsername,
      setGameUsername,
      clearGameUsername,
      getGameProfiles,
      searchNfts,
      previewNft,
      setGameProfileNft,
      refreshGameProfileNft,
      clearGameProfileNft,
      getGameData: () => request('/v1/profile/game-data'),
      saveGameData: data => {
        if (!data || typeof data !== 'object' || Array.isArray(data))
          throw new Error('profile.saveGameData requires the complete cloud-save snapshot as an object.')
        return request('/v1/profile/game-data', { method: 'POST', body: { data } })
      },
      patchGameData: input => {
        if (!input || typeof input !== 'object' || Array.isArray(input))
          throw new Error('profile.patchGameData requires expectedRevision, idempotencyKey and operations.')
        if (!Number.isSafeInteger(input.expectedRevision) || input.expectedRevision < 0)
          throw new Error('profile.patchGameData expectedRevision must be a non-negative whole number.')
        const idempotencyKey = String(input.idempotencyKey ?? '').trim()
        if (!/^[A-Za-z0-9][A-Za-z0-9:._-]{0,127}$/.test(idempotencyKey))
          throw new Error('profile.patchGameData idempotencyKey must be a stable 1-128 character value.')
        if (!Array.isArray(input.operations) || input.operations.length < 1 || input.operations.length > 64)
          throw new Error('profile.patchGameData operations must contain 1-64 changes.')
        return request('/v1/profile/game-data', {
          method: 'PATCH',
          body: {
            expectedRevision: input.expectedRevision,
            idempotencyKey,
            operations: input.operations,
            ...(input.expectedSchemaVersion === undefined
              ? {}
              : { expectedSchemaVersion: String(input.expectedSchemaVersion) }),
          },
        })
      },
      getSchema: () => request('/v1/profile/schema'),
      open: openProfile,
    },
    gating: {
      listRules: () => request('/v1/gating/rules', { auth: false }),
      evaluate: evaluateGates,
      evaluateV2: evaluateGatesV2,
      checkV2: checkGateV2,
      check: async gateKey => {
        const result = await evaluateGates([gateKey])
        return result.gates[0]
      },
    },
    nftMinting: {
      readiness: nftMintingConfig,
      prepare: prepareNftMint,
      status: nftMintingStatus,
      submitSigned: submitSignedNftMint,
      signAndSubmit: signAndSubmitNftMint,
      reconcile: reconcileNftMint,
    },
    shop: {
      info: shopInfo,
      infoV1: shopInfoV1,
      openCommits: openShopCommits,
      confirm: confirmShopCommit,
      reveal: revealShopCommit,
      watchDelivery: watchShopDelivery,
      prepareAssetAcceptance: prepareShopAssetAcceptance,
      prepare: prepareShopPurchase,
    },
    marketplace: {
      config: marketplaceConfig,
      listings: marketplaceListings,
      listing: assetId => request(`/v1/game-marketplace/listing/${encodeURIComponent(String(assetId))}`, { auth: false }),
      inventory: () => request('/v1/game-marketplace/inventory'),
      buildList: input => marketplaceBuild('list', input),
      buildBuy: input => marketplaceBuild('buy', input),
      buildCancel: input => marketplaceBuild('cancel', input),
      signAndSubmit: signAndSubmitMarketplace,
      list: (input, signingOptions) => runMarketplaceAction('list', input, signingOptions),
      buy: (input, signingOptions) => runMarketplaceAction('buy', input, signingOptions),
      cancel: (input, signingOptions) => runMarketplaceAction('cancel', input, signingOptions),
      open: openMarketplace,
      signers: {
        magic: magicMarketplaceSigner,
        magicXchain: magicXchainMarketplaceSigner,
        xchain: xchainMarketplaceSigner,
      },
    },
    leaderboard: {
      get: (options = {}) => {
        const query = new URLSearchParams()
        for (const [key, value] of Object.entries(options)) if (value !== undefined && value !== null && value !== '') query.set(key, String(value))
        return request(`/v1/leaderboard${query.size ? `?${query}` : ''}`)
      },
    },
    leaderboards: {
      list: () => request('/v1/game-leaderboards/boards'),
      get: (boardKey, options = {}) => {
        const key = String(boardKey ?? '').trim().toLowerCase()
        if (!/^[a-z0-9][a-z0-9_-]{0,63}$/.test(key))
          throw new Error('leaderboards.get requires a valid board key.')
        const query = new URLSearchParams()
        for (const [name, value] of Object.entries(options)) {
          if (value !== undefined && value !== null && value !== '') query.set(name, String(value))
        }
        return request(`/v1/game-leaderboards/${encodeURIComponent(key)}${query.size ? `?${query}` : ''}`)
      },
    },
    campaigns: {
      list: (options = {}) => request(
        options.public === false ? '/v1/campaigns' : '/v1/campaigns/public',
        options.public === false ? {} : { auth: false },
      ),
      get: (campaignKey, options = {}) => {
        const key = String(campaignKey ?? '').trim().toLowerCase()
        if (!/^[a-z0-9][a-z0-9_-]{0,63}$/.test(key))
          throw new Error('campaigns.get requires a valid campaign key.')
        const query = new URLSearchParams()
        if (options.period != null) query.set('period', String(options.period))
        if (options.limit != null) query.set('limit', String(options.limit))
        const isPublic = options.public !== false
        const prefix = isPublic ? '/v1/campaigns/public' : '/v1/campaigns'
        return request(
          `${prefix}/${encodeURIComponent(key)}${query.size ? `?${query}` : ''}`,
          isPublic ? { auth: false } : {},
        )
      },
      entitlements: (options = {}) => {
        const query = new URLSearchParams()
        if (options.status != null) query.set('status', String(options.status))
        if (options.limit != null) query.set('limit', String(options.limit))
        if (options.after != null) query.set('after', String(options.after))
        if (options.campaignKey != null) query.set('campaignKey', String(options.campaignKey))
        return request(`/v1/campaigns/entitlements${query.size ? `?${query}` : ''}`)
      },
    },
    automation: {
      getProfile: () => request('/v1/automation/profile'),
      createProfile: input => {
        const value = requireAutomationObject(input, 'createProfile')
        return request('/v1/automation/profile', {
          method: 'POST',
          body: { idempotencyKey: requireAutomationStableId(value.idempotencyKey, 'idempotencyKey') },
        })
      },
      setPaused: input => {
        const value = requireAutomationObject(input, 'setPaused')
        if (typeof value.paused !== 'boolean') throw new Error('automation.setPaused requires paused as true or false.')
        return request('/v1/automation/profile/pause', {
          method: 'POST',
          body: {
            paused: value.paused,
            expectedRevision: requireAutomationRevision(value.expectedRevision),
            idempotencyKey: requireAutomationStableId(value.idempotencyKey, 'idempotencyKey'),
          },
        })
      },
      getWalletPolicy: () => request('/v1/automation/wallet-policy'),
      updateWalletPolicy: input => {
        const value = requireAutomationObject(input, 'updateWalletPolicy')
        if (typeof value.enabled !== 'boolean') throw new Error('automation.updateWalletPolicy requires enabled as true or false.')
        return request('/v1/automation/wallet-policy', {
          method: 'PUT',
          body: {
            adapterKey: requireAutomationSimpleKey(value.adapterKey, 'adapterKey'),
            adapterVersion: requireAutomationVersion(value.adapterVersion, 'adapterVersion'),
            config: requireAutomationPolicyConfig(value.config),
            enabled: value.enabled,
            expectedRevision: requireAutomationRevision(value.expectedRevision),
            idempotencyKey: requireAutomationStableId(value.idempotencyKey, 'idempotencyKey'),
          },
        })
      },
      listSubjectPolicies: (options = {}) => {
        const value = requireAutomationObject(options, 'listSubjectPolicies')
        const query = new URLSearchParams()
        if (value.subjectKind != null) query.set('subjectKind', requireAutomationSubjectKind(value.subjectKind))
        if (value.after != null) query.set('after', requireAutomationStableId(value.after, 'after'))
        if (value.limit != null) query.set('limit', String(requireAutomationPageLimit(value.limit)))
        if (value.enabled != null) {
          if (typeof value.enabled !== 'boolean') throw new Error('enabled must be true or false.')
          query.set('enabled', String(value.enabled))
        }
        if (value.paused != null) {
          if (typeof value.paused !== 'boolean') throw new Error('paused must be true or false.')
          query.set('paused', String(value.paused))
        }
        return request(`/v1/automation/subjects${query.size ? `?${query}` : ''}`)
      },
      getSubjectPolicy: (subjectKind, subjectId) => request(
        `/v1/automation/subjects/${encodeURIComponent(requireAutomationSubjectKind(subjectKind))}/${encodeURIComponent(requireAutomationSubjectId(subjectId))}`,
      ),
      updateSubjectPolicy: input => {
        const value = requireAutomationObject(input, 'updateSubjectPolicy')
        if (typeof value.enabled !== 'boolean' || typeof value.paused !== 'boolean')
          throw new Error('automation.updateSubjectPolicy requires enabled and paused as true or false.')
        const subjectKind = requireAutomationSubjectKind(value.subjectKind)
        const subjectId = requireAutomationSubjectId(value.subjectId)
        return request(`/v1/automation/subjects/${encodeURIComponent(subjectKind)}/${encodeURIComponent(subjectId)}`, {
          method: 'PUT',
          body: {
            adapterKey: requireAutomationSimpleKey(value.adapterKey, 'adapterKey'),
            adapterVersion: requireAutomationVersion(value.adapterVersion, 'adapterVersion'),
            config: requireAutomationPolicyConfig(value.config),
            enabled: value.enabled,
            paused: value.paused,
            expectedRevision: requireAutomationRevision(value.expectedRevision),
            idempotencyKey: requireAutomationStableId(value.idempotencyKey, 'idempotencyKey'),
          },
        })
      },
      applyPolicyToSubjects: input => {
        const value = requireAutomationObject(input, 'applyPolicyToSubjects')
        if (!Array.isArray(value.subjectIds) || value.subjectIds.length < 1 || value.subjectIds.length > 500)
          throw new Error('automation.applyPolicyToSubjects requires between 1 and 500 subject IDs.')
        if (typeof value.enabled !== 'boolean' || typeof value.paused !== 'boolean')
          throw new Error('automation.applyPolicyToSubjects requires enabled and paused as true or false.')
        const subjectIds = value.subjectIds.map(requireAutomationSubjectId)
        if (new Set(subjectIds).size !== subjectIds.length)
          throw new Error('automation.applyPolicyToSubjects cannot contain duplicate subject IDs.')
        const rawRevisions = requireAutomationObject(value.expectedSubjectRevisions, 'expectedSubjectRevisions')
        const revisionEntries = Object.entries(rawRevisions).map(([rawSubjectId, rawRevision]) => {
          const subjectId = requireAutomationSubjectId(rawSubjectId)
          return [subjectId, requireAutomationRevision(rawRevision, `expectedSubjectRevisions.${subjectId}`)]
        })
        const expectedSubjectRevisions = Object.fromEntries(revisionEntries)
        if (revisionEntries.length !== subjectIds.length
          || subjectIds.some(subjectId => !Object.hasOwn(expectedSubjectRevisions, subjectId))) {
          throw new Error('automation.applyPolicyToSubjects requires one exact revision for every selected subject.')
        }
        return request('/v1/automation/subjects/apply', {
          method: 'POST',
          body: {
            subjectKind: requireAutomationSubjectKind(value.subjectKind),
            subjectIds,
            adapterKey: requireAutomationSimpleKey(value.adapterKey, 'adapterKey'),
            adapterVersion: requireAutomationVersion(value.adapterVersion, 'adapterVersion'),
            config: requireAutomationPolicyConfig(value.config),
            enabled: value.enabled,
            paused: value.paused,
            expectedProfileRevision: requireAutomationRevision(value.expectedProfileRevision, 'expectedProfileRevision'),
            expectedSubjectRevisions,
            idempotencyKey: requireAutomationStableId(value.idempotencyKey, 'idempotencyKey'),
          },
        })
      },
      listActions: (options = {}) => {
        const value = requireAutomationObject(options, 'listActions')
        const query = new URLSearchParams()
        if (value.status != null) {
          const status = String(value.status).trim()
          if (!automationActionStatuses.has(status)) throw new Error('status is not a supported Automation action status.')
          query.set('status', status)
        }
        if (value.subjectKind != null) query.set('subjectKind', requireAutomationSubjectKind(value.subjectKind))
        if (value.after != null) query.set('after', requireAutomationStableId(value.after, 'after'))
        if (value.limit != null) query.set('limit', String(requireAutomationPageLimit(value.limit)))
        return request(`/v1/automation/actions${query.size ? `?${query}` : ''}`)
      },
      requestApprovalIntent: (actionId, input) => {
        const value = requireAutomationObject(input, 'requestApprovalIntent')
        return request(`/v1/automation/actions/${encodeURIComponent(requireAutomationStableId(actionId, 'actionId'))}/approval-intent`, {
          method: 'POST',
          body: {
            expectedRevision: requireAutomationRevision(value.expectedRevision),
            idempotencyKey: requireAutomationStableId(value.idempotencyKey, 'idempotencyKey'),
          },
        })
      },
      submitApprovalIntent: (actionId, input) => {
        const value = requireAutomationObject(input, 'submitApprovalIntent')
        return request(`/v1/automation/actions/${encodeURIComponent(requireAutomationStableId(actionId, 'actionId'))}/submit`, {
          method: 'POST',
          body: {
            intentId: requireAutomationStableId(value.intentId, 'intentId'),
            signedTxnsBase64: requireSignedAutomationGroup(value.signedTxnsBase64),
            idempotencyKey: requireAutomationStableId(value.idempotencyKey, 'idempotencyKey'),
          },
        })
      },
    },
    groups: Object.freeze({
      getMembership(scopeKey) {
        return request(`/v1/groups/scopes/${encodeURIComponent(requireGroupKey(scopeKey, 'scopeKey', true))}/me`)
      },
      get(groupId) { return request(`/v1/groups/${encodeURIComponent(requireGroupKey(groupId, 'groupId'))}`) },
      create(scopeKey, input) {
        const value = requireGroupInput(input, ['operationId', 'name'])
        const name = typeof value.name === 'string' ? value.name.trim() : ''
        if (!name || [...name].length > 64 || /[\u0000-\u001f\u007f]/.test(name)) throw new Error('groups.create requires a 1–64 character name.')
        return request(`/v1/groups/scopes/${encodeURIComponent(requireGroupKey(scopeKey, 'scopeKey', true))}/groups`, {
          method: 'POST', body: { operationId: requireGroupKey(value.operationId, 'operationId'), name },
        })
      },
      createInvite(groupId, input) {
        const value = requireGroupInput(input, ['operationId', 'expiresInSeconds', 'maxUses'])
        return request(`/v1/groups/${encodeURIComponent(requireGroupKey(groupId, 'groupId'))}/invites`, {
          method: 'POST', body: { operationId: requireGroupKey(value.operationId, 'operationId'),
            ...(value.expiresInSeconds === undefined ? {} : { expiresInSeconds: requireGroupInteger(value.expiresInSeconds, 60, 604800, 'expiresInSeconds') }),
            ...(value.maxUses === undefined ? {} : { maxUses: requireGroupInteger(value.maxUses, 1, 100, 'maxUses') }) },
        })
      },
      join(scopeKey, input) {
        const value = requireGroupInput(input, ['operationId', 'inviteToken'])
        if (typeof value.inviteToken !== 'string' || !/^[A-Za-z0-9_-]{43}$/.test(value.inviteToken)) throw new Error('groups.join requires a valid inviteToken.')
        return request(`/v1/groups/scopes/${encodeURIComponent(requireGroupKey(scopeKey, 'scopeKey', true))}/join`, {
          method: 'POST', body: { operationId: requireGroupKey(value.operationId, 'operationId'), inviteToken: value.inviteToken },
        })
      },
      revokeInvite(groupId, inviteId, input) {
        const value = requireGroupInput(input, ['operationId'])
        return request(`/v1/groups/${encodeURIComponent(requireGroupKey(groupId, 'groupId'))}/invites/${encodeURIComponent(requireGroupKey(inviteId, 'inviteId'))}/revoke`, {
          method: 'POST', body: { operationId: requireGroupKey(value.operationId, 'operationId') },
        })
      },
      leave(groupId, input) {
        const value = requireGroupInput(input, ['operationId'])
        return request(`/v1/groups/${encodeURIComponent(requireGroupKey(groupId, 'groupId'))}/leave`, {
          method: 'POST', body: { operationId: requireGroupKey(value.operationId, 'operationId') },
        })
      },
      transferOwner(groupId, input) {
        const value = requireGroupInput(input, ['operationId', 'newOwnerWallet', 'expectedMembershipRevision'])
        return request(`/v1/groups/${encodeURIComponent(requireGroupKey(groupId, 'groupId'))}/transfer-owner`, {
          method: 'POST', body: { operationId: requireGroupKey(value.operationId, 'operationId'), newOwnerWallet: requireGroupWallet(value.newOwnerWallet),
            expectedMembershipRevision: requireGroupInteger(value.expectedMembershipRevision, 1, Number.MAX_SAFE_INTEGER - 1, 'expectedMembershipRevision') },
        })
      },
    }),
    progression: {
      get: (namespace, subjectKind, subjectId) => {
        const path = [namespace, subjectKind, subjectId]
          .map(value => encodeURIComponent(String(value ?? '').trim()))
          .join('/')
        return request(`/v1/progression/records/${path}`)
      },
      list: (options = {}) => {
        const query = new URLSearchParams()
        if (options.namespace != null) query.set('namespace', String(options.namespace))
        if (options.subjectKind != null) query.set('subjectKind', String(options.subjectKind))
        if (options.after != null) query.set('after', String(options.after))
        if (options.limit != null) query.set('limit', String(options.limit))
        return request(`/v1/progression/records?${query}`)
      },
    },
    yieldRewards: {
      list: (options = {}) => request(
        options.public === false ? '/v1/yield/programs' : '/v1/yield/public',
        options.public === false ? {} : { auth: false },
      ),
      balance: programKey => {
        const key = String(programKey ?? '').trim().toLowerCase()
        if (!/^[a-z0-9][a-z0-9_-]{0,63}$/.test(key))
          throw new Error('yieldRewards.balance requires a valid program key.')
        return request(`/v1/yield/programs/${encodeURIComponent(key)}/balance`)
      },
      claim: (programKey, input = {}) => {
        const key = String(programKey ?? '').trim().toLowerCase()
        const idempotencyKey = String(input.idempotencyKey ?? '').trim()
        if (!/^[a-z0-9][a-z0-9_-]{0,63}$/.test(key))
          throw new Error('yieldRewards.claim requires a valid program key.')
        if (!/^[A-Za-z0-9][A-Za-z0-9:._-]{0,127}$/.test(idempotencyKey))
          throw new Error('yieldRewards.claim requires a stable 1–128 character idempotencyKey.')
        if (input.amountBaseUnits != null && (!Number.isSafeInteger(input.amountBaseUnits) || input.amountBaseUnits <= 0))
          throw new Error('yieldRewards.claim amountBaseUnits must be a positive safe whole number.')
        return request(`/v1/yield/programs/${encodeURIComponent(key)}/claims`, {
          method: 'POST',
          body: {
            idempotencyKey,
            ...(input.amountBaseUnits == null ? {} : { amountBaseUnits: input.amountBaseUnits }),
          },
        })
      },
      claims: (options = {}) => {
        const query = new URLSearchParams()
        if (options.programKey != null) query.set('programKey', String(options.programKey))
        if (options.status != null) query.set('status', String(options.status))
        if (options.limit != null) query.set('limit', String(options.limit))
        return request(`/v1/yield/claims${query.size ? `?${query}` : ''}`)
      },
    },
    analytics: {
      start: startAnalyticsSession,
    },
    rewards: {
      info: () => request('/v1/rewards/info'),
      earnings: () => request('/v1/rewards/earnings'),
    },
    funding: {
      config: () => request('/v1/funding-guide/config', { auth: false }),
      get: () => request('/v1/funding-guide/me'),
      open: openFundingGuide,
    },
    onramp: {
      config: () => request('/v1/onramp/config', { auth: false }),
      createSession: createOnrampSession,
      getOrder: orderId => {
        const id = String(orderId ?? '').trim()
        if (!id) throw new Error('onramp.getOrder requires an orderId.')
        return request(`/v1/onramp/orders/${encodeURIComponent(id)}`)
      },
      get lastOrderId() { return lastOnrampOrderId || null },
      resumeLastOrder: options => lastOnrampOrderId ? trackOnrampOrder(lastOnrampOrderId, options) : null,
      clearLastOrder: () => storeLastOnrampOrder(null),
      trackOrder: trackOnrampOrder,
      open: openOnramp,
    },
    onboarding: {
      open: openOnboarding,
    },
    account: {
      open: openAccount,
    },
  })
}

export default createBlockmaker
