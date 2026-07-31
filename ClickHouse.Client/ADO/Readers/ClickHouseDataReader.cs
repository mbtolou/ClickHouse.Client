using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ClickHouse.Client.Formats;
using ClickHouse.Client.Numerics;
using ClickHouse.Client.Types;
using ClickHouse.Client.Utility;

namespace ClickHouse.Client.ADO.Readers;

public class ClickHouseDataReader : DbDataReader, IEnumerator<IDataReader>, IEnumerable<IDataReader>, IDataRecord
{
    private const int BufferSize = 512 * 1024;

    private readonly HttpResponseMessage httpResponse;
    private readonly ExtendedBinaryReader reader;

    // ✅ فیلدهای خصوصی — دسترسی مستقیم (ldfld) بدون هیچ call
    private object[] _currentRow;
    private string[] _fieldNames;
    private ClickHouseType[] _rawTypes;

    private ClickHouseDataReader(HttpResponseMessage httpResponse, ExtendedBinaryReader reader, string[] names, ClickHouseType[] types)
    {
        this.httpResponse = httpResponse ?? throw new ArgumentNullException(nameof(httpResponse));
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _rawTypes = types;
        _fieldNames = names;
        _currentRow = new object[_fieldNames.Length];
    }

    internal static ClickHouseDataReader FromHttpResponse(HttpResponseMessage httpResponse, TypeSettings settings)
    {
        if (httpResponse is null) throw new ArgumentNullException(nameof(httpResponse));
        ExtendedBinaryReader reader = null;
        try
        {
            var stream = new BufferedStream(httpResponse.Content.ReadAsStreamAsync().GetAwaiter().GetResult(), BufferSize);
            reader = new ExtendedBinaryReader(stream);
            var (names, types) = ReadHeaders(reader, settings);
            return new ClickHouseDataReader(httpResponse, reader, names, types);
        }
        catch (Exception)
        {
            httpResponse?.Dispose();
            reader?.Dispose();
            throw;
        }
    }

    // ✅ propertyها فقط برای سازگاری — به فیلد اشاره می‌کنند
    protected object[] CurrentRow { get => _currentRow; set => _currentRow = value; }

    protected string[] FieldNames { get => _fieldNames; set => _fieldNames = value; }

    private protected ClickHouseType[] RawTypes { get => _rawTypes; set => _rawTypes = value; }

    internal ClickHouseType GetEffectiveClickHouseType(int ordinal)
    {
        var type = _rawTypes[ordinal];
        return type is NullableType nt ? nt.UnderlyingType : type;
    }

    internal ClickHouseType GetClickHouseType(int ordinal) => _rawTypes[ordinal];

    // ✅ indexer مستقیم به فیلد — GetValue (یک virtual call) حذف شد
    public override object this[int ordinal] => _currentRow[ordinal];

    public override object this[string name] => _currentRow[GetOrdinal(name)];

    public override int Depth { get; }

    public override int FieldCount => _rawTypes?.Length ?? throw new InvalidOperationException();

    public override bool IsClosed => false;

    public sealed override bool HasRows => true;

    public override int RecordsAffected { get; }

    // ✅ همه متدهای typed مستقیم به فیلد — bypass کامل GetValue
    public override bool GetBoolean(int ordinal) => Convert.ToBoolean(_currentRow[ordinal], CultureInfo.InvariantCulture);

    public override byte GetByte(int ordinal) => (byte)_currentRow[ordinal];

    public override long GetBytes(int ordinal, long dataOffset, byte[] buffer, int bufferOffset, int length) => throw new NotImplementedException();

    public override char GetChar(int ordinal) => (char)_currentRow[ordinal];

    public override long GetChars(int ordinal, long dataOffset, char[] buffer, int bufferOffset, int length) => throw new NotImplementedException();

    public override string GetDataTypeName(int ordinal) => _rawTypes[ordinal].ToString();

    public override DateTime GetDateTime(int ordinal) => (DateTime)_currentRow[ordinal];

    public virtual DateTimeOffset GetDateTimeOffset(int ordinal) => GetEffectiveClickHouseType(ordinal) is AbstractDateTimeType adt ?
        adt.CoerceToDateTimeOffset(GetDateTime(ordinal)) : throw new InvalidCastException();

