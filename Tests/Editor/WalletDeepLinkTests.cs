using System.Reflection;
using NUnit.Framework;

namespace Blockmaker.Tests
{
    public class WalletDeepLinkTests
    {
        private static MethodInfo PeraLaunchUrlBuilder =>
            typeof(WalletDeepLink).GetMethod(
                "BuildPeraLaunchUrl",
                BindingFlags.NonPublic | BindingFlags.Static);

        [Test]
        public void PeraAndroidLaunchUsesRawWalletConnectUriWithAlgorandHint()
        {
            const string wcUri =
                "wc:topic@1?bridge=https%3A%2F%2Fwallet-connect.perawallet.app&key=abc";

            var result = (string)PeraLaunchUrlBuilder.Invoke(null, new object[] { wcUri, true });

            StringAssert.StartsWith("wc:", result);
            StringAssert.Contains("&algorand=true", result);
            StringAssert.DoesNotContain("https://perawallet.app/qr/", result);
        }

        [Test]
        public void PeraIosLaunchUsesRegisteredPeraScheme()
        {
            const string wcUri =
                "wc:topic@1?bridge=https%3A%2F%2Fwallet-connect.perawallet.app&key=abc";

            var result = (string)PeraLaunchUrlBuilder.Invoke(null, new object[] { wcUri, false });

            StringAssert.StartsWith("perawallet-wc://wc?uri=", result);
            StringAssert.Contains("algorand", result);
            StringAssert.DoesNotContain("https://perawallet.app/qr/", result);
        }

        [Test]
        public void PeraAlgorandHintIsNotDuplicated()
        {
            const string wcUri = "wc:topic@1?bridge=test&algorand=true&key=abc";

            var result = (string)PeraLaunchUrlBuilder.Invoke(null, new object[] { wcUri, true });

            Assert.That(result.Split("algorand=").Length - 1, Is.EqualTo(1));
        }
    }
}
