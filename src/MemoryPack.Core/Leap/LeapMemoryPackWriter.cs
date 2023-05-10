using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using MemoryPack;

namespace MemoryPack;

#if NET7_0_OR_GREATER
using static MemoryMarshal;
#else
using static MemoryPack.Internal.MemoryMarshalEx;
#endif

public ref partial struct MemoryPackWriter<TBufferWriter>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteLeapArray<T>(T?[]? value)
    {
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            DangerousWriteLeapUnmanagedArray(value);
            return;
        }

        if (value == null)
        {
            WriteLeapNullCollectionHeader();
            return;
        }

        var formatter = GetFormatter<T>();
        WriteLeapCollectionHeader(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            formatter.Serialize(ref this, ref value[i]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DangerousWriteLeapUnmanagedArray<T>(T[]? value)
    {
        if (value == null)
        {
            WriteLeapNullCollectionHeader();
            return;
        }

        if (value.Length == 0)
        {
            WriteLeapCollectionHeader(0);
            return;
        }

        var srcLength = Unsafe.SizeOf<T>() * value.Length;
        var allocSize = srcLength; // Write7BitEncodedInt is calling Advance()
        Write7BitEncodedInt(value.Length);

        ref var dest = ref GetSpanReference(allocSize);
        ref var src = ref Unsafe.As<T, byte>(ref GetArrayDataReference(value));

        Unsafe.CopyBlockUnaligned(ref Unsafe.Add(ref dest, 4), ref src, (uint) srcLength);

        Advance(allocSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DangerousWriteLeapUnmanagedArray<T>(T[]? value, int length)
    {
        var srcLength = Unsafe.SizeOf<T>() * length;
        var allocSize = srcLength; // Write7BitEncodedInt is calling Advance()

        ref var dest = ref GetSpanReference(allocSize);
        ref var src = ref Unsafe.As<T, byte>(ref GetArrayDataReference(value));

        Unsafe.CopyBlockUnaligned(ref Unsafe.Add(ref dest, 4), ref src, (uint) srcLength);

        Advance(allocSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteLeapCollectionHeader(int length)
    {
        Write7BitEncodedInt(length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteLeapNullCollectionHeader()
    {
        byte nullByte = 0;
        Unsafe.WriteUnaligned(ref GetSpanReference(1), nullByte);
        Advance(1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write7BitEncodedInt(int value)
    {
        var allocSize = Get7BitEncodedAllocSize(value);
        // Write out an int 7 bits at a time.  The high bit of the byte,
        // when on, tells reader to continue reading more bytes.
        var v = (uint) value; // support negative numbers
        while (v >= 0x80)
        {
            Unsafe.WriteUnaligned(ref GetSpanReference(1), (byte) (v | 0x80));
            v >>= 7;
        }

        Unsafe.WriteUnaligned(ref GetSpanReference(1), (byte) v);
        Advance(allocSize);
    }

    public static int Get7BitEncodedAllocSize(int value)
    {
        // 7 bits = up to 127 = 1 byte
        // 14 bits = up to 16383 = 2 byte
        // 21 bits = up to 2097151 = 3 byte
        // else 4 byte
        if (value < 128)
            return 1;
        if (value < 16384)
            return 2;
        if (value < 2097152)
            return 3;
        return 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteLeapStringArray(string?[]? value)
    {
        if (value == null)
        {
            WriteLeapNullCollectionHeader();
            return;
        }

        foreach (var leapString in value)
        {
            WriteLeapString(leapString);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public unsafe void WriteLeapString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Write7BitEncodedInt(bytes.Length);
        DangerousWriteLeapUnmanagedArray(bytes, bytes.Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteLeapSpan<T>(scoped Span<T?> value)
    {
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            DangerousWriteLeapUnmanagedSpan(value);
            return;
        }

        var formatter = GetFormatter<T>();
        WriteLeapCollectionHeader(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            formatter.Serialize(ref this, ref value[i]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteLeapReadOnlySpan<T>(scoped ReadOnlySpan<T?> value)
    {
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            DangerousWriteLeapUnmanagedReadOnlySpan(value);
            return;
        }

        var formatter = GetFormatter<T>();
        WriteLeapCollectionHeader(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            var elem = value[i];
            formatter.Serialize(ref this, ref elem);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DangerousWriteLeapUnmanagedSpan<T>(scoped Span<T> value)
    {
        if (value.Length == 0)
        {
            WriteLeapCollectionHeader(0);
            return;
        }

        var srcLength = Unsafe.SizeOf<T>() * value.Length;
        var allocSize = srcLength + 1;

        ref var dest = ref GetSpanReference(allocSize);
        ref var src = ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(value));

        Write7BitEncodedInt(value.Length);
        //Unsafe.WriteUnaligned(ref dest, value.Length);
        Unsafe.CopyBlockUnaligned(ref Unsafe.Add(ref dest, 1), ref src, (uint)srcLength);

        Advance(allocSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DangerousWriteLeapUnmanagedReadOnlySpan<T>(scoped ReadOnlySpan<T> value)
    {
        if (value.Length == 0)
        {
            WriteLeapCollectionHeader(0);
            return;
        }

        var srcLength = Unsafe.SizeOf<T>() * value.Length;
        var allocSize = srcLength + 1;

        ref var dest = ref GetSpanReference(allocSize);
        ref var src = ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(value));

        Write7BitEncodedInt(value.Length);
        //Unsafe.WriteUnaligned(ref dest, value.Length);
        Unsafe.CopyBlockUnaligned(ref Unsafe.Add(ref dest, 1), ref src, (uint)srcLength);

        Advance(allocSize);
    }
}
