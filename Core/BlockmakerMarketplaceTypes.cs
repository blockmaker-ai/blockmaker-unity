using System;
using System.Collections.Generic;
using UnityEngine;

namespace Blockmaker;

// ── Sort modes ─────────────────────────────────────────────────────────────────

public enum MarketplaceSortMode
{
    PriceLowHigh,
    PriceHighLow,
    ScoreHighLow,
    Newest,
    Oldest
}

// ── NFT metadata ───────────────────────────────────────────────────────────────

/// <summary>
/// Metadata carried by a fully built car NFT.
/// The 5 part asset IDs are used by the 3D car render system.
/// </summary>
[Serializable]
public class MarketplaceCarNft
{
    public long   assetId;
    public string name;
    // Part slot asset IDs — feed to CarRenderRequest
    public long   bodyPartId;
    public long   enginePartId;
    public long   wheelsPartId;
    public long   spoilerPartId;
    public long   exhaustPartId;
    // Part model IDs — used by CarPartLibrary for thumbnails
    public string bodyCarId;
    public string wheelsCarId;
    public string spoilerCarId;
    public string steeringCarId;
    public string exhaustCarId;
    // Stats 0–100
    public float speed;
    public float handling;
    public float acceleration;
    public float grip;
    public float braking;
    public float overall;
}

/// <summary>Metadata carried by a single car-part NFT.</summary>
[Serializable]
public class MarketplacePartNft
{
    public long   assetId;
    public string name;
    public string imageUrl;
    public string partType;   // "body" | "wheels" | "spoiler" | "steering" | "exhaust"
    public float  score;      // e.g. 0–10
    public float  scoreMax;   // 10 by default
}

// ── Listing ────────────────────────────────────────────────────────────────────

/// <summary>A single active marketplace listing (car or part).</summary>
[Serializable]
public class MarketplaceListing
{
    public string listingId;
    public string listingType;    // "car" | "part"
    public string sellerAddress;
    public long   priceNiko;      // whole Niko tokens
    public long   listedAtUnix;   // Unix timestamp seconds

    /// <summary>Populated when listingType == "car".</summary>
    public MarketplaceCarNft  car;
    /// <summary>Populated when listingType == "part".</summary>
    public MarketplacePartNft part;
}

// ── Response wrappers ──────────────────────────────────────────────────────────

[Serializable]
public class MarketplaceListingsResult
{
    public bool                     success;
    public List<MarketplaceListing>  listings = new List<MarketplaceListing>();
    public int                      total;
    public string                   error;
}

[Serializable]
public class MarketplaceListResult
{
    public bool   success;
    public string listingId;
    public string error;
}

[Serializable]
public class MarketplaceBuyResult
{
    public bool   success;
    public string txId;
    public string error;
}

[Serializable]
public class MarketplaceCancelResult
{
    public bool   success;
    public string error;
}

// ── Request bodies ─────────────────────────────────────────────────────────────

[Serializable]
public class MarketplaceListRequest
{
    public long   assetId;
    public long   priceNiko;
    public string listingType;
}

[Serializable]
public class MarketplaceBuyRequest
{
    public string listingId;
}

[Serializable]
public class MarketplaceCancelRequest
{
    public string listingId;
}

[Serializable]
public class MarketplaceQueryRequest
{
    public string sortMode;
    public int    page;
    public int    pageSize;
}

// ── 3D render hook ─────────────────────────────────────────────────────────────

/// <summary>
/// Passed to MarketplaceController.OnCarRenderRequested.
/// Subscribe in your 3D layer to render the car into a Texture2D and
/// return it via onTextureReady.  Return null to show the placeholder.
/// </summary>
public class CarRenderRequest
{
    public long   assetId;
    public long   bodyPartId;
    public long   enginePartId;
    public long   wheelsPartId;
    public long   spoilerPartId;
    public long   exhaustPartId;
    public Action<Texture2D> onTextureReady;
}
