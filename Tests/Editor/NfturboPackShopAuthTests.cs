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
    }
}
