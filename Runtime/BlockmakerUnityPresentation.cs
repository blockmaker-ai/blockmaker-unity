using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockmaker
{

    /// <summary>
    /// Self-contained QR code encoder. No external dependencies.
    /// Supports byte-mode encoding with error correction level M.
    /// Returns a bool[,] module matrix suitable for rendering.
    /// </summary>
    public static class UnityPeraQRCodeEncoder
    {
        public static bool[,] Encode(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            int version = FindMinVersion(data.Length);
            if (version < 0)
                throw new ArgumentException("Data too long for QR code");

            int totalDataCW = TotalDataCodewords(version);
            byte[] encoded = EncodeData(data, version, totalDataCW);
            byte[] withEC = AddErrorCorrection(encoded, version);
            bool[,] matrix = BuildMatrix(version, withEC);
            return matrix;
        }

        // ── Version selection ────────────────────────────────────────────────────

        static int FindMinVersion(int byteCount)
        {
            for (int v = 1; v <= 40; v++)
            {
                int overhead = v < 10 ? 2 : 3;
                int cap = TotalDataCodewords(v) - overhead;
                if (cap >= byteCount) return v;
            }
            return -1;
        }

        // ── Data encoding (byte mode = 0100) ─────────────────────────────────────

        static byte[] EncodeData(byte[] data, int version, int totalDataCW)
        {
            var bits = new BitBuffer();
            bits.Append(0b0100, 4); // byte mode indicator
            int lenBits = version < 10 ? 8 : 16;
            bits.Append(data.Length, lenBits);
            foreach (byte b in data)
                bits.Append(b, 8);
            bits.Append(0, Math.Min(4, totalDataCW * 8 - bits.Length));
            while (bits.Length % 8 != 0)
                bits.Append(0, 1);
            byte[] pads = { 0xEC, 0x11 };
            for (int i = 0; bits.Length < totalDataCW * 8; i++)
                bits.Append(pads[i % 2], 8);
            return bits.ToBytes();
        }

        // ── Error correction ─────────────────────────────────────────────────────

        static byte[] AddErrorCorrection(byte[] data, int version)
        {
            var spec = GetECSpec(version);
            int ecCWPerBlock = spec.ecCWPerBlock;
            var dataBlocks = new List<byte[]>();
            var ecBlocks = new List<byte[]>();
            int offset = 0;
            foreach (var (count, dcw) in spec.blocks)
            {
                for (int i = 0; i < count; i++)
                {
                    byte[] block = new byte[dcw];
                    Array.Copy(data, offset, block, 0, dcw);
                    offset += dcw;
                    dataBlocks.Add(block);
                    ecBlocks.Add(ReedSolomon(block, ecCWPerBlock));
                }
            }
            var result = new List<byte>();
            int maxData = 0;
            foreach (var b in dataBlocks)
                if (b.Length > maxData) maxData = b.Length;
            for (int i = 0; i < maxData; i++)
                foreach (var b in dataBlocks)
                    if (i < b.Length) result.Add(b[i]);
            for (int i = 0; i < ecCWPerBlock; i++)
                foreach (var b in ecBlocks)
                    if (i < b.Length) result.Add(b[i]);
            int remainder = RemainderBits(version);
            // Pad remainder bits (not full bytes — handled during placement)
            return result.ToArray();
        }

        // ── Reed-Solomon over GF(256) ────────────────────────────────────────────

        static readonly int[] GF_EXP = new int[512];
        static readonly int[] GF_LOG = new int[256];

        static UnityPeraQRCodeEncoder()
        {
            int x = 1;
            for (int i = 0; i < 255; i++)
            {
                GF_EXP[i] = x;
                GF_LOG[x] = i;
                x <<= 1;
                if (x >= 256) x ^= 0x11D;
            }
            for (int i = 255; i < 512; i++)
                GF_EXP[i] = GF_EXP[i - 255];
        }

        static int GFMul(int a, int b)
        {
            if (a == 0 || b == 0) return 0;
            return GF_EXP[GF_LOG[a] + GF_LOG[b]];
        }

        static byte[] ReedSolomon(byte[] data, int ecLen)
        {
            int[] gen = GeneratorPoly(ecLen);
            int[] msg = new int[data.Length + ecLen];
            for (int i = 0; i < data.Length; i++)
                msg[i] = data[i];
            for (int i = 0; i < data.Length; i++)
            {
                int coef = msg[i];
                if (coef == 0) continue;
                for (int j = 0; j < gen.Length; j++)
                    msg[i + j] ^= GFMul(gen[j], coef);
            }
            byte[] result = new byte[ecLen];
            for (int i = 0; i < ecLen; i++)
                result[i] = (byte)msg[data.Length + i];
            return result;
        }

        static int[] GeneratorPoly(int degree)
        {
            int[] poly = { 1 };
            for (int i = 0; i < degree; i++)
            {
                int[] factor = { 1, GF_EXP[i] };
                poly = PolyMul(poly, factor);
            }
            return poly;
        }

        static int[] PolyMul(int[] a, int[] b)
        {
            int[] result = new int[a.Length + b.Length - 1];
            for (int i = 0; i < a.Length; i++)
                for (int j = 0; j < b.Length; j++)
                    result[i + j] ^= GFMul(a[i], b[j]);
            return result;
        }

        // ── Matrix construction ──────────────────────────────────────────────────

        static bool[,] BuildMatrix(int version, byte[] codewords)
        {
            int size = 17 + version * 4;
            bool[,] modules = new bool[size, size];
            bool[,] reserved = new bool[size, size];

            PlaceFinderPatterns(modules, reserved, size);
            PlaceAlignmentPatterns(modules, reserved, version, size);
            PlaceTimingPatterns(modules, reserved, size);
            ReserveFormatArea(reserved, size);
            modules[size - 8, 8] = true; // dark module

            if (version >= 7)
                PlaceVersionInfo(modules, reserved, version, size);

            PlaceData(modules, reserved, codewords, version, size);

            int bestMask = 0;
            int bestScore = int.MaxValue;
            bool[,] bestMatrix = null;

            for (int mask = 0; mask < 8; mask++)
            {
                bool[,] trial = (bool[,])modules.Clone();
                ApplyMask(trial, reserved, mask, size);
                PlaceFormatInfo(trial, mask, size);
                int score = EvaluatePenalty(trial, size);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestMask = mask;
                    bestMatrix = trial;
                }
            }
            return bestMatrix;
        }

        static void PlaceFinderPatterns(bool[,] m, bool[,] r, int size)
        {
            int[][] positions = { new[] { 0, 0 }, new[] { size - 7, 0 }, new[] { 0, size - 7 } };
            foreach (var pos in positions)
            {
                int row = pos[0], col = pos[1];
                for (int dr = -1; dr <= 7; dr++)
                for (int dc = -1; dc <= 7; dc++)
                {
                    int rr = row + dr, cc = col + dc;
                    if (rr < 0 || rr >= size || cc < 0 || cc >= size) continue;
                    r[rr, cc] = true;
                    bool inOuter = dr == 0 || dr == 6 || dc == 0 || dc == 6;
                    bool inInner = dr >= 2 && dr <= 4 && dc >= 2 && dc <= 4;
                    m[rr, cc] = (dr >= 0 && dr <= 6 && dc >= 0 && dc <= 6) && (inOuter || inInner);
                }
            }
        }

        static void PlaceAlignmentPatterns(bool[,] m, bool[,] r, int version, int size)
        {
            if (version < 2) return;
            int[] coords = AlignmentPositions(version);
            for (int i = 0; i < coords.Length; i++)
            for (int j = 0; j < coords.Length; j++)
            {
                int cr = coords[i], cc = coords[j];
                if (r[cr, cc]) continue;
                for (int dr = -2; dr <= 2; dr++)
                for (int dc = -2; dc <= 2; dc++)
                {
                    int rr = cr + dr, ccc = cc + dc;
                    r[rr, ccc] = true;
                    m[rr, ccc] = Math.Abs(dr) == 2 || Math.Abs(dc) == 2 || (dr == 0 && dc == 0);
                }
            }
        }

        static void PlaceTimingPatterns(bool[,] m, bool[,] r, int size)
        {
            for (int i = 8; i < size - 8; i++)
            {
                m[6, i] = i % 2 == 0;
                r[6, i] = true;
                m[i, 6] = i % 2 == 0;
                r[i, 6] = true;
            }
        }

        static void ReserveFormatArea(bool[,] r, int size)
        {
            for (int c = 0; c <= 8; c++) r[8, c] = true;
            for (int rr = 0; rr <= 8; rr++) r[rr, 8] = true;
            for (int c = size - 8; c < size; c++) r[8, c] = true;
            for (int rr = size - 7; rr < size; rr++) r[rr, 8] = true;
            r[size - 8, 8] = true;
        }

        static void PlaceVersionInfo(bool[,] m, bool[,] r, int version, int size)
        {
            int bits = VersionInfoBits(version);
            for (int i = 0; i < 18; i++)
            {
                bool bit = ((bits >> i) & 1) == 1;
                int row = i / 3, col = size - 11 + (i % 3);
                m[row, col] = bit; r[row, col] = true;
                m[col, row] = bit; r[col, row] = true;
            }
        }

        static void PlaceFormatInfo(bool[,] m, int mask, int size)
        {
            int bits = FormatInfoBits(mask);
            int[] rowPositions = { 0, 1, 2, 3, 4, 5, 7, 8, size - 7, size - 6, size - 5, size - 4, size - 3, size - 2, size - 1 };
            int[] colPositions = { size - 1, size - 2, size - 3, size - 4, size - 5, size - 6, size - 7, size - 8, 7, 5, 4, 3, 2, 1, 0 };
            for (int i = 0; i < 15; i++)
            {
                bool bit = ((bits >> i) & 1) == 1;
                m[8, colPositions[i]] = bit;
                m[rowPositions[i], 8] = bit;
            }
        }

        static void PlaceData(bool[,] m, bool[,] r, byte[] codewords, int version, int size)
        {
            int bitIdx = 0;
            int totalBits = codewords.Length * 8 + RemainderBits(version);
            int col = size - 1;
            bool upward = true;
            while (col >= 0)
            {
                if (col == 6) col--;
                for (int row = 0; row < size; row++)
                {
                    int actualRow = upward ? size - 1 - row : row;
                    for (int dc = 0; dc <= 1; dc++)
                    {
                        int c = col - dc;
                        if (c < 0 || r[actualRow, c]) continue;
                        if (bitIdx < totalBits)
                        {
                            int byteIdx = bitIdx / 8;
                            int bitPos = 7 - (bitIdx % 8);
                            if (byteIdx < codewords.Length)
                                m[actualRow, c] = ((codewords[byteIdx] >> bitPos) & 1) == 1;
                            bitIdx++;
                        }
                    }
                }
                upward = !upward;
                col -= 2;
            }
        }

        static void ApplyMask(bool[,] m, bool[,] r, int mask, int size)
        {
            for (int row = 0; row < size; row++)
            for (int col = 0; col < size; col++)
            {
                if (r[row, col]) continue;
                bool flip = mask switch
                {
                    0 => (row + col) % 2 == 0,
                    1 => row % 2 == 0,
                    2 => col % 3 == 0,
                    3 => (row + col) % 3 == 0,
                    4 => (row / 2 + col / 3) % 2 == 0,
                    5 => (row * col) % 2 + (row * col) % 3 == 0,
                    6 => ((row * col) % 2 + (row * col) % 3) % 2 == 0,
                    7 => ((row + col) % 2 + (row * col) % 3) % 2 == 0,
                    _ => false
                };
                if (flip) m[row, col] = !m[row, col];
            }
        }

        // ── Penalty evaluation ───────────────────────────────────────────────────

        static int EvaluatePenalty(bool[,] m, int size)
        {
            int penalty = 0;
            // Rule 1: runs of same color
            for (int row = 0; row < size; row++)
            {
                int run = 1;
                for (int col = 1; col < size; col++)
                {
                    if (m[row, col] == m[row, col - 1]) run++;
                    else { if (run >= 5) penalty += run - 2; run = 1; }
                }
                if (run >= 5) penalty += run - 2;
            }
            for (int col = 0; col < size; col++)
            {
                int run = 1;
                for (int row = 1; row < size; row++)
                {
                    if (m[row, col] == m[row - 1, col]) run++;
                    else { if (run >= 5) penalty += run - 2; run = 1; }
                }
                if (run >= 5) penalty += run - 2;
            }
            // Rule 2: 2x2 blocks
            for (int row = 0; row < size - 1; row++)
            for (int col = 0; col < size - 1; col++)
            {
                bool v = m[row, col];
                if (v == m[row, col + 1] && v == m[row + 1, col] && v == m[row + 1, col + 1])
                    penalty += 3;
            }
            // Rule 3: finder-like patterns (1011101 0000 or 0000 1011101)
            for (int row = 0; row < size; row++)
            for (int col = 0; col <= size - 11; col++)
            {
                if (m[row,col] && !m[row,col+1] && m[row,col+2] && m[row,col+3] && m[row,col+4] && !m[row,col+5] && m[row,col+6]
                    && !m[row,col+7] && !m[row,col+8] && !m[row,col+9] && !m[row,col+10])
                    penalty += 40;
                if (!m[row,col] && !m[row,col+1] && !m[row,col+2] && !m[row,col+3] && m[row,col+4]
                    && !m[row,col+5] && m[row,col+6] && m[row,col+7] && m[row,col+8] && !m[row,col+9] && m[row,col+10])
                    penalty += 40;
            }
            for (int col = 0; col < size; col++)
            for (int row = 0; row <= size - 11; row++)
            {
                if (m[row,col] && !m[row+1,col] && m[row+2,col] && m[row+3,col] && m[row+4,col] && !m[row+5,col] && m[row+6,col]
                    && !m[row+7,col] && !m[row+8,col] && !m[row+9,col] && !m[row+10,col])
                    penalty += 40;
                if (!m[row,col] && !m[row+1,col] && !m[row+2,col] && !m[row+3,col] && m[row+4,col]
                    && !m[row+5,col] && m[row+6,col] && m[row+7,col] && m[row+8,col] && !m[row+9,col] && m[row+10,col])
                    penalty += 40;
            }
            // Rule 4: proportion of dark modules
            int dark = 0;
            for (int r = 0; r < size; r++)
                for (int c = 0; c < size; c++)
                    if (m[r, c]) dark++;
            int pct = dark * 100 / (size * size);
            int prev5 = pct - (pct % 5);
            int next5 = prev5 + 5;
            penalty += Math.Min(Math.Abs(prev5 - 50) / 5, Math.Abs(next5 - 50) / 5) * 10;
            return penalty;
        }

        // ── Format / version info ────────────────────────────────────────────────

        static int FormatInfoBits(int mask)
        {
            // EC level M = 00, mask 0-7
            int data = (0b00 << 3) | mask;
            int rem = data << 10;
            int gen = 0b10100110111;
            for (int i = 4; i >= 0; i--)
            {
                if ((rem & (1 << (i + 10))) != 0)
                    rem ^= gen << i;
            }
            int result = ((data << 10) | rem) ^ 0b101010000010010;
            return result;
        }

        static int VersionInfoBits(int version)
        {
            int rem = version << 12;
            int gen = 0b1111100100101;
            for (int i = 5; i >= 0; i--)
            {
                if ((rem & (1 << (i + 12))) != 0)
                    rem ^= gen << i;
            }
            return (version << 12) | rem;
        }

        // ── EC specification tables ──────────────────────────────────────────────

        struct ECSpec
        {
            public int ecCWPerBlock;
            public (int count, int dcw)[] blocks;
        }

        static int TotalDataCodewords(int version)
        {
            var spec = GetECSpec(version);
            int total = 0;
            foreach (var (count, dcw) in spec.blocks)
                total += count * dcw;
            return total;
        }

        static ECSpec GetECSpec(int version)
        {
            // EC level M specifications per version
            return version switch
            {
                1  => new ECSpec { ecCWPerBlock = 10, blocks = new[] { (1, 16) } },
                2  => new ECSpec { ecCWPerBlock = 16, blocks = new[] { (1, 28) } },
                3  => new ECSpec { ecCWPerBlock = 26, blocks = new[] { (1, 44) } },
                4  => new ECSpec { ecCWPerBlock = 18, blocks = new[] { (2, 32) } },
                5  => new ECSpec { ecCWPerBlock = 24, blocks = new[] { (2, 43) } },
                6  => new ECSpec { ecCWPerBlock = 16, blocks = new[] { (4, 27) } },
                7  => new ECSpec { ecCWPerBlock = 18, blocks = new[] { (4, 31) } },
                8  => new ECSpec { ecCWPerBlock = 22, blocks = new[] { (2, 38), (2, 39) } },
                9  => new ECSpec { ecCWPerBlock = 22, blocks = new[] { (3, 36), (2, 37) } },
                10 => new ECSpec { ecCWPerBlock = 26, blocks = new[] { (4, 43), (1, 44) } },
                11 => new ECSpec { ecCWPerBlock = 30, blocks = new[] { (1, 50), (4, 51) } },
                12 => new ECSpec { ecCWPerBlock = 22, blocks = new[] { (6, 36), (2, 37) } },
                13 => new ECSpec { ecCWPerBlock = 22, blocks = new[] { (8, 37), (1, 38) } },
                14 => new ECSpec { ecCWPerBlock = 24, blocks = new[] { (4, 40), (5, 41) } },
                15 => new ECSpec { ecCWPerBlock = 24, blocks = new[] { (5, 41), (5, 42) } },
                16 => new ECSpec { ecCWPerBlock = 28, blocks = new[] { (7, 45), (3, 46) } },
                17 => new ECSpec { ecCWPerBlock = 28, blocks = new[] { (10, 46), (1, 47) } },
                18 => new ECSpec { ecCWPerBlock = 26, blocks = new[] { (9, 43), (4, 44) } },
                19 => new ECSpec { ecCWPerBlock = 26, blocks = new[] { (3, 44), (11, 45) } },
                20 => new ECSpec { ecCWPerBlock = 26, blocks = new[] { (3, 41), (13, 42) } },
                21 => new ECSpec { ecCWPerBlock = 26, blocks = new[] { (17, 42) } },
                22 => new ECSpec { ecCWPerBlock = 28, blocks = new[] { (17, 46) } },
                23 => new ECSpec { ecCWPerBlock = 28, blocks = new[] { (4, 47), (14, 48) } },
                24 => new ECSpec { ecCWPerBlock = 28, blocks = new[] { (6, 45), (14, 46) } },
                25 => new ECSpec { ecCWPerBlock = 28, blocks = new[] { (8, 47), (13, 48) } },
                26 => new ECSpec { ecCWPerBlock = 28, blocks = new[] { (19, 46), (4, 47) } },
                27 => new ECSpec { ecCWPerBlock = 28, blocks = new[] { (22, 45), (3, 46) } },
                28 => new ECSpec { ecCWPerBlock = 28, blocks = new[] { (3, 45), (23, 46) } },
                29 => new ECSpec { ecCWPerBlock = 28, blocks = new[] { (21, 45), (7, 46) } },
                30 => new ECSpec { ecCWPerBlock = 28, blocks = new[] { (19, 47), (10, 48) } },
                31 => new ECSpec { ecCWPerBlock = 28, blocks = new[] { (2, 46), (29, 47) } },
                32 => new ECSpec { ecCWPerBlock = 28, blocks = new[] { (10, 46), (23, 47) } },
                33 => new ECSpec { ecCWPerBlock = 28, blocks = new[] { (14, 46), (21, 47) } },
                34 => new ECSpec { ecCWPerBlock = 28, blocks = new[] { (14, 46), (23, 47) } },
                35 => new ECSpec { ecCWPerBlock = 28, blocks = new[] { (12, 47), (26, 48) } },
                36 => new ECSpec { ecCWPerBlock = 28, blocks = new[] { (6, 47), (34, 48) } },
                37 => new ECSpec { ecCWPerBlock = 28, blocks = new[] { (29, 46), (14, 47) } },
                38 => new ECSpec { ecCWPerBlock = 28, blocks = new[] { (13, 46), (32, 47) } },
                39 => new ECSpec { ecCWPerBlock = 28, blocks = new[] { (40, 47), (7, 48) } },
                40 => new ECSpec { ecCWPerBlock = 28, blocks = new[] { (18, 47), (31, 48) } },
                _  => throw new ArgumentException($"Invalid QR version: {version}")
            };
        }

        static int RemainderBits(int version)
        {
            if (version <= 1) return 0;
            if (version <= 6) return 7;
            if (version <= 13) return 0;
            if (version <= 20) return 3;
            if (version <= 27) return 4;
            if (version <= 34) return 3;
            return 0;
        }

        static int[] AlignmentPositions(int version)
        {
            if (version == 1) return Array.Empty<int>();
            int first = 6;
            int last = 17 + version * 4 - 7;
            int count = version / 7 + 2;
            if (count == 2)
                return new[] { first, last };
            int step = (int)Math.Ceiling((double)(last - first) / (count - 1));
            if (step % 2 != 0) step++;
            var positions = new List<int> { first };
            for (int pos = last; positions.Count < count; pos -= step)
                positions.Insert(1, pos);
            return positions.ToArray();
        }

        // ── Bit buffer ───────────────────────────────────────────────────────────

        class BitBuffer
        {
            readonly List<byte> _bytes = new List<byte>();
            int _bitCount;

            public int Length => _bitCount;

            public void Append(int value, int numBits)
            {
                for (int i = numBits - 1; i >= 0; i--)
                {
                    int byteIdx = _bitCount / 8;
                    int bitIdx = 7 - (_bitCount % 8);
                    while (_bytes.Count <= byteIdx)
                        _bytes.Add(0);
                    if (((value >> i) & 1) == 1)
                        _bytes[byteIdx] |= (byte)(1 << bitIdx);
                    _bitCount++;
                }
            }

            public byte[] ToBytes()
            {
                return _bytes.ToArray();
            }
        }
    }

}
namespace Blockmaker
{

