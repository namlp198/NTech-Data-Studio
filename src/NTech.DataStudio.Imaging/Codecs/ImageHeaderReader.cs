using System.Buffers.Binary;
using System.Text;
using NTech.DataStudio.Application.Prepare;

namespace NTech.DataStudio.Imaging.Codecs;

/// <summary>
/// Reads image facts from file headers without decoding pixels, which keeps the
/// audit of thousands of large images fast. Returns null for a variant it does
/// not understand (the caller then falls back to a full decode); throws when the
/// file is visibly truncated.
/// </summary>
public static class ImageHeaderReader
{
    private const int TailSearchBytes = 4096;

    public static ImageHeader? Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var start = new byte[64];
        var count = stream.ReadAtLeast(start, start.Length, throwOnEndOfStream: false);
        if (count < 12) throw new ImageReadException("File quá ngắn, không phải ảnh.");

        if (start[0] == 0x89 && start[1] == 'P' && start[2] == 'N' && start[3] == 'G') return ReadPng(stream, start);
        if (start[0] == 0xFF && start[1] == 0xD8) return ReadJpeg(stream);
        if (start[0] == 'B' && start[1] == 'M') return ReadBmp(stream, start);
        if ((start[0] == 'I' && start[1] == 'I') || (start[0] == 'M' && start[1] == 'M')) return ReadTiff(stream, start);
        if (Ascii(start, 0, 4) == "RIFF" && Ascii(start, 8, 4) == "WEBP") return ReadWebp(start);
        return null;
    }

    private static ImageHeader ReadPng(Stream stream, byte[] start)
    {
        var tail = ReadTail(stream, 12);
        if (Encoding.ASCII.GetString(tail).IndexOf("IEND", StringComparison.Ordinal) < 0)
            throw new ImageReadException("File PNG bị cắt cụt: thiếu đoạn kết thúc.");

        var channels = start[25] switch
        {
            0 => 1,
            2 => 3,
            3 => 3,
            4 => 2,
            6 => 4,
            _ => 3
        };
        return new ImageHeader(
            "PNG",
            BinaryPrimitives.ReadInt32BigEndian(start.AsSpan(16)),
            BinaryPrimitives.ReadInt32BigEndian(start.AsSpan(20)),
            start[24],
            channels);
    }

    private static ImageHeader? ReadJpeg(Stream stream)
    {
        // Entropy-coded data stuffs every 0xFF, so FF D9 near the end can only be the end marker.
        var tail = ReadTail(stream, TailSearchBytes);
        var hasEnd = false;
        for (var index = tail.Length - 2; index >= 0 && !hasEnd; index--)
            hasEnd = tail[index] == 0xFF && tail[index + 1] == 0xD9;
        if (!hasEnd) throw new ImageReadException("File JPEG bị cắt cụt: thiếu dấu kết thúc.");

        stream.Position = 2;
        var lengthBytes = new byte[2];
        while (true)
        {
            if (stream.ReadByte() != 0xFF) return null;

            // Any number of fill bytes may precede the marker code.
            int type;
            do
            {
                type = stream.ReadByte();
            }
            while (type == 0xFF);

            // Start of scan before a frame header: nothing more to learn here.
            if (type < 0 || type == 0xDA) return null;
            if (stream.ReadAtLeast(lengthBytes, 2, throwOnEndOfStream: false) < 2) return null;

            var isFrame = type is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
            if (isFrame)
            {
                var frame = new byte[6];
                if (stream.ReadAtLeast(frame, 6, throwOnEndOfStream: false) < 6) return null;
                return new ImageHeader(
                    "JPEG",
                    BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(3)),
                    BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(1)),
                    frame[0],
                    frame[5]);
            }

            stream.Position += BinaryPrimitives.ReadUInt16BigEndian(lengthBytes) - 2;
        }
    }
