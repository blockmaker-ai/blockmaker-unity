using System;
using System.IO;
using System.Text;

namespace Blockmaker
{

    /// <summary>
    /// Pure C# xChain Accounts implementation. Derives deterministic Algorand
    /// LogicSig addresses from EVM addresses and provides signing utilities.
    /// SHA-512/256, Base32, and msgpack encoding are implemented internally
    /// so the Blockmaker SDK has zero external crypto dependencies.
    /// </summary>
    public static class XChainAddressDeriver
    {
        private static readonly byte[] ProgramTag = Encoding.ASCII.GetBytes("Program");
        private static readonly byte[] TxTag      = Encoding.ASCII.GetBytes("TX");

        private static readonly byte[] ProgramPrefix = { 0x0b, 0x26, 0x01, 0x14 };

        private static readonly byte[] ProgramSuffix = {
            0x32, 0x04, 0x81, 0x01, 0x12, 0x41, 0x00, 0x79, 0x31, 0x17,
            0x2d, 0x49, 0x57, 0x00, 0x01, 0x80, 0x01, 0x01, 0x12, 0x44,
            0x49, 0x57, 0x01, 0x20, 0x4b, 0x01, 0x57, 0x21, 0x20, 0x4f,
            0x02, 0x81, 0x41, 0x55, 0x81, 0x1b, 0x09, 0x80, 0x20, 0x61,
            0x2f, 0x25, 0x98, 0xeb, 0xd9, 0x64, 0xc1, 0x6b, 0xa6, 0x7a,
            0x8b, 0x06, 0xd6, 0xf0, 0x8c, 0xe2, 0x4a, 0xb0, 0x91, 0x1f,
            0x0f, 0xf5, 0xa2, 0x67, 0xa2, 0x2f, 0xe0, 0x1e, 0x68, 0x73,
            0x34, 0x4f, 0x04, 0x50, 0x02, 0x80, 0x22, 0x19, 0x01, 0xce,
            0xf8, 0xb9, 0x82, 0x94, 0x14, 0xba, 0x4a, 0x13, 0xea, 0x8f,
            0x8c, 0x44, 0x2b, 0x74, 0x7f, 0xfe, 0x11, 0x9c, 0x64, 0x3d,
            0x22, 0x13, 0xd2, 0x2b, 0x4e, 0x13, 0x70, 0x36, 0xa2, 0xd5,
            0x73, 0x4c, 0x50, 0x02, 0x4c, 0x4f, 0x03, 0x4f, 0x03, 0x07,
            0x00, 0x50, 0x02, 0x57, 0x0c, 0x14, 0x28, 0x12, 0x43, 0x32,
            0x0b, 0x42, 0xff, 0x84
        };

        public static string DeriveAlgorandAddress(string evmAddress)
        {
            var program = GetLogicSigProgram(evmAddress);
            var data = new byte[ProgramTag.Length + program.Length];
            Buffer.BlockCopy(ProgramTag, 0, data, 0, ProgramTag.Length);
            Buffer.BlockCopy(program, 0, data, ProgramTag.Length, program.Length);
            return EncodeAlgorandAddress(Sha512_256(data));
        }

        public static byte[] GetLogicSigProgram(string evmAddress)
        {
            var hex = evmAddress.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? evmAddress.Substring(2) : evmAddress;
            if (hex.Length != 40)
                throw new ArgumentException("EVM address must be 20 bytes (40 hex chars).");

            var evmBytes = HexToBytes(hex);
            var program = new byte[ProgramPrefix.Length + evmBytes.Length + ProgramSuffix.Length];
            Buffer.BlockCopy(ProgramPrefix, 0, program, 0, ProgramPrefix.Length);
            Buffer.BlockCopy(evmBytes, 0, program, ProgramPrefix.Length, evmBytes.Length);
            Buffer.BlockCopy(ProgramSuffix, 0, program, ProgramPrefix.Length + evmBytes.Length, ProgramSuffix.Length);
            return program;
        }

