using System;
using System.IO;

namespace ClickHouse.Client.Formats;

/// <summary>
///  Universal stream wrapper allowing to add 'PeekChar' support to streams with CanSeek=false
///  Suggestions for better solution wanted
/// </summary>
internal class PeekableStreamWrapper : Stream, IDisposable
{
    private readonly Stream stream;
    private bool hasReadAheadByte;
    private int readAheadByte;

    public PeekableStreamWrapper(Stream stream)
    {
        this.stream = stream;
        hasReadAheadByte = false;
        readAheadByte = 0;
    }

    public override bool CanRead => stream.CanRead;

    public override bool CanSeek => stream.CanSeek;

    public override bool CanWrite => stream.CanWrite;

    public override long Length => stream.Length;

    public override long Position { get => stream.Position; set => stream.Position = value; }

    public override void Flush() => stream.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (count == 0) return 0;

        int totalRead = 0;

        if (hasReadAheadByte)
        {
            buffer[offset] = (byte)readAheadByte;
            hasReadAheadByte = false;
            offset++;
            totalRead = 1;
            count--;
            if (count == 0) return totalRead;
        }

        totalRead += stream.Read(buffer, offset, count);
        return totalRead;
    }

    public override long Seek(long offset, SeekOrigin origin) => stream.Seek(offset, origin);

    public override void SetLength(long value) => stream.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count) => stream.Write(buffer, offset, count);

    public override int ReadByte()
    {
        if (!hasReadAheadByte)
        {
            return stream.ReadByte();
        }
        hasReadAheadByte = false;
        return readAheadByte;
    }

    public int Peek()
    {
        if (!hasReadAheadByte)
        {
            readAheadByte = stream.ReadByte();
            hasReadAheadByte = true;
        }
        return readAheadByte;
    }

    public new void Dispose() => stream.Dispose();
}
