using System;
using System.Collections.Generic;

namespace Blockmaker
{

    public enum ProfileTier    { Guest, Email, Wallet }
    public enum WalletType     { Guest, EmailDeviceKey, SelfCustody }
    public enum OnboardingStep { None, HasProfile, HasUsername, Complete }
    public enum TransactionType { Payment, AssetTransfer, AssetOptIn }

    // ── Flow types ─────────────────────────────────────────────────────────────────

    [Serializable] public class FlowRunRequest
    {
        public string wallet;
        public string context;
    }

    [Serializable] public class FlowResult
    {
        public bool   success;
        public bool   ownsNFT;
        public string output;
        public string error;
    }

    // ── Profile types ──────────────────────────────────────────────────────────────

    [Serializable] public class GameProfileField
    {
        public string key;
        public string label;
        public string type;   // "number" | "string" | "boolean" | "token_balance"
        public string value;
    }

    [Serializable] public class GameProfileData
    {
        public List<GameProfileField> fields = new List<GameProfileField>();
    }

    [Serializable] public class BlockmakerProfile
    {
        public string          walletAddress;
        public string          username;
        public string          displayName;
        public string          profileImageUrl;
        /// <summary>Algorand asset ID of the NFT set as the player's profile picture. 0 = not set.</summary>
        public long            profilePicAssetId;
        public string          nfdName;
        public string          tier;            // "guest" | "email" | "wallet"
        public string          walletType;      // "guest" | "email_device_key" | "self_custody"
        public string          onboardingStep;  // "none" | "has_profile" | "has_username" | "complete"
        public long            createdAt;
        public GameProfileData gameData;
    }

    [Serializable] public class ProfileResponse
    {
        public bool             success;
        public BlockmakerProfile profile;
        public string           error;
    }

    [Serializable] public class TokenBalance
    {
        public long   assetId;
        public string name;
        public string symbol;
        public long   amount;
        public int    decimals;
    }

    [Serializable] public class OnboardingStatus
    {
        public bool             success;
        public string           step;
        public string           nextAction;
        public string           walletAddress;
        public string           walletType;
        public string           walletMessage;
        public long             walletBalance;
        public List<TokenBalance> tokenBalances = new List<TokenBalance>();
        public string           error;
    }

    [Serializable] public class UsernameCheckResult
    {
        public bool   available;
        public string username;
        public string reason;
        public string code;
        public string error;
        public long   priceMicroAlgo;
        public float  priceAlgo;
    }

    [Serializable] public class UsernamePrepareResult
    {
        public bool     success;
        public string   username;
        public string   newUsername;
        public string   oldUsername;
        public string   unsignedTxnBase64;
        public string[] unsignedTxnsBase64;
        public string   reservationId;
        public long     priceMicroAlgo;
        public float    priceAlgo;
        public long     appId;
        public string   error;
    }

    [Serializable] public class UsernameClaimResult
    {
        public bool             success;
        public string           username;
        public long             appId;
        public string           txId;
        public BlockmakerProfile profile;
        public string           error;
    }

    [Serializable] public class ProfileImageResult
    {
        public bool             success;
        public string           profileImageUrl;
        public string           cid;
        public BlockmakerProfile profile;
        public string           error;
    }

    [Serializable] public class GameDataResponse
    {
        public bool   success;
        public string gameId;
        public string walletAddress;
        // Raw JSON object — use JsonUtility or a custom parser on this field
        public string data;
        public string error;
    }

    [Serializable] public class SchemaField
    {
        public string key;
        public string label;
        public string type;         // "number" | "string" | "boolean" | "token_balance"
        public string defaultValue;
        public long   assetId;      // required when type = "token_balance"
    }

    [Serializable] public class SchemaResponse
    {
        public bool            success;
        public List<SchemaField> schema = new List<SchemaField>();
        public string          error;
    }

    // ── Username request types ────────────────────────────────────────────────────

    [Serializable] public class UsernameCheckRequest
    {
        public string username;
        public string walletAddress;
        public bool   isChange;
    }

    [Serializable] public class UsernameClaimPrepareRequest
    {
        public string username;
    }

    [Serializable] public class UsernameChangePrepareRequest
    {
        public string newUsername;
    }