    /// <summary>
    /// Generates QR code textures from string data using the built-in UnityPeraQRCodeEncoder.
    /// No external dependencies required.
    /// The caller owns the returned Texture2D and must call Destroy() when done.
    /// </summary>
    public static class UnityPeraQRTextureGenerator
    {
        private const int QUIET_ZONE = 4;

        public static Texture2D Generate(string data, int pixelSize = 256)
        {
            pixelSize = Mathf.Max(1, pixelSize);

            if (string.IsNullOrEmpty(data))
            {
                Debug.LogWarning("[UnityPeraQRTextureGenerator] No data provided for QR generation.");
                return CreatePlaceholder(pixelSize);
            }

            try
            {
                bool[,] matrix = UnityPeraQRCodeEncoder.Encode(data);
                int moduleCount = matrix.GetLength(0);
                int totalModules = moduleCount + QUIET_ZONE * 2;
                int scale = Mathf.Max(1, pixelSize / totalModules);
                int texSize = totalModules * scale;

                var tex = new Texture2D(texSize, texSize, TextureFormat.RGB24, false)
                {
                    filterMode = FilterMode.Point
                };

                var pixels = new Color[texSize * texSize];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = Color.white;

                for (int row = 0; row < moduleCount; row++)
                for (int col = 0; col < moduleCount; col++)
                {
                    if (!matrix[row, col]) continue;
                    int px = (col + QUIET_ZONE) * scale;
                    int py = (totalModules - 1 - row - QUIET_ZONE) * scale;
                    for (int dy = 0; dy < scale; dy++)
                    for (int dx = 0; dx < scale; dx++)
                        pixels[(py + dy) * texSize + px + dx] = Color.black;
                }

                tex.SetPixels(pixels);
                tex.Apply();
                return tex;
            }
            catch (Exception e)
            {
                Debug.LogError($"[UnityPeraQRTextureGenerator] Failed to generate QR code: {e.Message}");
                return CreatePlaceholder(pixelSize);
            }
        }

