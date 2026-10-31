using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.Core.Tests;

/// <summary>One entry of a ZIP archive as its central directory describes it.</summary>
internal sealed record ZipEntryInfo(string Name, ushort VersionNeeded, ushort Flags, ushort Method, ushort DosTime, ushort DosDate, uint Crc32,
    uint CompressedSize, uint UncompressedSize, uint LocalHeaderOffset)
{
    internal DateTime Modified => new(1980 + (DosDate >> 9), (DosDate >> 5) & 15, DosDate & 31, DosTime >> 11, (DosTime >> 5) & 63, (DosTime & 31) * 2);
}

/// <summary>
/// A reader of ZIP archives that shares no code with <see cref="ZipArchive"/>, which is what writes them: it finds the end record,
/// walks the central directory and the local headers, and refuses anything this exporter does not write (a comment, extra fields,
/// Zip64 fields, a gap or trailing bytes between entries).
/// </summary>
internal static class ZipDirectory
{
    private const uint LocalSignature = 0x04034b50, CentralSignature = 0x02014b50, EndSignature = 0x06054b50, DescriptorSignature = 0x08074b50;

    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));

    /// <summary>The entries in the order of the central directory. Every local header and data descriptor is checked against it.</summary>
    internal static IReadOnlyList<ZipEntryInfo> Read(byte[] archive)
    {
        int end = archive.Length - 22;
        Assert.IsTrue(end >= 0, "The archive is shorter than an end record.");
        Assert.AreEqual(EndSignature, U32(archive, end), "The archive does not end with an end record that has no comment.");
        Assert.AreEqual(0, U16(archive, end + 4), "disk number");
        Assert.AreEqual(0, U16(archive, end + 6), "disk of the central directory");
        int count = U16(archive, end + 10);
        Assert.AreEqual(count, U16(archive, end + 8), "entries on this disk");
        Assert.AreNotEqual(0xFFFF, count, "Zip64 is not expected for an archive this small.");
        uint size = U32(archive, end + 12), offset = U32(archive, end + 16);
        Assert.AreNotEqual(0xFFFFFFFFu, offset, "Zip64 is not expected for an archive this small.");
        Assert.AreEqual((uint)end, offset + size, "The central directory must end where the end record begins.");
        Assert.AreEqual(0, U16(archive, end + 20), "comment length");

        var entries = new List<ZipEntryInfo>();
        int position = checked((int)offset);
        for (int index = 0; index < count; index++)
        {
            Assert.AreEqual(CentralSignature, U32(archive, position), $"central directory header {index}");
            ushort versionNeeded = U16(archive, position + 6), flags = U16(archive, position + 8), method = U16(archive, position + 10);
            ushort time = U16(archive, position + 12), date = U16(archive, position + 14);
            uint crc = U32(archive, position + 16), compressed = U32(archive, position + 20), uncompressed = U32(archive, position + 24);
            int nameLength = U16(archive, position + 28), extraLength = U16(archive, position + 30), commentLength = U16(archive, position + 32);
            Assert.AreEqual(0, extraLength, $"entry {index}: extra field");
            Assert.AreEqual(0, commentLength, $"entry {index}: comment");
            Assert.AreEqual(0, U16(archive, position + 34), $"entry {index}: disk");
            uint local = U32(archive, position + 42);
            Assert.AreNotEqual(0xFFFFFFFFu, compressed, $"entry {index}: Zip64 is not expected for an archive this small.");
            string name = Encoding.UTF8.GetString(archive, position + 46, nameLength);
            entries.Add(new ZipEntryInfo(name, versionNeeded, flags, method, time, date, crc, compressed, uncompressed, local));
            position += 46 + nameLength + extraLength + commentLength;
        }
        Assert.AreEqual(end, position, "The central directory holds more than its entries.");

        // The entries follow each other from the start of the file with nothing between them, and each local header says what the
        // central directory says.
        long next = 0;
        foreach (var entry in entries)
        {
            Assert.AreEqual(next, entry.LocalHeaderOffset, $"{entry.Name}: where its local header starts");
            int at = checked((int)entry.LocalHeaderOffset);
            Assert.AreEqual(LocalSignature, U32(archive, at), $"{entry.Name}: local header");
            ushort flags = U16(archive, at + 6), method = U16(archive, at + 8);
            Assert.AreEqual(entry.Flags, flags, $"{entry.Name}: flags");
            Assert.AreEqual(entry.Method, method, $"{entry.Name}: method");
            int nameLength = U16(archive, at + 26), extraLength = U16(archive, at + 28);
            Assert.AreEqual(entry.Name, Encoding.UTF8.GetString(archive, at + 30, nameLength), "name in the local header");
            Assert.AreEqual(0, extraLength, $"{entry.Name}: extra field in the local header");
            long data = at + 30L + nameLength + extraLength;
            long after = data + entry.CompressedSize;
            if ((flags & 8) == 0)
            {
                Assert.AreEqual(entry.Crc32, U32(archive, at + 14), $"{entry.Name}: CRC-32 in the local header");
                Assert.AreEqual(entry.CompressedSize, U32(archive, at + 18), $"{entry.Name}: compressed size in the local header");
                Assert.AreEqual(entry.UncompressedSize, U32(archive, at + 22), $"{entry.Name}: size in the local header");
            }
            else
            {
                // A data descriptor follows the data: the signature is optional, then the CRC-32 and the two sizes.
                int descriptor = checked((int)after);
                if (U32(archive, descriptor) == DescriptorSignature) descriptor += 4;
                Assert.AreEqual(entry.Crc32, U32(archive, descriptor), $"{entry.Name}: CRC-32 in the data descriptor");
                Assert.AreEqual(entry.CompressedSize, U32(archive, descriptor + 4), $"{entry.Name}: compressed size in the data descriptor");
                Assert.AreEqual(entry.UncompressedSize, U32(archive, descriptor + 8), $"{entry.Name}: size in the data descriptor");
                after = descriptor + 12;
            }
            next = after;
        }
        Assert.AreEqual(offset, (uint)next, "Nothing may lie between the last entry and the central directory.");
        return entries;
    }

    /// <summary>The bytes of an entry, expanded (Deflate, or stored as they are).</summary>
    internal static byte[] Extract(byte[] archive, ZipEntryInfo entry)
    {
        int at = checked((int)entry.LocalHeaderOffset);
        int data = at + 30 + U16(archive, at + 26) + U16(archive, at + 28);
        var compressed = new MemoryStream(archive, data, checked((int)entry.CompressedSize), writable: false);
        byte[] result;
        if (entry.Method == 0) result = compressed.ToArray();
        else
        {
            Assert.AreEqual(8, entry.Method, $"{entry.Name}: compression method");
            using var inflated = new DeflateStream(compressed, CompressionMode.Decompress);
            using var all = new MemoryStream();
            inflated.CopyTo(all);
            result = all.ToArray();
        }
        Assert.AreEqual((long)entry.UncompressedSize, result.LongLength, $"{entry.Name}: expanded size");
        return result;
    }
}

