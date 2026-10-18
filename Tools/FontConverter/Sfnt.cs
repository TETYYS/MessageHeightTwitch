using System.Buffers.Binary;
using System.Text;

// Minimal OpenType table-directory access: https://learn.microsoft.com/typography/opentype/spec/otff
internal static class Sfnt
{
    private const int HeaderSize = 12;
    private const int TableRecordSize = 16;
    private const int CheckSumAdjustmentOffset = 8;
    private const uint CheckSumMagic = 0xB1B0AFBA;
    private const uint TrueTypeVersion = 0x00010000;
    private static readonly uint CffTag = Tag("OTTO");
    private static readonly uint CollectionTag = Tag("ttcf");
    private static readonly uint HeadTag = Tag("head");

    internal static uint Tag(string Tag) => BinaryPrimitives.ReadUInt32BigEndian(Encoding.ASCII.GetBytes(Tag));

    internal static bool IsSfnt(ReadOnlySpan<byte> Data) =>
        Data.Length >= HeaderSize && BinaryPrimitives.ReadUInt32BigEndian(Data) is var version && (version == TrueTypeVersion || version == CffTag);

    internal static bool HasTable(ReadOnlySpan<byte> Data, string Tag) => FindTable(Data, 0, Sfnt.Tag(Tag)) >= 0;

    private static int FindTable(ReadOnlySpan<byte> Data, int Directory, uint Tag)
    {
        var count = BinaryPrimitives.ReadUInt16BigEndian(Data[(Directory + 4)..]);
        for (var x = 0; x < count; x++)
            if (BinaryPrimitives.ReadUInt32BigEndian(Data[(Directory + HeaderSize + x * TableRecordSize)..]) == Tag)
                return x;
        return -1;
    }

    /// <summary>
    /// Copies one face out of a TrueType collection into a standalone font. Table
    /// data is copied byte for byte; only offsets and the head checksum change.
    /// </summary>
    internal static byte[] ExtractFace(byte[] Data, int Index)
    {
        if (BinaryPrimitives.ReadUInt32BigEndian(Data) != CollectionTag)
            return Index == 0 ? Data : throw new InvalidDataException("Only collections have more than one face.");
        var faces = BinaryPrimitives.ReadInt32BigEndian(Data.AsSpan(8));
        if ((uint)Index >= (uint)faces)
            throw new InvalidDataException($"Collection has {faces} faces; face {Index} does not exist.");
        var directory = BinaryPrimitives.ReadInt32BigEndian(Data.AsSpan(12 + Index * 4));
        var count = BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan(directory + 4));
        var records = Enumerable.Range(0, count)
            .Select(i => Data.AsSpan(directory + HeaderSize + i * TableRecordSize, TableRecordSize).ToArray())
            .ToArray();
        var size = HeaderSize + count * TableRecordSize;
        size += records.Sum(record => Align(BinaryPrimitives.ReadInt32BigEndian(record.AsSpan(12))));
        var font = new byte[size];
        // The version and binary-search fields depend only on the table count.
        Data.AsSpan(directory, HeaderSize).CopyTo(font);
        var position = HeaderSize + count * TableRecordSize;
        var head = -1;
        for (var x = 0; x < count; x++)
        {
            var record = font.AsSpan(HeaderSize + x * TableRecordSize, TableRecordSize);
            records[x].CopyTo(record);
            var offset = BinaryPrimitives.ReadInt32BigEndian(record[8..]);
            var length = BinaryPrimitives.ReadInt32BigEndian(record[12..]);
            Data.AsSpan(offset, length).CopyTo(font.AsSpan(position));
            BinaryPrimitives.WriteInt32BigEndian(record[8..], position);
            if (BinaryPrimitives.ReadUInt32BigEndian(record) == HeadTag)
                head = position;
            position += Align(length);
        }
        if (head < 0)
            throw new InvalidDataException("Font has no head table.");
        var adjustment = font.AsSpan(head + CheckSumAdjustmentOffset, 4);
        adjustment.Clear();
        BinaryPrimitives.WriteUInt32BigEndian(adjustment, CheckSumMagic - CheckSum(font));
        return font;
    }

    private static int Align(int Length) => (Length + 3) & ~3;

    private static uint CheckSum(ReadOnlySpan<byte> Data)
    {
        uint sum = 0;
        for (var x = 0; x < Data.Length; x += 4)
            sum += BinaryPrimitives.ReadUInt32BigEndian(Data[x..]);
        return sum;
    }
}