        private static Texture2D CreatePlaceholder(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            var pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color(0.85f, 0.85f, 0.85f);
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }

}
namespace Blockmaker
{
    /// <summary>Reusable wallet marks. Returned textures are owned and destroyed by the caller.</summary>
    public static class BlockmakerUnityWalletIcons
    {
        // Original Pera mark from the previous Unity SDK (e86c1ba); not game branding.
        // Pera's logo remains its owner's mark. Keep it unmodified when identifying Pera.
        private const string PeraImage = "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";
        public static Texture2D CreatePeraLogo()
        {
            var icon = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "Pera logo" };
            if (ImageConversion.LoadImage(icon, Convert.FromBase64String(PeraImage), true)) return icon;
            UnityEngine.Object.Destroy(icon);
            return null;
        }
    }

    /// <summary>Game-owned branding and wording; game navigation stays outside the package.</summary>
    [Serializable]
    public sealed class BlockmakerUnityWalletAppearance
    {
        public string AppName = "Your game";
        public Font Font;
        public Color Surface = new Color(.09f, .09f, .12f);
        public Color Accent = new Color(.93f, .96f, .3f);
        public Color Text = Color.white;
        public string ChooseTitle = "Sign in";
        public string ChooseMessage = "How would you like to sign in?";
        public string PeraLabel = "Pera";
        public Texture2D PeraLogo;
        public string WalletLabel = "Wallet";
        public string WalletsTitle = "Choose a wallet";
        public string WalletsMessage = "Select the wallet you use.";
        public string BackLabel = "Back";
        public string EmailLabel = "Email";
        public string EmailTitle = "Your email wallet";
        public string EmailMessage = "Follow the email steps to sign in.";
        public string ConnectTitle = "Connect Pera Wallet";
        public string ConnectMessage = "Scan this code with Pera to connect your wallet.";
        public string ApprovalTitle = "One more approval";
        public string ApprovalMessage = "Approve the sign-in request in Pera. There is no charge.";
        public string ConnectingMessage = "Opening Pera…";
        public string VerifyingMessage = "Checking your sign-in…";
        public string ConnectStep = "STEP 1 OF 2";
        public string ApprovalStep = "STEP 2 OF 2";
        public string CancelLabel = "Cancel";
        public string OpenWalletLabel = "Confirm";
        public string ChangeAccountLabel = "Use another account";
        public string DisconnectingMessage = "Disconnecting…";
    }

