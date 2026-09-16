import { createHash } from 'node:crypto'
import { basename, dirname, isAbsolute, relative, resolve } from 'node:path'

export const WALLET_PACKAGE_LOCK_SCHEMA = 'blockmaker-wallet-package-lock/v1'
export const WALLET_PACKAGE_INSTALLATION_SCHEMA = 'blockmaker-wallet-package-installation-evidence/v1'
export const WALLET_PACKAGE_DEPLOYED_SCHEMA = 'blockmaker-wallet-package-deployed-evidence/v1'
export const WALLET_PACKAGE_DOCTOR_ATTESTATION_SCHEMA =
  'blockmaker-wallet-package-operator-doctor-attestation/v1'
export const WALLET_PACKAGE_DOCTOR_REQUIRED_CHECKS = Object.freeze([
  'wallet.package_local_closure',
  'wallet.package_deployed_evidence',
  'wallet.package_live_manifest',
  'wallet.package_deployed_runtime',
])

const SRI = /^sha256-[A-Za-z0-9+/]{43}=$/
const RELEASE = /^sha256-[A-Za-z0-9_-]{43}$/
const SHA256_BASE64URL = /^[A-Za-z0-9_-]{43}$/
const SHA256_HEX = /^[a-f0-9]{64}$/
const QUALIFICATION_RUN_ID = /^[A-Za-z0-9_-]{24,80}$/
const GAME_ID = /^[A-Za-z0-9][A-Za-z0-9_-]{5,127}$/
const VERSION = /^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$/
const COMMIT = /^[0-9a-f]{7,64}$/i
const MAINNET = Object.freeze({
  chain: 'algorand',
  network: 'mainnet',
  genesisId: 'mainnet-v1.0',
  genesisHashBase64: 'wGHE2Pwdvd7S12BL5FaOP20EGYesN73ktiC1qzkkit8=',
})
const WEB_PROVIDER_POLICY = Object.freeze({
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
const UNITY_WEBGL_V2_PROVIDER_POLICY = Object.freeze({
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
export const UNITY_WEBGL_RUNTIME_PROFILE_IDS = Object.freeze([
  'pera_lute',
  'pera_lute_txnlab_web3auth',
])
const UNITY_WEBGL_RUNTIME_PROFILE_PROVIDER_IDS = Object.freeze({
  pera_lute: Object.freeze(['pera', 'lute']),
  pera_lute_txnlab_web3auth: Object.freeze(['pera', 'lute', 'txnlab_web3auth']),
})
const unityWebGlFundingGuide = providerIds => Object.freeze({
  optional: true,
  accountKind: 'algorand_wallet',
  authProviders: providerIds,
  authOnlyEmailAddressExposed: false,
})
const UNITY_WEBGL_PROVIDER_POLICY = Object.freeze({
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
      providerIds: UNITY_WEBGL_RUNTIME_PROFILE_PROVIDER_IDS.pera_lute,
      requiredEconomicProviders: UNITY_WEBGL_RUNTIME_PROFILE_PROVIDER_IDS.pera_lute,
      authOnlyProvider: null,
      fundingGuide: unityWebGlFundingGuide(UNITY_WEBGL_RUNTIME_PROFILE_PROVIDER_IDS.pera_lute),
    }),
    Object.freeze({
      id: 'pera_lute_txnlab_web3auth',
      clientId: 'valid_public',
      providerIds: UNITY_WEBGL_RUNTIME_PROFILE_PROVIDER_IDS.pera_lute_txnlab_web3auth,
      requiredEconomicProviders: UNITY_WEBGL_RUNTIME_PROFILE_PROVIDER_IDS.pera_lute_txnlab_web3auth,
      authOnlyProvider: null,
      fundingGuide: unityWebGlFundingGuide(
        UNITY_WEBGL_RUNTIME_PROFILE_PROVIDER_IDS.pera_lute_txnlab_web3auth,
      ),
    }),
  ]),
  unlistedAdaptersAllowed: false,
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
const WEB_DEPENDENCIES = Object.freeze([
  Object.freeze({ package: 'algosdk', version: '3.6.0' }),
  Object.freeze({ package: '@perawallet/connect', version: '1.6.0' }),
  Object.freeze({ package: 'lute-connect', version: '2.0.1' }),
])
const UNITY_WEBGL_DEPENDENCIES = Object.freeze([
  Object.freeze({ package: 'algosdk', version: '3.6.0' }),
  Object.freeze({ package: '@txnlab/use-wallet', version: '5.0.0' }),
  Object.freeze({ package: '@txnlab/use-wallet-pera', version: '5.0.0' }),
  Object.freeze({ package: '@txnlab/use-wallet-lute', version: '5.0.0' }),
  Object.freeze({ package: '@txnlab/use-wallet-web3auth', version: '5.0.0' }),
])

const SPECIFICATIONS = Object.freeze({
  web: Object.freeze({
    key: 'webWalletPackage',
    schemaVersion: 'blockmaker-web-wallet-package/v1',
    packagePrefix: 'web-wallet-package',
    target: 'browser',
    clientKind: 'web',
    dependencyDelivery: 'consumer_install',
    providerPolicy: WEB_PROVIDER_POLICY,
    lockPath: 'public/vendor/blockmaker/blockmaker-wallet-package.lock.json',
    installationEvidencePath: 'public/vendor/blockmaker/blockmaker-wallet-package.installation.json',
    members: Object.freeze([
      Object.freeze({ file: 'blockmaker.js', installPath: 'public/vendor/blockmaker/blockmaker.js', role: 'browser_runtime' }),
      Object.freeze({ file: 'blockmaker.d.ts', installPath: 'public/vendor/blockmaker/blockmaker.d.ts', role: 'typescript_declarations' }),
    ]),
  }),
  unity_webgl: Object.freeze({
    key: 'unityWebGlWalletPackage',
    schemaVersion: 'blockmaker-unity-webgl-package/v3',
    legacySchemaVersions: Object.freeze(['blockmaker-unity-webgl-package/v2']),
    packagePrefix: 'unity-webgl-package',
    target: 'unity_webgl',
    clientKind: 'unity_webgl',
    dependencyDelivery: 'embedded',
    providerPolicy: UNITY_WEBGL_PROVIDER_POLICY,
    playerRequestOnce: UNITY_WEBGL_PLAYER_REQUEST_ONCE,
    storeSubmissionOnce: UNITY_WEBGL_STORE_SUBMISSION_ONCE,
    lockPath: 'Assets/Blockmaker/blockmaker-unity-webgl-package.lock.json',
    installationEvidencePath: 'Assets/Blockmaker/blockmaker-unity-webgl-package.installation.json',
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

export class WalletPackageContractError extends Error {
  constructor(message, code = 'WALLET_PACKAGE_INVALID', path = '$') {
    super(message)
    this.name = 'WalletPackageContractError'
    this.code = code
    this.path = path
  }
}

function fail(path, message, code = 'WALLET_PACKAGE_INVALID') {
  throw new WalletPackageContractError(`${path}: ${message}`, code, path)
}

function record(value, path) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) fail(path, 'must be an object.')
  return value
}

function exactKeys(value, keys, path) {
  const actual = Object.keys(value).sort()
  const expected = [...keys].sort()
  if (actual.length !== expected.length || actual.some((key, index) => key !== expected[index])) {
    fail(path, `must contain exactly: ${expected.join(', ')}.`)
  }
}

function exactArray(value, expected, path) {
  if (!Array.isArray(value) || value.length !== expected.length
    || value.some((item, index) => item !== expected[index])) {
    fail(path, `must equal [${expected.join(', ')}].`)
  }
  return [...value]
}

export function unityWebGlRuntimeProfileForProviderIds(providerIds) {
  if (Array.isArray(providerIds)) {
    for (const runtimeProfile of UNITY_WEBGL_RUNTIME_PROFILE_IDS) {
      const expected = UNITY_WEBGL_RUNTIME_PROFILE_PROVIDER_IDS[runtimeProfile]
      if (providerIds.length === expected.length
        && providerIds.every((providerId, index) => providerId === expected[index])) {
        return runtimeProfile
      }
    }
  }
  fail(
    '$.providerIds',
    'must equal the exact ordered Unity WebGL runtime profile [pera, lute] or [pera, lute, txnlab_web3auth].',
  )
}

export function unityWebGlProviderIdsForRuntimeProfile(runtimeProfile) {
  const providerIds = UNITY_WEBGL_RUNTIME_PROFILE_PROVIDER_IDS[runtimeProfile]
  if (!providerIds) {
    fail(
      '$.runtimeProfile',
      `must equal one of: ${UNITY_WEBGL_RUNTIME_PROFILE_IDS.join(', ')}.`,
    )
  }
  return [...providerIds]
}

export function walletPackageSupportsUnityWebGlRuntimeProfile(packageManifest, runtimeProfile) {
  if (!UNITY_WEBGL_RUNTIME_PROFILE_PROVIDER_IDS[runtimeProfile]
    || packageManifest?.clientKind !== 'unity_webgl') return false
  if (packageManifest.schemaVersion === 'blockmaker-unity-webgl-package/v2') {
    return runtimeProfile === 'pera_lute_txnlab_web3auth'
  }
  return packageManifest.schemaVersion === 'blockmaker-unity-webgl-package/v3'
    && packageManifest.providerPolicy?.mode === 'exact_runtime_profile'
    && packageManifest.providerPolicy.profiles?.some(profile => profile.id === runtimeProfile)
}

function canonicalString(value, path, maximum = 2_048) {
  if (typeof value !== 'string' || !value || value.trim() !== value || value.length > maximum)
    fail(path, `must be a non-empty canonical string of at most ${maximum} characters.`)
  return value
}

function safeInteger(value, path, { minimum = 0, maximum = 10_000_000 } = {}) {
  if (!Number.isSafeInteger(value) || value < minimum || value > maximum)
    fail(path, `must be a whole number from ${minimum} to ${maximum}.`)
  return value
}

function normalizedApiOrigin(value, path) {
  const candidate = canonicalString(value, path)
  let parsed
  try { parsed = new URL(candidate) } catch { fail(path, 'must be an absolute API origin.') }
  const loopback = ['localhost', '127.0.0.1', '[::1]', '::1'].includes(parsed.hostname.toLowerCase())
  if ((parsed.protocol !== 'https:' && !(parsed.protocol === 'http:' && loopback))
    || parsed.username || parsed.password || parsed.origin !== candidate) {
    fail(path, 'must be one canonical HTTPS origin (HTTP is allowed only on loopback).')
  }
  return candidate
}

function normalizedProductionOrigin(value, path) {
  const candidate = canonicalString(value, path)
  let parsed
  try { parsed = new URL(candidate) } catch { fail(path, 'must be an absolute production origin.') }
  const loopback = /(^127\.\d+\.\d+\.\d+$|(^|\.)localhost\.?$|^\[::1\]$)/i.test(parsed.hostname)
  if (parsed.protocol !== 'https:' || loopback || parsed.username || parsed.password || parsed.origin !== candidate)
    fail(path, 'must be one canonical non-loopback HTTPS production origin.')
  return candidate
}

function digestParts(integrity, path) {
  if (typeof integrity !== 'string' || !SRI.test(integrity)) fail(path, 'must be a canonical SHA-256 SRI value.')
  let digest
  try { digest = Buffer.from(integrity.slice(7), 'base64') } catch { fail(path, 'must contain a valid SHA-256 digest.') }
  if (digest.length !== 32) fail(path, 'must contain exactly one SHA-256 digest.')
  return {
    integrity,
    release: `sha256-${digest.toString('base64url')}`,
    sha256: digest.toString('base64url'),
  }
}

function normalizedLoadedUrl(value, origin, file, path) {
  const candidate = canonicalString(value, path)
  let parsed
  try { parsed = new URL(candidate) } catch { fail(path, 'must be an absolute URL.') }
  if (parsed.origin !== origin || parsed.username || parsed.password || parsed.search || parsed.hash
    || basename(parsed.pathname) !== file) {
    fail(path, `must be an exact same-origin deployed URL ending in ${file}.`)
  }
  return parsed.toString()
}

function normalizedRelativePath(value, expected, path) {
  const candidate = canonicalString(value, path, 300)
  const parts = candidate.split('/')
  if (candidate !== expected || candidate.startsWith('/') || candidate.startsWith('~')
    || candidate.includes('\\') || candidate.includes(':')
    || parts.some(part => !part || part === '.' || part === '..' || !/^[A-Za-z0-9._-]+$/.test(part))) {
    fail(path, `must equal the canonical path ${expected}.`)
  }
  return candidate
}

function memberByRole(packageManifest, role, path) {
  const member = packageManifest.members.find(candidate => candidate.role === role)
  if (!member) fail(path, `cannot be bound because the package has no ${role} member.`)
  return member
}

function loadedRuntimeMembersForPackage(specification, packageManifest) {
  const browserRuntime = packageManifest.members.find(member => member.role === 'browser_runtime')
  return specification.clientKind === 'unity_webgl'
    ? [
        packageManifest.members.find(member => member.role === 'wallet_host'),
        browserRuntime,
        packageManifest.members.find(member => member.role === 'wallet_runtime'),
      ]
    : [browserRuntime]
}

function parsePackageExecution(value, specification, packageManifest, path) {
  const input = record(value, path)
  const browserRuntime = memberByRole(packageManifest, 'browser_runtime', path)
  if (specification.clientKind === 'web') {
    exactKeys(input, ['schemaVersion', 'executionId', 'operationKind', 'phase', 'browserRuntimeIntegrity'], path)
    if (input.schemaVersion !== 'blockmaker-web-wallet-package-execution/v1'
      || typeof input.executionId !== 'string' || !/^wpexec_[A-Za-z0-9_-]{43}$/.test(input.executionId)
      || input.operationKind !== 'initialize' || input.phase !== 'ready'
      || input.browserRuntimeIntegrity !== browserRuntime.integrity) {
      fail(path, 'must be the exact Web initialize/ready execution result bound to the browser runtime hash.')
    }
    return {
      schemaVersion: 'blockmaker-web-wallet-package-execution/v1',
      executionId: input.executionId,
      operationKind: 'initialize',
      phase: 'ready',
      browserRuntimeIntegrity: browserRuntime.integrity,
    }
  }
  const host = memberByRole(packageManifest, 'wallet_host', path)
  const unityClient = memberByRole(packageManifest, 'unity_client', path)
  const facade = memberByRole(packageManifest, 'unity_wallet_facade', path)
  const bridge = memberByRole(packageManifest, 'unity_webgl_bridge', path)
  if (packageManifest.schemaVersion === 'blockmaker-unity-webgl-package/v3') {
    exactKeys(input, [
      'schemaVersion', 'executionId', 'envelopeSchemaVersion', 'callbackMethod', 'lifecycleId',
      'operationId', 'operationKind', 'phase', 'runtimeProfile', 'hostIntegrity',
      'browserRuntimeIntegrity', 'unityClientIntegrity', 'unityFacadeIntegrity',
      'webGlBridgeIntegrity',
    ], path)
    if (input.schemaVersion !== 'blockmaker-unity-webgl-wallet-package-execution/v2'
      || input.envelopeSchemaVersion !== 'blockmaker-unity-webgl-wallet-package/v2'
      || input.callbackMethod !== 'OnBlockmakerWalletPackageSuccess'
      || typeof input.lifecycleId !== 'string' || !/^[a-f0-9]{32}$/.test(input.lifecycleId)
      || input.executionId !== input.lifecycleId
      || input.operationId !== 0 || input.operationKind !== 'initialize' || input.phase !== 'ready'
      || !walletPackageSupportsUnityWebGlRuntimeProfile(packageManifest, input.runtimeProfile)
      || input.hostIntegrity !== host.integrity
      || input.browserRuntimeIntegrity !== browserRuntime.integrity
      || input.unityClientIntegrity !== unityClient.integrity
      || input.unityFacadeIntegrity !== facade.integrity
      || input.webGlBridgeIntegrity !== bridge.integrity) {
      fail(path, 'must be the exact v2 .jslib-to-C# initialize/ready envelope roundtrip bound to one package runtime profile and all runtime boundary hashes.')
    }
    return {
      schemaVersion: 'blockmaker-unity-webgl-wallet-package-execution/v2',
      executionId: input.lifecycleId,
      envelopeSchemaVersion: 'blockmaker-unity-webgl-wallet-package/v2',
      callbackMethod: 'OnBlockmakerWalletPackageSuccess',
      lifecycleId: input.lifecycleId,
      operationId: 0,
      operationKind: 'initialize',
      phase: 'ready',
      runtimeProfile: input.runtimeProfile,
      hostIntegrity: host.integrity,
      browserRuntimeIntegrity: browserRuntime.integrity,
      unityClientIntegrity: unityClient.integrity,
      unityFacadeIntegrity: facade.integrity,
      webGlBridgeIntegrity: bridge.integrity,
    }
  }
  exactKeys(input, [
    'schemaVersion', 'executionId', 'envelopeSchemaVersion', 'callbackMethod', 'lifecycleId',
    'operationId', 'operationKind', 'phase', 'hostIntegrity',
    'browserRuntimeIntegrity', 'unityClientIntegrity', 'unityFacadeIntegrity',
    'webGlBridgeIntegrity',
  ], path)
  if (input.schemaVersion !== 'blockmaker-unity-webgl-wallet-package-execution/v1'
    || input.envelopeSchemaVersion !== 'blockmaker-unity-webgl-wallet-package/v1'
    || input.callbackMethod !== 'OnBlockmakerWalletPackageSuccess'
    || typeof input.lifecycleId !== 'string' || !/^[a-f0-9]{32}$/.test(input.lifecycleId)
    || input.executionId !== input.lifecycleId
    || input.operationId !== 0 || input.operationKind !== 'initialize' || input.phase !== 'ready'
    || input.hostIntegrity !== host.integrity
    || input.browserRuntimeIntegrity !== browserRuntime.integrity
    || input.unityClientIntegrity !== unityClient.integrity
    || input.unityFacadeIntegrity !== facade.integrity
    || input.webGlBridgeIntegrity !== bridge.integrity) {
    fail(path, 'must be the exact v1 .jslib-to-C# initialize/ready envelope roundtrip bound to all runtime boundary hashes.')
  }
  return {
    schemaVersion: 'blockmaker-unity-webgl-wallet-package-execution/v1',
    executionId: input.lifecycleId,
    envelopeSchemaVersion: 'blockmaker-unity-webgl-wallet-package/v1',
    callbackMethod: 'OnBlockmakerWalletPackageSuccess',
    lifecycleId: input.lifecycleId,
    operationId: 0,
    operationKind: 'initialize',
    phase: 'ready',
    hostIntegrity: host.integrity,
    browserRuntimeIntegrity: browserRuntime.integrity,
    unityClientIntegrity: unityClient.integrity,
    unityFacadeIntegrity: facade.integrity,
    webGlBridgeIntegrity: bridge.integrity,
  }
}

export function walletPackageSpecification(target) {
  const key = target === 'browser' ? 'web' : target
  const specification = SPECIFICATIONS[key]
  if (!specification) {
    throw new WalletPackageContractError(
      'Wallet packages support only web and unity_webgl. Native Unity is intentionally unsupported.',
      'WALLET_PACKAGE_TARGET_UNSUPPORTED',
      '$.target',
    )
  }
  return specification
}

function parseNetwork(value, path) {
  const input = record(value, path)
  exactKeys(input, Object.keys(MAINNET), path)
  for (const [key, expected] of Object.entries(MAINNET)) {
    if (input[key] !== expected) fail(`${path}.${key}`, `must equal ${JSON.stringify(expected)}.`)
  }
  return { ...MAINNET }
}

function parseExactProviderPolicy(value, expectedPolicy, specification, path) {
  const input = record(value, path)
  exactKeys(input, ['mode', 'providerIds', 'requiredEconomicProviders', 'authOnlyProvider', 'unlistedAdaptersAllowed', 'fundingGuide'], path)
  if (input.mode !== expectedPolicy.mode) fail(`${path}.mode`, 'must be exact.')
  const providerIds = exactArray(input.providerIds, expectedPolicy.providerIds, `${path}.providerIds`)
  const requiredEconomicProviders = exactArray(
    input.requiredEconomicProviders,
    expectedPolicy.requiredEconomicProviders,
    `${path}.requiredEconomicProviders`,
  )
  if (input.authOnlyProvider !== expectedPolicy.authOnlyProvider) {
    fail(
      `${path}.authOnlyProvider`,
      specification.clientKind === 'unity_webgl'
        ? 'must be null because every Unity provider is an economic Algorand signer.'
        : 'must be web3auth_avm_email.',
    )
  }
  if (input.unlistedAdaptersAllowed !== false)
    fail(`${path}.unlistedAdaptersAllowed`, 'must be false.')
  const funding = record(input.fundingGuide, `${path}.fundingGuide`)
  exactKeys(funding, ['optional', 'accountKind', 'authProviders', 'authOnlyEmailAddressExposed'], `${path}.fundingGuide`)
  if (funding.optional !== true || funding.accountKind !== 'algorand_wallet'
    || funding.authOnlyEmailAddressExposed !== false) {
    fail(
      `${path}.fundingGuide`,
      specification.clientKind === 'unity_webgl'
        ? 'does not match the reviewed optional all-provider Algorand funding-guide contract.'
        : 'does not match the reviewed optional Pera/Lute funding-guide contract.',
    )
  }
  const authProviders = exactArray(funding.authProviders, expectedPolicy.fundingGuide.authProviders, `${path}.fundingGuide.authProviders`)
  return {
    mode: 'exact',
    providerIds,
    requiredEconomicProviders,
    authOnlyProvider: expectedPolicy.authOnlyProvider,
    unlistedAdaptersAllowed: false,
    fundingGuide: {
      optional: true,
      accountKind: 'algorand_wallet',
      authProviders,
      authOnlyEmailAddressExposed: false,
    },
  }
}

function parseUnityWebGlRuntimeProfile(value, expected, path) {
  const input = record(value, path)
  exactKeys(input, [
    'id', 'clientId', 'providerIds', 'requiredEconomicProviders',
    'authOnlyProvider', 'fundingGuide',
  ], path)
  if (input.id !== expected.id) fail(`${path}.id`, `must equal ${expected.id}.`)
  if (input.clientId !== expected.clientId)
    fail(`${path}.clientId`, `must equal ${expected.clientId}.`)
  const providerIds = exactArray(input.providerIds, expected.providerIds, `${path}.providerIds`)
  const requiredEconomicProviders = exactArray(
    input.requiredEconomicProviders,
    expected.requiredEconomicProviders,
    `${path}.requiredEconomicProviders`,
  )
  if (input.authOnlyProvider !== null)
    fail(`${path}.authOnlyProvider`, 'must be null because every enabled Unity provider is an economic Algorand signer.')
  const funding = record(input.fundingGuide, `${path}.fundingGuide`)
  exactKeys(funding, ['optional', 'accountKind', 'authProviders', 'authOnlyEmailAddressExposed'], `${path}.fundingGuide`)
  if (funding.optional !== true || funding.accountKind !== 'algorand_wallet'
    || funding.authOnlyEmailAddressExposed !== false) {
    fail(`${path}.fundingGuide`, 'does not match the reviewed optional all-provider Algorand funding-guide contract.')
  }
  const authProviders = exactArray(
    funding.authProviders,
    expected.fundingGuide.authProviders,
    `${path}.fundingGuide.authProviders`,
  )
  return {
    id: expected.id,
    clientId: expected.clientId,
    providerIds,
    requiredEconomicProviders,
    authOnlyProvider: null,
    fundingGuide: {
      optional: true,
      accountKind: 'algorand_wallet',
      authProviders,
      authOnlyEmailAddressExposed: false,
    },
  }
}

function parseUnityWebGlV3ProviderPolicy(value, path) {
  const input = record(value, path)
  exactKeys(input, ['mode', 'selection', 'profiles', 'unlistedAdaptersAllowed'], path)
  if (input.mode !== 'exact_runtime_profile')
    fail(`${path}.mode`, 'must equal exact_runtime_profile.')
  const selection = record(input.selection, `${path}.selection`)
  exactKeys(selection, ['discriminator', 'absent', 'valid', 'invalid'], `${path}.selection`)
  for (const [key, expected] of Object.entries(UNITY_WEBGL_PROVIDER_POLICY.selection)) {
    if (selection[key] !== expected)
      fail(`${path}.selection.${key}`, `must equal ${expected}.`)
  }
  if (!Array.isArray(input.profiles)
    || input.profiles.length !== UNITY_WEBGL_PROVIDER_POLICY.profiles.length) {
    fail(`${path}.profiles`, 'must contain exactly the two reviewed runtime profiles in canonical order.')
  }
  const profiles = input.profiles.map((profile, index) => parseUnityWebGlRuntimeProfile(
    profile,
    UNITY_WEBGL_PROVIDER_POLICY.profiles[index],
    `${path}.profiles[${index}]`,
  ))
  if (input.unlistedAdaptersAllowed !== false)
    fail(`${path}.unlistedAdaptersAllowed`, 'must be false.')
  return {
    mode: 'exact_runtime_profile',
    selection: {
      discriminator: 'txnlab_web3auth_public_client_id',
      absent: 'pera_lute',
      valid: 'pera_lute_txnlab_web3auth',
      invalid: 'reject',
    },
    profiles,
    unlistedAdaptersAllowed: false,
  }
}

function parseProviderPolicy(value, specification, schemaVersion, path) {
  if (specification.clientKind === 'unity_webgl'
    && schemaVersion === 'blockmaker-unity-webgl-package/v3') {
    return parseUnityWebGlV3ProviderPolicy(value, path)
  }
  const expectedPolicy = specification.clientKind === 'unity_webgl'
    ? UNITY_WEBGL_V2_PROVIDER_POLICY
    : WEB_PROVIDER_POLICY
  return parseExactProviderPolicy(value, expectedPolicy, specification, path)
}

function parseDependencies(value, specification, path) {
  const expectedDependencies = specification.clientKind === 'unity_webgl'
    ? UNITY_WEBGL_DEPENDENCIES
    : WEB_DEPENDENCIES
  if (!Array.isArray(value) || value.length !== expectedDependencies.length)
    fail(path, `must contain exactly ${expectedDependencies.length} reviewed dependencies.`)
  return value.map((item, index) => {
    const dependencyPath = `${path}[${index}]`
    const input = record(item, dependencyPath)
    exactKeys(input, ['package', 'version', 'delivery'], dependencyPath)
    const expected = expectedDependencies[index]
    if (input.package !== expected.package || input.version !== expected.version
      || input.delivery !== specification.dependencyDelivery) {
      fail(dependencyPath, 'does not match the exact reviewed package/version/delivery tuple.')
    }
    return { package: expected.package, version: expected.version, delivery: specification.dependencyDelivery }
  })
}

function parseMember(value, expected, apiOrigin, path) {
  const input = record(value, path)
  exactKeys(input, [
    'file', 'installPath', 'role', 'url', 'release', 'sha256', 'integrity',
    'rawBytes', 'gzipBytes', 'mediaType',
  ], path)
  if (input.file !== expected.file || input.role !== expected.role)
    fail(path, `must describe the exact ${expected.file} / ${expected.role} member.`)
  const installPath = normalizedRelativePath(input.installPath, expected.installPath, `${path}.installPath`)
  const digest = digestParts(input.integrity, `${path}.integrity`)
  if (!RELEASE.test(input.release) || input.release !== digest.release)
    fail(`${path}.release`, 'must be the release encoded by integrity.')
  if (!SHA256_BASE64URL.test(input.sha256) || input.sha256 !== digest.sha256)
    fail(`${path}.sha256`, 'must be the base64url digest encoded by integrity.')
  const expectedUrl = `${apiOrigin}/sdk/${input.release}/${expected.file}`
  let url
  try { url = new URL(input.url) } catch { fail(`${path}.url`, 'must be an absolute content-addressed URL.') }
  if (url.toString() !== expectedUrl || url.origin !== apiOrigin || url.username || url.password
    || url.search || url.hash) {
    fail(`${path}.url`, `must equal ${expectedUrl}.`)
  }
  const rawBytes = safeInteger(input.rawBytes, `${path}.rawBytes`, { minimum: 1 })
  const gzipBytes = safeInteger(input.gzipBytes, `${path}.gzipBytes`, { minimum: 1 })
  const expectedMediaType = expected.file.endsWith('.js') || expected.file.endsWith('.mjs')
    ? 'text/javascript; charset=utf-8'
    : 'text/plain; charset=utf-8'
  if (input.mediaType !== expectedMediaType)
    fail(`${path}.mediaType`, `must equal ${JSON.stringify(expectedMediaType)}.`)
  return {
    file: expected.file,
    installPath,
    role: expected.role,
    url: expectedUrl,
    release: input.release,
    sha256: input.sha256,
    integrity: input.integrity,
    rawBytes,
    gzipBytes,
    mediaType: expectedMediaType,
  }
}

export function walletPackageIdentity(manifest, target = manifest?.clientKind) {
  const specification = walletPackageSpecification(target)
  return `${specification.packagePrefix}-sha256-${createHash('sha256').update(JSON.stringify({
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
  })).digest('base64url')}`
}

export function walletPackageMemberHashClosure(packageManifest) {
  if (!packageManifest || !Array.isArray(packageManifest.members))
    fail('$.members', 'must be a parsed wallet-package member array.')
  return packageManifest.members.map(member => Object.freeze({
    file: member.file,
    integrity: member.integrity,
    rawBytes: member.rawBytes,
  }))
}

export function parseWalletPackageManifest(value, { apiOrigin, target } = {}) {
  const specification = walletPackageSpecification(target)
  const origin = normalizedApiOrigin(apiOrigin, '$apiOrigin')
  const input = record(value, '$')
  exactKeys(input, [
    'schemaVersion', 'packageId', 'releaseVersion', 'target', 'clientKind', 'complete',
    'activation', 'network', 'providerPolicy',
    ...(specification.playerRequestOnce ? ['playerRequestOnce'] : []),
    ...(specification.storeSubmissionOnce ? ['storeSubmissionOnce'] : []),
    'dependencies', 'members', 'provenance',
  ], '$')
  const acceptedSchemaVersions = [
    specification.schemaVersion,
    ...(specification.legacySchemaVersions ?? []),
  ]
  if (!acceptedSchemaVersions.includes(input.schemaVersion))
    fail('$.schemaVersion', `must equal one of: ${acceptedSchemaVersions.join(', ')}.`)
  const schemaVersion = input.schemaVersion
  if (input.target !== specification.target || input.clientKind !== specification.clientKind)
    fail('$.target', `must be the exact ${specification.target} / ${specification.clientKind} target.`)
  if (input.complete !== true) fail('$.complete', 'must be true.')
  if (input.activation !== 'requires_game_qualification')
    fail('$.activation', 'must remain requires_game_qualification.')
  const releaseVersion = canonicalString(input.releaseVersion, '$.releaseVersion', 80)
  if (!VERSION.test(releaseVersion)) fail('$.releaseVersion', 'must be a canonical semantic version.')
  const network = parseNetwork(input.network, '$.network')
  const providerPolicy = parseProviderPolicy(
    input.providerPolicy,
    specification,
    schemaVersion,
    '$.providerPolicy',
  )
  let playerRequestOnce
  if (specification.playerRequestOnce) {
    const request = record(input.playerRequestOnce, '$.playerRequestOnce')
    exactKeys(request, Object.keys(specification.playerRequestOnce), '$.playerRequestOnce')
    if (JSON.stringify(request) !== JSON.stringify(specification.playerRequestOnce))
      fail('$.playerRequestOnce', 'must match the exact one-transmission Unity player-request contract.')
    playerRequestOnce = {
      ...specification.playerRequestOnce,
      outcomeMetadata: [...specification.playerRequestOnce.outcomeMetadata],
    }
  }
  let storeSubmissionOnce
  if (specification.storeSubmissionOnce) {
    const request = record(input.storeSubmissionOnce, '$.storeSubmissionOnce')
    exactKeys(request, Object.keys(specification.storeSubmissionOnce), '$.storeSubmissionOnce')
    if (JSON.stringify(request) !== JSON.stringify(specification.storeSubmissionOnce))
      fail('$.storeSubmissionOnce', 'must match the exact typed one-transmission Unity Store contract.')
    storeSubmissionOnce = {
      ...specification.storeSubmissionOnce,
      methods: [...specification.storeSubmissionOnce.methods],
      validation: { ...specification.storeSubmissionOnce.validation },
      outcomeMetadata: [...specification.storeSubmissionOnce.outcomeMetadata],
    }
  }
  const dependencies = parseDependencies(input.dependencies, specification, '$.dependencies')
  if (!Array.isArray(input.members) || (input.members.length !== specification.members.length
    && !(specification.clientKind === 'unity_webgl' && input.members.length === 8)))
    fail('$.members', `must contain exactly ${specification.members.length} canonical members.`)
  const members = input.members.map((member, index) => parseMember(
    member,
    specification.members[index],
    origin,
    `$.members[${index}]`,
  ))
  const provenanceInput = record(input.provenance, '$.provenance')
  exactKeys(provenanceInput, ['sourceCommit', 'contentAddressing', 'allMembersVerifiedBeforePublication'], '$.provenance')
  if (provenanceInput.sourceCommit !== null
    && (typeof provenanceInput.sourceCommit !== 'string' || !COMMIT.test(provenanceInput.sourceCommit))) {
    fail('$.provenance.sourceCommit', 'must be null or a hexadecimal source commit.')
  }
  if (provenanceInput.contentAddressing !== 'sha256'
    || provenanceInput.allMembersVerifiedBeforePublication !== true) {
    fail('$.provenance', 'must assert complete SHA-256 publication provenance.')
  }
  const manifest = {
    schemaVersion,
    packageId: canonicalString(input.packageId, '$.packageId', 100),
    releaseVersion,
    target: specification.target,
    clientKind: specification.clientKind,
    complete: true,
    activation: 'requires_game_qualification',
    network,
    providerPolicy,
    ...(playerRequestOnce ? { playerRequestOnce } : {}),
    ...(storeSubmissionOnce ? { storeSubmissionOnce } : {}),
    dependencies,
    members,
    provenance: {
      sourceCommit: provenanceInput.sourceCommit,
      contentAddressing: 'sha256',
      allMembersVerifiedBeforePublication: true,
    },
  }
  const expectedPackageId = walletPackageIdentity(manifest, specification.clientKind)
  if (manifest.packageId !== expectedPackageId)
    fail('$.packageId', 'does not bind the exact package member/policy/dependency closure.')
  return manifest
}

export function createWalletPackageLock({ gameId, apiOrigin, packageManifest }) {
  if (typeof gameId !== 'string' || !GAME_ID.test(gameId)) fail('$.gameId', 'must be a public Blockmaker game ID.')
  return {
    schemaVersion: WALLET_PACKAGE_LOCK_SCHEMA,
    gameId,
    apiOrigin: normalizedApiOrigin(apiOrigin, '$.apiOrigin'),
    package: packageManifest,
  }
}

export function parseWalletPackageLock(value, { gameId, apiOrigin, target } = {}) {
  const input = record(value, '$')
  exactKeys(input, ['schemaVersion', 'gameId', 'apiOrigin', 'package'], '$')
  if (input.schemaVersion !== WALLET_PACKAGE_LOCK_SCHEMA)
    fail('$.schemaVersion', `must equal ${WALLET_PACKAGE_LOCK_SCHEMA}.`)
  if (typeof input.gameId !== 'string' || !GAME_ID.test(input.gameId))
    fail('$.gameId', 'must be a public Blockmaker game ID.')
  if (gameId && input.gameId !== gameId) fail('$.gameId', 'belongs to another game.')
  const origin = normalizedApiOrigin(input.apiOrigin, '$.apiOrigin')
  if (apiOrigin && origin !== apiOrigin) fail('$.apiOrigin', 'belongs to another Blockmaker API origin.')
  const packageManifest = parseWalletPackageManifest(input.package, { apiOrigin: origin, target })
  return { schemaVersion: WALLET_PACKAGE_LOCK_SCHEMA, gameId: input.gameId, apiOrigin: origin, package: packageManifest }
}

export function canonicalJsonBytes(value) {
  return new TextEncoder().encode(`${JSON.stringify(value, null, 2)}\n`)
}

export function integrityForWalletPackageBytes(bytes) {
  return `sha256-${createHash('sha256').update(bytes).digest('base64')}`
}

export function createWalletPackageInstallationEvidence({ gameId, apiOrigin, packageManifest, packageLockIntegrity }) {
  const specification = walletPackageSpecification(packageManifest.clientKind)
  digestParts(packageLockIntegrity, '$.packageLockIntegrity')
  return {
    schemaVersion: WALLET_PACKAGE_INSTALLATION_SCHEMA,
    gameId,
    apiOrigin,
    target: specification.target,
    clientKind: specification.clientKind,
    packageId: packageManifest.packageId,
    packageLockPath: specification.lockPath,
    packageLockIntegrity,
    members: packageManifest.members.map(member => ({
      file: member.file,
      installPath: member.installPath,
      integrity: member.integrity,
      rawBytes: member.rawBytes,
    })),
  }
}

export function parseWalletPackageInstallationEvidence(value, { gameId, apiOrigin, packageManifest, packageLockIntegrity } = {}) {
  const specification = walletPackageSpecification(packageManifest?.clientKind)
  const input = record(value, '$')
  exactKeys(input, [
    'schemaVersion', 'gameId', 'apiOrigin', 'target', 'clientKind', 'packageId',
    'packageLockPath', 'packageLockIntegrity', 'members',
  ], '$')
  if (input.schemaVersion !== WALLET_PACKAGE_INSTALLATION_SCHEMA)
    fail('$.schemaVersion', `must equal ${WALLET_PACKAGE_INSTALLATION_SCHEMA}.`)
  if (input.gameId !== gameId || input.apiOrigin !== apiOrigin
    || input.target !== specification.target || input.clientKind !== specification.clientKind
    || input.packageId !== packageManifest.packageId) {
    fail('$', 'belongs to another game, API, target or package.')
  }
  normalizedRelativePath(input.packageLockPath, specification.lockPath, '$.packageLockPath')
  digestParts(input.packageLockIntegrity, '$.packageLockIntegrity')
  if (input.packageLockIntegrity !== packageLockIntegrity)
    fail('$.packageLockIntegrity', 'does not match the exact local package-lock bytes.')
  if (!Array.isArray(input.members) || input.members.length !== packageManifest.members.length)
    fail('$.members', 'must bind the complete package member closure.')
  const members = input.members.map((member, index) => {
    const memberPath = `$.members[${index}]`
    const memberInput = record(member, memberPath)
    exactKeys(memberInput, ['file', 'installPath', 'integrity', 'rawBytes'], memberPath)
    const expected = packageManifest.members[index]
    if (memberInput.file !== expected.file || memberInput.installPath !== expected.installPath
      || memberInput.integrity !== expected.integrity || memberInput.rawBytes !== expected.rawBytes) {
      fail(memberPath, 'does not match the package member at this position.')
    }
    return { file: expected.file, installPath: expected.installPath, integrity: expected.integrity, rawBytes: expected.rawBytes }
  })
  return {
    schemaVersion: WALLET_PACKAGE_INSTALLATION_SCHEMA,
    gameId,
    apiOrigin,
    target: specification.target,
    clientKind: specification.clientKind,
    packageId: packageManifest.packageId,
    packageLockPath: specification.lockPath,
    packageLockIntegrity,
    members,
  }
}

export function parseDeployedWalletPackageEvidence(value, {
  gameId,
  apiOrigin,
  origin,
  packageManifest,
  packageLockIntegrity,
} = {}) {
  const specification = walletPackageSpecification(packageManifest?.clientKind)
  const input = record(value, '$')
  exactKeys(input, [
    'schemaVersion', 'gameId', 'apiOrigin', 'origin', 'target', 'clientKind',
    'packageId', 'packageLockIntegrity', 'observationKind', 'complete',
    'members', 'actuallyLoadedRuntimeMembers', 'actuallyLoadedBrowserRuntime', 'execution',
  ], '$')
  if (input.schemaVersion !== WALLET_PACKAGE_DEPLOYED_SCHEMA)
    fail('$.schemaVersion', `must equal ${WALLET_PACKAGE_DEPLOYED_SCHEMA}.`)
  const deployedOrigin = normalizedProductionOrigin(input.origin, '$.origin')
  if (input.gameId !== gameId || input.apiOrigin !== apiOrigin || deployedOrigin !== origin
    || input.target !== specification.target || input.clientKind !== specification.clientKind
    || input.packageId !== packageManifest.packageId
    || input.packageLockIntegrity !== packageLockIntegrity) {
    fail('$', 'belongs to another game, API, origin, target, package or local lock.')
  }
  if (input.observationKind !== 'runtime_loaded' || input.complete !== true)
    fail('$', 'must be a complete runtime_loaded observation.')
  if (!Array.isArray(input.members) || input.members.length !== packageManifest.members.length) {
    fail('$.members', 'must contain the complete ordered package member hash closure.')
  }
  const members = input.members.map((entry, index) => {
    const entryPath = `$.members[${index}]`
    const observed = record(entry, entryPath)
    exactKeys(observed, ['file', 'integrity', 'rawBytes'], entryPath)
    const expected = packageManifest.members[index]
    if (observed.file !== expected.file || observed.integrity !== expected.integrity
      || observed.rawBytes !== expected.rawBytes) {
      fail(entryPath, 'does not match the package member hash and size at this position.')
    }
    return {
      file: expected.file,
      integrity: expected.integrity,
      rawBytes: expected.rawBytes,
    }
  })
  const runtime = packageManifest.members.find(member => member.role === 'browser_runtime')
  if (!runtime) fail('$.actuallyLoadedBrowserRuntime', 'cannot be bound because the package has no browser_runtime member.')
  const expectedLoadedRuntimeMembers = loadedRuntimeMembersForPackage(specification, packageManifest)
  if (expectedLoadedRuntimeMembers.some(member => !member))
    fail('$.actuallyLoadedRuntimeMembers', 'cannot be bound because a required runtime member is absent.')
  if (!Array.isArray(input.actuallyLoadedRuntimeMembers)
    || input.actuallyLoadedRuntimeMembers.length !== expectedLoadedRuntimeMembers.length) {
    fail(
      '$.actuallyLoadedRuntimeMembers',
      `must contain exactly ${expectedLoadedRuntimeMembers.map(member => member.file).join(', ')} in runtime-load order.`,
    )
  }
  const actuallyLoadedRuntimeMembers = input.actuallyLoadedRuntimeMembers.map((entry, index) => {
    const entryPath = `$.actuallyLoadedRuntimeMembers[${index}]`
    const loaded = record(entry, entryPath)
    exactKeys(loaded, ['file', 'url', 'integrity'], entryPath)
    const expected = expectedLoadedRuntimeMembers[index]
    if (loaded.file !== expected.file || loaded.integrity !== expected.integrity)
      fail(entryPath, 'does not match the exact target-dependent runtime-load member at this position.')
    return {
      file: expected.file,
      url: normalizedLoadedUrl(loaded.url, deployedOrigin, expected.file, `${entryPath}.url`),
      integrity: expected.integrity,
    }
  })
  const loadedInput = record(input.actuallyLoadedBrowserRuntime, '$.actuallyLoadedBrowserRuntime')
  exactKeys(loadedInput, ['file', 'url', 'integrity'], '$.actuallyLoadedBrowserRuntime')
  if (loadedInput.file !== runtime.file || loadedInput.integrity !== runtime.integrity)
    fail('$.actuallyLoadedBrowserRuntime', 'does not match the exact browser_runtime member.')
  const actuallyLoadedBrowserRuntime = {
    file: runtime.file,
    url: normalizedLoadedUrl(
      loadedInput.url,
      deployedOrigin,
      runtime.file,
      '$.actuallyLoadedBrowserRuntime.url',
    ),
    integrity: runtime.integrity,
  }
  const runtimeClosureBrowserMember = actuallyLoadedRuntimeMembers.find(member => member.file === runtime.file)
  if (!runtimeClosureBrowserMember
    || JSON.stringify(runtimeClosureBrowserMember) !== JSON.stringify(actuallyLoadedBrowserRuntime)) {
    fail('$.actuallyLoadedBrowserRuntime', 'must exactly repeat the browser_runtime entry from actuallyLoadedRuntimeMembers.')
  }
  if (specification.clientKind === 'unity_webgl') {
    const loadedHost = actuallyLoadedRuntimeMembers[0]
    const hostDirectory = new URL(loadedHost.url).pathname.replace(/[^/]+$/, '')
    if (actuallyLoadedRuntimeMembers.some(member =>
      new URL(member.url).pathname.replace(/[^/]+$/, '') !== hostDirectory)) {
      fail(
        '$.actuallyLoadedRuntimeMembers',
        'must place the Unity wallet host, blockmaker.js and TxnLab wallet runtime in the same deployed directory.',
      )
    }
  }
  const execution = parsePackageExecution(input.execution, specification, packageManifest, '$.execution')
  return {
    schemaVersion: WALLET_PACKAGE_DEPLOYED_SCHEMA,
    gameId,
    apiOrigin,
    origin: deployedOrigin,
    target: specification.target,
    clientKind: specification.clientKind,
    packageId: packageManifest.packageId,
    packageLockIntegrity,
    observationKind: 'runtime_loaded',
    complete: true,
    members,
    actuallyLoadedRuntimeMembers,
    actuallyLoadedBrowserRuntime,
    execution,
  }
}

export function parseWalletPackageDoctorAttestation(value) {
  const input = record(value, '$')
  exactKeys(input, [
    'schemaVersion', 'authority', 'qualificationRunId', 'gameId',
    'gameOriginSha256', 'apiOriginSha256', 'target', 'clientKind',
    'packageId', 'packageLockIntegrity', 'memberClosureSha256',
    'canonicalEvidenceSha256', 'claimedRuntimeClosureSha256', 'executionId',
    'platformReleaseFingerprint', 'checks', 'observedAt',
  ], '$')
  if (input.schemaVersion !== WALLET_PACKAGE_DOCTOR_ATTESTATION_SCHEMA)
    fail('$.schemaVersion', `must equal ${WALLET_PACKAGE_DOCTOR_ATTESTATION_SCHEMA}.`)
  if (input.authority !== 'operator') fail('$.authority', 'must equal operator.')
  const qualificationRunId = canonicalString(input.qualificationRunId, '$.qualificationRunId', 80)
  if (!QUALIFICATION_RUN_ID.test(qualificationRunId))
    fail('$.qualificationRunId', 'must be one canonical qualification run ID.')
  const gameId = canonicalString(input.gameId, '$.gameId', 128)
  if (!GAME_ID.test(gameId)) fail('$.gameId', 'must be one canonical public game ID.')
  const clientKind = input.clientKind
  if (clientKind !== 'web' && clientKind !== 'unity_webgl')
    fail('$.clientKind', 'must equal web or unity_webgl.')
  const target = input.target
  if ((clientKind === 'web' && target !== 'browser')
    || (clientKind === 'unity_webgl' && target !== 'unity_webgl')) {
    fail('$.target', 'does not match the exact client kind.')
  }
  const exactHash = (candidate, path) => {
    const hash = canonicalString(candidate, path, 64)
    if (!SHA256_HEX.test(hash)) fail(path, 'must be one lowercase SHA-256 digest.')
    return hash
  }
  const packageId = canonicalString(input.packageId, '$.packageId', 100)
  const expectedPackagePrefix = clientKind === 'web'
    ? 'web-wallet-package-sha256-'
    : 'unity-webgl-package-sha256-'
  if (!packageId.startsWith(expectedPackagePrefix)
    || !SHA256_BASE64URL.test(packageId.slice(expectedPackagePrefix.length))) {
    fail('$.packageId', 'must be the exact target-dependent content-addressed package ID.')
  }
  const packageLockIntegrity = canonicalString(
    input.packageLockIntegrity, '$.packageLockIntegrity', 51,
  )
  digestParts(packageLockIntegrity, '$.packageLockIntegrity')
  const executionId = canonicalString(input.executionId, '$.executionId', 50)
  if ((clientKind === 'web' && !/^wpexec_[A-Za-z0-9_-]{43}$/.test(executionId))
    || (clientKind === 'unity_webgl' && !/^[a-f0-9]{32}$/.test(executionId))) {
    fail('$.executionId', 'must be the exact target-dependent package execution ID.')
  }
  if (!Array.isArray(input.checks)
    || input.checks.length !== WALLET_PACKAGE_DOCTOR_REQUIRED_CHECKS.length) {
    fail('$.checks', 'must contain the exact required Doctor pass set.')
  }
  const checks = input.checks.map((candidate, index) => {
    const path = `$.checks[${index}]`
    const observed = record(candidate, path)
    exactKeys(observed, ['id', 'status'], path)
    if (observed.id !== WALLET_PACKAGE_DOCTOR_REQUIRED_CHECKS[index]
      || observed.status !== 'pass') {
      fail(path, 'must be the exact required Doctor check with pass status.')
    }
    return { id: WALLET_PACKAGE_DOCTOR_REQUIRED_CHECKS[index], status: 'pass' }
  })
  return {
    schemaVersion: WALLET_PACKAGE_DOCTOR_ATTESTATION_SCHEMA,
    authority: 'operator',
    qualificationRunId,
    gameId,
    gameOriginSha256: exactHash(input.gameOriginSha256, '$.gameOriginSha256'),
    apiOriginSha256: exactHash(input.apiOriginSha256, '$.apiOriginSha256'),
    target,
    clientKind,
    packageId,
    packageLockIntegrity,
    memberClosureSha256: exactHash(input.memberClosureSha256, '$.memberClosureSha256'),
    canonicalEvidenceSha256: exactHash(
      input.canonicalEvidenceSha256, '$.canonicalEvidenceSha256',
    ),
    claimedRuntimeClosureSha256: exactHash(
      input.claimedRuntimeClosureSha256, '$.claimedRuntimeClosureSha256',
    ),
    executionId,
    platformReleaseFingerprint: exactHash(
      input.platformReleaseFingerprint, '$.platformReleaseFingerprint',
    ),
    checks,
    observedAt: safeInteger(input.observedAt, '$.observedAt', {
      minimum: 1,
      maximum: Number.MAX_SAFE_INTEGER,
    }),
  }
}

export function createWalletPackageDoctorAttestation(input) {
  return parseWalletPackageDoctorAttestation({
    schemaVersion: WALLET_PACKAGE_DOCTOR_ATTESTATION_SCHEMA,
    authority: 'operator',
    qualificationRunId: input.qualificationRunId,
    gameId: input.gameId,
    gameOriginSha256: input.gameOriginSha256,
    apiOriginSha256: input.apiOriginSha256,
    target: input.target,
    clientKind: input.clientKind,
    packageId: input.packageId,
    packageLockIntegrity: input.packageLockIntegrity,
    memberClosureSha256: input.memberClosureSha256,
    canonicalEvidenceSha256: input.canonicalEvidenceSha256,
    claimedRuntimeClosureSha256: input.claimedRuntimeClosureSha256,
    executionId: input.executionId,
    platformReleaseFingerprint: input.platformReleaseFingerprint,
    checks: WALLET_PACKAGE_DOCTOR_REQUIRED_CHECKS.map(id => ({ id, status: 'pass' })),
    observedAt: input.observedAt,
  })
}

export function parseCanonicalWalletPackageDoctorAttestationBytes(bytes) {
  const raw = Buffer.isBuffer(bytes) ? bytes : Buffer.from(bytes)
  if (raw.byteLength < 2 || raw.byteLength > 32_768)
    fail('$bytes', 'must be a non-empty Doctor attestation no larger than 32 KiB.')
  let decoded
  try { decoded = JSON.parse(raw.toString('utf8')) } catch {
    fail('$bytes', 'must contain valid JSON.')
  }
  const parsed = parseWalletPackageDoctorAttestation(decoded)
  if (!raw.equals(Buffer.from(canonicalJsonBytes(parsed))))
    fail('$bytes', 'must be the exact canonical Doctor attestation bytes.')
  return parsed
}

export function createDeployedWalletPackageEvidence({
  gameId,
  apiOrigin,
  origin,
  packageManifest,
  packageLockIntegrity,
  actuallyLoadedBrowserRuntimeUrl,
  actuallyLoadedRuntimeMemberUrls = {},
  unityBridgeRoundTrip = null,
  executionId = '',
}) {
  const runtime = packageManifest?.members?.find(member => member.role === 'browser_runtime')
  if (!runtime) fail('$.actuallyLoadedBrowserRuntime', 'requires one parsed browser_runtime member.')
  const specification = walletPackageSpecification(packageManifest?.clientKind)
  const expectedLoadedRuntimeMembers = loadedRuntimeMembersForPackage(specification, packageManifest)
  if (expectedLoadedRuntimeMembers.some(member => !member))
    fail('$.actuallyLoadedRuntimeMembers', 'requires the exact target-dependent runtime member closure.')
  const loadedRuntimeMembers = expectedLoadedRuntimeMembers.map(member => ({
    file: member.file,
    url: member.file === runtime.file
      ? actuallyLoadedBrowserRuntimeUrl
      : actuallyLoadedRuntimeMemberUrls[member.file]
        ?? (member.role === 'wallet_runtime'
          ? new URL(member.file, actuallyLoadedBrowserRuntimeUrl).toString()
          : undefined),
    integrity: member.integrity,
  }))
  const execution = packageManifest.clientKind === 'unity_webgl'
    ? {
        schemaVersion: packageManifest.schemaVersion === 'blockmaker-unity-webgl-package/v3'
          ? 'blockmaker-unity-webgl-wallet-package-execution/v2'
          : 'blockmaker-unity-webgl-wallet-package-execution/v1',
        executionId: unityBridgeRoundTrip?.lifecycleId,
        envelopeSchemaVersion: packageManifest.schemaVersion === 'blockmaker-unity-webgl-package/v3'
          ? 'blockmaker-unity-webgl-wallet-package/v2'
          : 'blockmaker-unity-webgl-wallet-package/v1',
        callbackMethod: 'OnBlockmakerWalletPackageSuccess',
        lifecycleId: unityBridgeRoundTrip?.lifecycleId,
        operationId: 0,
        operationKind: 'initialize',
        phase: 'ready',
        ...(packageManifest.schemaVersion === 'blockmaker-unity-webgl-package/v3'
          ? { runtimeProfile: unityBridgeRoundTrip?.runtimeProfile }
          : {}),
        hostIntegrity: memberByRole(packageManifest, 'wallet_host', '$.execution').integrity,
        browserRuntimeIntegrity: runtime.integrity,
        unityClientIntegrity: memberByRole(packageManifest, 'unity_client', '$.execution').integrity,
        unityFacadeIntegrity: memberByRole(packageManifest, 'unity_wallet_facade', '$.execution').integrity,
        webGlBridgeIntegrity: memberByRole(packageManifest, 'unity_webgl_bridge', '$.execution').integrity,
      }
    : {
        schemaVersion: 'blockmaker-web-wallet-package-execution/v1',
        executionId,
        operationKind: 'initialize',
        phase: 'ready',
        browserRuntimeIntegrity: runtime.integrity,
      }
  return parseDeployedWalletPackageEvidence({
    schemaVersion: WALLET_PACKAGE_DEPLOYED_SCHEMA,
    gameId,
    apiOrigin,
    origin,
    target: packageManifest.target,
    clientKind: packageManifest.clientKind,
    packageId: packageManifest.packageId,
    packageLockIntegrity,
    observationKind: 'runtime_loaded',
    complete: true,
    members: walletPackageMemberHashClosure(packageManifest),
    actuallyLoadedRuntimeMembers: loadedRuntimeMembers,
    actuallyLoadedBrowserRuntime: {
      file: runtime.file,
      url: actuallyLoadedBrowserRuntimeUrl,
      integrity: runtime.integrity,
    },
    execution,
  }, {
    gameId,
    apiOrigin,
    origin,
    packageManifest,
    packageLockIntegrity,
  })
}

export function parseCanonicalDeployedWalletPackageEvidenceBytes(bytes, context = {}) {
  const raw = Buffer.isBuffer(bytes) ? bytes : Buffer.from(bytes)
  if (raw.byteLength < 2 || raw.byteLength > 2_000_000)
    fail('$bytes', 'must be a non-empty deployed-evidence JSON file no larger than two megabytes.')
  let value
  try { value = JSON.parse(raw.toString('utf8')) } catch { fail('$bytes', 'must contain valid JSON.') }
  const parsed = parseDeployedWalletPackageEvidence(value, context)
  if (!raw.equals(Buffer.from(canonicalJsonBytes(parsed))))
    fail('$bytes', 'must use the exact canonical JSON representation.')
  return parsed
}

export function walletPackageProjectRoot(lockPath, target) {
  const specification = walletPackageSpecification(target)
  const absolute = resolve(lockPath)
  const segments = specification.lockPath.split('/')
  let root = dirname(absolute)
  for (let index = 1; index < segments.length; index += 1) root = dirname(root)
  if (resolve(root, specification.lockPath) !== absolute)
    fail('$lockPath', `must end with ${specification.lockPath}.`, 'WALLET_PACKAGE_LOCK_PATH_INVALID')
  return root
}

export function walletPackagePaths(projectRoot, target) {
  const specification = walletPackageSpecification(target)
  const root = resolve(projectRoot)
  const candidate = value => {
    const absolute = resolve(root, value)
    const relation = relative(root, absolute)
    if (relation.startsWith('..') || isAbsolute(relation))
      fail('$path', 'escaped the selected project root.', 'WALLET_PACKAGE_PATH_INVALID')
    return absolute
  }
  return {
    root,
    lock: candidate(specification.lockPath),
    installationEvidence: candidate(specification.installationEvidencePath),
    members: specification.members.map(member => ({ ...member, path: candidate(member.installPath) })),
  }
}
