using System;
using UnityEngine;

namespace Blockmaker;

/// <summary>
/// BlockmakerClient partial — marketplace endpoints and test-mode mock data.
/// </summary>
public partial class BlockmakerClient
{
    // ═══════════════════════════════════════════════════════════════════════════
    // MARKETPLACE
    // ═══════════════════════════════════════════════════════════════════════════

    public void GetCarListings(
        MarketplaceSortMode              sort,
        Action<MarketplaceListingsResult> onSuccess,
        Action<string>                   onError = null,
        int page = 0, int pageSize = 30)
    {
        if (config.marketplaceTestMode) { onSuccess?.Invoke(BuildTestCarListings()); return; }
        StartCoroutine(PostJsonAuth<MarketplaceListingsResult>(
            $"{_baseUrl}/v1/marketplace/cars",
            new MarketplaceQueryRequest { sortMode = sort.ToString(), page = page, pageSize = pageSize },
            config.defaultTimeoutSeconds, onSuccess, onError));
    }

    public void GetPartListings(
        MarketplaceSortMode              sort,
        Action<MarketplaceListingsResult> onSuccess,
        Action<string>                   onError = null,
        int page = 0, int pageSize = 40)
    {
        if (config.marketplaceTestMode) { onSuccess?.Invoke(BuildTestPartListings()); return; }
        StartCoroutine(PostJsonAuth<MarketplaceListingsResult>(
            $"{_baseUrl}/v1/marketplace/parts",
            new MarketplaceQueryRequest { sortMode = sort.ToString(), page = page, pageSize = pageSize },
            config.defaultTimeoutSeconds, onSuccess, onError));
    }

    public void GetMyListings(
        Action<MarketplaceListingsResult> onSuccess,
        Action<string>                   onError = null)
    {
        if (config.marketplaceTestMode) { onSuccess?.Invoke(BuildTestMyListings()); return; }
        StartCoroutine(GetJson<MarketplaceListingsResult>(
            $"{_baseUrl}/v1/marketplace/my-listings",
            config.defaultTimeoutSeconds, onSuccess, onError));
    }

    public void ListItem(
        long                        assetId,
        long                        priceNiko,
        string                      listingType,
        Action<MarketplaceListResult> onSuccess,
        Action<string>              onError = null)
    {
        if (config.marketplaceTestMode)
        {
            onSuccess?.Invoke(new MarketplaceListResult
                { success = true, listingId = $"test-{System.Guid.NewGuid()}" });
            return;
        }
        StartCoroutine(PostJsonAuth<MarketplaceListResult>(
            $"{_baseUrl}/v1/marketplace/list",
            new MarketplaceListRequest { assetId = assetId, priceNiko = priceNiko, listingType = listingType },
            config.rewardTimeoutSeconds, onSuccess, onError));
    }

    public void BuyListing(
        string                       listingId,
        Action<MarketplaceBuyResult>  onSuccess,
        Action<string>               onError = null)
    {
        if (config.marketplaceTestMode)
        {
            onSuccess?.Invoke(new MarketplaceBuyResult
                { success = true, txId = $"TESTTX{UnityEngine.Random.Range(100000, 999999)}" });
            return;
        }
        StartCoroutine(PostJsonAuth<MarketplaceBuyResult>(
            $"{_baseUrl}/v1/marketplace/buy",
            new MarketplaceBuyRequest { listingId = listingId },
            config.rewardTimeoutSeconds, onSuccess, onError));
    }

    public void CancelListing(
        string                          listingId,
        Action<MarketplaceCancelResult>  onSuccess,
        Action<string>                  onError = null)
    {
        if (config.marketplaceTestMode)
        {
            onSuccess?.Invoke(new MarketplaceCancelResult { success = true });
            return;
        }
        StartCoroutine(PostJsonAuth<MarketplaceCancelResult>(
            $"{_baseUrl}/v1/marketplace/cancel",
            new MarketplaceCancelRequest { listingId = listingId },
            config.rewardTimeoutSeconds, onSuccess, onError));
    }

    // ── Test-mode mock data ────────────────────────────────────────────────────

