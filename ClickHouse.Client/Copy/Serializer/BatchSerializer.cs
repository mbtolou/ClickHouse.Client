using System;
using System.IO;
using System.Text;
using ClickHouse.Client.Formats;

namespace ClickHouse.Client.Copy.Serializer;

internal class BatchSerializer : IBatchSerializer
{
    public static BatchSerializer GetByRowBinaryFormat(RowBinaryFormat format)
    {
        return format switch
        {
            RowBinaryFormat.RowBinary => new BatchSerializer(new RowBinarySerializer()),
            RowBinaryFormat.RowBinaryWithDefaults => new BatchSerializer(new RowBinaryWithDefaultsSerializer()),
            _ => throw new NotSupportedException(format.ToString())
        };
    }

    private readonly IRowSerializer rowSerializer;

    public BatchSerializer(IRowSerializer rowSerializer)
    {
        this.rowSerializer = rowSerializer;
    }

    public void Serialize(Batch batch, Stream stream)
    {
        // ✅ StreamWriter با leaveOpen: true → stream اصلی باز می‌ماند
        using (var textWriter = new StreamWriter(stream, Encoding.UTF8, 4 * 1024, leaveOpen: true))
        {
            textWriter.WriteLine(batch.Query);
        }

        // ✅ BinaryWriter با leaveOpen: true
        var writer = new ExtendedBinaryWriter(stream, leaveOpen: true);

        object[] row = null;
        int counter = 0;
        var enumerator = batch.Rows.GetEnumerator();
        try
        {
            while (enumerator.MoveNext())
            {
                row = (object[])enumerator.Current;
                rowSerializer.Serialize(row, batch.Types, writer);

                counter++;
                if (counter >= batch.Size)
                    break;
            }

            writer.Flush();
        }
        catch (Exception e)
        {
            throw new ClickHouseBulkCopySerializationException(row, e);
        }
        // ⚠️ writer را dispose نکنید! stream اصلی باید باز بماند
    }
}
