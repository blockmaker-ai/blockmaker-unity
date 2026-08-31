using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;

namespace Blockmaker.Tests
{
    public sealed class NfturboPackShopAuthTests
    {
        private const string Wallet =
            "AEAQCAIBAEAQCAIBAEAQCAIBAEAQCAIBAEAQCAIBAEAQCAIBAEA5RCDXMI";
        private const string GameId = "8ce5540810522886dc7957d8";
        private const string Origin = "https://nfturbo.example";

        private GameObject _gameObject;
        private BlockmakerConfig _config;
        private BlockmakerClient _client;

        [TearDown]
        public void TearDown()
        {
            foreach (string provider in new[] { "pera", "lute" })
            {
                string key = BlockmakerPrefs.Key("wc_session_" + provider);
                SecurePrefs.DeleteKey(key);
                SecurePrefs.DeleteKey(key + "_wcv1");
            }
            SecurePrefs.Save();
            if (_gameObject != null) UnityEngine.Object.DestroyImmediate(_gameObject);
            if (_config != null) UnityEngine.Object.DestroyImmediate(_config);
        }

        private BlockmakerClient CreateClient(string editorKey = "sk_editor_only")
        {
            _config = ScriptableObject.CreateInstance<BlockmakerConfig>();
            _config.gameId = GameId;
            _config.serverUrl = "https://blockmaker.polaris.city";
            _config.apiKey = editorKey;
            _gameObject = new GameObject("NfturboPackShopAuthTests");
            _client = _gameObject.AddComponent<BlockmakerClient>();
            _client.config = _config;
            _client.InitFromAuth();
            return _client;
        }