    private static MarketplaceListingsResult BuildTestCarListings()
    {
        var listings = new System.Collections.Generic.List<MarketplaceListing>
        {
            new MarketplaceListing {
                listingId = "test-car-1", listingType = "car",
                sellerAddress = "SELLER1AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                priceNiko = 2400, listedAtUnix = 1700000000,
                car = new MarketplaceCarNft { assetId = 111111111, name = "NITRO VIPER X",
                    bodyPartId=10001, enginePartId=10002, wheelsPartId=10003, spoilerPartId=10004, exhaustPartId=10005,
                    speed=88, handling=74, acceleration=91, grip=69, braking=72, overall=79 }
            },
            new MarketplaceListing {
                listingId = "test-car-2", listingType = "car",
                sellerAddress = "SELLER2AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                priceNiko = 1800, listedAtUnix = 1700001000,
                car = new MarketplaceCarNft { assetId = 222222222, name = "GHOST RUNNER",
                    bodyPartId=20001, enginePartId=20002, wheelsPartId=20003, spoilerPartId=20004, exhaustPartId=20005,
                    speed=62, handling=85, acceleration=70, grip=90, braking=88, overall=79 }
            },
            new MarketplaceListing {
                listingId = "test-car-3", listingType = "car",
                sellerAddress = "SELLER3AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                priceNiko = 3500, listedAtUnix = 1700002000,
                car = new MarketplaceCarNft { assetId = 333333333, name = "TURBO PHANTOM",
                    bodyPartId=30001, enginePartId=30002, wheelsPartId=30003, spoilerPartId=30004, exhaustPartId=30005,
                    speed=95, handling=80, acceleration=93, grip=78, braking=66, overall=83 }
            },
            new MarketplaceListing {
                listingId = "test-car-4", listingType = "car",
                sellerAddress = "SELLER4AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                priceNiko = 950, listedAtUnix = 1700003000,
                car = new MarketplaceCarNft { assetId = 444444444, name = "STREET HAWK",
                    bodyPartId=40001, enginePartId=40002, wheelsPartId=40003, spoilerPartId=40004, exhaustPartId=40005,
                    speed=55, handling=60, acceleration=58, grip=62, braking=70, overall=61 }
            },
            new MarketplaceListing {
                listingId = "test-car-5", listingType = "car",
                sellerAddress = "SELLER5AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                priceNiko = 4200, listedAtUnix = 1700004000,
                car = new MarketplaceCarNft { assetId = 555555555, name = "APEX PREDATOR",
                    bodyPartId=50001, enginePartId=50002, wheelsPartId=50003, spoilerPartId=50004, exhaustPartId=50005,
                    speed=92, handling=89, acceleration=88, grip=87, braking=85, overall=89 }
            },
            new MarketplaceListing {
                listingId = "test-car-6", listingType = "car",
                sellerAddress = "SELLER6AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                priceNiko = 1200, listedAtUnix = 1700005000,
                car = new MarketplaceCarNft { assetId = 666666666, name = "CIRCUIT BREAKER",
                    bodyPartId=60001, enginePartId=60002, wheelsPartId=60003, spoilerPartId=60004, exhaustPartId=60005,
                    speed=75, handling=68, acceleration=72, grip=65, braking=78, overall=72 }
            },
        };
        return new MarketplaceListingsResult { success = true, listings = listings, total = listings.Count };
    }

    private static MarketplaceListingsResult BuildTestPartListings()
    {
        string[] types   = { "body", "engine", "wheels", "spoiler", "exhaust" };
        string[] names   = {
            "CARBON SHELL", "TURBO V8", "SLICK RUBBER", "WING MASTER", "QUAD PIPE",
            "AERO BODY MK2", "NITRO CORE", "RACING SLICKS", "DELTA WING", "SIDE EXIT",
            "STEALTH FRAME", "ECO BOOST", "DRIFT TIRES", "DUCK TAIL", "TWIN STACK",
        };
        long baseAssetId = 800000000;
        var listings = new System.Collections.Generic.List<MarketplaceListing>();
        for (int i = 0; i < 15; i++)
        {
            int typeIdx = i % 5;
            listings.Add(new MarketplaceListing {
                listingId = $"test-part-{i+1}", listingType = "part",
                sellerAddress = $"PSELLER{i}AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                priceNiko = 100 + (i * 80), listedAtUnix = 1700000000 + i * 600,
                part = new MarketplacePartNft {
                    assetId   = baseAssetId + i,
                    name      = names[i],
                    imageUrl  = "",
                    partType  = types[typeIdx],
                    score     = 4f + (i % 6),
                    scoreMax  = 10f
                }
            });
        }
        return new MarketplaceListingsResult { success = true, listings = listings, total = listings.Count };
    }

    private static MarketplaceListingsResult BuildTestMyListings()
    {
        var listings = new System.Collections.Generic.List<MarketplaceListing>
        {
            new MarketplaceListing {
                listingId = "my-car-1", listingType = "car",
                sellerAddress = BlockmakerAuth.Instance?.Address ?? "",
                priceNiko = 2000, listedAtUnix = 1700006000,
                car = new MarketplaceCarNft { assetId = 777777777, name = "MY RACER PRO",
                    bodyPartId=70001, enginePartId=70002, wheelsPartId=70003, spoilerPartId=70004, exhaustPartId=70005,
                    speed=80, handling=77, acceleration=82, grip=75, braking=73, overall=77 }
            },
            new MarketplaceListing {
                listingId = "my-part-1", listingType = "part",
                sellerAddress = BlockmakerAuth.Instance?.Address ?? "",
                priceNiko = 450, listedAtUnix = 1700007000,
                part = new MarketplacePartNft {
                    assetId  = 888888888, name = "MY NITRO CORE",
                    imageUrl = "", partType = "engine", score = 8f, scoreMax = 10f
                }
            },
        };
        return new MarketplaceListingsResult { success = true, listings = listings, total = listings.Count };
    }
}
