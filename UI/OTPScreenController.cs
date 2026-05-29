using System;
using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockmaker;

/// <summary>
/// Manages the OTPScreen UXML two-step flow.
///   Step 1 — player enters email, taps "Send Code"
///   Step 2 — player enters OTP, taps "Verify"; 60 s countdown to resend
///
/// Needs a MonoBehaviour host (BlockmakerAuthUI) to drive coroutines.
/// </summary>
public class OTPScreenController
{
    private const int ResendCooldownSeconds = 60;

    // ── Elements ───────────────────────────────────────────────────────────

    private readonly VisualElement _step1;
    private readonly VisualElement _step2;
    private readonly TextField     _inputEmail;
    private readonly TextField     _inputOtp;
    private readonly Button        _btnSendCode;
    private readonly Button        _btnVerify;
    private readonly Button        _btnBack;
    private readonly Button        _btnResend;
    private readonly Label         _lblSubtitle;
    private readonly Label         _lblStatus;
    private readonly Label         _lblCountdown;

    // ── Callbacks ──────────────────────────────────────────────────────────

    public Action<string>         OnSendCode  { get; set; }
    public Action<string, string> OnVerify    { get; set; }
    public Action                 OnBack      { get; set; }

    // ── State ──────────────────────────────────────────────────────────────

    private MonoBehaviour _host;
    private string        _pendingEmail;
    private Coroutine     _countdownCoroutine;

    // ── Constructor ────────────────────────────────────────────────────────

    public OTPScreenController(VisualElement root, MonoBehaviour host)
    {
        _host = host;

        _step1        = root.Q<VisualElement>("step-1");
        _step2        = root.Q<VisualElement>("step-2");
        _inputEmail   = root.Q<TextField>("input-email");
        _inputOtp     = root.Q<TextField>("input-otp");
        _btnSendCode  = root.Q<Button>("btn-send-code");
        _btnVerify    = root.Q<Button>("btn-verify");
        _btnBack      = root.Q<Button>("btn-back");
        _btnResend    = root.Q<Button>("btn-resend");
        _lblSubtitle  = root.Q<Label>("lbl-otp-subtitle");
        _lblStatus    = root.Q<Label>("lbl-otp-status");
        _lblCountdown = root.Q<Label>("lbl-countdown");

        if (_btnSendCode != null) _btnSendCode.clicked += HandleSendCode;
        if (_btnVerify != null)   _btnVerify.clicked   += HandleVerify;
        if (_btnBack != null)     _btnBack.clicked     += () => OnBack?.Invoke();
        if (_btnResend != null)   _btnResend.clicked   += HandleResend;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Reset to step 1 and clear all inputs/state.</summary>
    public void Reset()
    {
        _pendingEmail = "";
        if (_inputEmail != null) _inputEmail.value = "";
        if (_inputOtp != null)   _inputOtp.value   = "";
        if (_lblStatus != null)  _lblStatus.text   = "";

        if (_countdownCoroutine != null && _host != null)
            _host.StopCoroutine(_countdownCoroutine);
        _countdownCoroutine = null;

        if (_btnResend != null)    _btnResend.SetEnabled(true);
        if (_lblCountdown != null) _lblCountdown.text = "";

        ShowStep(1);
    }

    public void SetStatus(string message, bool isError = true)
    {
        if (_lblStatus == null) return;
        _lblStatus.text = message;
        _lblStatus.style.color = isError
            ? new StyleColor(new Color(0.973f, 0.443f, 0.443f))   // --bm-error #F87171
            : new StyleColor(new Color(0.204f, 0.827f, 0.600f));  // --bm-success #34D399
    }

    public void SetLoading(bool loading)
    {
        _btnSendCode?.SetEnabled(!loading);
        _btnVerify?.SetEnabled(!loading);
        if (_btnResend != null && (_countdownCoroutine == null || loading))
            _btnResend.SetEnabled(!loading);
    }

    // ── Private ────────────────────────────────────────────────────────────

    private static readonly Regex EmailRegex = new Regex(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    private void HandleSendCode()
    {
        var email = _inputEmail?.value?.Trim() ?? "";
        if (string.IsNullOrEmpty(email) || !EmailRegex.IsMatch(email))
        {
            SetStatus("Please enter a valid email address.");
            return;
        }
        _pendingEmail = email;
        if (_lblStatus != null) _lblStatus.text = "";
        OnSendCode?.Invoke(email);
    }

    private static bool IsDigitsOnly(string s)
    {
        if (s.Length == 0) return false;
        foreach (char c in s) if (c < '0' || c > '9') return false;
        return true;
    }

    private void HandleVerify()
    {
        if (string.IsNullOrEmpty(_pendingEmail)) { SetStatus("Something went wrong. Please go back and re-enter your email."); return; }
        var otp = _inputOtp?.value?.Trim() ?? "";
        if (otp.Length != 6 || !IsDigitsOnly(otp))
        {
            SetStatus("Please enter the 6-digit code.");
            return;
        }
        if (_lblStatus != null) _lblStatus.text = "";
        OnVerify?.Invoke(_pendingEmail, otp);
    }

    private void HandleResend()
    {
        if (string.IsNullOrEmpty(_pendingEmail)) return;
        if (_inputOtp != null)  _inputOtp.value = "";
        if (_lblStatus != null) _lblStatus.text = "";
        OnSendCode?.Invoke(_pendingEmail);
    }

    /// <summary>Called by BlockmakerAuthUI after OTP is sent successfully.</summary>
    public void AdvanceToStep2(string email)
    {
        _pendingEmail = email;
        if (_lblSubtitle != null) _lblSubtitle.text = $"Code sent to {email}";
        if (_inputOtp != null)    _inputOtp.value   = "";
        if (_lblStatus != null)   _lblStatus.text   = "";
        ShowStep(2);
        StartCountdown();
    }

    private void ShowStep(int step)
    {
        if (_step1 != null) _step1.style.display = step == 1 ? DisplayStyle.Flex : DisplayStyle.None;
        if (_step2 != null) _step2.style.display = step == 2 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void StartCountdown()
    {
        if (_host == null) return;
        if (_countdownCoroutine != null)
            _host.StopCoroutine(_countdownCoroutine);
        _countdownCoroutine = _host.StartCoroutine(CountdownRoutine());
    }

    private IEnumerator CountdownRoutine()
    {
        if (_btnResend != null) _btnResend.SetEnabled(false);
        int remaining = ResendCooldownSeconds;
        while (remaining > 0 && _host != null)
        {
            if (_lblCountdown != null) _lblCountdown.text = $"Resend in {remaining}s";
            yield return new WaitForSecondsRealtime(1f);
            remaining--;
        }
        if (_lblCountdown != null) _lblCountdown.text = "";
        if (_btnResend != null) _btnResend.SetEnabled(true);
    }
}
