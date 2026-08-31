using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;

namespace Blockmaker.Tests
{
    public sealed class NfturboPackShopAuthTests
    {
        private const string Wallet =
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAY5HFKQ";
        private const string GameId = "8ce5540810522886dc7957d8";
        private const string Origin = "https://nfturbo.example";

        private GameObject _gameObject;
        private BlockmakerConfig _config;
        private BlockmakerClient _client;

        [TearDown]
        public void TearDown()
        {
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
                name, BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private static MethodInfo PrivateAuthMethod(string name)
        {
            return typeof(BlockmakerAuth).GetMethod(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
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
            return new NfturboPackShopChallengeResult
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
                unsignedTxnBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
                txId = new string('A', 52),
                firstValidRound = 50_000_000,
                lastValidRound = 50_000_040,
                expiresAt = expiresAt,
            };
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

            // The non-WebGL bridge returns false. A true result here proves the
            // already-reserved browser window was reused without calling it again.
            Assert.That(client.PrimeNfturboPackShopApprovalWindow(), Is.True);
            Assert.That(reserved.GetValue(client), Is.EqualTo(true));
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
