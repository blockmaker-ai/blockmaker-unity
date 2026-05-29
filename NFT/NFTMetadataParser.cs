using System;
using UnityEngine;

namespace Blockmaker;

/// <summary>
/// Parses ARC-69 metadata JSON into strongly-typed data objects.
/// Data types (CarPartData, BuiltCarData, CharacterData etc.) live in NFTAssetTypes.cs.
///
/// ARC-69 stores metadata in the most recent asset config transaction note,
/// base64-encoded. This parser expects the note to already be decoded to a
/// JSON string — decoding from base64 is handled by NFTFetcher.
///
/// Expected JSON shape for a car PART:
/// {
///   "standard": "arc69",
///   "description": "rxelms NFTurbo Auto Parts NFT",
///   "properties": {
///     "category":     "Body",
///     "chain_theme":  "XRP",
///     "speed":        20,
///     "acceleration": 35,
///     "handling":     10,
///     "braking":      15,
///     "grip":         20
///   },
///   "image": "ipfs://..."
/// }
///
/// Expected JSON shape for a BUILT CAR:
/// {
///   "standard": "arc69",
///   "properties": {
///     "type":         "built_car",
///     "speed":        80,
///     "acceleration": 65,
///     "handling":     70,
///     "braking":      60,
///     "grip":         75
///   },
///   "image": "ipfs://..."
/// }
///
/// Expected JSON shape for a CHARACTER:
/// {
///   "standard": "arc69",
///   "properties": {
///     "type":        "character",
///     "prefab_key":  "Characters/RiderA"
///   },
///   "image": "ipfs://..."
/// }
/// </summary>
public static class NFTMetadataParser
{
    // ── Public entry points ────────────────────────────────────────────────────