    public override decimal GetDecimal(int ordinal)
    {
        var value = _currentRow[ordinal];
        return value is ClickHouseDecimal clickHouseDecimal ? clickHouseDecimal.ToDecimal(CultureInfo.InvariantCulture) : (decimal)value;
    }

    public override double GetDouble(int ordinal) => (double)_currentRow[ordinal];

    public override Type GetFieldType(int ordinal)
    {
        var rawType = _rawTypes[ordinal];
        return rawType is NullableType nt ? nt.UnderlyingType.FrameworkType : rawType.FrameworkType;
    }

    public override float GetFloat(int ordinal) => (float)_currentRow[ordinal];

    public override Guid GetGuid(int ordinal) => (Guid)_currentRow[ordinal];

    public override short GetInt16(int ordinal) => (short)_currentRow[ordinal];

    public override int GetInt32(int ordinal) => (int)_currentRow[ordinal];

    public override long GetInt64(int ordinal) => (long)_currentRow[ordinal];

    public override string GetName(int ordinal) => _fieldNames[ordinal];

    // ✅ حذف lambda و delegate allocation
    public override int GetOrdinal(string name)
    {
        var names = _fieldNames;
        for (int i = 0; i < names.Length; i++)
        {
            if (names[i] == name)
                return i;
        }
        throw new ArgumentException("Column does not exist", nameof(name));
    }

    // ✅ fast-path برای string — حذف virtual call و ToString بی‌مورد
    public override string GetString(int ordinal)
    {
        var value = _currentRow[ordinal];
        return value is string s ? s : value?.ToString();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override object GetValue(int ordinal) => _currentRow[ordinal];

    public override int GetValues(object[] values)
    {
        var row = _currentRow;
        if (row == null) throw new InvalidOperationException();
        row.CopyTo(values, 0);
        return row.Length;
    }

    public override bool IsDBNull(int ordinal)
    {
        var value = _currentRow[ordinal];
        return value is DBNull || value is null;
    }

    public override bool NextResult() => false;

    public override void Close() => Dispose();

    public override T GetFieldValue<T>(int ordinal) => (T)_currentRow[ordinal];

    public override DataTable GetSchemaTable() => SchemaDescriber.DescribeSchema(this);

    public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    public ushort GetUInt16(int ordinal) => (ushort)_currentRow[ordinal];

    public uint GetUInt32(int ordinal) => (uint)_currentRow[ordinal];

    public ulong GetUInt64(int ordinal) => (ulong)_currentRow[ordinal];

    public IPAddress GetIPAddress(int ordinal) => (IPAddress)_currentRow[ordinal];

#if !NET462
    public ITuple GetTuple(int ordinal) => (ITuple)_currentRow[ordinal];

#endif
    public sbyte GetSByte(int ordinal) => (sbyte)_currentRow[ordinal];

    public BigInteger GetBigInteger(int ordinal) => (BigInteger)_currentRow[ordinal];

    public override bool Read()
    {
        if (reader.PeekChar() == -1) return false;

        var types = _rawTypes;
        var data = _currentRow;

        // استفاده از Span برای دسترسی سریع‌تر
        var span = data.AsSpan();
        for (var i = 0; i < types.Length; i++)
        {
            span[i] = types[i].Read(reader);
        }
        return true;
    }

#pragma warning disable CA2215
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            httpResponse?.Dispose();
            reader?.Dispose();
        }
    }
#pragma warning restore CA2215

    private static (string[], ClickHouseType[]) ReadHeaders(ExtendedBinaryReader reader, TypeSettings settings)
    {
        if (reader.PeekChar() == -1)
        {
            return ([], []);
        }
        var count = reader.Read7BitEncodedInt();
        var names = new string[count];
        var types = new ClickHouseType[count];

        for (var i = 0; i < count; i++)
            names[i] = reader.ReadString();

        for (var i = 0; i < count; i++)
        {
            var chType = reader.ReadString();
            types[i] = TypeConverter.ParseClickHouseType(chType, settings);
        }
        return (names, types);
    }

    public bool MoveNext() => Read();

    public void Reset() => throw new NotSupportedException();

    public override IEnumerator GetEnumerator() => this;

    IEnumerator<IDataReader> IEnumerable<IDataReader>.GetEnumerator() => this;

    public IDataReader Current => this;

    object IEnumerator.Current => this;
}
