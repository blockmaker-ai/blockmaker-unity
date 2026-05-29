using System;
using System.Collections.Generic;

namespace Blockmaker;

/// <summary>
/// Data types representing NFT assets fetched from the Algorand blockchain.
/// Populated by NFTMetadataParser from ARC-69 metadata JSON.
///
/// Lives in Blockmaker/NFT/ — no game-specific dependencies.
/// </summary>

// ── Enums ──────────────────────────────────────────────────────────────────────

/// <summary>
/// The five part categories that combine to make a full car.
/// String values match the "category" field in ARC-69 metadata properties.
/// </summary>
public enum CarPartCategory
{
    Body,       // XRP body
    Wheels,     // Monko wheel set
    Spoiler,    // Algorand spoiler
    Steering,   // Cardano steering wheel
    Exhaust     // Drako exhaust
}

// ── Car part ───────────────────────────────────────────────────────────────────

/// <summary>
/// Runtime representation of a single car part NFT.
/// Each part contributes stat bonuses that sum to form a built car's stats.
/// </summary>
[Serializable]
public class CarPartData
{
    public long            AssetId;
    public string          Name;
    public string          UnitName;
    public CarPartCategory Category;

    /// <summary>Chain branding: "Algorand", "XRP", "Cardano" etc. Cosmetic only.</summary>
    public string          ChainTheme;

    /// <summary>
    /// Maps to a prefab folder in CarPartLibrary (e.g. "gonna").
    /// Parsed from the ARC-69 "car_id" property. Lowercase match.
    /// </summary>
    public string          CarId;

    public string          ImageUrl;

    /// <summary>
    /// Per-part stat contributions (0–100).
    /// Parsed from the ARC-69 "properties" block.
    /// Parts that don't affect a stat leave the value at 0.
    /// </summary>
    public float SpeedBonus;
    public float AccelerationBonus;
    public float HandlingBonus;
    public float BrakingBonus;
    public float GripBonus;
}

// ── Built car ──────────────────────────────────────────────────────────────────

/// <summary>
/// Runtime representation of a fully built car NFT —
/// the combined asset minted after burning 5 parts.
/// Stat values are totals (0–100), not bonuses.
/// </summary>
[Serializable]
public class BuiltCarData
{
    public long   AssetId;
    public string Name;
    public string ImageUrl;

    public float Speed;
    public float Acceleration;
    public float Handling;
    public float Braking;
    public float Grip;

    public long BodyPartAssetId;
    public long WheelsPartAssetId;
    public long SpoilerPartAssetId;
    public long SteeringPartAssetId;
    public long ExhaustPartAssetId;

    public string BodyCarId;
    public string WheelsCarId;
    public string SpoilerCarId;
    public string SteeringCarId;
    public string ExhaustCarId;
}

// ── Character ──────────────────────────────────────────────────────────────────

/// <summary>
/// Runtime representation of a character NFT.
/// PrefabKey maps to a Resources path or Addressable key used to load
/// the character prefab at runtime.
/// </summary>
[Serializable]
public class CharacterData
{
    public long   AssetId;
    public string Name;
    public string ImageUrl;

    /// <summary>
    /// Maps to a prefab in Resources/Characters/ or an Addressables key.
    /// e.g. "Characters/RiderA"
    /// </summary>
    public string PrefabKey;
}

// ── Inventory ──────────────────────────────────────────────────────────────────

/// <summary>
/// A player's full NFT inventory, returned by Blockmaker after a flow run.
/// Populated by GarageManager and held by PlayerManager during a session.
/// </summary>
[Serializable]
public class PlayerNFTInventory
{
    public List<BuiltCarData> Cars       = new();
    public List<CarPartData>  Parts      = new();
    public List<CharacterData> Characters = new();

    /// <summary>Parts grouped by category for the garage UI.</summary>
    public List<CarPartData> GetPartsByCategory(CarPartCategory category)
        => Parts.FindAll(p => p.Category == category);

    /// <summary>True if the player has one part of every category (can build a car).</summary>
    public bool CanBuildCar()
    {
        foreach (CarPartCategory cat in Enum.GetValues(typeof(CarPartCategory)))
        {
            if (Parts.Find(p => p.Category == cat) == null)
                return false;
        }
        return true;
    }
}