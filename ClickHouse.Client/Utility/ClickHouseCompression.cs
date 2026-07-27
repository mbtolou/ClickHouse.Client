/// <summary>
/// Originally sourced from https://stackoverflow.com/questions/16673714/how-to-compress-http-request-on-the-fly-and-without-loading-compressed-buffer-in
/// </summary>
namespace ClickHouse.Client.Utility;

public enum ClickHouseCompression
{
    None,
    GZip,
    Deflate,
    Zstd,
}
