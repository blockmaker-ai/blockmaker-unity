/** MainNet-only provider identifiers exposed by the TxnLab creator package. */
export type BlockmakerTxnLabProviderId = 'pera' | 'lute' | 'txnlab_web3auth'

export type BlockmakerTxnLabProviderProfile =
  | readonly ['pera', 'lute']
  | readonly ['pera', 'lute', 'txnlab_web3auth']

export type BlockmakerTxnLabWalletAccount = {
  address: string
  name?: string
  metadata?: Record<string, unknown>
}

/** ARC-60 metadata used only by providers that advertise `canSignData`. */
export type BlockmakerTxnLabSignDataMetadata = {
  scope: -1 | 1
  encoding: string
}

/** ARC-60 response used only by providers that advertise `canSignData`. */
export type BlockmakerTxnLabSignDataResponse = {
  data: string
  signer: Uint8Array
  domain: string
  authenticatorData: Uint8Array
  requestId?: string
  hdPath?: string
  signature: Uint8Array
}

/** Provider-neutral full signing surface for a game's headless or drop-in UI. */
export interface BlockmakerTxnLabWalletBrowser {
  readonly name?: string
  readonly providerId?: BlockmakerTxnLabProviderId
  readonly metadata?: {
    readonly name?: string
    readonly icon?: string
    readonly providerId?: BlockmakerTxnLabProviderId
    readonly networkGenesisId?: string
  }
  readonly networkGenesisId?: string
  readonly accounts?: BlockmakerTxnLabWalletAccount[]
  connect(): Promise<BlockmakerTxnLabWalletAccount[]>
  resumeSession?(): Promise<void | BlockmakerTxnLabWalletAccount[]>
  disconnect?(): Promise<void> | void
  signTransactions(
    transactions: any,
    indexesToSign?: number[],
  ): Promise<Array<Uint8Array | null>>
  /** Algokit/ATC-compatible signer for complete transaction groups. */
  transactionSigner(
    transactions: any[],
    indexesToSign: number[],
  ): Promise<Uint8Array[]>
  /** ARC-60 data signing is provider-dependent and separate from transaction signing. */
  readonly canSignData: boolean
  signData(
    data: string,
    metadata: BlockmakerTxnLabSignDataMetadata,
  ): Promise<BlockmakerTxnLabSignDataResponse>
}

export interface BlockmakerTxnLabNamedWallet {
  readonly id: 'web3auth' | 'pera' | 'lute'
  readonly providerId: BlockmakerTxnLabProviderId
  readonly name: string
  readonly wallet: BlockmakerTxnLabWalletBrowser
  readonly featured?: boolean
}

/** The algosdk subset consumed by Blockmaker's Algorand account flow. */
export interface BlockmakerTxnLabAlgorandSdk {
  decodeUnsignedTransaction(bytes: Uint8Array): unknown
  encodeUnsignedTransaction(transaction: unknown): Uint8Array
  computeGroupID(transactions: readonly unknown[]): Uint8Array
  decodeSignedTransaction(bytes: Uint8Array): {
    txn: {
      readonly sender?: Uint8Array | { readonly publicKey: Uint8Array }
      readonly from?: Uint8Array | { readonly publicKey: Uint8Array }
      bytesToSign(): Uint8Array
      toByte(): Uint8Array
    }
    sig?: Uint8Array
    sgnr?: Uint8Array | { readonly publicKey: Uint8Array }
    msig?: unknown
    lsig?: unknown
  }
  encodeAddress(publicKey: Uint8Array): string
  decodeAddress?(address: string): { readonly publicKey: Uint8Array; toString(): string }
  getApplicationAddress?(appId: number | bigint): string | { readonly publicKey: Uint8Array; toString(): string }
}

export type TxnLabEmbeddedWalletOptions = {
  /** Public MetaMask Embedded Wallets dashboard identifier. */
  clientId: string
  /** Neutral application name passed to Lute and Blockmaker's wallet chooser. */
  appName: string
  /** Expose and restore Pera/Lute alongside email/Google. Defaults to true. */
  includeExternalWallets?: boolean
  /** Short chooser label. Defaults to `Email or Google`. */
  walletName?: string
}

type BlockmakerTxnLabWalletCommonOptions = {
  /** The game's neutral name passed to Lute and Blockmaker's wallet chooser. */
  appName: string
  /** Short embedded-provider label. Defaults to `Email or Google`. */
  walletName?: string
}

export type BlockmakerTxnLabWalletOptions = BlockmakerTxnLabWalletCommonOptions & (
  | {
      /** TxnLab-managed Pera and Lute; Web3Auth is not imported or initialized. */
      providerIds: readonly ['pera', 'lute']
      clientId?: never
    }
  | {
      /** TxnLab-managed Pera, Lute and email/Google. */
      providerIds: readonly ['pera', 'lute', 'txnlab_web3auth']
      /** Public MetaMask Embedded Wallets dashboard identifier. */
      clientId: string
    }
)

/** Complete provider-neutral setup for a game's headless or drop-in account UI. */
export type BlockmakerTxnLabWalletRuntime = {
  readonly algorandWallets: readonly BlockmakerTxnLabNamedWallet[]
  /** Null for the exact Pera/Lute-only profile. */
  readonly txnLabWeb3Auth: BlockmakerTxnLabWalletBrowser | null
  readonly algosdk: BlockmakerTxnLabAlgorandSdk
  readonly resumeError: Error | null
  disconnectAll(): Promise<void>
}

/** Compatibility shape used by Blockmaker's attended canary. */
export type TxnLabEmbeddedWalletRuntime = {
  readonly wallet: BlockmakerTxnLabWalletBrowser
  readonly peraWallet: BlockmakerTxnLabWalletBrowser | null
  readonly luteWallet: BlockmakerTxnLabWalletBrowser | null
  readonly algorandWallets: readonly BlockmakerTxnLabNamedWallet[]
  readonly resumeError: Error | null
  disconnectAll(): Promise<void>
}

export declare const TXNLAB_EMBEDDED_ALGORAND_NETWORK: 'mainnet'
export declare const TXNLAB_EMBEDDED_ALGORAND_GENESIS_ID: 'mainnet-v1.0'
export declare const TXNLAB_EMBEDDED_ALGORAND_GENESIS_HASH: 'wGHE2Pwdvd7S12BL5FaOP20EGYesN73ktiC1qzkkit8='
export declare const TXNLAB_EMBEDDED_WEB3AUTH_NETWORK: 'sapphire_mainnet'

export declare function loadBlockmakerTxnLabWallets(
  options: BlockmakerTxnLabWalletOptions,
): Promise<BlockmakerTxnLabWalletRuntime>

export declare function loadTxnLabEmbeddedWallet(
  options: TxnLabEmbeddedWalletOptions,
): Promise<TxnLabEmbeddedWalletRuntime>
