using System;
using System.Collections.Generic;
using System.Text;

namespace Blockmaker;

/// <summary>
/// Self-contained QR code encoder. No external dependencies.
/// Supports byte-mode encoding with error correction level M.
/// Returns a bool[,] module matrix suitable for rendering.
/// </summary>
public static class QRCodeEncoder
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

    static QRCodeEncoder()
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
