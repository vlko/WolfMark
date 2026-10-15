using System.Runtime.InteropServices;

namespace Zapisnik.Index.Embeddings;

/// <summary>sqlite-vec expects float32 vectors as little-endian blobs.</summary>
internal static class VectorBlob
{
    public static byte[] ToBlob(ReadOnlySpan<float> vector) =>
        BitConverter.IsLittleEndian
            ? MemoryMarshal.AsBytes(vector).ToArray()
            : throw new PlatformNotSupportedException("Big-endian hosts are not supported.");

    public static float[] Normalize(float[] vector)
    {
        double sum = 0;
        foreach (var v in vector)
        {
            sum += v * v;
        }

        var norm = (float)Math.Sqrt(sum);
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }

        return vector;
    }
}
