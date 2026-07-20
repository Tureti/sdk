namespace Proton.Sdk.Serialization;

public static class BytesExtensions
{
    public static ReadOnlySpan<byte> ToHexStringLower(this ReadOnlySpan<byte> bytes, Span<byte> hexBytes)
    {
        if (!Convert.TryToHexStringLower(bytes, hexBytes, out var hexByteCount))
        {
            throw new InvalidOperationException("Could not convert to hex string");
        }

        return hexBytes[..hexByteCount];
    }
}