    /// <summary>
    /// In-game Pera view, adapted from the previous SDK's two-step Pera modal.
    /// Attach to the game's existing UI Toolkit root so canvas fullscreen never
    /// changes. QR textures and event handlers belong to this attempt only.
    /// </summary>
    public sealed class BlockmakerUnityWalletView : IDisposable
    {
        private readonly BlockmakerWalletPackageWebGL package;
        private readonly BlockmakerUnityWalletAppearance appearance;
        private readonly VisualElement overlay, qr;
        private readonly Label title, message, step;
        private readonly Button openWallet, peraChoice, emailChoice, walletChoice, backChoice, changeAccount, cancel;
        private Texture2D ownedPeraLogo;
        private bool opened, resetting;
        private Action<BlockmakerWalletPackageWebGLResult> completion;
        public bool IsOpen => !disposed;
        public void Focus() { if (!disposed) overlay.BringToFront(); }
        private Action<BlockmakerWalletPackageWebGLResult> choiceCompletion;
#if UNITY_WEBGL && !UNITY_EDITOR
        private bool keyboardReleased;
        private bool previousKeyboardCapture;
#endif
        private Texture2D texture;
        private string uri;
        private bool disposed;

        public BlockmakerUnityWalletView(VisualElement parent,
            BlockmakerWalletPackageWebGL walletPackage,
            BlockmakerUnityWalletAppearance settings = null)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            package = walletPackage ?? throw new ArgumentNullException(nameof(walletPackage));
            appearance = settings ?? new BlockmakerUnityWalletAppearance();
            overlay = new VisualElement { name = "blockmaker-wallet-overlay" };
            overlay.style.position = Position.Absolute;
            overlay.style.left = overlay.style.right = overlay.style.top = overlay.style.bottom = 0;
            overlay.style.backgroundColor = new Color(0, 0, 0, .86f);
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;
            overlay.style.color = appearance.Text;
            if (appearance.Font != null) overlay.style.unityFontDefinition = FontDefinition.FromFont(appearance.Font);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.width = Length.Percent(100);
            scroll.style.flexShrink = 1;
            scroll.contentContainer.style.alignItems = Align.Center;
            var card = new VisualElement();
            card.style.width = Length.Percent(94);
            card.style.maxWidth = 520;
            card.style.maxHeight = Length.Percent(96);
            card.style.flexShrink = 1;
            card.style.backgroundColor = appearance.Surface;
            card.style.paddingLeft = card.style.paddingRight = 16;
            card.style.paddingTop = card.style.paddingBottom = 16;
            card.style.borderTopLeftRadius = card.style.borderTopRightRadius =
                card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 20;
            card.style.alignItems = Align.Center;
            var brand = Text(appearance.AppName, 16);
            brand.style.color = appearance.Accent;
            title = Text(appearance.ConnectTitle, 24);
            step = Text(appearance.ConnectStep, 16);
            qr = new VisualElement { name = "blockmaker-wallet-qr" };
            qr.style.width = qr.style.height = 240;
            qr.style.flexShrink = 0;
            qr.style.backgroundColor = Color.white;
            qr.style.display = DisplayStyle.None;
            message = Text(appearance.ConnectingMessage, 18);
            openWallet = Button(appearance.OpenWalletLabel, () => {
                if (disposed) return;
                // Explicit player action; no automatic app switch or fullscreen request.
                if (string.IsNullOrEmpty(uri)) { Application.OpenURL("perawallet://"); return; }
                // Reuse the previous SDK's Pera mobile URI convention.
                var hinted = uri.Contains("algorand=") ? uri : uri + (uri.Contains("?") ? "&" : "?") + "algorand=true";
                bool android = SystemInfo.operatingSystem.IndexOf("Android", StringComparison.OrdinalIgnoreCase) >= 0;
                Application.OpenURL(android ? hinted : "perawallet-wc://wc?uri=" + Uri.EscapeDataString(hinted));
            });
            openWallet.style.display = Application.isMobilePlatform ? DisplayStyle.Flex : DisplayStyle.None;
            peraChoice = Button("", () => Choose("pera"));
            peraChoice.name = "blockmaker-pera-choice";
            peraChoice.style.flexDirection = FlexDirection.Row;
            peraChoice.style.alignItems = Align.Center;
            peraChoice.style.justifyContent = Justify.Center;
            var logo = appearance.PeraLogo;
            if (logo == null) logo = ownedPeraLogo = BlockmakerUnityWalletIcons.CreatePeraLogo();
            var logoImage = new Image { image = logo, pickingMode = PickingMode.Ignore };
            logoImage.style.width = logoImage.style.height = 32;
            logoImage.style.marginRight = 12;
            peraChoice.Add(logoImage);
            peraChoice.Add(new Label(appearance.PeraLabel) { pickingMode = PickingMode.Ignore });
            emailChoice = Button(appearance.EmailLabel, () => Choose(BlockmakerWalletPackageWebGL.TxnLabWeb3AuthProvider));
            emailChoice.name = "blockmaker-email-choice";
            walletChoice = Button(appearance.WalletLabel, ShowWalletChoices);
            walletChoice.name = "blockmaker-wallet-choice";
            backChoice = Button(appearance.BackLabel, ShowSignInChoices);
            backChoice.name = "blockmaker-wallet-back";
            peraChoice.style.display = emailChoice.style.display = walletChoice.style.display = backChoice.style.display = DisplayStyle.None;
            changeAccount = Button(appearance.ChangeAccountLabel, ChangeAccount);
            changeAccount.name = "blockmaker-change-account";
            changeAccount.style.backgroundColor = new Color(.22f, .23f, .28f);
            changeAccount.style.color = appearance.Text;
            cancel = Button(appearance.CancelLabel, () => {
                if (disposed) return;
                if (choiceCompletion != null) {
                    var done = choiceCompletion; choiceCompletion = null;
                    Dispose(); done(BlockmakerWalletPackageWebGLResult.Failed("PLAYER_CANCELLED"));
                } else package.Cancel();
            });
            scroll.Add(brand); scroll.Add(title); scroll.Add(step); scroll.Add(qr);
            scroll.Add(message); scroll.Add(emailChoice); scroll.Add(walletChoice); scroll.Add(peraChoice); scroll.Add(backChoice);
            // Keep cancellation and account switching reachable when the QR or
            // instructions need to scroll on a short phone viewport.
            var actions = new VisualElement { name = "blockmaker-wallet-actions" };
            actions.style.width = Length.Percent(100);
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.flexWrap = Wrap.Wrap;
            actions.style.flexShrink = 0;
            foreach (var button in new[] { openWallet, changeAccount, cancel }) {
                button.style.width = 0;
                button.style.minWidth = 140;
                button.style.flexGrow = 1;
                button.style.marginLeft = button.style.marginRight = 4;
                button.style.whiteSpace = WhiteSpace.Normal;
                actions.Add(button);
            }
            changeAccount.style.fontSize = 16;
            card.Add(scroll); card.Add(actions); overlay.Add(card); parent.Add(overlay);
            overlay.BringToFront();
            package.PresentationChanged += Progress;
            overlay.RegisterCallback<DetachFromPanelEvent>(e => {
                // Editor hierarchy previews temporarily detach/re-attach documents.
                if (disposed || e.target != overlay || !Application.isPlaying) return;
                // A scene leaving must not strand a hidden provider request.
                package.Cancel();
                Finish(BlockmakerWalletPackageWebGLResult.Failed("PLAYER_CANCELLED"));
            });
        }