    [Serializable] public class UsernameCompleteRequest
    {
        public string   reservationId;
        public string   signedTxnBase64;
        public string[] signedTxnsBase64;
    }

    [Serializable] public class OnboardingAdvanceRequest
    {
        public string step;
    }

    // ── Profile picture NFT types ─────────────────────────────────────────────────

    /// <summary>
    /// POST /v1/profile/pfp
    /// Server verifies the player owns the NFT, fetches its ARC image URL, and
    /// stores both assetId and imageUrl on the profile.
    /// </summary>
    [Serializable] public class SetProfilePicRequest
    {
        /// <summary>Algorand asset ID of the NFT to use as the profile picture.</summary>
        public long assetId;
    }

    [Serializable] public class SetProfilePicResult
    {
        public bool             success;
        /// <summary>Resolved image URL fetched from the NFT's ARC metadata.</summary>
        public string           profileImageUrl;
        public long             assetId;
        /// <summary>Full updated profile returned after the change is persisted.</summary>
        public BlockmakerProfile profile;
        public string           error;
    }

    // ── Default avatar types ──────────────────────────────────────────────────────

    [Serializable] public class SetDefaultAvatarRequest
    {
        public string avatarId;
    }

    // ── Wallet NFT search types ───────────────────────────────────────────────────

    [Serializable] public class WalletNFTSearchRequest
    {
        public string search;
    }

    [Serializable] public class WalletNFTAsset
    {
        public long   assetId;
        public string name;
        public string unitName;
        public string imageUrl;
    }

    [Serializable] public class WalletNFTSearchResult
    {
        public bool                success;
        public List<WalletNFTAsset> assets = new List<WalletNFTAsset>();
        public string              error;
    }

    [Serializable] public class NftImageRequest
    {
        public long assetId;
    }

    [Serializable] public class NftImageResult
    {
        public bool   success;
        public long   assetId;
        public string imageUrl;
        public string error;
    }

    // ── Wallet holdings types ─────────────────────────────────────────────────────

    [Serializable] public class AssetHolding
    {
        public long assetId;
        public long amount;
    }

    [Serializable] public class HoldingsResponse
    {
        public bool                success;
        public string              walletAddress;
        public List<AssetHolding>  holdings = new List<AssetHolding>();
        public string              error;
    }

    // ── Collection check types ───────────────────────────────────────────────────

    [Serializable] public class CollectionCheckRequest
    {
        public string[] creators;
    }

    [Serializable] public class CollectionCheckEntry
    {
        public string creator;
        public bool   holds;
        public int    count;
        public bool   reliable;
    }

    [Serializable] public class CollectionCheckResponse
    {
        public bool                        success;
        public string                      walletAddress;
        public List<CollectionCheckEntry>  results = new List<CollectionCheckEntry>();
        public string                      error;
        public bool                        degraded;
    }

    // ── Asset registry types ─────────────────────────────────────────────────────

    [Serializable] public class AssetRegistryEntry
    {
        public string id;
        public string slug;
        public string name;
        public string type;
        public string creatorAddress;
        public string unitName;
        public long   assetId;
        public int    decimals;
        public string imageUrl;
        public long   totalSupply;
        public int    verified;
    }

    [Serializable] public class AssetRegistryResponse
    {
        public bool                     success;
        public List<AssetRegistryEntry> entries = new List<AssetRegistryEntry>();
        public string                   error;
    }

    // ── Magic auth types ──────────────────────────────────────────────────────────

    [Serializable] public class MagicVerifyRequest
    {
        public string didToken;
        public string email;
    }

    // ── Email auth types ───────────────────────────────────────────────────────────

    [Serializable] public class EmailOTPRequest
    {
        public string email;
    }

    /// <summary>Returned by POST /v1/auth/email/request.</summary>
    [Serializable] public class OTPRequestResult
    {
        public bool   success;
        public string error;
    }

    [Serializable] public class EmailVerifyRequest
    {
        public string email;
        public string otp;
    }

    /// <summary>Returned by POST /v1/auth/email/verify.</summary>
    [Serializable] public class EmailVerifyResult
    {
        public bool   success;
        public string walletAddress;  // managed Algorand wallet for this email
        public string sessionToken;   // JWT — pass as Bearer token to /v1/auth/sign
        public string refreshToken;   // long-lived refresh token for token rotation
        public string displayName;
        public bool   isNewAccount;   // true on first login (wallet was just created)
        public string error;
    }

