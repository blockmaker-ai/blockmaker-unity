using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Blockmaker;

/// <summary>
/// Encrypted wrapper around PlayerPrefs. Values are stored as
/// AES-256-CBC ciphertext with HMAC-SHA256 (encrypt-then-MAC).
///
/// Key storage:
///   Native: random 32-byte key file in Application.persistentDataPath
///   WebGL:  key in PlayerPrefs (same-origin policy provides isolation)
///
/// v2 format: "$bm2:" + base64( iv[16] + hmac[32] + ciphertext )
///   - Separate encryption/MAC subkeys derived from master key
///   - HMAC covers IV + ciphertext (prevents IV bit-flipping)
/// v1 format: "$bm1:" + base64( iv[16] + hmac[32] + ciphertext )
///   - Single key for both AES and HMAC; HMAC covers ciphertext only
///   - Read-only for backward compat; re-encrypted as v2 on read
/// Legacy (non-prefixed) values are migrated on first read.
/// </summary>
public static class SecurePrefs
{
    private const string PrefixV2 = "$bm2:";
    private const string PrefixV1 = "$bm1:";
    private const int IvLen = 16;
    private const int HmacLen = 32;
    private const string KeyPlayerPrefsKey = "bm_secure_prefs_key";

    private static byte[] _key;

    private static byte[] GetOrCreateKey()
    {
        if (_key != null) return _key;

#if UNITY_WEBGL && !UNITY_EDITOR
        string stored = PlayerPrefs.GetString(KeyPlayerPrefsKey, "");
        if (!string.IsNullOrEmpty(stored))
        {
            _key = Convert.FromBase64String(stored);
            return _key;
        }
        _key = new byte[32];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(_key);
        PlayerPrefs.SetString(KeyPlayerPrefsKey, Convert.ToBase64String(_key));
        PlayerPrefs.Save();
        return _key;
#else
        string dir = Application.persistentDataPath;
        string path = Path.Combine(dir, ".bm_key");
        if (File.Exists(path))
        {
            _key = File.ReadAllBytes(path);
            if (_key.Length == 32) return _key;
        }
        _key = new byte[32];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(_key);
        File.WriteAllBytes(path, _key);
        return _key;
#endif
    }

    public static void SetString(string prefsKey, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            PlayerPrefs.DeleteKey(prefsKey);
            return;
        }

        byte[] masterKey = GetOrCreateKey();
        DeriveSubkeys(masterKey, out byte[] encKey, out byte[] macKey);
        byte[] plaintext = Encoding.UTF8.GetBytes(value);

        byte[] iv = new byte[IvLen];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(iv);

        byte[] ciphertext;
        using (var aes = Aes.Create())
        {
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = encKey;
            aes.IV = iv;
            using var enc = aes.CreateEncryptor();
            ciphertext = enc.TransformFinalBlock(plaintext, 0, plaintext.Length);
        }

        byte[] hmac;
        using (var hmacSha = new HMACSHA256(macKey))
        {
            hmacSha.TransformBlock(iv, 0, iv.Length, null, 0);
            hmacSha.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            hmac = hmacSha.Hash;
        }

        byte[] blob = new byte[IvLen + HmacLen + ciphertext.Length];
        Buffer.BlockCopy(iv, 0, blob, 0, IvLen);
        Buffer.BlockCopy(hmac, 0, blob, IvLen, HmacLen);
        Buffer.BlockCopy(ciphertext, 0, blob, IvLen + HmacLen, ciphertext.Length);

