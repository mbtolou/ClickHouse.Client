using System;
using System.Globalization;
using ClickHouse.Client.Formats;

namespace ClickHouse.Client.Types;

internal class StringType : ClickHouseType
{
    public override Type FrameworkType => typeof(string);

    public override object Read(ExtendedBinaryReader reader) => reader.ReadString();

    public override string ToString() => "String";

    public override void Write(ExtendedBinaryWriter writer, object value)
    {
        // ✅ مسیر سریع: حذف Convert.ToString برای stringها
        if (value is string s)
        {
            writer.Write(s);
            return;
        }

        // مسیر کند: فقط برای انواع غیر-string
        writer.Write(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
    }
}
