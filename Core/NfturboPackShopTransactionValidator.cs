using System;
using System.Text;

namespace Blockmaker
{
    /// <summary>
    /// Parses and validates the exact canonical Algorand transaction authored for
    /// NFTURBO Pack-Shop authentication. This intentionally is not a general-purpose
    /// msgpack decoder: accepting any field outside the nine-field, zero-ALGO
    /// self-payment produced by algosdk would expand what the sign-in UI can sign.
    /// </summary>
    internal static class NfturboPackShopTransactionValidator
    {
        private const string MainnetGenesisId = "mainnet-v1.0";
        private const string TransactionType = "pay";
        private const ulong ExactFeeMicroAlgo = 1_000;
        private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        private static readonly byte[] MainnetGenesisHash = Convert.FromBase64String(
            "wGHE2Pwdvd7S12BL5FaOP20EGYesN73ktiC1qzkkit8=");

        internal static bool IsExactChallengeTransaction(
            string unsignedTxnBase64,
            string walletAddress,
            string message,
            long firstValidRound,
            long lastValidRound,
            string transactionId)
        {
            try
            {
                if (!WalletConnectIdentity.IsValidAlgorandAddress(walletAddress) ||
                    string.IsNullOrEmpty(message) || firstValidRound <= 0 ||
                    lastValidRound < firstValidRound ||
                    lastValidRound - firstValidRound > 40 ||
                    string.IsNullOrEmpty(transactionId)) return false;

                byte[] encoded = Convert.FromBase64String(unsignedTxnBase64);
                byte[] decodedAddress = XChainAddressDeriver.Base32Decode(walletAddress);
                if (decodedAddress.Length != 36) return false;
                var publicKey = new byte[32];
                Buffer.BlockCopy(decodedAddress, 0, publicKey, 0, publicKey.Length);

                var reader = new CanonicalReader(encoded);

                // algosdk's canonical codec omits the default zero `amt` field. The
                // exact safe self-payment consequently has only these nine keys, in
                // bytewise lexical order. A map with an explicit zero amount is not
                // the server-authored transaction and is rejected too.
                if (!reader.ReadExactFixMapCount(9) ||
                    !reader.ReadExactKey("fee") ||
                    !reader.ReadExactUnsigned(ExactFeeMicroAlgo) ||
                    !reader.ReadExactKey("fv") ||
                    !reader.ReadExactUnsigned((ulong)firstValidRound) ||
                    !reader.ReadExactKey("gen") ||
                    !reader.ReadExactString(MainnetGenesisId) ||
                    !reader.ReadExactKey("gh") ||
                    !reader.ReadExactBinary(MainnetGenesisHash) ||
                    !reader.ReadExactKey("lv") ||
                    !reader.ReadExactUnsigned((ulong)lastValidRound) ||
                    !reader.ReadExactKey("note") ||
                    !reader.ReadExactBinary(Encoding.UTF8.GetBytes(message)) ||
                    !reader.ReadExactKey("rcv") ||
                    !reader.ReadExactBinary(publicKey) ||
                    !reader.ReadExactKey("snd") ||
                    !reader.ReadExactBinary(publicKey) ||
                    !reader.ReadExactKey("type") ||
                    !reader.ReadExactString(TransactionType) ||
                    !reader.AtEnd) return false;

                byte[] digest = XChainAddressDeriver.ComputeTransactionId(encoded);
                return string.Equals(
                    Base32Encode(digest), transactionId, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static string Base32Encode(byte[] data)
        {
            var result = new StringBuilder((data.Length * 8 + 4) / 5);
            int buffer = 0;
            int bits = 0;
            for (int i = 0; i < data.Length; i++)
            {
                buffer = (buffer << 8) | data[i];
                bits += 8;
                while (bits >= 5)
                {
                    bits -= 5;
                    result.Append(Base32Alphabet[(buffer >> bits) & 0x1f]);
                }
            }
            if (bits > 0)
                result.Append(Base32Alphabet[(buffer << (5 - bits)) & 0x1f]);
            return result.ToString();
        }

        private sealed class CanonicalReader
        {
            private readonly byte[] _data;
            private int _offset;

            internal CanonicalReader(byte[] data)
            {
                _data = data ?? Array.Empty<byte>();
            }

            internal bool AtEnd => _offset == _data.Length;

            internal bool ReadExactFixMapCount(int count)
            {
                return count >= 0 && count <= 15 &&
                    ReadByte(out byte header) && header == (byte)(0x80 | count);
            }

            internal bool ReadExactKey(string expected)
            {
                // Algorand canonical transaction keys are ASCII fixstr values.
                byte[] bytes = Encoding.ASCII.GetBytes(expected);
                if (bytes.Length > 31 || !ReadByte(out byte header) ||
                    header != (byte)(0xa0 | bytes.Length)) return false;
                return ReadExactBytes(bytes);
            }

            internal bool ReadExactString(string expected)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(expected);
                if (bytes.Length > 31 || !ReadByte(out byte header) ||
                    header != (byte)(0xa0 | bytes.Length)) return false;
                return ReadExactBytes(bytes);
            }

            internal bool ReadExactBinary(byte[] expected)
            {
                if (expected == null || !ReadCanonicalBinaryLength(out int length) ||
                    length != expected.Length) return false;
                return ReadExactBytes(expected);
            }

            internal bool ReadExactUnsigned(ulong expected)
            {
                if (!ReadByte(out byte header)) return false;
                ulong value;
                if (header <= 0x7f)
                {
                    value = header;
                }
                else if (header == 0xcc)
                {
                    if (!ReadByte(out byte part)) return false;
                    value = part;
                    if (value <= 0x7f) return false;
                }
                else if (header == 0xcd)
                {
                    if (!ReadBigEndian(2, out value) || value <= byte.MaxValue)
                        return false;
                }
                else if (header == 0xce)
                {
                    if (!ReadBigEndian(4, out value) || value <= ushort.MaxValue)
                        return false;
                }
                else if (header == 0xcf)
                {
                    if (!ReadBigEndian(8, out value) || value <= uint.MaxValue)
                        return false;
                }
                else
                {
                    return false;
                }
                return value == expected;
            }

            private bool ReadCanonicalBinaryLength(out int length)
            {
                length = 0;
                if (!ReadByte(out byte header)) return false;
                ulong value;
                if (header == 0xc4)
                {
                    if (!ReadByte(out byte part)) return false;
                    value = part;
                }
                else if (header == 0xc5)
                {
                    if (!ReadBigEndian(2, out value) || value <= byte.MaxValue)
                        return false;
                }
                else if (header == 0xc6)
                {
                    if (!ReadBigEndian(4, out value) || value <= ushort.MaxValue)
                        return false;
                }
                else
                {
                    return false;
                }
                if (value > int.MaxValue || value > (ulong)(_data.Length - _offset))
                    return false;
                length = (int)value;
                return true;
            }

            private bool ReadBigEndian(int count, out ulong value)
            {
                value = 0;
                if (count < 0 || _offset > _data.Length - count) return false;
                for (int i = 0; i < count; i++)
                    value = (value << 8) | _data[_offset++];
                return true;
            }

            private bool ReadExactBytes(byte[] expected)
            {
                if (expected == null || _offset > _data.Length - expected.Length)
                    return false;
                int difference = 0;
                for (int i = 0; i < expected.Length; i++)
                    difference |= _data[_offset + i] ^ expected[i];
                _offset += expected.Length;
                return difference == 0;
            }

            private bool ReadByte(out byte value)
            {
                value = 0;
                if (_offset >= _data.Length) return false;
                value = _data[_offset++];
                return true;
            }
        }
    }
}
