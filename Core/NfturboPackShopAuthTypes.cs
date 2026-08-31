using System;

namespace Blockmaker
{
    /// <summary>
    /// Public, non-secret constants for NFTURBO's deliberately narrow Pack-Shop
    /// authentication contract. The resulting credential is not a Blockmaker player
    /// session and must never be supplied to another API family.
    /// </summary>
    public static class NfturboPackShopAuthContract
    {
        public const string ChallengeVersion = "nfturbo-pack-shop-1";
        public const string Purpose          = "nfturbo_pack_shop";
        public const string Network          = "mainnet";
        public const string ClientHeader     = "unity-webgl";
        public const string ClientKind       = "unity_webgl";
        public const int    MaximumTtlSeconds = 15 * 60;
    }

    [Serializable]
    public sealed class NfturboPackShopChallengeRequest
    {
        public string walletAddress;
        public string gameId;
        public string network;
        public string purpose;
        public string providerId;
    }

    /// <summary>
    /// Server-authored harmless sign-in transaction. <see cref="unsignedTxnBase64"/>
    /// is signed directly by Pera or Lute; the generic transaction builder is never
    /// involved in this flow.
    /// </summary>
    [Serializable]
    public sealed class NfturboPackShopChallengeResult
    {
        public bool   success;
        public string code;
        public string error;
        public string challengeVersion;
        public string purpose;
        public string gameId;
        public string network;
        public string providerId;
        public string clientKind;
        public string origin;
        public string walletAddress;
        public string nonce;
        public string message;
        public string unsignedTxnBase64;
        public string txId;
        public long   firstValidRound;
        public long   lastValidRound;
        public long   expiresAt;
    }

    [Serializable]
    public sealed class NfturboPackShopVerifyRequest
    {
        public string walletAddress;
        public string gameId;
        public string network;
        public string purpose;
        public string providerId;
        public string nonce;
        public string signedTxn;
    }

    [Serializable]
    public sealed class NfturboPackShopVerifyResult
    {
        public bool   success;
        public string code;
        public string error;
        public string sessionToken;
        public string sessionScope;
        public int    expiresInSeconds;
        public string walletAddress;
        public string accountKind;
        public string authProvider;
        public string gameId;
        public string network;
    }
}
