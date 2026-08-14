using System.Buffers.Binary;
using LnisAfsValidator.Core;
using LnisAfsValidator.Infrastructure;

namespace LnisAfsValidator.Tests;

/// <summary>프로토콜 독립 COM 수집의 원본 보존, 스트림 분할 처리와 capture.graw 생성을 검증한다.</summary>
public sealed class GnssComCaptureTests
{
    [Fact]
    public async Task CanonicalAdapterCreatesSenderCompatibleCaptureAcrossChunks()
    {
        var directory = TempDirectory();
        var inputCodec = new GnssRawBinaryCodec();
        var observation = new ObservationEpochMessage(345600.25, 2300, 18, 1, 1,
            [new(GnssConstellation.Gps, 8, 0, 0, 21_000_000, 120.5, -1250, 500, 42, 1, 1, 1, 1)]);
        var navigation = new NavigationUpdateMessage(GnssConstellation.Gps, 8, 0, 0, 1, [0x12345678, 0x9ABCDEF0]);
        var serialBytes = StreamBytes(inputCodec, observation, navigation);
        var service = new GnssComCaptureService(new MemorySourceFactory(serialBytes, 7), new GnssProtocolAdapterCatalog());
        var settings = new GnssSerialCaptureSettings("COM-TEST", 115200, "lnis-canonical-v1", directory, "test", "simulator", "1.0");

        var result = await service.CaptureAsync(settings, null, default);

        Assert.True(result.Completed);
        Assert.Null(result.Error);
        Assert.Equal(serialBytes, await File.ReadAllBytesAsync(result.RawSerialPath));
        Assert.True(File.Exists(result.CanonicalPath));
        var envelopes = await ReadCaptureAsync(result.CanonicalPath, inputCodec);
        Assert.Collection(envelopes,
            x => Assert.IsType<ReceiverMetadataMessage>(x.Message),
            x =>
            {
                var actual = Assert.IsType<ObservationEpochMessage>(x.Message);
                Assert.Equal(observation.ReceiverTowSeconds, actual.ReceiverTowSeconds);
                Assert.Equal(observation.Week, actual.Week);
                Assert.Equal(observation.Observations, actual.Observations);
            },
            x =>
            {
                var actual = Assert.IsType<NavigationUpdateMessage>(x.Message);
                Assert.Equal(navigation.SatelliteId, actual.SatelliteId);
                Assert.Equal(navigation.Words, actual.Words);
            });
        Assert.Equal(3, result.Statistics.EnvelopesWritten);
        Assert.Equal(1, result.Statistics.ObservationEpochs);
        Assert.Equal(1, result.Statistics.NavigationUpdates);
    }

    [Fact]
    public async Task RawOnlyAdapterPreservesUnknownProtocolWithoutCreatingCanonicalFile()
    {
        var directory = TempDirectory();
        var serialBytes = Enumerable.Range(0, 4097).Select(x => (byte)(x * 31)).ToArray();
        var service = new GnssComCaptureService(new MemorySourceFactory(serialBytes, 113), new GnssProtocolAdapterCatalog());
        var settings = new GnssSerialCaptureSettings("COM-UNKNOWN", 460800, "raw-only", directory, "raw", "unknown", "unknown");

        var result = await service.CaptureAsync(settings, null, default);

        Assert.True(result.Completed);
        Assert.Null(result.Error);
        Assert.Equal(string.Empty, result.CanonicalPath);
        Assert.Equal(serialBytes, await File.ReadAllBytesAsync(result.RawSerialPath));
        Assert.Equal(serialBytes.Length, result.Statistics.BytesRead);
        Assert.Equal(0, result.Statistics.EnvelopesWritten);
    }

    private static byte[] StreamBytes(GnssRawBinaryCodec codec, params GnssRawMessage[] messages)
    {
        using var output = new MemoryStream();
        ulong sequence = 0;
        var length = new byte[4];
        foreach (var message in messages)
        {
            var record = codec.Encode(new GnssRawEnvelope(1, Guid.NewGuid(), Guid.NewGuid(), sequence++, DateTimeOffset.UtcNow, message));
            BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)record.Length));
            output.Write(length); output.Write(record);
        }
        return output.ToArray();
    }

    private static async Task<IReadOnlyList<GnssRawEnvelope>> ReadCaptureAsync(string path, GnssRawBinaryCodec codec)
    {
        var data = await File.ReadAllBytesAsync(path); var result = new List<GnssRawEnvelope>(); var offset = 0;
        while (offset < data.Length)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4))); offset += 4;
            result.Add(codec.Decode(data.AsSpan(offset, length))); offset += length;
        }
        return result;
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "LnisGnssCapture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path;
    }

    private sealed class MemorySourceFactory(byte[] data, int chunkSize) : IGnssByteSourceFactory
    {
        public ValueTask<IGnssByteSource> OpenAsync(GnssSerialCaptureSettings settings, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IGnssByteSource>(new MemorySource(data, chunkSize));
    }

    private sealed class MemorySource(byte[] data, int chunkSize) : IGnssByteSource
    {
        private int offset;
        public string Description => "memory-test-source";
        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (offset >= data.Length) return ValueTask.FromResult(0);
            var count = Math.Min(Math.Min(buffer.Length, chunkSize), data.Length - offset);
            data.AsSpan(offset, count).CopyTo(buffer.Span); offset += count; return ValueTask.FromResult(count);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