        public static byte[] ComputeTransactionId(byte[] unsignedTxn)
        {
            var data = new byte[TxTag.Length + unsignedTxn.Length];
            Buffer.BlockCopy(TxTag, 0, data, 0, TxTag.Length);
            Buffer.BlockCopy(unsignedTxn, 0, data, TxTag.Length, unsignedTxn.Length);
            return Sha512_256(data);
        }

        /// <summary>
        /// Extracts the Algorand GroupID from an unsigned transaction's msgpack bytes.
        /// Returns null if the transaction has no group field (not part of a group).
        /// Walks the top-level msgpack map keys structurally (not a brute-force scan)
        /// to avoid false matches inside binary field values.
        /// </summary>
        public static byte[] ExtractGroupId(byte[] d)
        {
            if (d == null || d.Length < 2) return null;
            int pos = 0;

            // Read top-level fixmap or map16 entry count
            int mapSize;
            byte header = d[pos++];
            if ((header & 0xF0) == 0x80)
                mapSize = header & 0x0F;
            else if (header == 0xDE && pos + 1 < d.Length)
                { mapSize = (d[pos] << 8) | d[pos + 1]; pos += 2; }
            else
                return null;

            for (int entry = 0; entry < mapSize && pos < d.Length; entry++)
            {
                // Read key (must be fixstr for Algorand canonical msgpack)
                if (pos >= d.Length) return null;
                byte kh = d[pos];
                if ((kh & 0xE0) != 0xA0) return null; // not a fixstr
                int keyLen = kh & 0x1F;
                pos++;
                if (pos + keyLen > d.Length) return null;

                bool isGrp = keyLen == 3 && d[pos] == 0x67 && d[pos+1] == 0x72 && d[pos+2] == 0x70;
                pos += keyLen;

                if (isGrp)
                {
                    // Value must be bin8 with length 32
                    if (pos + 34 > d.Length) return null;
                    if (d[pos] == 0xc4 && d[pos+1] == 0x20)
                    {
                        var groupId = new byte[32];
                        Buffer.BlockCopy(d, pos + 2, groupId, 0, 32);
                        return groupId;
                    }
                    return null; // grp key found but value format unexpected
                }

                // Skip value — need to walk past it to reach next key
                if (pos >= d.Length) return null;
                pos = SkipMsgpackValue(d, pos);
                if (pos < 0) return null;
            }
            return null;
        }

        static int SkipMsgpackValue(byte[] d, int pos)
        {
            if (pos >= d.Length) return -1;
            byte b = d[pos++];

            // positive fixint (0x00-0x7f) or negative fixint (0xe0-0xff)
            if (b <= 0x7f || b >= 0xe0) return pos;
            // fixstr (0xa0-0xbf)
            if ((b & 0xE0) == 0xA0) return pos + (b & 0x1F);
            // fixarray (0x90-0x9f)
            if ((b & 0xF0) == 0x90) { int n = b & 0x0F; for (int i = 0; i < n; i++) { pos = SkipMsgpackValue(d, pos); if (pos < 0) return -1; } return pos; }
            // fixmap (0x80-0x8f)
            if ((b & 0xF0) == 0x80) { int n = b & 0x0F; for (int i = 0; i < n * 2; i++) { pos = SkipMsgpackValue(d, pos); if (pos < 0) return -1; } return pos; }

            switch (b)
            {
                case 0xc0: return pos; // nil
                case 0xc2: case 0xc3: return pos; // bool
                case 0xc4: if (pos >= d.Length) return -1; return pos + 1 + d[pos]; // bin8
                case 0xc5: if (pos + 1 >= d.Length) return -1; return pos + 2 + ((d[pos] << 8) | d[pos+1]); // bin16
                case 0xc6: if (pos + 3 >= d.Length) return -1; return pos + 4 + ((d[pos] << 24) | (d[pos+1] << 16) | (d[pos+2] << 8) | d[pos+3]); // bin32
                case 0xca: return pos + 4; // float32
                case 0xcb: return pos + 8; // float64
                case 0xcc: return pos + 1; // uint8
                case 0xcd: return pos + 2; // uint16
                case 0xce: return pos + 4; // uint32
                case 0xcf: return pos + 8; // uint64
                case 0xd0: return pos + 1; // int8
                case 0xd1: return pos + 2; // int16
                case 0xd2: return pos + 4; // int32
                case 0xd3: return pos + 8; // int64
                case 0xd9: if (pos >= d.Length) return -1; return pos + 1 + d[pos]; // str8
                case 0xda: if (pos + 1 >= d.Length) return -1; return pos + 2 + ((d[pos] << 8) | d[pos+1]); // str16
                case 0xdc: // array16
                    if (pos + 1 >= d.Length) return -1;
                    { int n = (d[pos] << 8) | d[pos+1]; pos += 2; for (int i = 0; i < n; i++) { pos = SkipMsgpackValue(d, pos); if (pos < 0) return -1; } return pos; }
                case 0xde: // map16
                    if (pos + 1 >= d.Length) return -1;
                    { int n = (d[pos] << 8) | d[pos+1]; pos += 2; for (int i = 0; i < n * 2; i++) { pos = SkipMsgpackValue(d, pos); if (pos < 0) return -1; } return pos; }
                default: return -1;
            }
        }