        private static MethodInfo PrivateMethod(string name)
        {
            return typeof(BlockmakerClient).GetMethod(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private static MethodInfo PrivateStaticMethod(string name)
        {
            return typeof(BlockmakerClient).GetMethod(
                name, BindingFlags.Static | BindingFlags.NonPublic);
        }

        private static FieldInfo PrivateField(Type type, string name)
        {
            return type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private static PropertyInfo PrivateAuthProperty(string name)
        {
            return typeof(BlockmakerAuth).GetProperty(
                name, BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
        }

        private static MethodInfo PrivateAuthMethod(string name)
        {
            return typeof(BlockmakerAuth).GetMethod(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private static byte[] DecodeAddressPublicKey(string address)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var decoded = new byte[address.Length * 5 / 8];
            int buffer = 0;
            int bits = 0;
            int offset = 0;
            for (int i = 0; i < address.Length; i++)
            {
                int value = alphabet.IndexOf(address[i]);
                Assert.That(value, Is.GreaterThanOrEqualTo(0));
                buffer = (buffer << 5) | value;
                bits += 5;
                if (bits >= 8)
                {
                    bits -= 8;
                    decoded[offset++] = (byte)((buffer >> bits) & 0xff);
                }
            }
            return decoded.Take(32).ToArray();
        }

        private static string Base32Encode(byte[] data)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var result = new StringBuilder();
            int buffer = 0;
            int bits = 0;
            foreach (byte part in data)
            {
                buffer = (buffer << 8) | part;
                bits += 8;
                while (bits >= 5)
                {
                    bits -= 5;
                    result.Append(alphabet[(buffer >> bits) & 0x1f]);
                }
            }
            if (bits > 0) result.Append(alphabet[(buffer << (5 - bits)) & 0x1f]);
            return result.ToString();
        }

        private static void WriteFixString(Stream stream, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            Assert.That(bytes.Length, Is.LessThanOrEqualTo(31));
            stream.WriteByte((byte)(0xa0 | bytes.Length));
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteBinary(Stream stream, byte[] value)
        {
            if (value.Length <= byte.MaxValue)
            {
                stream.WriteByte(0xc4);
                stream.WriteByte((byte)value.Length);
            }
            else
            {
                Assert.That(value.Length, Is.LessThanOrEqualTo(ushort.MaxValue));
                stream.WriteByte(0xc5);
                stream.WriteByte((byte)(value.Length >> 8));
                stream.WriteByte((byte)value.Length);
            }
            stream.Write(value, 0, value.Length);
        }

        private static void WriteUnsigned(Stream stream, ulong value)
        {
            if (value <= 0x7f)
            {
                stream.WriteByte((byte)value);
                return;
            }
            int byteCount;
            if (value <= byte.MaxValue) { stream.WriteByte(0xcc); byteCount = 1; }
            else if (value <= ushort.MaxValue) { stream.WriteByte(0xcd); byteCount = 2; }
            else if (value <= uint.MaxValue) { stream.WriteByte(0xce); byteCount = 4; }
            else { stream.WriteByte(0xcf); byteCount = 8; }
            for (int shift = (byteCount - 1) * 8; shift >= 0; shift -= 8)
                stream.WriteByte((byte)(value >> shift));
        }

        private static byte[] EncodeCanonicalTransaction(
            SortedDictionary<string, object> fields)
        {
            Assert.That(fields.Count, Is.LessThanOrEqualTo(15));
            using (var stream = new MemoryStream())
            {
                stream.WriteByte((byte)(0x80 | fields.Count));
                foreach (var field in fields)
                {
                    WriteFixString(stream, field.Key);
                    if (field.Value is string text) WriteFixString(stream, text);
                    else if (field.Value is byte[] binary) WriteBinary(stream, binary);
                    else WriteUnsigned(stream, (ulong)field.Value);
                }
                return stream.ToArray();
            }
        }

        private static void SetChallengeTransaction(
            NfturboPackShopChallengeResult challenge,
            Action<SortedDictionary<string, object>> mutate = null)
        {
            byte[] publicKey = DecodeAddressPublicKey(challenge.walletAddress);
            var fields = new SortedDictionary<string, object>(StringComparer.Ordinal)
            {
                ["fee"] = 1_000UL,
                ["fv"] = (ulong)challenge.firstValidRound,
                ["gen"] = "mainnet-v1.0",
                ["gh"] = Convert.FromBase64String(
                    "wGHE2Pwdvd7S12BL5FaOP20EGYesN73ktiC1qzkkit8="),
                ["lv"] = (ulong)challenge.lastValidRound,
                ["note"] = Encoding.UTF8.GetBytes(challenge.message),
                ["rcv"] = publicKey,
                ["snd"] = publicKey,
                ["type"] = "pay",
            };
            mutate?.Invoke(fields);
            SetEncodedChallenge(challenge, EncodeCanonicalTransaction(fields));
        }

        private static void SetEncodedChallenge(
            NfturboPackShopChallengeResult challenge,
            byte[] encoded)
        {
            challenge.unsignedTxnBase64 = Convert.ToBase64String(encoded);
            challenge.txId = Base32Encode(
                XChainAddressDeriver.ComputeTransactionId(encoded));
        }

        private static NfturboPackShopChallengeResult ValidChallenge()
        {
            long expiresAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 5 * 60_000;
            string nonce = new string('N', 32);
            string message = string.Join("\n", new[]
            {
                "Blockmaker NFTURBO Pack Shop sign-in",
                "purpose: nfturbo_pack_shop",
                "developer: " + GameId,
                "network: mainnet",
                "provider: pera",
                "address: " + Wallet,
                "policy: " + new string('a', 64),
                "origin: " + Origin,
                "client: unity_webgl",
                "nonce: " + nonce,
                "expires: " + DateTimeOffset.FromUnixTimeMilliseconds(expiresAt)
                    .UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                        CultureInfo.InvariantCulture),
            });
            var challenge = new NfturboPackShopChallengeResult
            {
                success = true,
                challengeVersion = NfturboPackShopAuthContract.ChallengeVersion,
                purpose = NfturboPackShopAuthContract.Purpose,
                gameId = GameId,
                network = NfturboPackShopAuthContract.Network,
                providerId = "pera",
                clientKind = NfturboPackShopAuthContract.ClientKind,
                origin = Origin,
                walletAddress = Wallet,
                nonce = nonce,
                message = message,
                firstValidRound = 50_000_000,
                lastValidRound = 50_000_040,
                expiresAt = expiresAt,
            };
            SetChallengeTransaction(challenge);
            return challenge;
        }

        private static bool ValidateChallenge(
            NfturboPackShopChallengeResult challenge,
            string provider = "pera")
        {
            var args = new object[] { challenge, Wallet, provider, GameId, null };
            return (bool)PrivateStaticMethod("ValidateNfturboPackShopChallenge")
                .Invoke(null, args);
        }

        private static bool ValidateVerification(NfturboPackShopVerifyResult verified)
        {
            var args = new object[] { verified, Wallet, "pera", GameId, null };
            return (bool)PrivateStaticMethod("ValidateNfturboPackShopVerification")
                .Invoke(null, args);
        }

        private static string StageTaggedShopWalletConnect(
            BlockmakerAuth auth,
            string provider,
            Action<IBlockmakerIdentity> onSuccess = null,
            Action<string> onError = null)
        {
            PrivateField(typeof(BlockmakerAuth), "_isWalletConnecting")
                .SetValue(auth, true);
            PrivateField(typeof(BlockmakerAuth), "_nfturboPackShopConnectInFlight")
                .SetValue(auth, true);
            PrivateField(typeof(BlockmakerAuth), "_pendingConnectSuccess")
                .SetValue(auth, onSuccess);
            PrivateField(typeof(BlockmakerAuth), "_pendingConnectError")
                .SetValue(auth, onError);
            PrivateAuthProperty("IsAuthenticating").SetValue(auth, true);
            return (string)PrivateAuthMethod("BeginWalletConnectAttempt")
                .Invoke(auth, new object[] { provider });
        }

        private static void CompleteTaggedConnect(
            BlockmakerAuth auth,
            string provider,
            string attemptId)
        {
            if (provider == BlockmakerAuth.ProviderPera)
                auth.OnPeraJsConnected(attemptId + "|" + Wallet);
            else
                auth.OnWalletConnectedFromJS(attemptId + "|Lute:" + Wallet);
        }

        private static void ErrorTaggedConnect(
            BlockmakerAuth auth,
            string provider,
            string attemptId)
        {
            if (provider == BlockmakerAuth.ProviderPera)
                auth.OnPeraJsError(attemptId + "|late Pera error");
            else
                auth.OnWalletErrorFromJS(attemptId + "|late Lute error");
        }

        [TestCase("/v1/pack-shop", true)]
        [TestCase("/v1/pack-shop/info", true)]
        [TestCase("/v1/pack-shop/history?wallet=ABC", true)]
        [TestCase("/v1/pack-shop-evil", false)]
        [TestCase("/v1/auth/session", false)]
        [TestCase("/v1/pack-shop/../auth/session", false)]
        [TestCase("/v1/pack-shop/%2e%2e/auth/session", false)]
        [TestCase("/v1/pack-shop/%2Fauth/session", false)]
        [TestCase("/v1/pack-shop/\tcommit", false)]
        [TestCase("https://attacker.example/v1/pack-shop", false)]
        public void ScopedPathGateHasAnExactPackShopBoundary(string path, bool expected)
        {
            var allowed = (bool)PrivateStaticMethod("IsExactNfturboPackShopPath")
                .Invoke(null, new object[] { path });
            Assert.That(allowed, Is.EqualTo(expected));
        }

        [Test]
        public void ScopedChallengeCarriesClientAndGameButNoGenericCredential()
        {
            var client = CreateClient();
            var build = PrivateMethod("BuildNfturboPackShopAuthPost");
            using (var request = (UnityWebRequest)build.Invoke(client, new object[]
            {
                client.BaseUrl + "/v1/auth/wallet/nfturbo-pack-shop/challenge",
                "{}",
                10f,
            }))
            {
                Assert.That(request.GetRequestHeader("X-Blockmaker-Game"), Is.EqualTo(GameId));
                Assert.That(request.GetRequestHeader("X-Blockmaker-Client"),
                    Is.EqualTo("unity-webgl"));
                Assert.That(request.GetRequestHeader("Authorization"), Is.Null.Or.Empty);
                Assert.That(request.redirectLimit, Is.EqualTo(0));
            }
        }

        [Test]
        public void ScopedCredentialIsAttachedOnlyByScopedPackShopBuilders()
        {
            var client = CreateClient();
            const string scopedToken = "scoped.jwt.must-not-become-generic";
            using (var request = (UnityWebRequest)PrivateMethod("BuildNfturboPackShopPost")
                .Invoke(client, new object[]
                {
                    client.BaseUrl + "/v1/pack-shop/commit", "{}", 10f, scopedToken,
                }))
            {
                Assert.That(request.GetRequestHeader("Authorization"),
                    Is.EqualTo("Bearer " + scopedToken));
                Assert.That(request.GetRequestHeader("X-Blockmaker-Client"),
                    Is.EqualTo("unity-webgl"));
                Assert.That(request.redirectLimit, Is.EqualTo(0));
            }

            using (var request = (UnityWebRequest)PrivateMethod("BuildNfturboPackShopGet")
                .Invoke(client, new object[]
                {
                    client.BaseUrl + "/v1/pack-shop/info", 10f, scopedToken,
                }))
            {
                Assert.That(request.GetRequestHeader("Authorization"),
                    Is.EqualTo("Bearer " + scopedToken));
                Assert.That(request.redirectLimit, Is.EqualTo(0));
            }

            using (var generic = (UnityWebRequest)PrivateMethod("BuildPost")
                .Invoke(client, new object[]
                {
                    client.BaseUrl + "/v1/profile", "{}", 10f, true,
                }))
            {
                Assert.That(generic.GetRequestHeader("Authorization"),
                    Is.EqualTo("Bearer sk_editor_only"));
                Assert.That(generic.GetRequestHeader("Authorization"),
                    Does.Not.Contain(scopedToken));
            }
        }

        [Test]
        public void PublicShopInfoTransportCarriesNoCredentialAndCannotRedirect()
        {
            var client = CreateClient();
            using (var request = (UnityWebRequest)PrivateMethod(
                "BuildNfturboPackShopPublicGet").Invoke(client, new object[]
                {
                    client.BaseUrl + "/v1/pack-shop/info", 10f,
                }))
            {
                Assert.That(request.GetRequestHeader("Authorization"), Is.Null.Or.Empty);
                Assert.That(request.GetRequestHeader("X-Blockmaker-Game"),
                    Is.EqualTo(GameId));
                Assert.That(request.GetRequestHeader("X-Blockmaker-Client"),
                    Is.EqualTo("unity-webgl"));
                Assert.That(request.redirectLimit, Is.EqualTo(0));
            }

            Assert.That(typeof(BlockmakerClient).GetMethod(
                "GetNfturboPackShopPublic", BindingFlags.Instance | BindingFlags.Public),
                Is.Not.Null);
            string error = null;
            client.GetNfturboPackShopPublic<NfturboPackShopOptInSubmitResult>(
                "/v1/pack-shop/info?redirect=1", null, value => error = value);
            Assert.That(error, Does.Contain("public NFTURBO Store catalogue"));
        }

        [Test]
        public void ServerAuthoredChallengeMustMatchEveryScopedBindingBeforeSigning()
        {
            var challenge = ValidChallenge();
            Assert.That(ValidateChallenge(challenge), Is.True);

            challenge.clientKind = "web";
            Assert.That(ValidateChallenge(challenge), Is.False);
            challenge = ValidChallenge();
            challenge.providerId = "lute";
            Assert.That(ValidateChallenge(challenge), Is.False);
            challenge = ValidChallenge();
            challenge.lastValidRound = challenge.firstValidRound + 41;
            Assert.That(ValidateChallenge(challenge), Is.False);
            challenge = ValidChallenge();
            challenge.unsignedTxnBase64 = "not base64";
            Assert.That(ValidateChallenge(challenge), Is.False);
            challenge = ValidChallenge();
            challenge.message = challenge.message.Replace(
                "purpose: nfturbo_pack_shop", "purpose: career");
            Assert.That(ValidateChallenge(challenge), Is.False);

            challenge = ValidChallenge();
            challenge.expiresAt = long.MaxValue;
            Assert.That(ValidateChallenge(challenge), Is.False,
                "Malformed server time must fail closed instead of escaping validation.");

            challenge = ValidChallenge();
            string canonicalExpiry = DateTimeOffset
                .FromUnixTimeMilliseconds(challenge.expiresAt)
                .UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                    CultureInfo.InvariantCulture);
            challenge.message = challenge.message.Replace(canonicalExpiry,
                DateTimeOffset.FromUnixTimeMilliseconds(challenge.expiresAt)
                    .UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            SetChallengeTransaction(challenge);
            Assert.That(ValidateChallenge(challenge), Is.False,
                "A parse-equivalent expiry is not the exact server-authored note.");
        }

        [Test]
        public void ScopedChallengeRejectsAValueMovingOrSurprisinglyExpensivePayment()
        {
            var amount = ValidChallenge();
            SetChallengeTransaction(amount, fields => fields["amt"] = 1UL);
            Assert.That(ValidateChallenge(amount), Is.False);

            var explicitZeroAmount = ValidChallenge();
            SetChallengeTransaction(explicitZeroAmount, fields => fields["amt"] = 0UL);
            Assert.That(ValidateChallenge(explicitZeroAmount), Is.False,
                "The exact algosdk-authored zero payment omits its default amount field.");

            var fee = ValidChallenge();
            SetChallengeTransaction(fee, fields => fields["fee"] = 2_000UL);
            Assert.That(ValidateChallenge(fee), Is.False);

            var receiver = ValidChallenge();
            SetChallengeTransaction(receiver, fields => fields["rcv"] =
                Enumerable.Repeat((byte)2, 32).ToArray());
            Assert.That(ValidateChallenge(receiver), Is.False);

            var sender = ValidChallenge();
            SetChallengeTransaction(sender, fields => fields["snd"] =
                Enumerable.Repeat((byte)2, 32).ToArray());
            Assert.That(ValidateChallenge(sender), Is.False);
        }

        [Test]
        public void ScopedChallengeRejectsAnyFieldOutsideTheExactSelfPayment()
        {
            foreach (string forbidden in new[] { "grp", "rekey", "close", "lx", "apaa" })
            {
                var challenge = ValidChallenge();
                SetChallengeTransaction(challenge, fields =>
                    fields[forbidden] = Enumerable.Repeat((byte)3, 32).ToArray());
                Assert.That(ValidateChallenge(challenge), Is.False,
                    "Unexpected field was accepted: " + forbidden);
            }
        }

        [Test]
        public void ScopedChallengeBindsNetworkRoundsNoteAndTransactionIdToSignedBytes()
        {
            var genesisId = ValidChallenge();
            SetChallengeTransaction(genesisId, fields => fields["gen"] = "testnet-v1.0");
            Assert.That(ValidateChallenge(genesisId), Is.False);

            var genesisHash = ValidChallenge();
            SetChallengeTransaction(genesisHash, fields => fields["gh"] = new byte[32]);
            Assert.That(ValidateChallenge(genesisHash), Is.False);

            var firstRound = ValidChallenge();
            SetChallengeTransaction(firstRound, fields =>
                fields["fv"] = (ulong)(firstRound.firstValidRound + 1));
            Assert.That(ValidateChallenge(firstRound), Is.False);

            var lastRound = ValidChallenge();
            SetChallengeTransaction(lastRound, fields =>
                fields["lv"] = (ulong)(lastRound.lastValidRound - 1));
            Assert.That(ValidateChallenge(lastRound), Is.False);

            var note = ValidChallenge();
            SetChallengeTransaction(note, fields =>
                fields["note"] = Encoding.UTF8.GetBytes(note.message + "\nmalicious"));
            Assert.That(ValidateChallenge(note), Is.False);

            var type = ValidChallenge();
            SetChallengeTransaction(type, fields => fields["type"] = "axfer");
            Assert.That(ValidateChallenge(type), Is.False);

            var wrongId = ValidChallenge();
            wrongId.txId = new string('A', 52);
            Assert.That(ValidateChallenge(wrongId), Is.False);

            var trailing = ValidChallenge();
            byte[] original = Convert.FromBase64String(trailing.unsignedTxnBase64);
            SetEncodedChallenge(trailing, original.Concat(new byte[] { 0 }).ToArray());
            Assert.That(ValidateChallenge(trailing), Is.False);
        }

        [Test]
        public void ScopedChallengeRejectsNonCanonicalMsgpackEncodings()
        {
            var wideMap = ValidChallenge();
            byte[] canonical = Convert.FromBase64String(wideMap.unsignedTxnBase64);
            Assert.That(canonical[0], Is.EqualTo(0x89));
            SetEncodedChallenge(wideMap, new byte[] { 0xde, 0x00, 0x09 }
                .Concat(canonical.Skip(1)).ToArray());
            Assert.That(ValidateChallenge(wideMap), Is.False,
                "The exact nine-field transaction must use msgpack's shortest map form.");

            var wideFee = ValidChallenge();
            canonical = Convert.FromBase64String(wideFee.unsignedTxnBase64);
            Assert.That(canonical.Take(8), Is.EqualTo(new byte[]
                { 0x89, 0xa3, (byte)'f', (byte)'e', (byte)'e', 0xcd, 0x03, 0xe8 }));
            SetEncodedChallenge(wideFee, canonical.Take(5)
                .Concat(new byte[] { 0xce, 0x00, 0x00, 0x03, 0xe8 })
                .Concat(canonical.Skip(8)).ToArray());
            Assert.That(ValidateChallenge(wideFee), Is.False,
                "The exact fee must use msgpack's shortest unsigned-integer form.");
        }

        [Test]
        public void VerificationAcceptsOnlyShortLivedShopScopeForTheSameWallet()
        {
            var verified = new NfturboPackShopVerifyResult
            {
                success = true,
                sessionToken = new string('a', 30) + "." +
                    new string('b', 30) + "." + new string('c', 30),
                sessionScope = NfturboPackShopAuthContract.Purpose,
                expiresInSeconds = NfturboPackShopAuthContract.MaximumTtlSeconds,
                walletAddress = Wallet,
                accountKind = "algorand_wallet",
                authProvider = "pera",
                gameId = GameId,
                network = NfturboPackShopAuthContract.Network,
            };
            Assert.That(ValidateVerification(verified), Is.True);

            verified.sessionScope = "career";
            Assert.That(ValidateVerification(verified), Is.False);
            verified.sessionScope = NfturboPackShopAuthContract.Purpose;
            verified.expiresInSeconds = NfturboPackShopAuthContract.MaximumTtlSeconds + 1;
            Assert.That(ValidateVerification(verified), Is.False);
            verified.expiresInSeconds = NfturboPackShopAuthContract.MaximumTtlSeconds;
            verified.authProvider = "lute";
            Assert.That(ValidateVerification(verified), Is.False);
        }

        [Test]
        public void ScopedSessionTokenHasNoPublicStringGetter()
        {
            var publicTokenSurface = typeof(BlockmakerClient)
                .GetMembers(BindingFlags.Instance | BindingFlags.Public)
                .Where(member => member.Name.IndexOf("NfturboPackShop", StringComparison.Ordinal) >= 0)
                .Where(member =>
                    (member is PropertyInfo property && property.PropertyType == typeof(string)) ||
                    (member is MethodInfo method && method.ReturnType == typeof(string)))
                .ToArray();
            Assert.That(publicTokenSurface, Is.Empty);
            Assert.That(typeof(BlockmakerClient).GetProperty("HasNfturboPackShopSession")
                ?.PropertyType, Is.EqualTo(typeof(bool)));
        }

        [Test]
        public void OnlyConcretePeraAndLuteIdentitiesEnterTheScopedFlow()
        {
            var method = PrivateStaticMethod("TryNfturboPackShopProvider");
            var peraArgs = new object[] { new PeraIdentity(Wallet), null };
            var luteArgs = new object[] { new LuteIdentity(Wallet), null };
            var deflyArgs = new object[] { new DeflyIdentity(Wallet), null };

            Assert.That((bool)method.Invoke(null, peraArgs), Is.True);
            Assert.That(peraArgs[1], Is.EqualTo("pera"));
            Assert.That((bool)method.Invoke(null, luteArgs), Is.True);
            Assert.That(luteArgs[1], Is.EqualTo("lute"));
            Assert.That((bool)method.Invoke(null, deflyArgs), Is.False);
            Assert.That(deflyArgs[1], Is.Null);
        }

        [Test]
        public void CancelledScopedWalletCallbackCannotCompleteAFutureRetry()
        {
            CreateClient();
            var auth = _gameObject.AddComponent<BlockmakerAuth>();
            var identity = new PeraIdentity(Wallet);
            PrivateAuthMethod("SetIdentity").Invoke(auth, new object[] { identity });

            var begin = PrivateAuthMethod("BeginNfturboPackShopSigningAttempt");
            var cancel = PrivateAuthMethod("CancelNfturboPackShopSigning");
            var signedField = PrivateField(
                typeof(BlockmakerAuth), "_nfturboPackShopSignedTxn");

            string cancelledAttempt = (string)begin.Invoke(auth, new object[] { identity });
            Assert.That(cancel.Invoke(auth, new object[] { identity }), Is.EqualTo(true));
            string currentAttempt = (string)begin.Invoke(auth, new object[] { identity });

            auth.OnNfturboPackShopTxnSignedFromJS(cancelledAttempt + "|late-signed-bytes");
            Assert.That(signedField.GetValue(auth), Is.Null,
                "A cancelled callback must not populate the next attempt's result slot.");

            auth.OnNfturboPackShopTxnSignedFromJS(currentAttempt + "|current-signed-bytes");
            Assert.That(signedField.GetValue(auth), Is.EqualTo("current-signed-bytes"));
            cancel.Invoke(auth, new object[] { identity });
        }

        [Test]
        public void ClearingScopedAuthReleasesAPreSignLuteWindowReservation()
        {
            var client = CreateClient();
            var reserved = PrivateField(
                typeof(BlockmakerClient), "_nfturboPackShopLutePrimeReserved");
            reserved.SetValue(client, true);

            client.ClearNfturboPackShopSession();

            Assert.That(reserved.GetValue(client), Is.EqualTo(false));
        }

        [Test]
        public void ExistingLutePrimeReservationIsReusedInsteadOfReplaced()
        {
            var client = CreateClient();
            var auth = _gameObject.AddComponent<BlockmakerAuth>();
            typeof(BlockmakerAuth).GetProperty("Instance",
                BindingFlags.Static | BindingFlags.Public)
                .SetValue(null, auth);
            var identity = new LuteIdentity(Wallet);
            PrivateAuthMethod("SetIdentity").Invoke(auth, new object[] { identity });
            var reserved = PrivateField(
                typeof(BlockmakerClient), "_nfturboPackShopLutePrimeReserved");
            reserved.SetValue(client, true);
            PrivateField(typeof(BlockmakerClient),
                "_nfturboPackShopLutePrimeIdentity").SetValue(client, identity);

            // The non-WebGL bridge returns false. A true result here proves the
            // already-reserved browser window was reused without calling it again.
            Assert.That(client.PrimeNfturboPackShopApprovalWindow(), Is.True);
            Assert.That(reserved.GetValue(client), Is.EqualTo(true));
        }

        [Test]
        public void LutePrimeReservationIsConsumedBeforeAnExternalSign()
        {
            var client = CreateClient();
            var auth = _gameObject.AddComponent<BlockmakerAuth>();
            typeof(BlockmakerAuth).GetProperty("Instance",
                BindingFlags.Static | BindingFlags.Public)
                .SetValue(null, auth);
            var identity = new LuteIdentity(Wallet);
            PrivateAuthMethod("SetIdentity").Invoke(auth, new object[] { identity });
            var reserved = PrivateField(
                typeof(BlockmakerClient), "_nfturboPackShopLutePrimeReserved");
            reserved.SetValue(client, true);
            PrivateField(typeof(BlockmakerClient),
                "_nfturboPackShopLutePrimeIdentity").SetValue(client, identity);

            Assert.That(client.TryConsumeNfturboPackShopApprovalWindow(
                new LuteIdentity(Wallet)), Is.False,
                "A lookalike identity must not consume another sign's reservation.");
            Assert.That(reserved.GetValue(client), Is.EqualTo(true));
            Assert.That(client.TryConsumeNfturboPackShopApprovalWindow(identity), Is.True);
            Assert.That(reserved.GetValue(client), Is.EqualTo(false));

            // Outside WebGL an actual prime returns false. This proves the stale
            // marker no longer short-circuits the next player-click prime attempt.
            Assert.That(client.PrimeNfturboPackShopApprovalWindow(), Is.False);
        }

        [Test]
        public void UnusedLutePrimeReservationCanBeCancelledByItsOwner()
        {
            var client = CreateClient();
            var auth = _gameObject.AddComponent<BlockmakerAuth>();
            typeof(BlockmakerAuth).GetProperty("Instance",
                BindingFlags.Static | BindingFlags.Public)
                .SetValue(null, auth);
            var identity = new LuteIdentity(Wallet);
            PrivateAuthMethod("SetIdentity").Invoke(auth, new object[] { identity });
            var reserved = PrivateField(
                typeof(BlockmakerClient), "_nfturboPackShopLutePrimeReserved");
            reserved.SetValue(client, true);
            PrivateField(typeof(BlockmakerClient),
                "_nfturboPackShopLutePrimeIdentity").SetValue(client, identity);

            Assert.That(client.CancelUnusedNfturboPackShopApprovalWindow(
                new LuteIdentity(Wallet)), Is.False);
            Assert.That(reserved.GetValue(client), Is.EqualTo(true));
            Assert.That(client.CancelUnusedNfturboPackShopApprovalWindow(identity), Is.True);
            Assert.That(reserved.GetValue(client), Is.EqualTo(false));
        }

        [Test]
        public void LutePrimeReservationCannotFollowAReplacementIdentityObject()
        {
            var client = CreateClient();
            var auth = _gameObject.AddComponent<BlockmakerAuth>();
            var owner = new LuteIdentity(Wallet);
            PrivateAuthMethod("SetIdentity").Invoke(auth, new object[] { owner });
            var reserved = PrivateField(
                typeof(BlockmakerClient), "_nfturboPackShopLutePrimeReserved");
            var reservationOwner = PrivateField(
                typeof(BlockmakerClient), "_nfturboPackShopLutePrimeIdentity");
            reserved.SetValue(client, true);
            reservationOwner.SetValue(client, owner);

            var replacement = new LuteIdentity(Wallet);
            PrivateAuthMethod("SetIdentity").Invoke(auth, new object[] { replacement });

            Assert.That(client.TryConsumeNfturboPackShopApprovalWindow(replacement),
                Is.False);
            Assert.That(client.PrimeNfturboPackShopApprovalWindow(), Is.False,
                "Editor cannot prime; the old identity's marker must not short-circuit.");
            Assert.That(reserved.GetValue(client), Is.EqualTo(false));
            Assert.That(reservationOwner.GetValue(client), Is.Null);
        }

        [TestCase(BlockmakerAuth.ProviderPera)]
        [TestCase(BlockmakerAuth.ProviderLute)]
        public void CancelledWalletConnectCallbacksCannotConsumeASuccessor(
            string provider)
        {
            CreateClient();
            var auth = _gameObject.AddComponent<BlockmakerAuth>();
            int oldSuccess = 0;
            int oldErrors = 0;
            int currentSuccess = 0;
            int qrCount = 0;
            Action<WalletQREventArgs> qrHandler = _ => qrCount++;
            BlockmakerAuth.OnWalletQRReady += qrHandler;
            try
            {
                string cancelled = StageTaggedShopWalletConnect(
                    auth, provider, _ => oldSuccess++, _ => oldErrors++);
                auth.CancelWalletConnect();
                string current = StageTaggedShopWalletConnect(
                    auth, provider, _ => currentSuccess++);

                if (provider == BlockmakerAuth.ProviderPera)
                    auth.OnWalletQRFromJS(
                        cancelled + "|Pera|wc:late|late-image");
                ErrorTaggedConnect(auth, provider, cancelled);
                CompleteTaggedConnect(auth, provider, cancelled);

                Assert.That(oldSuccess, Is.Zero);
                Assert.That(oldErrors, Is.Zero);
                Assert.That(currentSuccess, Is.Zero);
                Assert.That(qrCount, Is.Zero);
                Assert.That(PrivateField(typeof(BlockmakerAuth),
                    "_walletConnectAttemptId").GetValue(auth), Is.EqualTo(current));
                Assert.That(PrivateField(typeof(BlockmakerAuth),
                    "_isWalletConnecting").GetValue(auth), Is.EqualTo(true));

                if (provider == BlockmakerAuth.ProviderPera)
                    auth.OnWalletQRFromJS(
                        current + "|Pera|wc:current|current-image");
                CompleteTaggedConnect(auth, provider, current);

                Assert.That(currentSuccess, Is.EqualTo(1));
                Assert.That(qrCount, Is.EqualTo(
                    provider == BlockmakerAuth.ProviderPera ? 1 : 0));
                Assert.That(auth.Identity.ProviderName, Is.EqualTo(provider));
            }
            finally
            {
                BlockmakerAuth.OnWalletQRReady -= qrHandler;
            }
        }

        [TestCase(BlockmakerAuth.ProviderPera)]
        [TestCase(BlockmakerAuth.ProviderLute)]
        public void ShopOnlyWalletConnectCannotPersistOrStartGenericLogin(
            string provider)
        {
            CreateClient();
            var auth = _gameObject.AddComponent<BlockmakerAuth>();
            string key = BlockmakerPrefs.Key(
                "wc_session_" + provider.ToLowerInvariant());
            SecurePrefs.DeleteKey(key);
            SecurePrefs.DeleteKey(key + "_wcv1");
            SecurePrefs.Save();

            string attempt = StageTaggedShopWalletConnect(auth, provider);
            CompleteTaggedConnect(auth, provider, attempt);

            var identity = auth.Identity as WalletConnectIdentity;
            Assert.That(identity, Is.Not.Null);
            Assert.That(identity.SessionToken, Is.Null.Or.Empty);
            Assert.That(identity.RefreshToken, Is.Null.Or.Empty);
            Assert.That(WalletConnectIdentity.TryLoadSessionData(provider), Is.Null);
            Assert.That(WalletConnectIdentity.TryLoadWCv1Session(provider), Is.Null);
            Assert.That(PrivateField(typeof(BlockmakerAuth), "_walletLoginInFlight")
                .GetValue(auth), Is.EqualTo(false));
            Assert.That(PrivateField(typeof(BlockmakerAuth), "_walletLoginSeq")
                .GetValue(auth), Is.EqualTo(0),
                "No generic challenge/build/JWT coroutine may start from Shop connect.");
            Assert.That(PrivateField(typeof(BlockmakerAuth), "_walletLoginCoroutine")
                .GetValue(auth), Is.Null);

            PrivateAuthMethod("SetIdentity").Invoke(auth,
                new object[] { new GuestIdentity() });
            Assert.That((bool)PrivateAuthMethod("TryRestoreSession")
                .Invoke(auth, null), Is.False,
                "A restart must have no tokenless Shop identity to auto-upgrade.");
        }

        [Test]
        public void PeraApprovalReservationOperationsAreSuccessfulNoOps()
        {
            var client = CreateClient();
            var auth = _gameObject.AddComponent<BlockmakerAuth>();
            typeof(BlockmakerAuth).GetProperty("Instance",
                BindingFlags.Static | BindingFlags.Public)
                .SetValue(null, auth);
            var identity = new PeraIdentity(Wallet);
            PrivateAuthMethod("SetIdentity").Invoke(auth, new object[] { identity });
            var reserved = PrivateField(
                typeof(BlockmakerClient), "_nfturboPackShopLutePrimeReserved");

            Assert.That(client.TryConsumeNfturboPackShopApprovalWindow(identity), Is.True);
            Assert.That(client.CancelUnusedNfturboPackShopApprovalWindow(identity), Is.True);
            Assert.That(reserved.GetValue(client), Is.EqualTo(false));
        }

        [Test]
        public void CancelledPaymentCallbackCannotCompleteAFutureRetry()
        {
            CreateClient();
            var auth = _gameObject.AddComponent<BlockmakerAuth>();
            var identity = new PeraIdentity(Wallet);
            PrivateAuthMethod("SetIdentity").Invoke(auth, new object[] { identity });
            var begin = PrivateAuthMethod("BeginAttemptTaggedPendingSign");

            var firstArgs = new object[] { identity, null };
            begin.Invoke(auth, firstArgs);
            string cancelledAttempt = (string)firstArgs[1];
            Assert.That(auth.CancelPendingWalletSign(new LuteIdentity(Wallet)), Is.False,
                "An unrelated identity must not cancel the owned payment sign.");
            Assert.That(auth.CancelPendingWalletSign(identity), Is.True);

            var retryArgs = new object[] { identity, null };
            begin.Invoke(auth, retryArgs);
            string retryAttempt = (string)retryArgs[1];
            auth.OnTxnSignedFromJS(cancelledAttempt + "|late-payment");
            Assert.That(PrivateAuthProperty("PendingSignedTxn").GetValue(auth), Is.Null);

            auth.OnTxnSignedFromJS(retryAttempt + "|current-payment");
            Assert.That(PrivateAuthProperty("PendingSignedTxn").GetValue(auth),
                Is.EqualTo("current-payment"));
            Assert.That(auth.CancelPendingWalletSign(identity), Is.True);
        }

        [Test]
        public void CancelledGroupCallbackCannotCompleteAFutureRetry()
        {
            CreateClient();
            var auth = _gameObject.AddComponent<BlockmakerAuth>();
            var identity = new LuteIdentity(Wallet);
            PrivateAuthMethod("SetIdentity").Invoke(auth, new object[] { identity });
            var begin = PrivateAuthMethod("BeginAttemptTaggedPendingSign");

            var firstArgs = new object[] { identity, null };
            begin.Invoke(auth, firstArgs);
            string cancelledAttempt = (string)firstArgs[1];
            Assert.That(auth.CancelPendingWalletSign(identity), Is.True);

            var retryArgs = new object[] { identity, null };
            begin.Invoke(auth, retryArgs);
            string retryAttempt = (string)retryArgs[1];
            auth.OnGroupTxnSignedFromJS(cancelledAttempt + "|[\"late-group\"]");
            Assert.That(PrivateAuthProperty("PendingSignedTxns").GetValue(auth), Is.Null);

            auth.OnGroupTxnSignedFromJS(retryAttempt + "|[\"current-group\"]");
            Assert.That(PrivateAuthProperty("PendingSignedTxns").GetValue(auth),
                Is.EqualTo(new[] { "current-group" }));
            Assert.That(auth.CancelPendingWalletSign(identity), Is.True);
        }

        [Test]
        public void ScopedOptInApiUsesOnlyCommitBoundPackShopContracts()
        {
            Assert.That(typeof(BlockmakerClient).GetMethod(
                "PrepareNfturboPackShopOptIns", BindingFlags.Instance | BindingFlags.Public),
                Is.Not.Null);
            Assert.That(typeof(BlockmakerClient).GetMethod(
                "SubmitNfturboPackShopOptIns", BindingFlags.Instance | BindingFlags.Public),
                Is.Not.Null);
            Assert.That(typeof(BlockmakerClient).GetMethod(
                "TryConsumeNfturboPackShopApprovalWindow",
                BindingFlags.Instance | BindingFlags.Public), Is.Not.Null);
            Assert.That(typeof(BlockmakerClient).GetMethod(
                "CancelUnusedNfturboPackShopApprovalWindow",
                BindingFlags.Instance | BindingFlags.Public), Is.Not.Null);
            Assert.That(typeof(NfturboPackShopOptInPrepareRequest).GetField("walletAddress"),
                Is.Null, "The scoped token, not a caller-supplied wallet, owns the request.");
            Assert.That(typeof(NfturboPackShopOptInSubmitRequest).GetField("optInIntent"),
                Is.Not.Null);
            Assert.That(typeof(AuthPromptController).GetMethod(
                "ShowNfturboPackShopWallets", BindingFlags.Instance | BindingFlags.Public),
                Is.Not.Null);
            Assert.That(typeof(AuthPromptController).GetEvent(
                "OnNfturboPackShopAuthSucceeded", BindingFlags.Static | BindingFlags.Public),
                Is.Not.Null);
        }
    }
}
