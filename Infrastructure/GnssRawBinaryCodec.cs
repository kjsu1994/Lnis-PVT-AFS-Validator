using System.Buffers.Binary;
using System.Text;
using LnisAfsValidator.Core;

namespace LnisAfsValidator.Infrastructure;

public sealed class GnssRawBinaryCodec : IGnssRawCodec
{
    private static readonly byte[] Magic = "LGRW"u8.ToArray();
    private const ushort CurrentVersion = 1;
    private const ushort HeaderLength = 62;
    private const int MaximumPayloadLength = 1024 * 1024;

    public byte[] Encode(GnssRawEnvelope envelope)
    {
        if (envelope.SchemaVersion != CurrentVersion) throw new InvalidDataException($"Unsupported schema version {envelope.SchemaVersion}.");
        var payload = EncodeMessage(envelope.Message);
        if (payload.Length > MaximumPayloadLength) throw new InvalidDataException("GNSS RAW payload is too large.");
        var output = new byte[HeaderLength + payload.Length + 4];
        var span = output.AsSpan(); Magic.CopyTo(span); BinaryPrimitives.WriteUInt16BigEndian(span[4..], CurrentVersion);
        span[6] = (byte)envelope.Message.Type; span[7] = 0; BinaryPrimitives.WriteUInt16BigEndian(span[8..], HeaderLength);
        BinaryPrimitives.WriteUInt32BigEndian(span[10..], (uint)payload.Length);
        WriteGuid(span[14..30], envelope.TestId); WriteGuid(span[30..46], envelope.MessageId);
        BinaryPrimitives.WriteUInt64BigEndian(span[46..], envelope.SequenceNumber);
        BinaryPrimitives.WriteInt64BigEndian(span[54..], (envelope.CapturedAtUtc.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / 10);
        payload.CopyTo(span[HeaderLength..]);
        BinaryPrimitives.WriteUInt32BigEndian(span[(HeaderLength + payload.Length)..], Hashing.Crc32(span[..(HeaderLength + payload.Length)]));
        return output;
    }

    public GnssRawEnvelope Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderLength + 4 || !data[..4].SequenceEqual(Magic)) throw new InvalidDataException("Invalid GNSS RAW magic.");
        var version = BinaryPrimitives.ReadUInt16BigEndian(data[4..]); if (version != CurrentVersion) throw new InvalidDataException($"Unsupported schema version {version}.");
        if (data[7] != 0 || BinaryPrimitives.ReadUInt16BigEndian(data[8..]) != HeaderLength) throw new InvalidDataException("Unsupported GNSS RAW header.");
        var payloadLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[10..]));
        if (payloadLength > MaximumPayloadLength || data.Length != HeaderLength + payloadLength + 4) throw new InvalidDataException("Invalid GNSS RAW payload length.");
        var expected = BinaryPrimitives.ReadUInt32BigEndian(data[(HeaderLength + payloadLength)..]);
        if (Hashing.Crc32(data[..(HeaderLength + payloadLength)]) != expected) throw new InvalidDataException("GNSS RAW CRC32 mismatch.");
        var type = data[6] switch { 1 => GnssRawMessageType.ObservationEpoch, 2 => GnssRawMessageType.NavigationUpdate, 3 => GnssRawMessageType.ReceiverMetadata, _ => throw new InvalidDataException("Unknown GNSS RAW message type.") };
        var captured = DateTimeOffset.UnixEpoch.AddTicks(checked(BinaryPrimitives.ReadInt64BigEndian(data[54..]) * 10));
        var message = DecodeMessage(type, data.Slice(HeaderLength, payloadLength));
        return new(version, ReadGuid(data[14..30]), ReadGuid(data[30..46]), BinaryPrimitives.ReadUInt64BigEndian(data[46..]), captured, message);
    }

    private static byte[] EncodeMessage(GnssRawMessage message)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        switch (message)
        {
            case ObservationEpochMessage x:
                Big(writer, x.ReceiverTowSeconds); Big(writer, x.Week); writer.Write(unchecked((byte)x.LeapSeconds)); writer.Write(x.ReceiverStatus); writer.Write(x.RawxVersion); Big(writer, checked((ushort)x.Observations.Count));
                foreach (var o in x.Observations) { writer.Write((byte)o.Constellation); writer.Write(o.SatelliteId); writer.Write(o.SignalId); writer.Write(o.FrequencyId); Big(writer, o.PseudorangeMeters); Big(writer, o.CarrierPhaseCycles); Big(writer, o.DopplerHz); Big(writer, o.LockTimeMilliseconds); writer.Write(o.CarrierToNoiseDbHz); writer.Write(o.PseudorangeStdDev); writer.Write(o.CarrierPhaseStdDev); writer.Write(o.DopplerStdDev); writer.Write(o.TrackingStatus); }
                break;
            case NavigationUpdateMessage x:
                writer.Write((byte)x.Constellation); writer.Write(x.SatelliteId); writer.Write(x.SignalId); writer.Write(x.FrequencyId); writer.Write(x.SfrbxVersion); Big(writer, checked((ushort)x.Words.Count)); foreach (var word in x.Words) Big(writer, word);
                break;
            case ReceiverMetadataMessage x:
                Text(writer, x.ReceiverModel); Text(writer, x.FirmwareVersion); Text(writer, x.PortName); Big(writer, x.BaudRate); Text(writer, x.SessionName);
                break;
            default: throw new InvalidDataException("Unknown GNSS RAW message payload.");
        }
        return stream.ToArray();
    }

    private static GnssRawMessage DecodeMessage(GnssRawMessageType type, ReadOnlySpan<byte> payload)
    {
        using var stream = new MemoryStream(payload.ToArray()); using var reader = new BinaryReader(stream, Encoding.UTF8);
        GnssRawMessage result = type switch
        {
            GnssRawMessageType.ObservationEpoch => ReadObservation(reader),
            GnssRawMessageType.NavigationUpdate => ReadNavigation(reader),
            GnssRawMessageType.ReceiverMetadata => new ReceiverMetadataMessage(Text(reader), Text(reader), Text(reader), I32(reader), Text(reader)),
            _ => throw new InvalidDataException("Unknown GNSS RAW payload.")
        };
        if (stream.Position != stream.Length) throw new InvalidDataException("GNSS RAW payload contains trailing data.");
        return result;
    }

    private static ObservationEpochMessage ReadObservation(BinaryReader r)
    {
        var tow = F64(r); var week = U16(r); var leap = unchecked((sbyte)r.ReadByte()); var status = r.ReadByte(); var version = r.ReadByte(); var count = U16(r); var observations = new List<GnssObservation>(count);
        for (var i = 0; i < count; i++) observations.Add(new((GnssConstellation)r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte(), F64(r), F64(r), F32(r), U16(r), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte()));
        return new(tow, week, leap, status, version, observations);
    }
    private static NavigationUpdateMessage ReadNavigation(BinaryReader r) { var c = (GnssConstellation)r.ReadByte(); var sv = r.ReadByte(); var sig = r.ReadByte(); var freq = r.ReadByte(); var version = r.ReadByte(); var count = U16(r); var words = new uint[count]; for (var i = 0; i < count; i++) words[i] = U32(r); return new(c, sv, sig, freq, version, words); }
    private static void WriteGuid(Span<byte> target, Guid value) { if (!value.TryWriteBytes(target, true, out var written) || written != 16) throw new InvalidOperationException("Unable to encode UUID."); }
    private static Guid ReadGuid(ReadOnlySpan<byte> source) => new(source, true);
    private static void Text(BinaryWriter w, string value) { var b = Encoding.UTF8.GetBytes(value); Big(w, checked((ushort)b.Length)); w.Write(b); }
    private static string Text(BinaryReader r) { var n = U16(r); var b = r.ReadBytes(n); if (b.Length != n) throw new EndOfStreamException(); return Encoding.UTF8.GetString(b); }
    private static void Big(BinaryWriter w, ushort v) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, v); w.Write(b); }
    private static void Big(BinaryWriter w, uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); w.Write(b); }
    private static void Big(BinaryWriter w, int v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(b, v); w.Write(b); }
    private static void Big(BinaryWriter w, float v) => Big(w, BitConverter.SingleToUInt32Bits(v));
    private static void Big(BinaryWriter w, double v) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteUInt64BigEndian(b, BitConverter.DoubleToUInt64Bits(v)); w.Write(b); }
    private static ushort U16(BinaryReader r) { Span<byte> b = stackalloc byte[2]; Read(r, b); return BinaryPrimitives.ReadUInt16BigEndian(b); }
    private static uint U32(BinaryReader r) { Span<byte> b = stackalloc byte[4]; Read(r, b); return BinaryPrimitives.ReadUInt32BigEndian(b); }
    private static int I32(BinaryReader r) { Span<byte> b = stackalloc byte[4]; Read(r, b); return BinaryPrimitives.ReadInt32BigEndian(b); }
    private static float F32(BinaryReader r) => BitConverter.UInt32BitsToSingle(U32(r));
    private static double F64(BinaryReader r) { Span<byte> b = stackalloc byte[8]; Read(r, b); return BitConverter.UInt64BitsToDouble(BinaryPrimitives.ReadUInt64BigEndian(b)); }
    private static void Read(BinaryReader r, Span<byte> b) { if (r.Read(b) != b.Length) throw new EndOfStreamException(); }
}
