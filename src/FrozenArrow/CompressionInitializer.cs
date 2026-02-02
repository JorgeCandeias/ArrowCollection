using Apache.Arrow.Compression;
using Apache.Arrow.Ipc;

namespace FrozenArrow;

/// <summary>
/// Initializes compression codec support for Arrow IPC operations.
/// </summary>
public static class CompressionInitializer
{
    private static bool _initialized;
    private static readonly object _lock = new();

    /// <summary>
    /// Gets the compression codec factory instance.
    /// </summary>
    internal static ICompressionCodecFactory CodecFactory { get; } = new CompressionCodecFactory();

    /// <summary>
    /// Ensures that compression codecs are registered with the Arrow library.
    /// This method is idempotent and can be called multiple times safely.
    /// </summary>
    public static void EnsureInitialized()
    {
        if (_initialized) return;

        lock (_lock)
        {
            if (_initialized) return;
            // Just ensure the factory is accessed to prove initialization
            _ = CodecFactory;
            _initialized = true;
        }
    }
}
