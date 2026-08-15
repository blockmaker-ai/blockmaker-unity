using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.TestTools;

namespace Blockmaker.Tests
{
    public sealed class BlockmakerClientSecurityTests
    {
        private GameObject _gameObject;
        private BlockmakerConfig _config;
        private BlockmakerClient _client;

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null) UnityEngine.Object.DestroyImmediate(_gameObject);
            if (_config != null) UnityEngine.Object.DestroyImmediate(_config);
        }

        private BlockmakerClient CreateClient(
            string gameId = "game-a",
            string serverUrl = "https://blockmaker.polaris.city",
            string editorKey = "")
        {
            _config = ScriptableObject.CreateInstance<BlockmakerConfig>();
            _config.gameId = gameId;
            _config.serverUrl = serverUrl;
            _config.apiKey = editorKey;
            _gameObject = new GameObject("BlockmakerClientSecurityTests");
            _client = _gameObject.AddComponent<BlockmakerClient>();
            _client.config = _config;
            _client.InitFromAuth();
            return _client;
        }

        private static MethodInfo PrivateMethod(string name)
        {
            return typeof(BlockmakerClient).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private static FieldInfo PrivateField(string name)
        {
            return typeof(BlockmakerClient).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        }

        [Test]
        public void DefaultsToCustomProductionOriginAndRequiresPublicGameId()
        {
            var client = CreateClient(serverUrl: "");
            Assert.That(client.IsConfigured, Is.True);
            Assert.That(client.BaseUrl, Is.EqualTo("https://blockmaker.polaris.city"));
            Assert.That(client.GameId, Is.EqualTo("game-a"));
        }

        [TestCase("http://example.com", "must use HTTPS")]
        [TestCase("https://example.com/v1", "exact HTTPS origin")]
        [TestCase("https://user:pass@example.com", "exact HTTPS origin")]
        [TestCase("https://example.com?next=other", "exact HTTPS origin")]
        public void RejectsUnsafeOrNonOriginServerUrls(string serverUrl, string expectedError)
        {
            LogAssert.Expect(LogType.Error, new Regex(@"\[BlockmakerClient\] Blockmaker server URL"));
            var client = CreateClient(serverUrl: serverUrl);
            Assert.That(client.IsConfigured, Is.False);
            Assert.That(client.ConfigurationError, Does.Contain(expectedError));
        }

        [TestCase("")]
        [TestCase("sk_not_a_public_game_id")]
        [TestCase("game-a\r\nX-Evil: injected")]
        public void RejectsMissingSecretOrHeaderUnsafeGameIds(string gameId)
        {
            LogAssert.Expect(LogType.Error, new Regex(@"\[BlockmakerClient\] Blockmaker (public game ID|gameId)"));
            var client = CreateClient(gameId: gameId);
            Assert.That(client.IsConfigured, Is.False);
            Assert.That(client.ConfigurationError, Is.Not.Empty);
        }

        [Test]
        public void LocalhostHttpRemainsAvailableForDevelopment()
        {
            var client = CreateClient(serverUrl: "http://127.0.0.1:8787");
            Assert.That(client.IsConfigured, Is.True);
            Assert.That(client.BaseUrl, Is.EqualTo("http://127.0.0.1:8787"));
        }

        [TestCase("/v1/profile", true)]
        [TestCase("/v1/profile?tab=game", true)]
        [TestCase("https://attacker.example/v1/profile", false)]
        [TestCase("/v1/../admin", false)]
        [TestCase("/health", false)]
        public void CustomApiPathsStayOnTheConfiguredV1Origin(string path, bool expected)
        {
            var client = CreateClient();
            var args = new object[] { path, null };
            var valid = (bool)PrivateMethod("TryBuildApiUrl").Invoke(client, args);
            Assert.That(valid, Is.EqualTo(expected));
            if (expected) Assert.That((string)args[1], Does.StartWith(client.BaseUrl + "/v1/"));
        }

        [Test]
        public void EveryBuiltRequestCarriesGameBindingButPublicAuthCanOmitServerKey()
        {
            var client = CreateClient(editorKey: "sk_editor_test_only");
            var build = PrivateMethod("BuildPost");
            using (var publicRequest = (UnityWebRequest)build.Invoke(client, new object[] {
                client.BaseUrl + "/v1/auth/email/request", "{}", 10f, false
            }))
            {
                Assert.That(publicRequest.GetRequestHeader("X-Blockmaker-Game"), Is.EqualTo("game-a"));
                Assert.That(publicRequest.GetRequestHeader("Authorization"), Is.Null.Or.Empty);
            }
            using (var editorRequest = (UnityWebRequest)build.Invoke(client, new object[] {
                client.BaseUrl + "/v1/profile/registry", "{}", 10f, true
            }))
            {
                Assert.That(editorRequest.GetRequestHeader("X-Blockmaker-Game"), Is.EqualTo("game-a"));
                Assert.That(editorRequest.GetRequestHeader("Authorization"), Is.EqualTo("Bearer sk_editor_test_only"));
            }
        }

        [Test]
        public void ExactSigningIntentIsBoundToTransactionBytesAndOrder()
        {
            var client = CreateClient();
            var remember = PrivateMethod("TryRememberSigningIntent");
            var find = PrivateMethod("FindSigningIntent");
            var expiresAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60_000;
            var json = "{\"signingIntent\":\"intent.token\",\"signingIntentExpiresAt\":" + expiresAt
                + ",\"unsignedTxnsBase64\":[\"TX_A\",\"TX_B\"]}";
            remember.Invoke(client, new object[] { json });

            Assert.That(find.Invoke(client, new object[] { new[] { "TX_A", "TX_B" } }), Is.EqualTo("intent.token"));
            Assert.That(find.Invoke(client, new object[] { new[] { "TX_B", "TX_A" } }), Is.Null);
            Assert.That(find.Invoke(client, new object[] { new[] { "TX_A", "TX_CHANGED" } }), Is.Null);
        }

        [Test]
        public void ExpiredSigningIntentIsNeverCached()
        {
            var client = CreateClient();
            var remember = PrivateMethod("TryRememberSigningIntent");
            var find = PrivateMethod("FindSigningIntent");
            var expired = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1;
            var json = "{\"signingIntent\":\"expired.token\",\"signingIntentExpiresAt\":" + expired
                + ",\"unsignedTxnBase64\":\"TX_A\"}";
            remember.Invoke(client, new object[] { json });
            Assert.That(find.Invoke(client, new object[] { new[] { "TX_A" } }), Is.Null);
        }

        [Test]
        public void NfturboBuilderFieldNamesAlsoCaptureExactSigningIntent()
        {
            var client = CreateClient();
            var remember = PrivateMethod("TryRememberSigningIntent");
            var find = PrivateMethod("FindSigningIntent");
            var expiresAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60_000;

            remember.Invoke(client, new object[] {
                "{\"signingIntent\":\"shop.intent\",\"signingIntentExpiresAt\":" + expiresAt
                + ",\"unsignedTxns\":[\"SHOP_A\",\"SHOP_B\"]}"
            });
            remember.Invoke(client, new object[] {
                "{\"signingIntent\":\"car.intent\",\"signingIntentExpiresAt\":" + expiresAt
                + ",\"unsignedOptInTxn\":\"CAR_OPTIN\"}"
            });

            Assert.That(find.Invoke(client, new object[] { new[] { "SHOP_A", "SHOP_B" } }), Is.EqualTo("shop.intent"));
            Assert.That(find.Invoke(client, new object[] { new[] { "CAR_OPTIN" } }), Is.EqualTo("car.intent"));
        }

        [Test]
        public void ManagedSigningFailsClosedBeforeNetworkWhenBuilderIntentIsMissing()
        {
            var client = CreateClient();
            string error = null;
            BlockmakerError structured = null;
            var routine = client.SignTransactionServerSide(
                "UNAUTHORISED_TX", "player.jwt", _ => { }, value => error = value, value => structured = value);

            Assert.That(routine.MoveNext(), Is.False);
            Assert.That(structured, Is.Not.Null);
            Assert.That(structured.Code, Is.EqualTo("TX_INTENT_REQUIRED"));
            Assert.That(error, Does.Contain("missing its Blockmaker authorization"));
        }

        [Test]
        public void ConcurrentRefreshCallersForSameTokenJoinOneInFlightExchange()
        {
            var client = CreateClient();
            PrivateField("_refreshInProgress").SetValue(client, true);
            PrivateField("_refreshTokenInFlight").SetValue(client, "same-refresh-token");

            client.RefreshToken("same-refresh-token", _ => { }, _ => { });
            client.RefreshToken("same-refresh-token", _ => { }, _ => { });

            var waiters = (ICollection)PrivateField("_refreshWaiters").GetValue(client);
            Assert.That(waiters.Count, Is.EqualTo(2));
        }

        [Test]
        public void ConcurrentRefreshForDifferentSessionFailsWithoutSendingReplay()
        {
            var client = CreateClient();
            PrivateField("_refreshInProgress").SetValue(client, true);
            PrivateField("_refreshTokenInFlight").SetValue(client, "current-refresh-token");
            string error = null;

            client.RefreshToken("different-refresh-token", _ => { }, value => error = value);

            var waiters = (ICollection)PrivateField("_refreshWaiters").GetValue(client);
            Assert.That(waiters.Count, Is.Zero);
            Assert.That(error, Does.Contain("different player session"));
        }

        [TestCase(
            "Email sign-in is still being prepared for this game address. Choose another supported option or try again shortly.",
            "Email sign-in is temporarily unavailable. Choose another sign-in option, or try again later.")]
        [TestCase(
            "Email wallet sign-in is temporarily unavailable. Please try again.",
            "Email sign-in is temporarily unavailable. Choose another sign-in option, or try again later.")]
        [TestCase(
            "That sign-in option is not enabled for this game. Choose one shown by the game.",
            "Email sign-in is not enabled for this game. Choose a sign-in option shown by the game.")]
        [TestCase(
            "This web address is not connected to the game. Ask the game owner to add it in Blockmaker.",
            "Email sign-in cannot continue on this website. Return to the game’s official website and start again.")]
        [TestCase(
            "Verification failed. Please sign in again.",
            "Email verification ended. Please start email sign-in again.")]
        [TestCase(
            "internal database path /secret/private.db",
            "Sign-in could not be completed. Please try again.")]
        public void MagicLoginErrorsStayPlayerSafeAndActionable(string serverError, string expected)
        {
            Assert.That(BlockmakerErrors.PlayerFacingMagicLoginError(serverError), Is.EqualTo(expected));
        }

        [TestCase(
            "MAGIC_CANCELLED",
            "Email sign-in was cancelled. You can try again when you’re ready.")]
        [TestCase(
            "User denied account access",
            "Email sign-in was cancelled. You can try again when you’re ready.")]
        [TestCase(
            "MAGIC_TIMEOUT",
            "Email sign-in timed out. Please try again.")]
        [TestCase(
            "MAGIC_NETWORK",
            "Email sign-in could not reach its service. Check your connection and try again.")]
        [TestCase(
            "MAGIC_UNAVAILABLE",
            "Email sign-in is temporarily unavailable. Choose another sign-in option, or try again later.")]
        [TestCase(
            "MAGIC_IDENTITY_MISMATCH",
            "Email verification ended because the wallet identity changed. Please start email sign-in again.")]
        [TestCase(
            "secret provider diagnostic for user@example.com",
            "Sign-in could not be completed. Please try again or choose another sign-in option.")]
        public void MagicProviderErrorsNeverExposeRawBrowserText(string providerError, string expected)
        {
            Assert.That(BlockmakerErrors.PlayerFacingMagicProviderError(providerError), Is.EqualTo(expected));
        }

        [Test]
        public void MagicIdentityBindingRequiresExactWalletAndSameEmail()
        {
            var matches = typeof(BlockmakerAuth).GetMethod(
                "MagicIdentityMatches",
                BindingFlags.Static | BindingFlags.NonPublic);
            var addressMatches = typeof(BlockmakerAuth).GetMethod(
                "MagicWalletAddressMatches",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(matches, Is.Not.Null);
            Assert.That(addressMatches, Is.Not.Null);

            bool Invoke(string expectedAddress, string expectedEmail, string actualAddress, string actualEmail) =>
                (bool)matches.Invoke(null, new object[] { expectedAddress, expectedEmail, actualAddress, actualEmail });

            Assert.That(Invoke("ALGO-ADDRESS", "Player@example.com", "ALGO-ADDRESS", "player@example.com"), Is.True);
            Assert.That(Invoke("ALGO-ADDRESS", "player@example.com", "OTHER-ADDRESS", "player@example.com"), Is.False);
            Assert.That(Invoke("ALGO-ADDRESS", "player@example.com", "ALGO-ADDRESS", "other@example.com"), Is.False);
            Assert.That(Invoke("", "player@example.com", "ALGO-ADDRESS", "player@example.com"), Is.False);
            Assert.That(
                (bool)addressMatches.Invoke(null, new object[] { "ALGO-ADDRESS", "ALGO-ADDRESS" }),
                Is.True);
            Assert.That(
                (bool)addressMatches.Invoke(null, new object[] { "ALGO-ADDRESS", "OTHER-ADDRESS" }),
                Is.False);
        }

        [TestCase(
            "MAGIC_UNAVAILABLE",
            "Email wallet signing is temporarily unavailable. Please try again later.")]
        [TestCase(
            "User rejected request with provider diagnostics",
            "You cancelled the transaction request. Nothing was submitted.")]
        [TestCase(
            "MAGIC_TIMEOUT",
            "The transaction request timed out. Start the action again.")]
        [TestCase(
            "Magic SDK not initialized. Log in first.",
            "Your wallet session ended. Reconnect your wallet and start the action again.")]
        [TestCase(
            "MAGIC_NETWORK",
            "Your wallet could not be reached. Check your connection and start the action again.")]
        [TestCase(
            "RPC endpoint leaked secret diagnostic",
            "The transaction could not be signed. Start the action again.")]
        public void WalletSigningErrorsNeverExposeRawProviderText(string providerError, string expected)
        {
            Assert.That(BlockmakerErrors.PlayerFacingWalletSigningError(providerError), Is.EqualTo(expected));
        }

        [Test]
        public void StructuredErrorsClassifyTenantIntentAndRateLimitFailures()
        {
            Assert.That(new BlockmakerError("GAME_MISMATCH", "m", 409).IsGameMismatch, Is.True);
            Assert.That(new BlockmakerError("TX_INTENT_EXPIRED", "m", 410).IsSigningIntentError, Is.True);
            Assert.That(new BlockmakerError("RATE_LIMITED", "m", 429, "req-1", 12).IsRateLimited, Is.True);
        }
    }
}
