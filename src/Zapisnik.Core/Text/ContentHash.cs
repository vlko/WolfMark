using System.IO.Hashing;
using System.Text;

namespace Zapisnik.Core.Text;

/// <summary>Fast, stable content hash (XxHash3, 64-bit, hex).</summary>
public static class ContentHash
{
    public static string Compute(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return Compute(bytes);
    }

    public static string Compute(ReadOnlySpan<byte> bytes) => XxHash3.HashToUInt64(bytes).ToString("x16");
}