        /// <summary>Offer Email first when configured, then Wallet and its supported choices.</summary>
        public void OpenAccount(Action<BlockmakerWalletPackageWebGLResult> done)
        {
            if (disposed || opened) throw new InvalidOperationException("Wallet choices are already closed or open.");
            opened = true;
            completion = done ?? (_ => { });
            choiceCompletion = Finish;
            package.UseUnityPresentation = true;
            if (package.EmailEnabled) ShowSignInChoices(); else ShowWalletChoices();
        }

        private void Finish(BlockmakerWalletPackageWebGLResult result)
        {
            if (resetting) return;
            var done = completion; completion = null;
            choiceCompletion = null;
            Dispose();
            done?.Invoke(result);
        }

        private void ChangeAccount()
        {
            if (disposed || resetting) return;
            resetting = true;
            choiceCompletion = null;
            changeAccount.SetEnabled(false);
            cancel.SetEnabled(false);
            peraChoice.style.display = emailChoice.style.display = walletChoice.style.display =
                backChoice.style.display = openWallet.style.display = qr.style.display = DisplayStyle.None;
            title.text = appearance.ChooseTitle;
            step.text = "";
            step.style.display = DisplayStyle.None;
            message.text = appearance.DisconnectingMessage;
            try {
                package.Logout(result => {
                    if (disposed) return;
                    resetting = false;
                    changeAccount.SetEnabled(true);
                    cancel.SetEnabled(true);
                    if (!result.Success) { Finish(result); return; }
                    choiceCompletion = Finish;
                    if (package.EmailEnabled) ShowSignInChoices(); else ShowWalletChoices();
                });
            } catch {
                resetting = false;
                Finish(BlockmakerWalletPackageWebGLResult.Failed("PROVIDER_CLEANUP_REQUIRED"));
            }
        }

