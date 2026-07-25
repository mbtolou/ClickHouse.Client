using System;
using System.Collections;
using System.Linq;
using ClickHouse.Client.Formats;
using ClickHouse.Client.Types.Grammar;

namespace ClickHouse.Client.Types;

internal class NestedType : TupleType
{
    public override string Name => "Nested";

    public override Type FrameworkType => base.FrameworkType.MakeArrayType();

    public override ParameterizedType Parse(SyntaxTreeNode node, Func<SyntaxTreeNode, ClickHouseType> parseClickHouseTypeFunc, TypeSettings settings)
    {
        var underlyingTypes = node.ChildNodes
            .Select(ClearFieldName)
            .Select(parseClickHouseTypeFunc)
            .ToArray();

        // اگر لیست خالی باشد (مثلاً وقتی نام پایه "Nested" از سیستم خوانده می‌شود)، 
        // یک نوع پیش‌فرض قرار می‌دهیم تا از خطای NullReferenceException در TupleType جلوگیری شود.
        if (underlyingTypes.Length == 0)
        {
            underlyingTypes = new ClickHouseType[] { new NothingType() };
        }

        return new NestedType
        {
            UnderlyingTypes = underlyingTypes,
        };
    }

    private static SyntaxTreeNode ClearFieldName(SyntaxTreeNode node)
    {
        if (node.ChildNodes.Count > 0)
            return node;

        var name = node.Value;
        var lastSpaceIndex = name.LastIndexOf(' ');
        return lastSpaceIndex > 0 ? new SyntaxTreeNode { Value = name.Substring(lastSpaceIndex + 1) } : node;
    }

    public override object Read(ExtendedBinaryReader reader)
    {
        var length = reader.Read7BitEncodedInt();
        var data = Array.CreateInstance(base.FrameworkType, length);
        for (var i = 0; i < length; i++)
        {
            data.SetValue(ClearDBNull(base.Read(reader)), i);
        }
        return data;
    }

    public override void Write(ExtendedBinaryWriter writer, object value)
    {
        if (value is null || value is DBNull)
        {
            writer.Write7BitEncodedInt(0);
            return;
        }

        var collection = (IList)value;
        writer.Write7BitEncodedInt(collection.Count);
        for (var i = 0; i < collection.Count; i++)
        {
            base.Write(writer, collection[i]);
        }
    }
}
