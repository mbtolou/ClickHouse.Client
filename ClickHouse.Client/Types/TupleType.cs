using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using ClickHouse.Client.Formats;
using ClickHouse.Client.Types.Grammar;
using ClickHouse.Client.Utility;

namespace ClickHouse.Client.Types;

internal class TupleType : ParameterizedType
{
    private Type frameworkType;
    private ClickHouseType[] underlyingTypes;

    // کش کردن Factory برای حذف Activator.CreateInstance در هر سطر
    private Func<object[], ITuple> tupleFactory;

    // کش کردن مبدل‌ها برای حذف Convert.ChangeType در موارد غیرضروری
    private Func<object, object>[] converters;

    public ClickHouseType[] UnderlyingTypes
    {
        get => underlyingTypes;
        set
        {
            underlyingTypes = value;
            frameworkType = DeviseFrameworkType(underlyingTypes);

            // ۱. ساخت Factory بهینه فقط یک بار
            tupleFactory = CreateTupleFactory(frameworkType, underlyingTypes.Length);

            // ۲. ساخت مبدل‌های بهینه فقط یک بار
            converters = new Func<object, object>[underlyingTypes.Length];
            for (int i = 0; i < underlyingTypes.Length; i++)
            {
                var targetType = underlyingTypes[i].FrameworkType;
                converters[i] = val =>
                {
                    // مسیر سریع: اگر نوع درست است یا قابل انتساب است، خودش را برمی‌گرداند
                    if (val == null || val.GetType() == targetType || targetType.IsAssignableFrom(val.GetType()))
                        return val;

                    // مسیر کند: فقط در صورت عدم تطابق نوع (که نادر است) از ChangeType استفاده می‌کند
                    return Convert.ChangeType(val, targetType, CultureInfo.InvariantCulture);
                };
            }
        }
    }

    private static Type DeviseFrameworkType(ClickHouseType[] underlyingTypes)
    {
        var count = underlyingTypes.Length;
#if !NET462
        if (count > 7)
            return typeof(LargeTuple);
#endif
        var typeArgs = new Type[count];
        for (var i = 0; i < count; i++)
        {
            typeArgs[i] = underlyingTypes[i].FrameworkType;
        }
        var genericType = Type.GetType("System.Tuple`" + typeArgs.Length);
        return genericType.MakeGenericType(typeArgs);
    }

#if !NET462
    // تغییر signature از params object[] به object[] برای جلوگیری از Allocation آرایه
    public ITuple MakeTuple(object[] values)
    {
        var count = values.Length;
        if (underlyingTypes.Length != count)
            throw new ArgumentException($"Count of tuple type elements ({underlyingTypes.Length}) does not match number of elements ({count})");

        // بررسی سریع: آیا اصلاً نیازی به کپی و تبدیل نوع داریم؟
        bool needsConversion = false;
        for (int i = 0; i < count; i++)
        {
            var val = values[i];
            var targetType = underlyingTypes[i].FrameworkType;
            if (val != null && val.GetType() != targetType && !targetType.IsAssignableFrom(val.GetType()))
            {
                needsConversion = true;
                break;
            }
        }

        // مسیر سریع (Happy Path): بدون هیچ Allocation اضافی یا Convert.ChangeType
        if (!needsConversion)
        {
            return tupleFactory(values);
        }

        // مسیر کند (Fallback): فقط زمانی که واقعاً نوع داده نیاز به تبدیل داشته باشد
        var valuesCopy = new object[count];
        for (int i = 0; i < count; i++)
        {
            valuesCopy[i] = converters[i](values[i]);
        }

        return tupleFactory(valuesCopy);
    }
#endif

    // ساخت یک Delegate بهینه از Constructor با استفاده از Expression Trees
    // این کار باعث می‌شود سرعت ساخت Tuple دقیقاً معادل new Tuple<...>(...) باشد
    private static Func<object[], ITuple> CreateTupleFactory(Type tupleType, int argCount)
    {
        if (tupleType == typeof(LargeTuple))
        {
            return args => new LargeTuple(args);
        }

        var param = Expression.Parameter(typeof(object[]), "args");
        var typeArgs = tupleType.GetGenericArguments();
        var constructor = tupleType.GetConstructor(typeArgs);

        if (constructor == null)
        {
            // Fallback نهایی اگر به هر دلیلی Constructor پیدا نشد
            return args => (ITuple)Activator.CreateInstance(tupleType, args);
        }

        var arguments = new Expression[argCount];
        for (int i = 0; i < argCount; i++)
        {
            var index = Expression.ArrayAccess(param, Expression.Constant(i));
            // Unbox کردن به نوع دقیق مورد نیاز Constructor
            arguments[i] = Expression.Convert(index, typeArgs[i]);
        }

        var newExpr = Expression.New(constructor, arguments);
        return Expression.Lambda<Func<object[], ITuple>>(newExpr, param).Compile();
    }

    public override Type FrameworkType => frameworkType;

    public override string Name => "Tuple";

    public override ParameterizedType Parse(SyntaxTreeNode node, Func<SyntaxTreeNode, ClickHouseType> parseClickHouseTypeFunc, TypeSettings settings)
    {
        if (node.ChildNodes.Count == 0)
        {
            return new TupleType
            {
                UnderlyingTypes = [new NothingType()],
            };
        }
        var underlyingTypes = node.ChildNodes.Select(parseClickHouseTypeFunc).ToArray();
        return new TupleType { UnderlyingTypes = underlyingTypes };
    }

    public override string ToString() => $"{Name}({string.Join(",", UnderlyingTypes.Select(t => t.ToString()))})";

    public override object Read(ExtendedBinaryReader reader)
    {
        var count = UnderlyingTypes.Length;
        var contents = new object[count];
        for (var i = 0; i < count; i++)
        {
            var value = UnderlyingTypes[i].Read(reader);
            contents[i] = ClearDBNull(value);
        }
#if !NET462
        return MakeTuple(contents);
#else
        return contents;
#endif
    }

    public override void Write(ExtendedBinaryWriter writer, object value)
    {
#if !NET462
        if (value is ITuple tuple)
        {
            if (tuple.Length != UnderlyingTypes.Length)
                throw new ArgumentException("Wrong number of elements in Tuple", nameof(value));
            for (var i = 0; i < tuple.Length; i++)
            {
                UnderlyingTypes[i].Write(writer, tuple[i]);
            }
            return;
        }
#endif
        if (value is IList list)
        {
            if (list.Count != UnderlyingTypes.Length)
                throw new ArgumentException("Wrong number of elements in Tuple", nameof(value));
            for (var i = 0; i < list.Count; i++)
            {
                UnderlyingTypes[i].Write(writer, list[i]);
            }
            return;
        }
    }
}