        private void ShowSignInChoices()
        {
            if (disposed || choiceCompletion == null) return;
            title.text = appearance.ChooseTitle;
            message.text = appearance.ChooseMessage;
            step.text = "";
            step.style.display = DisplayStyle.None;
            openWallet.style.display = qr.style.display = peraChoice.style.display = backChoice.style.display = DisplayStyle.None;
            emailChoice.style.display = walletChoice.style.display = DisplayStyle.Flex;
        }

        private void ShowWalletChoices()
        {
            if (disposed || choiceCompletion == null) return;
            title.text = appearance.WalletsTitle;
            message.text = appearance.WalletsMessage;
            step.text = "";
            step.style.display = DisplayStyle.None;
            openWallet.style.display = qr.style.display = emailChoice.style.display = walletChoice.style.display = DisplayStyle.None;
            peraChoice.style.display = DisplayStyle.Flex;
            backChoice.style.display = package.EmailEnabled ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void Choose(string providerId)
        {
            if (disposed || choiceCompletion == null) return;
            var done = choiceCompletion; choiceCompletion = null;
            peraChoice.style.display = emailChoice.style.display = walletChoice.style.display = backChoice.style.display = DisplayStyle.None;
            title.text = providerId == "pera" ? appearance.ConnectTitle : appearance.EmailTitle;
            message.text = providerId == "pera" ? appearance.ConnectingMessage : appearance.EmailMessage;
            step.text = appearance.ConnectStep;
            step.style.display = string.IsNullOrEmpty(step.text) ? DisplayStyle.None : DisplayStyle.Flex;
            openWallet.style.display = providerId == "pera" && Application.isMobilePlatform ? DisplayStyle.Flex : DisplayStyle.None;
            try { package.OpenAccount(providerId, done); }
            catch { Dispose(); done(BlockmakerWalletPackageWebGLResult.Failed("PROVIDER_UNAVAILABLE")); }
        }

        private Label Text(string value, int size)
        {
            var label = new Label(value);
            label.style.fontSize = size;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.marginTop = label.style.marginBottom = 4;
            label.style.flexShrink = 0;
            return label;
        }

        private Button Button(string value, Action clicked)
        {
            var button = new Button(clicked) { text = value };
            button.style.minHeight = 48;
            button.style.width = Length.Percent(100);
            button.style.marginTop = 8;
            button.style.flexShrink = 0;
            button.style.fontSize = 20;
            button.style.backgroundColor = appearance.Accent;
            button.style.color = Color.black;
            return button;
        }

        private void Progress(BlockmakerUnityWalletProgress value)
        {
            if (disposed || resetting || value == null) return;
            if (value.phase == "authenticated" || value.phase == "cancelled" || value.phase == "error") {
                Dispose(); return;
            }
            if (value.phase == "provider_auth") {
                title.text = appearance.EmailTitle;
                message.text = appearance.EmailMessage;
                openWallet.style.display = qr.style.display = DisplayStyle.None;
#if UNITY_WEBGL && !UNITY_EDITOR
                if (!keyboardReleased) {
                    previousKeyboardCapture = WebGLInput.captureAllKeyboardInput;
                    keyboardReleased = true;
                    WebGLInput.captureAllKeyboardInput = false;
                }
#endif
            } else if (value.phase == "qr") {
                uri = value.walletConnectUri;
                if (texture != null) ReleaseTexture(texture);
                texture = UnityPeraQRTextureGenerator.Generate(uri, 512);
                qr.style.backgroundImage = new StyleBackground(texture);
                qr.style.display = DisplayStyle.Flex;
                message.text = appearance.ConnectMessage;
            } else if (value.phase == "approval" || value.phase == "verifying") {
                uri = null;
                qr.style.display = DisplayStyle.None;
                step.text = appearance.ApprovalStep;
                step.style.display = string.IsNullOrEmpty(step.text) ? DisplayStyle.None : DisplayStyle.Flex;
                title.text = appearance.ApprovalTitle;
                message.text = value.phase == "verifying" || value.providerId == BlockmakerWalletPackageWebGL.TxnLabWeb3AuthProvider
                    ? appearance.VerifyingMessage : appearance.ApprovalMessage;
            }
        }

        private static void ReleaseTexture(Texture2D value)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (keyboardReleased) WebGLInput.captureAllKeyboardInput = previousKeyboardCapture;
#endif
            var pending = choiceCompletion; choiceCompletion = null;
            pending?.Invoke(BlockmakerWalletPackageWebGLResult.Failed("PLAYER_CANCELLED"));
            package.PresentationChanged -= Progress;
            overlay.RemoveFromHierarchy();
            if (texture != null) ReleaseTexture(texture);
            texture = null;
            if (ownedPeraLogo != null) ReleaseTexture(ownedPeraLogo);
            ownedPeraLogo = null;
            uri = null;
        }
    }
}
