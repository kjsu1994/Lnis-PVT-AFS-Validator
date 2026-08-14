using System.Buffers.Binary;
using LnisAfsValidator.Core;

namespace LnisAfsValidator.Infrastructure;

/// <summary>프로토콜이 정해지기 전 COM 원본 바이트만 보존하는 어댑터다.</summary>
public sealed class RawRecordingProtocolAdapter : IGnssDeviceProtocolAdapter
{
    public GnssProtocolDescriptor Descriptor { get; } = new(
        "raw-only",
        "원본 바이트 저장(프로토콜 미정)",
        "COM 바이트를 serial-input.bin에 보존하며 capture.graw는 만들지 않습니다.",
        false);

    public IReadOnlyList<GnssRawMessage> Push(ReadOnlySpan<byte> bytes) => [];
    public void Reset() { }
}

/// <summary>4바이트 길이와 LGRW 레코드가 반복되는 시험·외부 연동 스트림을 해석한다.</summary>
public sealed class CanonicalGnssStreamProtocolAdapter : IGnssDeviceProtocolAdapter
{
    private const int MaximumRecordLength = 1_048_642;
    private readonly GnssRawBinaryCodec codec = new();
    private byte[] pending = [];

    public GnssProtocolDescriptor Descriptor { get; } = new(
        "lnis-canonical-v1",
        "LNIS Canonical v1(시험/외부 연동)",
        "Big-endian 4바이트 길이 뒤에 LGRW v1 레코드가 오는 스트림입니다.",
        true);

    public IReadOnlyList<GnssRawMessage> Push(ReadOnlySpan<byte> bytes)
    {
        if (!bytes.IsEmpty)
        {
            var combined = new byte[pending.Length + bytes.Length];
            pending.CopyTo(combined, 0);
            bytes.CopyTo(combined.AsSpan(pending.Length));
            pending = combined;
        }

        var messages = new List<GnssRawMessage>();
        var offset = 0;
        while (pending.Length - offset >= 4)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(pending.AsSpan(offset, 4)));
            if (length <= 0 || length > MaximumRecordLength) throw new InvalidDataException($"Invalid canonical GNSS record length {length}.");
            if (pending.Length - offset - 4 < length) break;
            messages.Add(codec.Decode(pending.AsSpan(offset + 4, length)).Message);
            offset += 4 + length;
        }

        if (offset > 0) pending = pending.AsSpan(offset).ToArray();
        return messages;
    }

    public void Reset() => pending = [];
}

/// <summary>프로토콜 구현체를 중앙 등록하여 UI와 캡처 서비스의 의존성을 분리한다.</summary>
public sealed class GnssProtocolAdapterCatalog : IGnssProtocolAdapterCatalog
{
    private readonly IReadOnlyDictionary<string, Func<IGnssDeviceProtocolAdapter>> factories;

    public GnssProtocolAdapterCatalog(IEnumerable<Func<IGnssDeviceProtocolAdapter>>? registrations = null)
    {
        var items = registrations?.ToArray() ??
        [
            static () => new RawRecordingProtocolAdapter(),
            static () => new CanonicalGnssStreamProtocolAdapter()
        ];
        factories = items.ToDictionary(x => x().Descriptor.Id, StringComparer.OrdinalIgnoreCase);
        Protocols = factories.Values.Select(x => x().Descriptor).OrderBy(x => x.DisplayName).ToArray();
    }

    public IReadOnlyList<GnssProtocolDescriptor> Protocols { get; }

    public IGnssDeviceProtocolAdapter Create(string protocolId) =>
        factories.TryGetValue(protocolId, out var factory)
            ? factory()
            : throw new NotSupportedException($"GNSS protocol adapter '{protocolId}' is not registered.");
}
