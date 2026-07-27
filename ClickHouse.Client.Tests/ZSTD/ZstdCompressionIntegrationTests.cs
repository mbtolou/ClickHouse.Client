using ClickHouse.Client.ADO;
using ClickHouse.Client.ADO.Parameters;
using ClickHouse.Client.Copy;
using ClickHouse.Client.Utility;
using NUnit.Framework;
using System;
using System.Data;
using System.Threading.Tasks;

namespace ClickHouse.Client.Tests.Integration;

[TestFixture]
[Category("Integration")]
public class ZstdCompressionIntegrationTests
{

    [Test]
    public async Task BulkInsert_With_Zstd_Should_Succeed()
    {
        // Connection string با فعال‌سازی ZSTD
        var cs = new ClickHouseConnectionStringBuilder(Environment.GetEnvironmentVariable("CLICKHOUSE_CONNECTION"))
        {
            HttpCompression = ClickHouseCompression.Zstd
        };

        var connection = new ClickHouseConnection(cs.ToString());
        // Arrange
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE OR REPLACE TABLE test_zstd (id UInt64, name String, value Float64) ENGINE = Memory";
        await cmd.ExecuteNonQueryAsync();

        // Act - BulkCopy با ZSTD
        var table = new DataTable();
        table.Columns.Add("id", typeof(ulong));
        table.Columns.Add("name", typeof(string));
        table.Columns.Add("value", typeof(double));

        for (int i = 0; i < 100_000; i++)
        {
            table.Rows.Add((ulong)i, $"Name_{i}", i * 1.5);
        }

        using var bulkCopy = new ClickHouseBulkCopy(connection)
        {
            DestinationTableName = "test_zstd",
            BatchSize = 100_000,
        };

        await bulkCopy.InitAsync();
        await bulkCopy.WriteToServerAsync(table, default);

        // Assert
        cmd.CommandText = "SELECT count() FROM test_zstd";
        var count = await cmd.ExecuteScalarAsync();
        Assert.That(count, Is.EqualTo(100_000L));

        using var cmd_drop = connection.CreateCommand();
        cmd.CommandText = "DROP TABLE IF EXISTS test_zstd;";
        await cmd.ExecuteNonQueryAsync();
    }

    [Test]
    public async Task Select_Large_Data_With_Zstd_Should_Return_Correct_Results()
    {
        // Connection string با فعال‌سازی ZSTD
        var cs = new ClickHouseConnectionStringBuilder(Environment.GetEnvironmentVariable("CLICKHOUSE_CONNECTION"))
        {
            HttpCompression = ClickHouseCompression.Zstd
        };

        var connection = new ClickHouseConnection(cs.ToString());
        // Arrange - داده حجیم بساز
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT
                number AS id,
                concat('Name_', toString(number)) AS name,
                number * 1.5 AS value
            FROM numbers(500_000)";

        // Act
        using var reader = await cmd.ExecuteReaderAsync();
        var rowCount = 0;

        while (await reader.ReadAsync())
        {
            rowCount++;
            if (rowCount == 1)
            {
                Assert.That((ulong)reader.GetValue(0), Is.EqualTo(0UL));
                Assert.That(reader.GetString(1), Is.EqualTo("Name_0"));
            }
        }

        // Assert
        Assert.That(rowCount, Is.EqualTo(500_000));
    }

    [Test]
    public async Task RoundTrip_Insert_And_Select_Should_Match()
    {
        // Connection string با فعال‌سازی ZSTD
        var cs = new ClickHouseConnectionStringBuilder(Environment.GetEnvironmentVariable("CLICKHOUSE_CONNECTION"))
        {
            HttpCompression = ClickHouseCompression.Zstd
        };

        var connection = new ClickHouseConnection(cs.ToString());
        // Arrange
        var testData = new[]
        {
            (Id: 1, Name: "سلام دنیا", Value: 3.14),
            (Id: 2, Name: "Hello World", Value: 2.71),
            (Id: 3, Name: new string('X', 10_000), Value: 1.0), // رشته بزرگ
        };

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE OR REPLACE TABLE test_zstd_rt (id Int64, name String, value Float64) ENGINE = Memory";
        await cmd.ExecuteNonQueryAsync();

        // Insert
        foreach (var (id, name, value) in testData)
        {
            cmd.CommandText = $"INSERT INTO test_zstd_rt VALUES ({id}, '{name.Replace("'", "\\'")}', {value})";
            await cmd.ExecuteNonQueryAsync();
        }

        // Act - Select
        cmd.CommandText = "SELECT id, name, value FROM test_zstd_rt ORDER BY id";
        using var reader = await cmd.ExecuteReaderAsync();

        // Assert
        var index = 0;
        while (await reader.ReadAsync())
        {
            Assert.That(reader.GetInt64(0), Is.EqualTo(testData[index].Id));
            Assert.That(reader.GetString(1), Is.EqualTo(testData[index].Name));
            Assert.That(reader.GetDouble(2), Is.EqualTo(testData[index].Value).Within(0.001));
            index++;
        }
        Assert.That(index, Is.EqualTo(3));

        using var cmd_drop = connection.CreateCommand();
        cmd.CommandText = "DROP TABLE IF EXISTS test_zstd;";
        await cmd.ExecuteNonQueryAsync();
    }
}
