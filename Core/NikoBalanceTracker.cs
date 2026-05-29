using System;
using System.Collections;
using UnityEngine;

namespace Blockmaker;

/// <summary>
/// Tracks the player's live NIKO token balance with optimistic updates.
///
/// When a coin reward is sent, call AddPendingReward() to immediately bump
/// the displayed balance. The next poll confirms the on-chain balance and
/// clears the pending amount.
///
/// Persists across scenes (DontDestroyOnLoad). Use from any UI:
///   NikoBalanceTracker.Instance.DisplayBalance
///   NikoBalanceTracker.OnBalanceChanged += myHandler;
/// </summary>
public class NikoBalanceTracker : MonoBehaviour
{
    public static NikoBalanceTracker Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
        OnBalanceChanged = null;
        OnRewardAdded = null;
    }

    [Tooltip("Algorand ASA ID of the token to track. Set this in the Inspector for your game's token.")]
    public long tokenAssetId = 1265975021;

    private const int DefaultTokenDecimals = 6;

    [Tooltip("Seconds between on-chain balance polls.")]
    public float pollInterval = 10f;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Fires whenever the display balance changes (poll or optimistic).</summary>
    public static event Action<long> OnBalanceChanged;

    /// <summary>Fires when a pending reward is added. Amount = the reward size.</summary>
    public static event Action<long> OnRewardAdded;

    // ── State ─────────────────────────────────────────────────────────────────

    private long _confirmedBalance;
    private long _pendingRewards;
    private int  _nikoDecimals = DefaultTokenDecimals;
    private Coroutine _pollCoroutine;
    private float _lastRewardTime;

    [Tooltip("Seconds before unconfirmed pending rewards are cleared. " +
             "Handles failed transactions gracefully.")]
    public float pendingExpirySeconds = 30f;

    /// <summary>Confirmed on-chain balance + optimistic pending rewards.</summary>
    public long DisplayBalance => _confirmedBalance + _pendingRewards;

    /// <summary>Decimals for the NIKO token (from chain metadata).</summary>
    public int Decimals => _nikoDecimals;

    /// <summary>Display balance formatted as a human-readable string.</summary>
    public string FormattedBalance => FormatBalance(DisplayBalance, _nikoDecimals);

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        if (Instance != this) return;

        BlockmakerAuth.OnIdentityChanged += HandleIdentityChanged;

        if (BlockmakerAuth.Instance?.HasWallet == true)
            StartPolling();
    }

    private void OnDisable()
    {
        BlockmakerAuth.OnIdentityChanged -= HandleIdentityChanged;
        StopPolling();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Call when an instant NIKO reward has been sent. Updates the display
    /// balance immediately without waiting for the chain to confirm.
    /// </summary>
    public void AddPendingReward(long amount)
    {
        if (amount <= 0) return;
        _pendingRewards += amount;
        _lastRewardTime = Time.realtimeSinceStartup;
        OnRewardAdded?.Invoke(amount);
        OnBalanceChanged?.Invoke(DisplayBalance);
    }

    /// <summary>Force an immediate balance poll.</summary>
    public void RefreshNow()
    {
        if (BlockmakerAuth.Instance?.HasWallet != true) return;
        PollBalance();
    }

    // ── Identity ──────────────────────────────────────────────────────────────

    private void HandleIdentityChanged(IBlockmakerIdentity identity)
    {
        _confirmedBalance = 0;
        _pendingRewards   = 0;
        _nikoDecimals     = DefaultTokenDecimals;
        OnBalanceChanged?.Invoke(0);

        if (identity != null && identity.HasWallet)
            StartPolling();
        else
            StopPolling();
    }

    // ── Polling ───────────────────────────────────────────────────────────────

    private void StartPolling()
    {
        StopPolling();
        PollBalance();
        _pollCoroutine = StartCoroutine(PollRoutine());
    }

    private void StopPolling()
    {
        if (_pollCoroutine != null)
        {
            StopCoroutine(_pollCoroutine);
            _pollCoroutine = null;
        }
    }

    private IEnumerator PollRoutine()
    {
        while (true)
        {
            yield return new WaitForSecondsRealtime(pollInterval);
            PollBalance();
        }
    }

    private void PollBalance()
    {
        var bm = BlockmakerClient.Instance;
        if (bm == null) return;

        var expectedAddress = BlockmakerAuth.Instance?.Address;

        bm.GetOnboardingStatus(
            status =>
            {
                if (this == null) return;
                if (status?.success != true) return;
                if (BlockmakerAuth.Instance?.Address != expectedAddress) return;

                var niko = status.tokenBalances?.Find(t => t.assetId == tokenAssetId);
                long newConfirmed = niko?.amount ?? 0;
                if (niko != null) _nikoDecimals = niko.decimals;

                // Reduce pending by however much the chain has caught up
                if (newConfirmed > _confirmedBalance)
                {
                    long catchUp = newConfirmed - _confirmedBalance;
                    _pendingRewards = Math.Max(0, _pendingRewards - catchUp);
                }
                else if (_pendingRewards > 0 &&
                         Time.realtimeSinceStartup - _lastRewardTime > pendingExpirySeconds)
                {
                    // Pending rewards haven't confirmed after expiry — assume failed
                    _pendingRewards = 0;
                }

                _confirmedBalance = newConfirmed;
                OnBalanceChanged?.Invoke(DisplayBalance);
            },
            err => BlockmakerLog.Warning($"[NikoBalanceTracker] Poll failed: {err}")
        );
    }

    // ── Formatting ────────────────────────────────────────────────────────────

    public static string FormatBalance(long amount, int decimals)
    {
        if (decimals <= 0)
            return amount.ToString("N0");

        double divisor = Math.Pow(10, decimals);
        double value   = amount / divisor;
        return value.ToString("N" + Math.Min(decimals, 2));
    }
}
