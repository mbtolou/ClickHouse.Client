using System;
using ClickHouse.Client.Formats;

namespace ClickHouse.Client.Types;

internal class MultiLineStringType : ClickHouseType
{
	public override Type FrameworkType => typeof(string);

	public override object Read(ExtendedBinaryReader reader) => reader.ReadString();

	public override void Write(ExtendedBinaryWriter writer, object value) => writer.Write(value?.ToString() ?? "");

	public override string ToString() => "MultiLineString";
}
