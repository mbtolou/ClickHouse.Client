using System;
using System.Globalization;
using ClickHouse.Client.Formats;
using ClickHouse.Client.Types.Grammar;
using ClickHouse.Client.Utility;

namespace ClickHouse.Client.Types;

internal class Time64Type : AbstractDateTimeType
{
    public int Scale { get; set; }

    public override string Name => "Time64";

    public override Type FrameworkType => typeof(TimeSpan);

    public override string ToString() => $"Time64({Scale})";

    /// <summary>
    /// تبدیل تیک‌های متغیر کلیک‌هاوس به تیک‌های استاندارد دات‌نت (100 نانوثانیه)
    /// </summary>
    public TimeSpan FromClickHouseTicks(long clickHouseTicks)
    {
        var ticks = MathUtils.ShiftDecimalPlaces(clickHouseTicks, 7 - Scale);
        return TimeSpan.FromTicks(ticks);
    }

    /// <summary>
    /// تبدیل تیک‌های دات‌نت به فرمت مقیاس‌دار کلیک‌هاوس
    /// </summary>
    public long ToClickHouseTicks(TimeSpan timeSpan) => MathUtils.ShiftDecimalPlaces(timeSpan.Ticks, Scale - 7);

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

    public override object Read(ExtendedBinaryReader reader)
    {
        // نوع Time64 در پروتکل باینری به صورت Int64 ارسال می‌شود
        return FromClickHouseTicks(reader.ReadInt64());
    }

    public override void Write(ExtendedBinaryWriter writer, object value)
    {
        TimeSpan ts = (TimeSpan)value;
        writer.Write(ToClickHouseTicks(ts));
    }
}
