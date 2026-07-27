using System;
using ClickHouse.Client.Formats;

namespace ClickHouse.Client.Types;

internal class IntervalType : ClickHouseType
{
    public override Type FrameworkType => typeof(TimeSpan);

    public override object Read(ExtendedBinaryReader reader) => TimeSpan.FromSeconds(reader.ReadInt32());

    public override void Write(ExtendedBinaryWriter writer, object value) => writer.Write(1);

    public override string ToString() => "Interval";
}
