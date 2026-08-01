using System;
using ClickHouse.Client.Formats;
using ClickHouse.Client.Types.Grammar;

namespace ClickHouse.Client.Types;

internal class NullableType : ParameterizedType
{
    private ClickHouseType underlyingType;
    private Type frameworkType;   // کش‌شده — MakeGenericType فقط یک‌بار

    public ClickHouseType UnderlyingType
    {
        get => underlyingType;
        set
        {
            // ✅ fail-fast defensive (بدون lock — نیازی نیست)
            underlyingType = value ?? throw new ArgumentNullException(nameof(UnderlyingType));
            frameworkType = CalculateFrameworkType(value);
        }
    }

    private static Type CalculateFrameworkType(ClickHouseType type)
    {
        var ft = type.FrameworkType;
        return ft.IsValueType ? typeof(Nullable<>).MakeGenericType(ft) : ft;
    }

    public override Type FrameworkType => frameworkType;

    public override string Name => "Nullable";

    public override ParameterizedType Parse(SyntaxTreeNode node, Func<SyntaxTreeNode, ClickHouseType> parseClickHouseTypeFunc, TypeSettings settings)
    {
        return new NullableType
        {
            UnderlyingType = parseClickHouseTypeFunc(node.SingleChild),
        };
    }

    public override object Read(ExtendedBinaryReader reader) =>
        reader.ReadByte() > 0 ? DBNull.Value : underlyingType.Read(reader);

    public override string ToString() => $"{Name}({underlyingType})";

    public override void Write(ExtendedBinaryWriter writer, object value)
    {
        if (value == null || value is DBNull)
        {
            writer.Write((byte)1);
        }
        else
        {
            writer.Write((byte)0);
            underlyingType.Write(writer, value);
        }
    }
}