private static ImageHeader ReadBmp(Stream stream, byte[] start)
    {
        var dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(start.AsSpan(10));
        var width = BinaryPrimitives.ReadInt32LittleEndian(start.AsSpan(18));
        var height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(start.AsSpan(22)));
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(start.AsSpan(28));
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(start.AsSpan(30));

        // Only uncompressed rows have a size known up front.
        if (compression == 0)
        {
            var rowBytes = ((long)width * bits + 31) / 32 * 4;
            if (stream.Length < dataOffset + rowBytes * height)
                throw new ImageReadException("File BMP bị cắt cụt: thiếu dữ liệu điểm ảnh.");
        }

        var channels = bits switch
        {
            <= 8 => 1,
            32 => 4,
            _ => 3
        };
        return new ImageHeader("BMP", width, height, 8, channels);
    }

    private static ImageHeader? ReadTiff(Stream stream, byte[] start)
    {
        var little = start[0] == 'I';
        if (U16(start, 2, little) != 42) return null; // BigTIFF and others go to the full decode.

        var directory = new byte[2];
        stream.Position = U32(start, 4, little);
        if (stream.ReadAtLeast(directory, 2, throwOnEndOfStream: false) < 2) return null;

        var entryCount = U16(directory, 0, little);
        var entries = new byte[entryCount * 12];
        if (stream.ReadAtLeast(entries, entries.Length, throwOnEndOfStream: false) < entries.Length)
            throw new ImageReadException("File TIFF bị cắt cụt: thiếu thư mục thẻ.");

        int width = 0, height = 0, bits = 1, samples = 1;
        for (var index = 0; index < entryCount; index++)
        {
            var offset = index * 12;
            var type = U16(entries, offset + 2, little);
            var valueCount = U32(entries, offset + 4, little);
            var value = type == 3 ? U16(entries, offset + 8, little) : (int)U32(entries, offset + 8, little);
            switch (U16(entries, offset, little))
            {
                case 256:
                    width = value;
                    break;
                case 257:
                    height = value;
                    break;
                case 258 when valueCount * 2 <= 4:
                    bits = value;
                    break;
                case 258:
                    bits = ReadShortAt(stream, value, little);
                    break;
                case 277:
                    samples = value;
                    break;
            }
        }

        return width > 0 && height > 0 ? new ImageHeader("TIFF", width, height, bits, samples) : null;
    }

    private static int ReadShortAt(Stream stream, long position, bool little)
    {
        var buffer = new byte[2];
        var back = stream.Position;
        stream.Position = position;
        var read = stream.ReadAtLeast(buffer, 2, throwOnEndOfStream: false);
        stream.Position = back;
        return read == 2 ? U16(buffer, 0, little) : 8;
    }

    private static ImageHeader? ReadWebp(byte[] start)
    {
        switch (Ascii(start, 12, 4))
        {
            case "VP8 ":
                return new ImageHeader(
                    "WEBP",
                    BinaryPrimitives.ReadUInt16LittleEndian(start.AsSpan(26)) & 0x3FFF,
                    BinaryPrimitives.ReadUInt16LittleEndian(start.AsSpan(28)) & 0x3FFF,
                    8,
                    3);
            case "VP8L":
            {
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(start.AsSpan(21));
                var hasAlpha = ((bits >> 28) & 1) == 1;
                return new ImageHeader("WEBP", (int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1, 8, hasAlpha ? 4 : 3);
            }
            case "VP8X":
            {
                var hasAlpha = (start[20] & 0x10) != 0;
                var width = (start[24] | start[25] << 8 | start[26] << 16) + 1;
                var height = (start[27] | start[28] << 8 | start[29] << 16) + 1;
                return new ImageHeader("WEBP", width, height, 8, hasAlpha ? 4 : 3);
            }
            default:
                return null;
        }
    }

    private static byte[] ReadTail(Stream stream, int count)
    {
        var length = (int)Math.Min(count, stream.Length);
        var tail = new byte[length];
        stream.Position = stream.Length - length;
        stream.ReadExactly(tail);
        return tail;
    }

    private static string Ascii(byte[] data, int offset, int count) => Encoding.ASCII.GetString(data, offset, count);

    private static int U16(byte[] data, int offset, bool little) => little
        ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset))
        : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));

    private static long U32(byte[] data, int offset, bool little) => little
        ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset))
        : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
}
