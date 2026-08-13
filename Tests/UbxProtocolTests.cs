using System.Buffers.Binary;
using LnisAfsValidator.Core;
using LnisAfsValidator.Infrastructure;

namespace LnisAfsValidator.Tests;

/// <summary>UBX checksum 검사, 스트림 파싱과 GNSS 모델 변환을 검증한다.</summary>
public sealed class UbxProtocolTests
{
    [Fact]
    public void ParserHandlesNoiseByteChunksAndChecksumRecovery()
    {
        var good = Frame(0x02, 0x13, SfrbxPayload()); var bad = good.ToArray(); bad[^1] ^= 1; var parser = new UbxFrameParser(); var output = new List<UbxFrame>();
        var input = new byte[] { 1, 2, 3 }.Concat(bad).Concat(good).ToArray(); foreach (var b in input) output.AddRange(parser.Feed([b]));
        Assert.Single(output); Assert.Equal(1, parser.ChecksumErrors); Assert.Equal(0x13, output[0].MessageId);
    }

    [Fact]
    public void MapsGpsAndGalileoAndFiltersOtherConstellations()
    {
        var mapper = new UbxGnssMapper(Guid.NewGuid(), "ZED-F9P", "unknown", "COM3", 115200, "test");
        var rawx = mapper.Map(new(0x02, 0x15, RawxPayload()), DateTimeOffset.UnixEpoch);
        var observations = Assert.IsType<ObservationEpochMessage>(rawx!.Message); Assert.Equal(2, observations.Observations.Count); Assert.Equal(GnssConstellation.Gps, observations.Observations[0].Constellation); Assert.Equal(GnssConstellation.Galileo, observations.Observations[1].Constellation); Assert.Equal(1, mapper.UnsupportedConstellations);
        var nav = mapper.Map(new(0x02, 0x13, SfrbxPayload()), DateTimeOffset.UnixEpoch); Assert.Equal([0x01020304u, 0xAABBCCDDu], Assert.IsType<NavigationUpdateMessage>(nav!.Message).Words);
    }

    [Fact]
    public async Task ReplayWritesRawCanonicalAndManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "lnis-ubx-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source.ubx"); var bytes = Frame(0x02, 0x15, RawxPayload()).Concat(Frame(0x02, 0x13, SfrbxPayload())).ToArray(); await File.WriteAllBytesAsync(source, bytes);
            var result = await new GnssCaptureService().ReplayFileAsync(source, "replay", root, null, default); Assert.True(result.Completed); Assert.Equal(2, result.Statistics.ValidFrames); Assert.Equal(3, result.Statistics.EnvelopesWritten);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(result.RawUbxPath)); var envelopes = new List<GnssRawEnvelope>(); await foreach (var item in GnssCanonicalFile.ReadAsync(result.CanonicalPath)) envelopes.Add(item); Assert.Equal(3, envelopes.Count); Assert.True(File.Exists(result.ManifestPath));
        }
        finally { Directory.Delete(root, true); }
    }

    private static byte[] RawxPayload()
    {
        var p = new byte[16 + 3 * 32]; BinaryPrimitives.WriteInt64LittleEndian(p, BitConverter.DoubleToInt64Bits(100.5)); BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(8), 2300); p[10] = 18; p[11] = 3; p[12] = 3; p[13] = 1;
        Measurement(p.AsSpan(16, 32), 0, 7, 20_000_000); Measurement(p.AsSpan(48, 32), 2, 12, 21_000_000); Measurement(p.AsSpan(80, 32), 6, 3, 22_000_000); return p;
    }
    private static void Measurement(Span<byte> p, byte gnss, byte sv, double pseudorange) { BinaryPrimitives.WriteInt64LittleEndian(p, BitConverter.DoubleToInt64Bits(pseudorange)); BinaryPrimitives.WriteInt64LittleEndian(p[8..], BitConverter.DoubleToInt64Bits(100)); BinaryPrimitives.WriteInt32LittleEndian(p[16..], BitConverter.SingleToInt32Bits(-10)); p[20] = gnss; p[21] = sv; p[22] = 1; BinaryPrimitives.WriteUInt16LittleEndian(p[24..], 500); p[26] = 42; p[27] = 1; p[28] = 2; p[29] = 3; p[30] = 4; }
    private static byte[] SfrbxPayload() { var p = new byte[16]; p[0] = 2; p[1] = 4; p[2] = 1; p[4] = 2; p[6] = 2; BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(8), 0x01020304); BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(12), 0xAABBCCDD); return p; }
    private static byte[] Frame(byte cls, byte id, byte[] payload) { var f = new byte[payload.Length + 8]; f[0] = 0xB5; f[1] = 0x62; f[2] = cls; f[3] = id; BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(4), (ushort)payload.Length); payload.CopyTo(f, 6); byte a = 0, b = 0; for (var i = 2; i < f.Length - 2; i++) { a += f[i]; b += a; } f[^2] = a; f[^1] = b; return f; }
}
