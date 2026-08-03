# ClickHouse.Client — Optimized Fork

> A **performance-optimized fork** of [DarkWanderer/ClickHouse.Client](https://github.com/DarkWanderer/ClickHouse.Client) — the unofficial ADO.NET client for [ClickHouse](https://clickhouse.com).
> Full API compatibility with upstream, with substantial throughput improvements and new capabilities.

[![NuGet](https://img.shields.io/nuget/v/[YOUR.PACKAGE.NAME]?label=NuGet)](https://www.nuget.org/packages/[YOUR.PACKAGE.NAME]/)
[![Downloads](https://img.shields.io/nuget/dt/[YOUR.PACKAGE.NAME])](https://www.nuget.org/packages/[YOUR.PACKAGE.NAME]/)
[![License](https://img.shields.io/github/license/[YOUR-USER]/[YOUR-REPO])](LICENSE)
[![Tests](https://github.com/[YOUR-USER]/[YOUR-REPO]/actions/workflows/tests.yml/badge.svg)](https://github.com/[YOUR-USER]/[YOUR-REPO]/actions/workflows/tests.yml)

> ⚠️ **Unofficial.** Not affiliated with or endorsed by ClickHouse, Inc. Based on the excellent work of [Oleg V. Kozlyuk](https://github.com/DarkWanderer).

---

## 🎯 Why this fork?

The upstream `ClickHouse.Client` is a solid, well-designed ADO.NET client. However, profiling real-world workloads revealed meaningful overhead in the **read path** — virtual dispatch, per-row allocations, and general-purpose stream handling.

This fork applies a series of **low-level, measurement-driven optimizations** that remove that overhead while keeping the public API **drop-in compatible** with upstream. The result is an average **~30% faster read throughput**, with some workloads improving by over **50%**.

---

## ⚡ Performance vs. Upstream

Measured on the same machine, same runtime, reading **100,000 rows** per benchmark. _(See [Benchmark Methodology](#-benchmark-methodology).)_

| Benchmark | Upstream | This Fork | Improvement |
|---|---:|---:|---:|
| `SelectTuple` | 74.08 ms | 34.01 ms | 🟢 **−54%** |
| `SelectString` | 42.44 ms | 22.14 ms | 🟢 **−48%** |
| `SelectUInt32` | 45.53 ms | 29.23 ms | 🟢 −36% |
| `SelectFloat64` | 32.36 ms | 21.08 ms | 🟢 −35% |
| `SelectInt32` | 45.03 ms | 29.60 ms | 🟢 −34% |
| `SelectFloat32` | 46.21 ms | 31.05 ms | 🟢 −33% |
| `SelectDate32` | 44.29 ms | 30.14 ms | 🟢 −32% |
| `SelectDate` | 24.71 ms | 16.90 ms | 🟢 −32% |
| `SelectDecimal256` | 65.63 ms | 45.64 ms | 🟢 −30% |
| `SelectInt64` | 35.73 ms | 25.51 ms | 🟢 −29% |
| `SelectDecimal64` | 54.81 ms | 40.11 ms | 🟢 −27% |
| `SelectDecimal128` | 59.46 ms | 44.05 ms | 🟢 −26% |
| `SelectDateTime` | 44.46 ms | 33.25 ms | 🟢 −25% |
| `SelectUInt64` | 33.74 ms | 26.30 ms | 🟢 −22% |
| `BulkInsertInt32` | 734.18 ms | 631.00 ms | 🟢 −14% |
| `SelectArray` | 53.69 ms | 47.90 ms | 🟡 −11% |
| `SelectNullableInt32` | 33.11 ms | 30.88 ms | 🟡 −7% |

> **Every single benchmark improved.** A uniform directional shift (rather than random up/down) indicates a genuine, systematic gain rather than measurement noise.

### Memory allocations

The gains are not only in CPU time. For `SelectTuple`, allocation dropped dramatically:

| Benchmark | Upstream | This Fork | Change |
|---|---:|---:|---:|
| `SelectTuple` Allocated | 39.47 MB | 13.35 MB | 🟢 **−66%** |
| `SelectTuple` Gen0 collections | 4857 | 1600 | 🟢 −67% |

Less allocation means less GC pressure — which improves **p99 latency** across your whole service, not just the query itself.

---

## ✨ New features

### 🗜️ ZSTD compression

In addition to the existing methods, this fork supports **ZSTD** (`ZstdSharp`), which is now the default recommendation:

- **Lower CPU usage** — up to ~3× faster compression than GZip at comparable ratio on realistic data.
- **Less network transfer** — better compression ratio than GZip for typical ClickHouse payloads.
- **Faster decompression** — ~1.6× faster than GZip, with ~2× fewer allocations.

```csharp
var connection = new ClickHouseConnection(connectionString);
// ZSTD is used automatically for the compressed binary protocol where supported.