        public static string BuildEip712TypedData(byte[] txnId)
        {
            var hex = "0x" + BytesToHex(txnId);
            return "{" +
                "\"types\":{" +
                    "\"EIP712Domain\":[" +
                        "{\"name\":\"name\",\"type\":\"string\"}," +
                        "{\"name\":\"version\",\"type\":\"string\"}" +
                    "]," +
                    "\"Algorand Transaction\":[" +
                        "{\"name\":\"Transaction ID\",\"type\":\"bytes32\"}" +
                    "]" +
                "}," +
                "\"primaryType\":\"Algorand Transaction\"," +
                "\"domain\":{" +
                    "\"name\":\"Algorand x EVM\"," +
                    "\"version\":\"1\"" +
                "}," +
                "\"message\":{" +
                    "\"Transaction ID\":\"" + hex + "\"" +
                "}" +
            "}";
        }

        /// <summary>
        /// Converts a 65-byte EVM signature (r+s+v) into the 66-byte xChain LogicSig arg.
        /// Format: [0x01, r(32), s(32), v_normalized(1)]
        /// </summary>
        public static byte[] ParseEvmSignature(string hexSig)
        {
            var hex = hexSig.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? hexSig.Substring(2) : hexSig;
            if (hex.Length != 130)
                throw new ArgumentException("Expected 65-byte (130 hex char) EVM signature.");

            var sig = HexToBytes(hex);

            // Extract r, s, v from the 65-byte signature
            var r = new byte[32];
            var s = new byte[32];
            Buffer.BlockCopy(sig, 0, r, 0, 32);
            Buffer.BlockCopy(sig, 32, s, 0, 32);
            byte v = sig[64];

            // Lower-S normalization (prevents ECDSA malleability).
            // If s > secp256k1_n/2, flip to s = n - s and toggle v.
            // Matches the official xchain-accounts SDK's normalizeLowerS().
            if (IsHighS(s))
            {
                s = SubtractFromN(s);
                v = (byte)(v == 27 ? 28 : v == 28 ? 27 : v == 0 ? 1 : v == 1 ? 0 : v);
            }

            // Normalize v to 27/28 — the TEAL LogicSig subtracts 27 on-chain.
            // Some wallets return 0/1 (non-EIP-155), others return 27/28.
            if (v == 0) v = 27;
            else if (v == 1) v = 28;

            if (v != 27 && v != 28)
                throw new ArgumentException("Invalid EVM signature recovery id");

            var arg = new byte[66];
            arg[0] = 0x01;
            Buffer.BlockCopy(r, 0, arg, 1, 32);
            Buffer.BlockCopy(s, 0, arg, 33, 32);
            arg[65] = v;
            return arg;
        }

