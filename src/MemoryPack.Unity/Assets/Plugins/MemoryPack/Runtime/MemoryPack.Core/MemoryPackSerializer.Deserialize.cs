using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

#nullable enable
using MemoryPack.Internal;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Serilog;

namespace MemoryPack {

public static partial class MemoryPackSerializer
{
    [ThreadStatic]
    static MemoryPackReaderOptionalState? threadStaticReaderOptionalState;

    public static T? Deserialize<T>(ReadOnlySpan<byte> buffer, MemoryPackSerializerOptions? options = default)
    {
        T? value = default;
        Deserialize(buffer, ref value, options);
        return value;
    }

    public static T? DeserializeWithFormatter<TFormatter, T>(ReadOnlySpan<byte> buffer, TFormatter formatter, MemoryPackSerializerOptions? options = default)
        where TFormatter : IMemoryPackFormatter<T>
    {
        T? value = default;
        DeserializeWithFormatter(buffer, ref value, ref formatter, options);
        return value;
    }

    public static int Deserialize<T>(ReadOnlySpan<byte> buffer, ref T? value, MemoryPackSerializerOptions? options = default)
    {
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            if (buffer.Length < Unsafe.SizeOf<T>())
            {
                MemoryPackSerializationException.ThrowInvalidRange(Unsafe.SizeOf<T>(), buffer.Length);
            }
            value = Unsafe.ReadUnaligned<T>(ref MemoryMarshal.GetReference(buffer));
            return Unsafe.SizeOf<T>();
        }

        var state = threadStaticReaderOptionalState;
        if (state == null)
        {
            state = threadStaticReaderOptionalState = new MemoryPackReaderOptionalState();
        }
        state.Init(options);

        var reader = new MemoryPackReader(buffer, state);
        try
        {
            reader.ReadValue(ref value);
            return reader.Consumed;
        }
        finally
        {
            reader.Dispose();
            state.Reset();
        }
    }

    public static int DeserializeWithFormatter<TFormatter, T>(ReadOnlySpan<byte> buffer, ref T? value, ref TFormatter formatter, MemoryPackSerializerOptions? options = default)
        where TFormatter : IMemoryPackFormatter<T>
    {
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            if (buffer.Length < Unsafe.SizeOf<T>())
            {
                MemoryPackSerializationException.ThrowInvalidRange(Unsafe.SizeOf<T>(), buffer.Length);
            }
            value = Unsafe.ReadUnaligned<T>(ref MemoryMarshal.GetReference(buffer));
            return Unsafe.SizeOf<T>();
        }

        var state = threadStaticReaderOptionalState;
        if (state == null)
        {
            state = threadStaticReaderOptionalState = new MemoryPackReaderOptionalState();
        }
        state.Init(options);