    // ── Token refresh types ──────────────────────────────────────────────────────

    /// <summary>Returned by POST /v1/auth/refresh.</summary>
    [Serializable] public class RefreshTokenResult
    {
        public bool   success;
        public string sessionToken;
        public string refreshToken;
        public string error;
    }

    [Serializable] public class RefreshTokenRequest
    {
        public string refreshToken;
    }

    [Serializable] public class LogoutRequest
    {
        public string refreshToken;
    }

    // ── Server-side signing types ──────────────────────────────────────────────────

    [Serializable] public class ServerSignRequest
    {
        public string   unsignedTxnBase64;
        public string[] unsignedTxnsBase64;
    }

    /// <summary>Returned by POST /v1/auth/sign. Supports both single and group signing.</summary>
    [Serializable] public class ServerSignResult
    {
        public bool     success;
        public string   signedTxnBase64;
        public string[] signedTxnsBase64;
        public string   txId;
        public string   error;
    }

    [Serializable] public class ServerErrorResponse
    {
        public bool   success;
        public string code;
        public string error;
    }

    [Serializable] public class BlockmakerError
    {
        public string Code    { get; }
        public string Message { get; }
        public int    HttpStatus { get; }

        public BlockmakerError(string code, string message, int httpStatus = 0)
        {
            Code       = code ?? "";
            Message    = message ?? "Something went wrong.";
            HttpStatus = httpStatus;
        }

        public bool IsAuthError         => Code == "AUTH_MISSING" || Code == "AUTH_INVALID" || Code == "AUTH_EXPIRED";
        public bool IsConfigError       => Code == "SERVER_CONFIG";
        public bool IsRateLimited       => Code == "RATE_LIMITED";
        public bool IsNetworkError      => Code == "NETWORK" || HttpStatus == 0;
        public bool IsTransactionError  => Code.StartsWith("TX_") || Code == "INVALID_AMOUNT" || Code == "NOTE_TOO_LONG";
    }

    // ── WalletConnect v1 JSON-RPC response types ─────────────────────────────────

    [Serializable] public class WcJsonRpcResult
    {
        public long     id;
        public string[] result;
    }

    [Serializable] public class WcJsonRpcError
    {
        public long        id;
        public WcRpcError  error;
    }

    [Serializable] public class WcRpcError
    {
        public int    code;
        public string message;
    }

    // ── Rewards (generic platform feature) ───────────────────────────────────────

    [Serializable] public class RewardResult
    {
        public bool   success;
        public bool   sent;
        public string txId;
        public string recipientWallet;
        public long   amountMicroAlgo;
        public long   assetId;
        public string reason;
        public string raceId;
        public string code;
        public string error;
    }

    [Serializable] public class RewardRequest
    {
        public string recipientWallet;
        public long   assetId;
        public long   amountMicroAlgo;
        public string reason;
        public string raceId;
    }

    // ── Transaction builder types ───────────────────────────────────────────────

    [Serializable] public class BuildTransactionRequest
    {
        public string type;
        public string recipient;
        public long   amount;
        public long   assetId;
        public string note;
        public string walletAddress;

        public static string TypeToString(TransactionType t)
        {
            if (t == TransactionType.AssetTransfer) return "asset_transfer";
            if (t == TransactionType.AssetOptIn)    return "asset_optin";
            return "payment";
        }
    }

    [Serializable] public class BuildTransactionResult
    {
        public bool     success;
        public string   unsignedTxnBase64;
        public string[] unsignedTxnsBase64;
        public string   txType;
        public string   from;
        public string   code;
        public string   error;
    }

    [Serializable] public class SubmitTransactionRequest
    {
        public string   signedTxnBase64;
        public string[] signedTxnsBase64;
    }

    [Serializable] public class SubmitTransactionResult
    {
        public bool   success;
        public string txId;
        public bool   alreadyConfirmed;
        public string code;
        public string error;
    }

    // Game-specific types (NFTInventoryResult, CarStatsResult, LeaderboardResult,
    // RaceResultRequest, etc.) live in the game project, not the SDK.

}