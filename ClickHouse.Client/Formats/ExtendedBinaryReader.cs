using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace ClickHouse.Client.Formats;

internal class ExtendedBinaryReader : BinaryReader
{
    private readonly PeekableStreamWrapper streamWrapper;

    public ExtendedBinaryReader(Stream stream)
        : base(new PeekableStreamWrapper(stream), Encoding.UTF8, false)
    {
        streamWrapper = (PeekableStreamWrapper)BaseStream;
    }

    public new int Read7BitEncodedInt() => base.Read7BitEncodedInt();

    /// <summary>
    /// Performs guaranteed read of requested number of bytes, or throws an exception
    /// </summary>
    /// <param name="count">number of bytes to read</param>
    /// <returns>number of bytes read, always equals to count</returns>
    /// <exception cref="EndOfStreamException">thrown if requested number of bytes is not available</exception>
    public override byte[] ReadBytes(int count)
    {
        var buffer = new byte[count];
        Read(buffer, 0, count);
        return buffer;
    }

    /// <summary>
    /// Performs guaranteed read of requested number of bytes, or throws an exception
    /// </summary>
    /// <param name="buffer">buffer array</param>
    /// <param name="index">index to write to in the buffer</param>
    /// <param name="count">number of bytes to read</param>
    /// <returns>number of bytes read, always equals to count</returns>
    /// <exception cref="EndOfStreamException">thrown if requested number of bytes is not available</exception>
    public override int Read(byte[] buffer, int index, int count)
    {
        int bytesRead = base.Read(buffer, index, count);
        if (bytesRead < count)
            throw new EndOfStreamException($"Expected to read {count} bytes, got {bytesRead}");
        return bytesRead;
    }

    public override int PeekChar() => streamWrapper.Peek();

    // در کلاس ExtendedBinaryReader
    public override string ReadString()
    {
        // خواندن length prefix (7-bit encoded) — متد protected در BinaryReader
        int byteCount = Read7BitEncodedInt();

        if (byteCount == 0)
            return string.Empty;
        if (byteCount < 0)                                   // ✅ چک صحت (مثل BinaryReader استاندارد)
            throw new IOException("Invalid string length.");

        // ✅ مسیر سریع: stringهای کوتاه با stack allocation (صفر GC pressure)
        // اکثر stringهای ClickHouse (نام ستون، مقادیر enum، کدها) کوتاه هستند
        if (byteCount <= 512)
        {
            Span<byte> buffer = stackalloc byte[byteCount];
            ReadExact(buffer);
            return Encoding.UTF8.GetString(buffer);
        }

        // stringهای بلند: ArrayPool به جای new byte[]
        byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            ReadExact(rented.AsSpan(0, byteCount));
            return Encoding.UTF8.GetString(rented, 0, byteCount);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReadExact(Span<byte> buffer)
    {
        int read = BaseStream.Read(buffer);
        if (read == buffer.Length) return;          // fast-path: ۹۹٪ موارد
        if (read == 0) throw new EndOfStreamException();
        do
        {
            int r = BaseStream.Read(buffer.Slice(read));
            if (r == 0) throw new EndOfStreamException();
            read += r;
        } while (read < buffer.Length);
    }
}
