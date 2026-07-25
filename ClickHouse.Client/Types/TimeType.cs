using System;
using System.Globalization;
using ClickHouse.Client.Formats;
using ClickHouse.Client.Types.Grammar;

namespace ClickHouse.Client.Types;

internal class TimeType : AbstractDateTimeType
{
    public override string Name => "Time";

    // تغییر نوع فریم‌ورک از DateTime به TimeSpan
    public override Type FrameworkType => typeof(TimeSpan);

    public override string ToString() => "Time";

    public override object Read(ExtendedBinaryReader reader)
    {
        // نوع Time در کلیک‌هاوس به صورت UInt32 (ثانیه از شروع روز) ذخیره می‌شود
        uint seconds = reader.ReadUInt32();
        return TimeSpan.FromSeconds(seconds);
    }

    public override void Write(ExtendedBinaryWriter writer, object value)
    {
        TimeSpan ts = (TimeSpan)value;
        writer.Write((uint)ts.TotalSeconds);
    }

    public override ParameterizedType Parse(SyntaxTreeNode node, Func<SyntaxTreeNode, ClickHouseType> parseClickHouseTypeFunc, TypeSettings settings)
    {
        // استخراج پارامتر Scale از گره دستوری (مثلاً Time64(3))
        var scale = int.Parse(node.ChildNodes[0].Value, CultureInfo.InvariantCulture);

        // توجه: نوع Time64 در کلیک‌هاوس برخلاف DateTime64، پارامتر Timezone را قبول نمی‌کند
        return new Time64Type
        {
            Scale = scale,
        };
    }
}
