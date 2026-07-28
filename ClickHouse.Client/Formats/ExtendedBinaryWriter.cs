using System.IO;
using System.Text;

namespace ClickHouse.Client.Formats;

// ExtendedBinaryWriter.cs
public class ExtendedBinaryWriter : BinaryWriter
{
    // ✅ constructor موجود (احتمالاً):
    public ExtendedBinaryWriter(Stream stream)
        : base(stream) { }

    // ✅ این را اضافه کنید:
    public ExtendedBinaryWriter(Stream stream, bool leaveOpen)
        : base(stream, Encoding.UTF8, leaveOpen) { }
}
