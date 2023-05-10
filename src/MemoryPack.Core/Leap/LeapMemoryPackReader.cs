using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
//using System.Text.Unicode;
using MemoryPack;
using Serilog;
#if NET7_0_OR_GREATER
using static System.GC;
using static System.Runtime.InteropServices.MemoryMarshal;
#else
using static MemoryPack.Internal.MemoryMarshalEx;
#endif

namespace MemoryPack
{
    public ref partial struct MemoryPackReader
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReadLeapArray<T>(scoped ref T?[]? value)
        {
            if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                DangerousReadLeapUnmanagedArray(ref value);
                return;
            }

            if (!TryReadLeapCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            if (length == 0)
            {
                value = Array.Empty<T>();
                return;
            }

            // T[] support overwrite
            if (value == null || value.Length != length)
            {
                value = new T[length];
            }

            var formatter = GetFormatter<T>();
            for (int i = 0; i < length; i++)
            {
                formatter.Deserialize(ref this, ref value[i]);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DangerousReadLeapUnmanagedArray<T>(scoped ref T[]? value)
        {
            if (!TryReadLeapCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            if (length == 0)
            {
                value = Array.Empty<T>();
                return;
            }

            var byteCount = length * Unsafe.SizeOf<T>();
            ref var src = ref GetSpanReference(byteCount);

            if (value == null || value.Length != length)
            {
                value = AllocateUninitializedArray<T>(length);
            }

            ref var dest = ref Unsafe.As<T, byte>(ref GetArrayDataReference(value));
            Unsafe.CopyBlockUnaligned(ref dest, ref src, (uint) byteCount);

            Advance(byteCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DangerousReadLeapUnmanagedArray<T>(scoped ref T[]? value, int length)
        {
            if (length == 0)
            {
                value = Array.Empty<T>();
                return;
            }

            var byteCount = length * Unsafe.SizeOf<T>();
            ref var src = ref GetSpanReference(byteCount);

            if (value == null || value.Length != length)
            {
                value = AllocateUninitializedArray<T>(length);
            }

            ref var dest = ref Unsafe.As<T, byte>(ref GetArrayDataReference(value));
            Unsafe.CopyBlockUnaligned(ref dest, ref src, (uint)byteCount);

            Advance(byteCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReadLeapCollectionHeader(out int length)
        {
            length = Read7BitEncodedInt();
            //length = Unsafe.ReadUnaligned<int>(ref GetSpanReference(4));
            //Advance(4); // TODO Advance is happening in Read7BitEncodedInt() in ReadUnmanaged<byte>()-Calls

            // If collection-length is larger than buffer-length, it is invalid data.
            if (Remaining < length)
            {
                Log.Information("test1");
                MemoryPackSerializationException.ThrowInsufficientBufferUnless(length);
                Log.Information("test2");
            }

            return length != MemoryPackCode.NullCollection;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Read7BitEncodedInt()
        {
            /*
             * Copied from Microsofts BinaryReader Source-Code
             */

            // Unlike writing, we can't delegate to the 64-bit read on
            // 64-bit platforms. The reason for this is that we want to
            // stop consuming bytes if we encounter an integer overflow.

            uint result = 0;
            byte byteReadJustNow;

            // Read the integer 7 bits at a time. The high bit
            // of the byte when on means to continue reading more bytes.
            //
            // There are two failure cases: we've read more than 5 bytes,
            // or the fifth byte is about to cause integer overflow.
            // This means that we can read the first 4 bytes without
            // worrying about integer overflow.

            const int maxBytesWithoutOverflow = 4;
            for (var shift = 0; shift < maxBytesWithoutOverflow * 7; shift += 7)
            {
                // ReadByte handles end of stream cases for us.
                byteReadJustNow = ReadUnmanaged<byte>();
                result |= (byteReadJustNow & 0x7Fu) << shift;

                if (byteReadJustNow <= 0x7Fu)
                {
                    return (int) result; // early exit
                }
            }

            // Read the 5th byte. Since we already read 28 bits,
            // the value of this byte must fit within 4 bits (32 - 28),
            // and it must not have the high bit set.

            byteReadJustNow = ReadUnmanaged<byte>();
            if (byteReadJustNow > 0b_1111u)
            {
                throw new FormatException("Format_Bad7BitInt");
            }

            result |= (uint) byteReadJustNow << maxBytesWithoutOverflow * 7;
            return (int) result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReadLeapStringArray(scoped ref string?[]? value)
        {
            if (!TryReadLeapCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            if (length == 0)
            {
                value = Array.Empty<string>();
                return;
            }

            // T[] support overwrite
            if (value == null || value.Length != length)
            {
                value = new string[length];
            }

            for (int i = 0; i < value.Length; i++)
            {
                value[i] = ReadLeapString();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public unsafe string ReadLeapString()
        {
            var stringLength = Read7BitEncodedInt();
            if (stringLength != 0)
                return Encoding.UTF8.GetString(DangerousReadUnmanagedArray<byte>(stringLength));
            else
                return string.Empty;
        }
    }
}
