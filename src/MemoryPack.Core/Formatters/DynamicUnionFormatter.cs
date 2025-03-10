﻿namespace MemoryPack.Formatters;

public sealed class DynamicUnionFormatter<T> : MemoryPackFormatter<T>
    where T : class
{
    readonly Dictionary<Type, byte> typeToTag;
    readonly Dictionary<byte, Type> tagToType;

    public DynamicUnionFormatter(params (byte Tag, Type Type)[] memoryPackUnions)
    {
        typeToTag = memoryPackUnions.ToDictionary(x => x.Type, x => x.Tag);
        tagToType = memoryPackUnions.ToDictionary(x => x.Tag, x => x.Type);
    }

    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref T? value)
    {
        if (value == null)
        {
            writer.WriteNullUnionHeader();
            return;
        }

        var type = value.GetType();
        if (typeToTag.TryGetValue(type, out var tag))
        {
            writer.WriteUnionHeader(tag);
            writer.WriteValue(type, value);
        }
        else
        {
            MemoryPackSerializationException.ThrowNotFoundInUnionType(type, typeof(T));
        }
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref T? value)
    {
        if (!reader.TryReadUnionHeader(out var tag))
        {
            value = default;
            return;
        }
        
        if (tagToType.TryGetValue(tag, out var type))
        {
            object? v = value;
            reader.ReadValue(type, ref v);
            value = (T?)v;
        }
        else
        {
            MemoryPackSerializationException.ThrowInvalidTag(tag, typeof(T));
        }
    }
}
