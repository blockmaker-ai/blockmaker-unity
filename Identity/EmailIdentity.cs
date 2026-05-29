using System;
using UnityEngine;

namespace Blockmaker;

/// <summary>
/// Tier 2 identity. Player authenticated with email + OTP.
/// Blockmaker server holds an encrypted Algorand wallet on their behalf.
/// Transaction signing is transparent — the server signs, no wallet app needed.
/// </summary>
public class EmailIdentity : ServerSignedIdentity
{
    private static string SessionKey => BlockmakerPrefs.Key("email_session");

    public override string       Address      { get; }
    public override string       DisplayName  { get; }
    public override IdentityTier Tier         => IdentityTier.Email;
    public override string       ProviderName => "Email";
    public override bool         HasWallet    => true;
    public override bool         CanSign      => true;

    public string Email { get; }
    public override string SessionToken { get; protected set; }
    public override string RefreshToken { get; protected set; }

    public EmailIdentity(string email, string walletAddress, string sessionToken, string refreshToken = null)
    {
        if (string.IsNullOrEmpty(email))
            throw new ArgumentException("Email is required.", nameof(email));
        if (!email.Contains('@'))
            throw new ArgumentException("Invalid email format", nameof(email));
        if (string.IsNullOrEmpty(walletAddress))
            throw new ArgumentException("Wallet address is required.", nameof(walletAddress));

        Email        = email;
        Address      = walletAddress;
        SessionToken = sessionToken;
        RefreshToken = refreshToken;
        DisplayName  = email.Split('@')[0];
    }

    public override void SaveSession()
    {
        var data = new EmailSessionData
        {
            email         = Email,
            walletAddress = Address,
            sessionToken  = SessionToken,
            refreshToken  = RefreshToken
        };
        SecurePrefs.SetString(SessionKey, JsonUtility.ToJson(data));
        SecurePrefs.Save();
        BlockmakerLog.Info($"[EmailIdentity] Session saved for {Email}");
    }

    public override void ClearSession()
    {
        SecurePrefs.DeleteKey(SessionKey);
        SecurePrefs.Save();
        BlockmakerLog.Info("[EmailIdentity] Session cleared.");
    }

    public static EmailIdentity TryLoadSession()
    {
        string json = SecurePrefs.GetString(SessionKey, string.Empty);
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            var data = JsonUtility.FromJson<EmailSessionData>(json);

            if (string.IsNullOrEmpty(data.walletAddress))
                return null;
            if (string.IsNullOrEmpty(data.sessionToken) && string.IsNullOrEmpty(data.refreshToken))
                return null;

            BlockmakerLog.Info($"[EmailIdentity] Restored session for {data.email}");
            return new EmailIdentity(data.email, data.walletAddress, data.sessionToken, data.refreshToken);
        }
        catch (Exception e)
        {
            BlockmakerLog.Warning($"[EmailIdentity] Corrupt session data, clearing: {e.Message}");
            SecurePrefs.DeleteKey(SessionKey);
            SecurePrefs.Save();
            return null;
        }
    }

    [Serializable]
    private class EmailSessionData
    {
        public string email;
        public string walletAddress;
        public string sessionToken;
        public string refreshToken;
    }
}