        PlayerPrefs.SetString(prefsKey, PrefixV2 + Convert.ToBase64String(blob));
    }

    public static string GetString(string prefsKey, string defaultValue = "")
    {
        string raw = PlayerPrefs.GetString(prefsKey, "");
        if (string.IsNullOrEmpty(raw)) return defaultValue;

        if (raw.StartsWith(PrefixV2))
            return DecryptV2(prefsKey, raw, defaultValue);

        if (raw.StartsWith(PrefixV1))
        {
            string value = DecryptV1(prefsKey, raw, defaultValue);
            if (value != defaultValue)
            {
                BlockmakerLog.Info($"[SecurePrefs] Migrating key '{prefsKey}' from v1 to v2 format.");
                SetString(prefsKey, value);
                PlayerPrefs.Save();
            }
            return value;
        }

        BlockmakerLog.Info($"[SecurePrefs] Migrating plaintext key '{prefsKey}' to encrypted v2 format.");
        SetString(prefsKey, raw);
        PlayerPrefs.Save();
        return raw;
    }

    private static string DecryptV2(string prefsKey, string raw, string defaultValue)
    {
        try
        {
            byte[] masterKey = GetOrCreateKey();
            DeriveSubkeys(masterKey, out byte[] encKey, out byte[] macKey);
            byte[] blob = Convert.FromBase64String(raw.Substring(PrefixV2.Length));
            if (blob.Length < IvLen + HmacLen + 1) return defaultValue;

            byte[] iv = new byte[IvLen];
            byte[] storedHmac = new byte[HmacLen];
            int ctLen = blob.Length - IvLen - HmacLen;
            byte[] ciphertext = new byte[ctLen];
            Buffer.BlockCopy(blob, 0, iv, 0, IvLen);
            Buffer.BlockCopy(blob, IvLen, storedHmac, 0, HmacLen);
            Buffer.BlockCopy(blob, IvLen + HmacLen, ciphertext, 0, ctLen);

            byte[] computedHmac;
            using (var hmacSha = new HMACSHA256(macKey))
            {
                hmacSha.TransformBlock(iv, 0, iv.Length, null, 0);
                hmacSha.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                computedHmac = hmacSha.Hash;
            }

            if (!ConstantTimeEquals(computedHmac, storedHmac))
            {
                BlockmakerLog.Warning($"[SecurePrefs] HMAC mismatch for key '{prefsKey}' — data may be tampered");
                return defaultValue;
            }

            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = encKey;
            aes.IV = iv;
            using var dec = aes.CreateDecryptor();
            byte[] plainBytes = dec.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex)
        {
            BlockmakerLog.Warning($"[SecurePrefs] Decryption failed for key '{prefsKey}': {ex.Message}");
            return defaultValue;
        }
    }

    private static string DecryptV1(string prefsKey, string raw, string defaultValue)
    {
        try
        {
            byte[] key = GetOrCreateKey();
            byte[] blob = Convert.FromBase64String(raw.Substring(PrefixV1.Length));
            if (blob.Length < IvLen + HmacLen + 1) return defaultValue;

            byte[] iv = new byte[IvLen];
            byte[] storedHmac = new byte[HmacLen];
            int ctLen = blob.Length - IvLen - HmacLen;
            byte[] ciphertext = new byte[ctLen];
            Buffer.BlockCopy(blob, 0, iv, 0, IvLen);
            Buffer.BlockCopy(blob, IvLen, storedHmac, 0, HmacLen);
            Buffer.BlockCopy(blob, IvLen + HmacLen, ciphertext, 0, ctLen);

            byte[] computedHmac;
            using (var hmacSha = new HMACSHA256(key))
                computedHmac = hmacSha.ComputeHash(ciphertext);

            if (!ConstantTimeEquals(computedHmac, storedHmac))
            {
                BlockmakerLog.Warning($"[SecurePrefs] HMAC mismatch for key '{prefsKey}' — data may be tampered");
                return defaultValue;
            }

            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            aes.IV = iv;
            using var dec = aes.CreateDecryptor();
            byte[] plainBytes = dec.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex)
        {
            BlockmakerLog.Warning($"[SecurePrefs] Decryption failed for key '{prefsKey}': {ex.Message}");
            return defaultValue;
        }
    }

    public static void DeleteKey(string prefsKey)
    {
        PlayerPrefs.DeleteKey(prefsKey);
    }

    public static bool HasKey(string prefsKey)
    {
        return PlayerPrefs.HasKey(prefsKey);
    }

    public static void Save()
    {
        PlayerPrefs.Save();
    }

    private static void DeriveSubkeys(byte[] masterKey, out byte[] encKey, out byte[] macKey)
    {
        using (var h = new HMACSHA256(masterKey))
            encKey = h.ComputeHash(Encoding.UTF8.GetBytes("blockmaker-enc"));
        using (var h = new HMACSHA256(masterKey))
            macKey = h.ComputeHash(Encoding.UTF8.GetBytes("blockmaker-mac"));
    }

    private static bool ConstantTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