internal static class Crc32Reference
{
    private static readonly uint[] Table = Enumerable.Range(0, 256).Select(index =>
    {
        uint value = (uint)index;
        for (int bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
        return value;
    }).ToArray();

    /// <summary>CRC-32 as ZIP and PNG define it (polynomial 0xEDB88320, initial value and final XOR of 0xFFFFFFFF).</summary>
    internal static uint Of(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte value in bytes) crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }
}

/// <summary>A 16-bit PCM WAV file as a strict reader sees it: a canonical header (RIFF, "fmt " of 16 bytes, "data") and nothing after it.</summary>
internal sealed record WaveFile(int Channels, int Rate, int BitsPerSample, long Frames, byte[] Bytes)
{
    internal const int DataOffset = 44;

    internal short Sample(long frame, int channel) =>
        BinaryPrimitives.ReadInt16LittleEndian(Bytes.AsSpan(checked((int)(DataOffset + frame * 4 + channel * 2))));

    internal bool IsSilent(long fromFrame, long frames) =>
        Bytes.AsSpan(checked((int)(DataOffset + fromFrame * 4)), checked((int)(frames * 4))).IndexOfAnyExcept((byte)0) < 0;

    internal bool IsSilent() => IsSilent(0, Frames);

    internal static WaveFile Read(byte[] bytes, string what)
    {
        Assert.IsTrue(bytes.Length >= DataOffset, $"{what}: shorter than a header");
        Assert.AreEqual("RIFF", Encoding.ASCII.GetString(bytes, 0, 4), what);
        Assert.AreEqual((uint)(bytes.Length - 8), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)), $"{what}: RIFF size");
        Assert.AreEqual("WAVEfmt ", Encoding.ASCII.GetString(bytes, 8, 8), what);
        Assert.AreEqual(16u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16)), $"{what}: fmt chunk size");
        Assert.AreEqual((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(20)), $"{what}: PCM");
        ushort channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(22));
        uint rate = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(24));
        ushort bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(34));
        Assert.AreEqual(rate * channels * bits / 8, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(28)), $"{what}: bytes per second");
        Assert.AreEqual((ushort)(channels * bits / 8), BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(32)), $"{what}: bytes per frame");
        Assert.AreEqual("data", Encoding.ASCII.GetString(bytes, 36, 4), what);
        uint data = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(40));
        Assert.AreEqual((uint)(bytes.Length - DataOffset), data, $"{what}: data size (nothing may follow the data)");
        Assert.AreEqual(0u, data % (uint)(channels * bits / 8), $"{what}: whole frames");
        return new WaveFile(channels, checked((int)rate), bits, data / (uint)(channels * bits / 8), bytes);
    }
}

/// <summary>An archive exported by <see cref="WaveStemExporter"/>, read back by the independent readers above.</summary>
internal sealed class StemArchive
{
    internal required byte[] Bytes { get; init; }

    internal required IReadOnlyList<ZipEntryInfo> Entries { get; init; }

    internal IReadOnlyList<string> Names => Entries.Select(entry => entry.Name).ToArray();

    internal static StemArchive Read(byte[] bytes) => new() { Bytes = bytes, Entries = ZipDirectory.Read(bytes) };

    internal static async Task<StemArchive> ReadAsync(string path) => Read(await File.ReadAllBytesAsync(path));

    internal byte[] Extract(string name) =>
        ZipDirectory.Extract(Bytes, Entries.Single(entry => entry.Name == name));

    internal WaveFile Wave(string name) => WaveFile.Read(Extract(name), name);

    internal JsonDocument Manifest() => JsonDocument.Parse(Extract("manifest.json"));
}
