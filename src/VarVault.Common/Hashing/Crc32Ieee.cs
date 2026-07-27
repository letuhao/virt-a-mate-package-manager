using VarVault.Common;

namespace VarVault.Common.Hashing;

/// <summary>IEEE CRC-32 (polynomial 0xEDB88320) — same algorithm zip central directories use.</summary>
public static class Crc32Ieee
{
    private static readonly uint[] Table = CreateTable();

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    public static uint Compute(Stream stream, Span<byte> buffer)
    {
        Guard.NotNull(stream);
        var crc = 0xFFFFFFFFu;
        int n;
        while ((n = stream.Read(buffer)) > 0)
        {
            for (var i = 0; i < n; i++)
                crc = Table[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var j = 0; j < 8; j++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }

        return table;
    }
}