        /// <summary>
        /// Wraps an unsigned Algorand transaction with a LogicSig to produce
        /// the final signed transaction bytes (canonical msgpack).
        /// </summary>
        public static byte[] BuildSignedTransaction(byte[] unsignedTxn, byte[] program, byte[] sigArg)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x82); // fixmap(2): "lsig", "txn"

                MsgpackFixStr(ms, "lsig");
                ms.WriteByte(0x82); // fixmap(2): "arg", "l"

                MsgpackFixStr(ms, "arg");
                ms.WriteByte(0x91); // fixarray(1)
                MsgpackBin(ms, sigArg);

                MsgpackFixStr(ms, "l");
                MsgpackBin(ms, program);

                MsgpackFixStr(ms, "txn");
                ms.Write(unsignedTxn, 0, unsignedTxn.Length);

                return ms.ToArray();
            }
        }

        // ── Msgpack helpers ───────────────────────────────────────────────────────

        static void MsgpackFixStr(Stream s, string str)
        {
            var b = Encoding.ASCII.GetBytes(str);
            if (b.Length > 31)
                throw new ArgumentException($"MsgpackFixStr only supports strings up to 31 bytes, got {b.Length}.");
            s.WriteByte((byte)(0xa0 | b.Length));
            s.Write(b, 0, b.Length);
        }

        static void MsgpackBin(Stream s, byte[] data)
        {
            if (data.Length > 65535)
                throw new ArgumentException("MsgpackBin data exceeds maximum size");
            if (data.Length <= 255)
            {
                s.WriteByte(0xc4);
                s.WriteByte((byte)data.Length);
            }
            else
            {
                s.WriteByte(0xc5);
                s.WriteByte((byte)(data.Length >> 8));
                s.WriteByte((byte)data.Length);
            }
            s.Write(data, 0, data.Length);
        }

        // ── Algorand address encoding ─────────────────────────────────────────────

        static string EncodeAlgorandAddress(byte[] publicKey)
        {
            var checksum = Sha512_256(publicKey);
            var raw = new byte[36];
            Buffer.BlockCopy(publicKey, 0, raw, 0, 32);
            Buffer.BlockCopy(checksum, 28, raw, 32, 4);
            return Base32Encode(raw);
        }

        // ── Base32 (RFC 4648, no padding) ─────────────────────────────────────────

        const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        static string Base32Encode(byte[] data)
        {
            var sb = new StringBuilder((data.Length * 8 + 4) / 5);
            int buf = 0, bits = 0;
            for (int i = 0; i < data.Length; i++)
            {
                buf = (buf << 8) | data[i];
                bits += 8;
                while (bits >= 5) { bits -= 5; sb.Append(Base32Alphabet[(buf >> bits) & 0x1F]); }
            }
            if (bits > 0)
                sb.Append(Base32Alphabet[(buf << (5 - bits)) & 0x1F]);
            return sb.ToString();
        }

        internal static byte[] Base32Decode(string encoded)
        {
            int totalBits = encoded.Length * 5;
            byte[] result = new byte[totalBits / 8];
            int buf = 0, bits = 0, idx = 0;
            for (int i = 0; i < encoded.Length; i++)
            {
                char c = encoded[i];
                int val;
                if (c >= 'A' && c <= 'Z') val = c - 'A';
                else if (c >= '2' && c <= '7') val = c - '2' + 26;
                else throw new ArgumentException($"Invalid Base32 character: {c}");
                buf = (buf << 5) | val;
                bits += 5;
                if (bits >= 8)
                {
                    bits -= 8;
                    result[idx++] = (byte)((buf >> bits) & 0xFF);
                }
            }
            return result;
        }

        // ── secp256k1 lower-S normalization ───────────────────────────────────────

        // secp256k1 curve order n and n/2 (big-endian, 32 bytes)
        static readonly byte[] Secp256k1N = new byte[] {
            0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFE,
            0xBA,0xAE,0xDC,0xE6,0xAF,0x48,0xA0,0x3B,0xBF,0xD2,0x5E,0x8C,0xD0,0x36,0x41,0x41 };
        static readonly byte[] Secp256k1HalfN = new byte[] {
            0x7F,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,
            0x5D,0x57,0x6E,0x73,0x57,0xA4,0x50,0x1D,0xDF,0xE9,0x2F,0x46,0x68,0x1B,0x20,0xA0 };

        static bool IsHighS(byte[] s)
        {
            for (int i = 0; i < 32; i++)
            {
                if (s[i] < Secp256k1HalfN[i]) return false;
                if (s[i] > Secp256k1HalfN[i]) return true;
            }
            return false; // equal to halfN is not high
        }

        static byte[] SubtractFromN(byte[] s)
        {
            var result = new byte[32];
            int borrow = 0;
            for (int i = 31; i >= 0; i--)
            {
                int diff = Secp256k1N[i] - s[i] - borrow;
                if (diff < 0) { diff += 256; borrow = 1; }
                else borrow = 0;
                result[i] = (byte)diff;
            }
            return result;
        }

        // ── Hex utilities ─────────────────────────────────────────────────────────

        static byte[] HexToBytes(string hex)
        {
            var b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++)
                b[i] = (byte)((HexVal(hex[i * 2]) << 4) | HexVal(hex[i * 2 + 1]));
            return b;
        }

        static int HexVal(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            throw new ArgumentException($"Invalid hex character: {c}");
        }

        static string BytesToHex(byte[] data)
        {
            var sb = new StringBuilder(data.Length * 2);
            for (int i = 0; i < data.Length; i++)
                sb.Append(data[i].ToString("x2"));
            return sb.ToString();
        }

        // ── SHA-512/256 (FIPS 180-4) ──────────────────────────────────────────────

        static readonly ulong[] K = {
            0x428a2f98d728ae22, 0x7137449123ef65cd, 0xb5c0fbcfec4d3b2f, 0xe9b5dba58189dbbc,
            0x3956c25bf348b538, 0x59f111f1b605d019, 0x923f82a4af194f9b, 0xab1c5ed5da6d8118,
            0xd807aa98a3030242, 0x12835b0145706fbe, 0x243185be4ee4b28c, 0x550c7dc3d5ffb4e2,
            0x72be5d74f27b896f, 0x80deb1fe3b1696b1, 0x9bdc06a725c71235, 0xc19bf174cf692694,
            0xe49b69c19ef14ad2, 0xefbe4786384f25e3, 0x0fc19dc68b8cd5b5, 0x240ca1cc77ac9c65,
            0x2de92c6f592b0275, 0x4a7484aa6ea6e483, 0x5cb0a9dcbd41fbd4, 0x76f988da831153b5,
            0x983e5152ee66dfab, 0xa831c66d2db43210, 0xb00327c898fb213f, 0xbf597fc7beef0ee4,
            0xc6e00bf33da88fc2, 0xd5a79147930aa725, 0x06ca6351e003826f, 0x142929670a0e6e70,
            0x27b70a8546d22ffc, 0x2e1b21385c26c926, 0x4d2c6dfc5ac42aed, 0x53380d139d95b3df,
            0x650a73548baf63de, 0x766a0abb3c77b2a8, 0x81c2c92e47edaee6, 0x92722c851482353b,
            0xa2bfe8a14cf10364, 0xa81a664bbc423001, 0xc24b8b70d0f89791, 0xc76c51a30654be30,
            0xd192e819d6ef5218, 0xd69906245565a910, 0xf40e35855771202a, 0x106aa07032bbd1b8,
            0x19a4c116b8d2d0c8, 0x1e376c085141ab53, 0x2748774cdf8eeb99, 0x34b0bcb5e19b48a8,
            0x391c0cb3c5c95a63, 0x4ed8aa4ae3418acb, 0x5b9cca4f7763e373, 0x682e6ff3d6b2b8a3,
            0x748f82ee5defb2fc, 0x78a5636f43172f60, 0x84c87814a1f0ab72, 0x8cc702081a6439ec,
            0x90befffa23631e28, 0xa4506cebde82bde9, 0xbef9a3f7b2c67915, 0xc67178f2e372532b,
            0xca273eceea26619c, 0xd186b8c721c0c207, 0xeada7dd6cde0eb1e, 0xf57d4f7fee6ed178,
            0x06f067aa72176fba, 0x0a637dc5a2c898a6, 0x113f9804bef90dae, 0x1b710b35131c471b,
            0x28db77f523047d84, 0x32caab7b40c72493, 0x3c9ebe0a15c9bebc, 0x431d67c49c100d4c,
            0x4cc5d4becb3e42b6, 0x597f299cfc657e2a, 0x5fcb6fab3ad6faec, 0x6c44198c4a475817
        };

        internal static byte[] Sha512_256(byte[] msg)
        {
            ulong h0 = 0x22312194FC2BF72C, h1 = 0x9F555FA3C84C64C2;
            ulong h2 = 0x2393B86B6F53B151, h3 = 0x963877195940EABD;
            ulong h4 = 0x96283EE2A88EFFE3, h5 = 0xBE5E1E2553863992;
            ulong h6 = 0x2B0199FC2C85B8AA, h7 = 0x0EB72DDC81C52CA2;

            ulong bitLen = (ulong)msg.Length * 8;
            int padded = msg.Length + 1;
            int rem = padded % 128;
            padded += (rem <= 112 ? 112 - rem : 240 - rem) + 16;

            var buf = new byte[padded];
            Buffer.BlockCopy(msg, 0, buf, 0, msg.Length);
            buf[msg.Length] = 0x80;
            for (int i = 0; i < 8; i++)
                buf[padded - 1 - i] = (byte)(bitLen >> (i * 8));

            var W = new ulong[80];
            for (int blk = 0; blk < padded; blk += 128)
            {
                for (int t = 0; t < 16; t++)
                {
                    int o = blk + t * 8;
                    W[t] = ((ulong)buf[o] << 56) | ((ulong)buf[o+1] << 48) |
                           ((ulong)buf[o+2] << 40) | ((ulong)buf[o+3] << 32) |
                           ((ulong)buf[o+4] << 24) | ((ulong)buf[o+5] << 16) |
                           ((ulong)buf[o+6] << 8)  |  (ulong)buf[o+7];
                }
                for (int t = 16; t < 80; t++)
                {
                    ulong s0 = RotR(W[t-15], 1) ^ RotR(W[t-15], 8) ^ (W[t-15] >> 7);
                    ulong s1 = RotR(W[t-2], 19) ^ RotR(W[t-2], 61) ^ (W[t-2] >> 6);
                    W[t] = W[t-16] + s0 + W[t-7] + s1;
                }

                ulong a = h0, b = h1, c = h2, d = h3, e = h4, f = h5, g = h6, h = h7;

                for (int t = 0; t < 80; t++)
                {
                    ulong S1 = RotR(e, 14) ^ RotR(e, 18) ^ RotR(e, 41);
                    ulong ch = (e & f) ^ (~e & g);
                    ulong t1 = h + S1 + ch + K[t] + W[t];
                    ulong S0 = RotR(a, 28) ^ RotR(a, 34) ^ RotR(a, 39);
                    ulong maj = (a & b) ^ (a & c) ^ (b & c);
                    ulong t2 = S0 + maj;
                    h = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
                }

                h0 += a; h1 += b; h2 += c; h3 += d; h4 += e; h5 += f; h6 += g; h7 += h;
            }

            var result = new byte[32];
            StoreBE(result, 0, h0);  StoreBE(result, 8, h1);
            StoreBE(result, 16, h2); StoreBE(result, 24, h3);
            return result;
        }

        static ulong RotR(ulong x, int n) => (x >> n) | (x << (64 - n));

        static void StoreBE(byte[] b, int o, ulong v)
        {
            b[o]   = (byte)(v >> 56); b[o+1] = (byte)(v >> 48);
            b[o+2] = (byte)(v >> 40); b[o+3] = (byte)(v >> 32);
            b[o+4] = (byte)(v >> 24); b[o+5] = (byte)(v >> 16);
            b[o+6] = (byte)(v >> 8);  b[o+7] = (byte)v;
        }
    }

}