    /// <summary>
    /// Parse a car part from ARC-69 metadata JSON + asset info.
    /// Returns null if the JSON is invalid or missing required fields.
    /// </summary>
    public static CarPartData ParseCarPart(long assetId, string name, string unitName, string metadataJson)
    {
        try
        {
            var raw = JsonUtility.FromJson<Arc69Root>(metadataJson);
            if (raw?.properties == null) return null;

            if (!TryParseCategory(raw.properties.category, out CarPartCategory category))
            {
                BlockmakerLog.Warning($"[NFTMetadataParser] Unknown category '{raw.properties.category}' on asset {assetId}");
                return null;
            }

            return new CarPartData
            {
                AssetId           = assetId,
                Name              = name,
                UnitName          = unitName,
                Category          = category,
                ChainTheme        = raw.properties.chain_theme ?? string.Empty,
                CarId             = (raw.properties.car_id ?? string.Empty).ToLower(),
                ImageUrl          = ResolveIpfs(raw.image),
                SpeedBonus        = raw.properties.speed,
                AccelerationBonus = raw.properties.acceleration,
                HandlingBonus     = raw.properties.handling,
                BrakingBonus      = raw.properties.braking,
                GripBonus         = raw.properties.grip
            };
        }
        catch (Exception e)
        {
            BlockmakerLog.Error($"[NFTMetadataParser] Failed to parse car part {assetId}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parse a fully built car NFT from ARC-69 metadata JSON + asset info.
    /// Returns null if the JSON is invalid or missing required fields.
    /// </summary>
    public static BuiltCarData ParseBuiltCar(long assetId, string name, string metadataJson)
    {
        try
        {
            var raw = JsonUtility.FromJson<Arc69Root>(metadataJson);
            if (raw?.properties == null) return null;

            return new BuiltCarData
            {
                AssetId      = assetId,
                Name         = name,
                ImageUrl     = ResolveIpfs(raw.image),
                Speed        = raw.properties.speed,
                Acceleration = raw.properties.acceleration,
                Handling     = raw.properties.handling,
                Braking      = raw.properties.braking,
                Grip         = raw.properties.grip,

                BodyPartAssetId     = raw.properties.body_part_id,
                WheelsPartAssetId   = raw.properties.wheels_part_id,
                SpoilerPartAssetId  = raw.properties.spoiler_part_id,
                SteeringPartAssetId = raw.properties.steering_part_id,
                ExhaustPartAssetId  = raw.properties.exhaust_part_id,

                BodyCarId     = raw.properties.body_car_id     ?? "",
                WheelsCarId   = raw.properties.wheels_car_id   ?? "",
                SpoilerCarId  = raw.properties.spoiler_car_id  ?? "",
                SteeringCarId = raw.properties.steering_car_id ?? "",
                ExhaustCarId  = raw.properties.exhaust_car_id  ?? ""
            };
        }
        catch (Exception e)
        {
            BlockmakerLog.Error($"[NFTMetadataParser] Failed to parse built car {assetId}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parse a character NFT from ARC-69 metadata JSON + asset info.
    /// Returns null if the JSON is invalid or missing required fields.
    /// </summary>
    public static CharacterData ParseCharacter(long assetId, string name, string metadataJson)
    {
        try
        {
            var raw = JsonUtility.FromJson<Arc69Root>(metadataJson);
            if (raw?.properties == null) return null;

            return new CharacterData
            {
                AssetId   = assetId,
                Name      = name,
                ImageUrl  = ResolveIpfs(raw.image),
                PrefabKey = raw.properties.prefab_key ?? string.Empty
            };
        }
        catch (Exception e)
        {
            BlockmakerLog.Error($"[NFTMetadataParser] Failed to parse character {assetId}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Combine 5 car parts into a single BuiltCarData by summing their stat bonuses.
    /// Stats are clamped to 0–100. Used in the garage before the on-chain
    /// burn+mint transaction to give the player a live preview.
    /// </summary>
    public static BuiltCarData CombineParts(
        long        previewAssetId,
        string      carName,
        CarPartData body,
        CarPartData wheels,
        CarPartData spoiler,
        CarPartData steering,
        CarPartData exhaust)
    {
        float Stat(Func<CarPartData, float> getter)
        {
            float total = 0f;
            if (body     != null) total += getter(body);
            if (wheels   != null) total += getter(wheels);
            if (spoiler  != null) total += getter(spoiler);
            if (steering != null) total += getter(steering);
            if (exhaust  != null) total += getter(exhaust);
            return Mathf.Clamp(total, 0f, 100f);
        }

        return new BuiltCarData
        {
            AssetId      = previewAssetId,
            Name         = carName,
            Speed        = Stat(p => p.SpeedBonus),
            Acceleration = Stat(p => p.AccelerationBonus),
            Handling     = Stat(p => p.HandlingBonus),
            Braking      = Stat(p => p.BrakingBonus),
            Grip         = Stat(p => p.GripBonus),

            BodyPartAssetId     = body?.AssetId     ?? 0,
            WheelsPartAssetId   = wheels?.AssetId   ?? 0,
            SpoilerPartAssetId  = spoiler?.AssetId  ?? 0,
            SteeringPartAssetId = steering?.AssetId ?? 0,
            ExhaustPartAssetId  = exhaust?.AssetId  ?? 0,

            BodyCarId     = body?.CarId     ?? "",
            WheelsCarId   = wheels?.CarId   ?? "",
            SpoilerCarId  = spoiler?.CarId  ?? "",
            SteeringCarId = steering?.CarId ?? "",
            ExhaustCarId  = exhaust?.CarId  ?? ""
        };
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>IPFS gateway base URL. Change at startup to use a different gateway.</summary>
    public static string IpfsGateway = "https://ipfs.algonode.xyz/ipfs/";

    /// <summary>Convert ipfs:// URLs to an HTTPS gateway URL.</summary>
    private static string ResolveIpfs(string url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;
        if (url.StartsWith("ipfs://"))
            return IpfsGateway + url.Substring(7);
        return url;
    }

    private static bool TryParseCategory(string raw, out CarPartCategory category)
    {
        category = default;
        if (string.IsNullOrEmpty(raw)) return false;
        return Enum.TryParse(raw, ignoreCase: true, out category);
    }

    // ── JSON schema (mirrors ARC-69 structure for JsonUtility) ────────────────
    // JsonUtility requires plain serializable classes — no Dictionary support.

    [Serializable]
    private class Arc69Root
    {
        public string          standard;
        public string          description;
        public string          image;
        public Arc69Properties properties;
    }

    [Serializable]
    private class Arc69Properties
    {
        public string type;         // "built_car" | "character" | null (part)
        public string category;     // CarPartCategory string
        public string chain_theme;  // "Algorand", "XRP", "Cardano" etc.
        public string car_id;       // e.g. "gonna" — maps to CarPartLibrary entry

        // Stat fields — bonuses for parts, totals for built cars
        public float speed;
        public float acceleration;
        public float handling;
        public float braking;
        public float grip;

        public string prefab_key;   // Characters only

        // Built car part references (embedded in minted car NFT metadata)
        public long body_part_id;
        public long wheels_part_id;
        public long spoiler_part_id;
        public long steering_part_id;
        public long exhaust_part_id;
        public string body_car_id;
        public string wheels_car_id;
        public string spoiler_car_id;
        public string steering_car_id;
        public string exhaust_car_id;
    }
}