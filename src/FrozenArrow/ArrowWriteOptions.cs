using Apache.Arrow.Ipc;

namespace FrozenArrow;

/// <summary>
/// Options for writing and serializing Arrow collections.
/// </summary>
public sealed class ArrowWriteOptions
{
    /// <summary>
    /// Gets the default write options (no compression).
    /// </summary>
    public static ArrowWriteOptions Default { get; } = new();

    /// <summary>
    /// Gets or sets the compression codec to use when writing Arrow IPC data.
    /// Defaults to <c>null</c> (no compression).
    /// </summary>
    /// <remarks>
    /// Supported codecs:
    /// <list type="bullet">
    ///   <item><see cref="CompressionCodecType.Lz4Frame"/> - Fast compression/decompression, moderate ratio</item>
    ///   <item><see cref="CompressionCodecType.Zstd"/> - Better compression ratio, slightly slower</item>
    /// </list>
    /// Requires the Apache.Arrow.Compression package for codec implementations.
    /// </remarks>
    public CompressionCodecType? CompressionCodec { get; init; }
}
