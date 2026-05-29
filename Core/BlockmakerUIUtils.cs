using UnityEngine;

namespace Blockmaker
{

    public static class BlockmakerUIUtils
    {
        public static string ShortenAddress(string address, int prefixLen = 6, int suffixLen = 6)
        {
            if (string.IsNullOrEmpty(address) || address.Length <= prefixLen + suffixLen + 1)
                return address ?? string.Empty;
            return $"{address[..prefixLen]}…{address[^suffixLen..]}";
        }
    }

}