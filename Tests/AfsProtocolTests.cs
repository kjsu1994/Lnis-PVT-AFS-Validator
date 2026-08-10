using LnisAfsValidator.Core;
using LnisAfsValidator.Infrastructure;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace LnisAfsValidator.Tests;

public sealed class AfsProtocolTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(86)]
    [InlineData(87)]
    [InlineData(1000)]
    public void RawFragmentsRoundTrip(int length)
    {
        var source = Enumerable.Range(0, length).Select(i => (byte)(i * 17)).ToArray();
        var blocks = AfsRawFragmentCodec.Fragment(7, source); var reassembler = new AfsRawReassembler();
        foreach (var block in blocks.Reverse())
        {
            var bits = AfsRawFragmentCodec.ToSbBits(block); var decodedBlock = AfsRawFragmentCodec.FromSbBits(bits);
            reassembler.Add(AfsRawFragmentCodec.DecodeBlock(decodedBlock));
        }
        var record = Assert.Single(reassembler.CompleteRecords()); Assert.Equal(7u, record.Sequence); Assert.Equal(source, record.Record);
    }

    [Fact]
    public void PacketRoundTripAndCrcRejection()
    {
        var packet = new AfsPacket(AfsPacketKind.Frame, Guid.NewGuid(), 42, 2, 8, 1200, 12, 34, DateTimeOffset.UtcNow.UtcTicks, Enumerable.Range(0, 750).Select(i => (byte)i).ToArray());
        var bytes = AfsPacketCodec.Encode(packet); var decoded = AfsPacketCodec.Decode(bytes);
        Assert.Equal(packet.Kind, decoded.Kind); Assert.Equal(packet.TestId, decoded.TestId); Assert.Equal(packet.Sequence, decoded.Sequence);
        Assert.Equal(packet.CopyIndex, decoded.CopyIndex); Assert.Equal(packet.Prn, decoded.Prn); Assert.Equal(packet.Week, decoded.Week);
        Assert.Equal(packet.IntervalOfWeek, decoded.IntervalOfWeek); Assert.Equal(packet.TimeOfInterval, decoded.TimeOfInterval);
        Assert.Equal(packet.SentUtcTicks, decoded.SentUtcTicks); Assert.Equal(packet.Payload, decoded.Payload);
        bytes[60] ^= 1; Assert.Throws<InvalidDataException>(() => AfsPacketCodec.Decode(bytes));
    }

    [Fact]
    public void PerformanceUsesLogicalFramesAndLeavesFutureMetricsNotApplicable()
    {
        var counters = new AfsNetworkCounters(100, 98, 300, 270, 172, 0, 60, 57, 17200, TimeSpan.FromSeconds(2), [10, 20, 30]);
        var metrics = AfsPerformanceCalculator.Calculate(counters);
        Assert.Equal(2, metrics.Single(x => x.Name == "PacketLossRate").Value);
        Assert.Equal(98, metrics.Single(x => x.Name == "PacketDeliveryRate").Value);
        Assert.Equal(20, metrics.Single(x => x.Name == "AverageLatency").Value);
        Assert.Equal(MetricStatus.NotApplicable, metrics.Single(x => x.Name == "PositionError").Status);
    }

    [Fact]
    public async Task NativeCodecRoundTripWhenDllIsAvailable()
    {
        try
        {
            await using var codec = new AfsNativeCodec(); var sb2 = Pattern(1176); var sb3 = Pattern(846, 1); var sb4 = Pattern(846, 2);
            var frame = await codec.EncodeAsync(12, sb2, sb3, sb4, default); var decoded = await codec.DecodeAsync(12, frame, default);
            Assert.Equal(750, frame.Length); Assert.True(decoded.Sb2Valid); Assert.True(decoded.Sb3Valid); Assert.True(decoded.Sb4Valid);
            Assert.Equal(sb2, decoded.Sb2Bits); Assert.Equal(sb3, decoded.Sb3Bits); Assert.Equal(sb4, decoded.Sb4Bits);
        }
        catch (DllNotFoundException) { return; }
        static byte[] Pattern(int length, int offset = 0) => Enumerable.Range(0, length).Select(i => (byte)((i + offset) & 1)).ToArray();
    }

    [Fact]
    public async Task LocalUdpSessionRestoresCanonicalRaw()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LnisAfsIntegration-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var capture = Path.Combine(directory, "capture.graw"); var rawCodec = new GnssRawBinaryCodec();
        var envelope = new GnssRawEnvelope(1, Guid.NewGuid(), Guid.NewGuid(), 0, DateTimeOffset.UtcNow,
            new ObservationEpochMessage(345600, 2300, 18, 1, 1, [new(GnssConstellation.Gps, 8, 0, 0, 123.5, 45.25, -2, 100, 40, 1, 1, 1, 1)]));
        var record = rawCodec.Encode(envelope); var size = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(size, checked((uint)record.Length));
        await using (var stream = File.Create(capture)) { await stream.WriteAsync(size); await stream.WriteAsync(record); }
        var project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var openSource = Directory.EnumerateDirectories(project).First(x => Directory.Exists(Path.Combine(x, "LANS-AFS-SIM-main")));
        var almanac = Path.Combine(openSource, "LANS-AFS-SIM-main", "default_almanac.txt"); var dataPort = FreeUdpPort(); var resultPort = FreeUdpPort(); while (resultPort == dataPort) resultPort = FreeUdpPort();
        var settings = new AfsTestSettings(capture, almanac, directory); var network = new AfsTransportSettings("127.0.0.1", dataPort, resultPort, ResultTimeoutSeconds: 10);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)); var receiver = new AfsUdpSessionService().ReceiveAsync(settings, network, null, timeout.Token);
        await Task.Delay(100, timeout.Token); var sender = await new AfsUdpSessionService().SendAsync(settings, network, null, timeout.Token); var received = await receiver;
        Assert.Equal(Verdict.Pass, sender.Verdict); Assert.True(received.Integrity.Success); Assert.Equal(await File.ReadAllBytesAsync(capture), await File.ReadAllBytesAsync(Path.Combine(received.ResultDirectory, "reconstructed.graw")));
    }

    private static int FreeUdpPort() { using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)); return ((IPEndPoint)socket.Client.LocalEndPoint!).Port; }
}
