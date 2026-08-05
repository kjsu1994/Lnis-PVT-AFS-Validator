using LnisAfsValidator.Core;
using LnisAfsValidator.Infrastructure;

namespace LnisAfsValidator.Tests;

public sealed class GnssRawCodecTests
{
    [Fact]
    public void ObservationRoundTripIsDeterministic()
    {
        var envelope = new GnssRawEnvelope(1, Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"), Guid.Parse("10213243-5465-7687-98a9-bacbdcedfe0f"), 42, DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_123),
            new ObservationEpochMessage(345600.25, 2300, 18, 3, 1,
            [new(GnssConstellation.Gps, 7, 0, 0, 20_000_001.5, 123.25, -1200.5f, 900, 45, 2, 3, 4, 5), new(GnssConstellation.Galileo, 12, 1, 0, 21_000_002.5, 456.75, 300.25f, 1000, 40, 1, 2, 3, 4)]));
        var codec = new GnssRawBinaryCodec(); var first = codec.Encode(envelope); var decoded = codec.Decode(first); var second = codec.Encode(decoded);
        Assert.Equal(first, second); Assert.Equal(envelope with { Message = decoded.Message }, decoded);
        var actual = Assert.IsType<ObservationEpochMessage>(decoded.Message); var expected = Assert.IsType<ObservationEpochMessage>(envelope.Message); Assert.Equal(expected with { Observations = actual.Observations }, actual); Assert.Equal(expected.Observations, actual.Observations); Assert.Equal("4C475257", Convert.ToHexString(first[..4]));
    }

    [Fact]
    public void NavigationAndMetadataRoundTrip()
    {
        var codec = new GnssRawBinaryCodec(); var id = Guid.NewGuid();
        GnssRawEnvelope[] values =
        [
            new(1, id, Guid.NewGuid(), 1, DateTimeOffset.UnixEpoch, new NavigationUpdateMessage(GnssConstellation.Galileo, 4, 1, 0, 2, [0x01020304, 0xAABBCCDD])),
            new(1, id, Guid.NewGuid(), 2, DateTimeOffset.UnixEpoch, new ReceiverMetadataMessage("u-blox ZED-F9P", "unknown", "COM3", 115200, "test"))
        ];
        foreach (var value in values)
        {
            var bytes = codec.Encode(value); var decoded = codec.Decode(bytes); Assert.Equal(bytes, codec.Encode(decoded));
            Assert.Equal(value with { Message = decoded.Message }, decoded);
            if (value.Message is NavigationUpdateMessage expected) Assert.Equal(expected.Words, Assert.IsType<NavigationUpdateMessage>(decoded.Message).Words);
        }
    }

    [Fact]
    public void CorruptCrcIsRejected()
    {
        var codec = new GnssRawBinaryCodec(); var bytes = codec.Encode(new(1, Guid.NewGuid(), Guid.NewGuid(), 0, DateTimeOffset.UnixEpoch, new ReceiverMetadataMessage("x", "y", "z", 1, "s")));
        bytes[^1] ^= 0xFF; Assert.Throws<InvalidDataException>(() => codec.Decode(bytes));
    }
}