        var reader = new MemoryPackReader(buffer, state);
        try
        {
            value = reader.ReadValueWithFormatter<TFormatter, T>(formatter);
            return reader.Consumed;
        }
        finally
        {
            reader.Dispose();
            state.Reset();
        }
    }

    public static T? Deserialize<T>(in ReadOnlySequence<byte> buffer, MemoryPackSerializerOptions? options = default)
    {
        T? value = default;
        Deserialize<T>(buffer, ref value);
        return value;
    }

    public static TType? DeserializeWithFormatter<TFormatter, TType>(TFormatter formatter, in ReadOnlySequence<byte> buffer, MemoryPackSerializerOptions? options = default)
        where TFormatter : MemoryPackFormatter<TType>
    {
        TType? value = default;
        DeserializeWithFormatter<TFormatter,TType>(formatter, buffer, ref value);
        return value;
    }

    public static int Deserialize<T>(in ReadOnlySequence<byte> buffer, ref T? value, MemoryPackSerializerOptions? options = default)
    {
        var state = threadStaticReaderOptionalState;
        if (state == null)
        {
            state = threadStaticReaderOptionalState = new MemoryPackReaderOptionalState();
        }
        state.Init(options);

        var reader = new MemoryPackReader(buffer, state);
        try
        {
            reader.ReadValue(ref value);
            return reader.Consumed;
        }
        finally
        {
            reader.Dispose();
            state.Reset();
        }
    }

    public static int DeserializeWithFormatter<TFormatter, TType>(TFormatter formatter, in ReadOnlySequence<byte> buffer, ref TType? value, MemoryPackSerializerOptions? options = default)
        where TFormatter : MemoryPackFormatter<TType>
    {
        var state = threadStaticReaderOptionalState;
        if (state == null)
        {
            state = threadStaticReaderOptionalState = new MemoryPackReaderOptionalState();
        }
        state.Init(options);

        var reader = new MemoryPackReader(buffer, state);
        try
        {
            formatter.Deserialize(ref reader, ref value);
            return reader.Consumed;
        }
        finally
        {
            reader.Dispose();
            state.Reset();
        }
    }

    public static async ValueTask<T?> DeserializeAsync<T>(Stream stream, MemoryPackSerializerOptions? options = default, CancellationToken cancellationToken = default)
    {
        if (stream is MemoryStream ms && ms.TryGetBuffer(out var streamBuffer))
        {
            cancellationToken.ThrowIfCancellationRequested();
            T? value = default;
            var bytesRead = Deserialize<T>(streamBuffer.AsSpan(checked((int)ms.Position)), ref value, options);

            // Emulate that we had actually "read" from the stream.
            ms.Seek(bytesRead, SeekOrigin.Current);

            return value;
        }

        var builder = ReusableReadOnlySequenceBuilderPool.Rent();
        try
        {
            var buffer = ArrayPool<byte>.Shared.Rent(65536); // initial 64K
            var offset = 0;
            do
            {
                if (offset == buffer.Length)
                {
                    builder.Add(buffer, returnToPool: true);
                    buffer = ArrayPool<byte>.Shared.Rent(MathEx.NewArrayCapacity(buffer.Length));
                    offset = 0;
                }

                var read = 0;
                try
                {
                    read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // buffer is not added in builder, so return here.
                    ArrayPool<byte>.Shared.Return(buffer);
                    throw;
                }

                offset += read;

                if (read == 0)
                {
                    builder.Add(buffer.AsMemory(0, offset), returnToPool: true);
                    break;

                }
            } while (true);

            // If single buffer, we can avoid ReadOnlySequence build cost.
            if (builder.TryGetSingleMemory(out var memory))
            {
                return Deserialize<T>(memory.Span, options);
            }
            else
            {
                var seq = builder.Build();
                var result = Deserialize<T>(seq, options);
                return result;
            }
        }
        finally
        {
            builder.Reset();
        }
    }

    public static async ValueTask<TType?> DeserializeWithFormatterAsync<TFormatter, TType>(TFormatter formatter, Stream stream, MemoryPackSerializerOptions? options = default, CancellationToken cancellationToken = default)
    where TFormatter : MemoryPackFormatter<TType>
    {
        //var builder = ReusableReadOnlySequenceBuilderPool.Rent();
        var buffer = Array.Empty<byte>();
        try
        {
            const int sizeHeaderSize = sizeof(int);

            var lengthBuf = new byte[sizeHeaderSize];
            var read = await stream.ReadAsync(lengthBuf, 0, sizeHeaderSize, cancellationToken);
            while (read < sizeHeaderSize)
            {
                read += await stream.ReadAsync(lengthBuf, read, sizeHeaderSize - read, cancellationToken);
                Log.Information("read {0}", read);
            }

            if (read == 0)
                throw new Exception("reached end of Buffer");

            if (read > sizeHeaderSize)
                throw new Exception("Have read more than sizeHeaderSize");

            int objSize;
            unsafe
            {
                fixed (byte* bp = lengthBuf)
                {
                    objSize = Unsafe.ReadUnaligned<int>(bp);
                }
            }

            Log.Information("objSize is {0}", objSize);

            // read exactly the size of the Obj
            buffer = ArrayPool<byte>.Shared.Rent(objSize);
            read = await stream.ReadAsync(buffer, 0, objSize, cancellationToken);
            while (read < objSize)
            {
                read += await stream.ReadAsync(buffer, read, objSize - read, cancellationToken);
            }

            if (read == 0)
                throw new Exception("reached end of Buffer");

            if (read > objSize)
                throw new Exception("Have read more than objSize");

            var result = DeserializeWithFormatter<TFormatter, TType>(buffer, formatter, options);
            return result;
            //builder.Add(buffer, returnToPool: true);

            //if (builder.TryGetSingleMemory(out var memory))
            //{
            //    return DeserializeWithFormatter<TFormatter, TType>(memory.Span, formatter, options);
            //}
            //else
            //{
            //    var seq = builder.Build();
            //    return DeserializeWithFormatter<TFormatter, TType>(formatter, seq, options);
            //}

            //var offset = 0;
            //do
            //{
            //    if (offset == buffer.Length)
            //    {
            //        builder.Add(buffer, returnToPool: true);
            //        buffer = ArrayPool<byte>.Shared.Rent(MathEx.NewArrayCapacity(buffer.Length));
            //        offset = 0;
            //    }

            //    int read = 0;
            //    try
            //    {
            //        read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken).ConfigureAwait(false);
            //    }
            //    catch
            //    {
            //        // buffer is not added in builder, so return here.
            //        ArrayPool<byte>.Shared.Return(buffer);
            //        throw;
            //    }

            //    offset += read;

            //    if (read == 0)
            //    {
            //        builder.Add(buffer.AsMemory(0, offset), returnToPool: true);
            //        break;

            //    }
            //} while (true);

            //// If single buffer, we can avoid ReadOnlySequence build cost.
            //if (builder.TryGetSingleMemory(out var memory))
            //{
            //    return DeserializeWithFormatter<TFormatter, TType>(memory.Span, formatter, options);
            //}
            //else
            //{
            //    var seq = builder.Build();
            //    var result = DeserializeWithFormatter<TFormatter, TType>(formatter, seq, options);
            //    return result;
            //}
        }
        finally
        {
            if(buffer != Array.Empty<byte>())
                ArrayPool<byte>.Shared.Return(buffer);
            // Reset builder, return Arrays etc.
            //builder.Reset();
        }


        //if (stream is MemoryStream ms && ms.TryGetBuffer(out ArraySegment<byte> streamBuffer))
        //{
        //    cancellationToken.ThrowIfCancellationRequested();
        //    TType? value = default;
        //    var bytesRead = DeserializeWithFormatter<TFormatter, TType>(streamBuffer, ref value, ref formatter, options);
        //    // Emulate that we had actually "read" from the stream.
        //    ms.Seek(bytesRead, SeekOrigin.Current);

        //    return value;
        //}

        //var builder = ReusableReadOnlySequenceBuilderPool.Rent();
        //try
        //{
        //    var buffer = ArrayPool<byte>.Shared.Rent(65536); // initial 64K
        //    var offset = 0;
        //    do
        //    {
        //        if (offset == buffer.Length)
        //        {
        //            builder.Add(buffer, returnToPool: true);
        //            buffer = ArrayPool<byte>.Shared.Rent(MathEx.NewArrayCapacity(buffer.Length));
        //            offset = 0;
        //        }

        //        int read = 0;
        //        try
        //        {
        //            read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken).ConfigureAwait(false);
        //        }
        //        catch
        //        {
        //            // buffer is not added in builder, so return here.
        //            ArrayPool<byte>.Shared.Return(buffer);
        //            throw;
        //        }

        //        offset += read;

        //        if (read == 0)
        //        {
        //            builder.Add(buffer.AsMemory(0, offset), returnToPool: true);
        //            break;

        //        }
        //    } while (true);

        //    // If single buffer, we can avoid ReadOnlySequence build cost.
        //    if (builder.TryGetSingleMemory(out var memory))
        //    {
        //        return DeserializeWithFormatter<TFormatter, TType>(memory.Span, formatter, options);
        //    }
        //    else
        //    {
        //        var seq = builder.Build();
        //        var result = DeserializeWithFormatter<TFormatter, TType>(formatter, seq, options);
        //        return result;
        //    }
        //}
        //finally
        //{
        //    builder.Reset();
        //}
    }

    public static TType? DeserializeWithFormatter<TFormatter, TType>(TFormatter formatter, Stream stream, MemoryPackSerializerOptions? options = default, CancellationToken cancellationToken = default)
        where TFormatter : MemoryPackFormatter<TType>
    {
        var buffer = Array.Empty<byte>();
        try
        {
            const int sizeHeaderSize = sizeof(int);

            var lengthBuf = new byte[sizeHeaderSize];
            var read = stream.Read(lengthBuf, 0, sizeHeaderSize);
            while (read < sizeHeaderSize)
            {
                read += stream.Read(lengthBuf, read, sizeHeaderSize - read);
            }

            if (read == 0)
                throw new Exception("reached end of Buffer");

            if (read > sizeHeaderSize)
                throw new Exception("Have read more than sizeHeaderSize");

            int objSize;
            unsafe
            {
                fixed (byte* bp = lengthBuf)
                {
                    objSize = Unsafe.ReadUnaligned<int>(bp);
                }
            }

            // read exactly the size of the Obj
            buffer = ArrayPool<byte>.Shared.Rent(objSize);
            read = stream.Read(buffer, 0, objSize);
            while (read < objSize)
            {
                read += stream.Read(buffer, read, objSize - read);
            }

            if (read == 0)
                throw new Exception("reached end of Buffer");

            if (read > objSize)
                throw new Exception("Have read more than objSize");

            return DeserializeWithFormatter<TFormatter, TType>(buffer, formatter, options);
        }
        finally
        {
            if (buffer != Array.Empty<byte>())
                ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

}