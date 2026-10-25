using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.Core.Tests;

// Independent, bounded SMF reader. Delimit events structurally; never locate notes by byte-substring search.
internal sealed record SmfEvent(long Tick, byte Status, byte MetaType, byte[] Data);
internal sealed record SmfFile(int Ppq, IReadOnlyList<SmfEvent> Events);

internal static class MidiReader
{
    internal static SmfFile Read(byte[] bytes)
    {
        Assert.IsTrue(bytes.Length >= 26);
        Assert.AreEqual("MThd", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.AreEqual(6u, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4)));
        Assert.AreEqual((ushort)0, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(8)));
        Assert.AreEqual((ushort)1, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(10)));
        int ppq = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(12));
        Assert.IsTrue(ppq > 0 && ppq <= 32767);
        Assert.AreEqual("MTrk", Encoding.ASCII.GetString(bytes, 14, 4));
        Assert.AreEqual((uint)(bytes.Length - 22), BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(18)));
        int cursor = 22; long tick = 0; byte running = 0;
        var events = new List<SmfEvent>();
        while (cursor < bytes.Length)
        {
            tick += Variable(bytes, ref cursor);
            Assert.IsTrue(cursor < bytes.Length, "Missing event status.");
            byte status = bytes[cursor];
            if (status >= 0x80) cursor++;
            else { Assert.IsTrue(running >= 0x80 && running < 0xf0); status = running; }
            byte meta = 0;
            int length;
            if (status == 0xff)
            {
                running = 0;
                Assert.IsTrue(cursor < bytes.Length);
                meta = bytes[cursor++]; length = Variable(bytes, ref cursor);
            }
            else
            {
                Assert.IsTrue(status >= 0x80 && status < 0xf0, "Unexpected system event.");
                running = status;
                length = (status & 0xf0) is 0xc0 or 0xd0 ? 1 : 2;
            }
            Assert.IsTrue(length >= 0 && length <= bytes.Length - cursor, "Truncated event payload.");
            byte[] data = bytes.AsSpan(cursor, length).ToArray(); cursor += length;
            if (status != 0xff) Assert.IsTrue(data.All(b => b < 0x80), "Illegal channel data byte.");
            events.Add(new(tick, status, meta, data));
        }
        Assert.AreEqual(bytes.Length, cursor);
        Assert.AreEqual(1, events.Count(e => e.Status == 0xff && e.MetaType == 0x2f));
        Assert.AreEqual((byte)0x2f, events[^1].MetaType); Assert.AreEqual(0, events[^1].Data.Length);
        return new(ppq, events.AsReadOnly());
    }

    private static int Variable(byte[] bytes, ref int cursor)
    {
        int value = 0;
        for (int i = 0; i < 4; i++)
        {
            Assert.IsTrue(cursor < bytes.Length, "Truncated VLQ.");
            byte b = bytes[cursor++]; value = (value << 7) | (b & 0x7f);
            if ((b & 0x80) == 0) return value;
        }
        Assert.Fail("VLQ exceeds four bytes.");
        return 0;
    }
}
