using LnisAfsValidator.Core;
using LnisAfsValidator.Infrastructure;

namespace LnisAfsValidator.Tests;

public sealed class AfsErrorCorrectionExperimentServiceTests
{
    [Fact]
    public async Task WritesReferenceInjectedDecodedAndReportFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "LnisAfsFecTest-" + Guid.NewGuid().ToString("N"));
        var service = new AfsErrorCorrectionExperimentService(() => new FakeCodec());
        var result = await service.RunAsync(new(AfsErrorInjectionMode.Random, [2], 1, 7), root, null, default);

        var condition = Path.Combine(result.ResultDirectory, "frames", "Random-0002");
        Assert.Equal(750, new FileInfo(Path.Combine(condition, "trial-0001-reference.afs")).Length);
        Assert.Equal(750, new FileInfo(Path.Combine(condition, "trial-0001-injected.afs")).Length);
        Assert.True(File.Exists(Path.Combine(condition, "trial-0001-flipped-symbols.txt")));
        Assert.True(File.Exists(Path.Combine(condition, "trial-0001-decoded-sb2.bits")));
        Assert.True(File.Exists(Path.Combine(result.ResultDirectory, "fec-result.json")));
        Assert.True(File.Exists(Path.Combine(result.ResultDirectory, "fec-summary.csv")));
        Assert.True(File.Exists(Path.Combine(result.ResultDirectory, "fec-trials.csv")));
    }

    [Fact]
    public async Task SyncRecoveryFindsAndDecodesTheNextNormalFrame()
    {
        var root = Path.Combine(Path.GetTempPath(), "LnisAfsSyncTest-" + Guid.NewGuid().ToString("N"));
        var result = await new AfsSyncRecoveryExperimentService(() => new SyncFakeCodec())
            .RunAsync(new([20], 1, 3), root, null, default);
        var trial = Assert.Single(result.Trials);
        Assert.True(trial.DamagedFrameRejected); Assert.True(trial.NextSyncFound); Assert.True(trial.NextFrameDecoded);
        Assert.Equal(12, trial.RecoveryTimeSeconds);
        Assert.True(File.Exists(Path.Combine(result.ResultDirectory, "SyncLoss-20", "trial-0001-3frames.afsstream")));
        Assert.True(File.Exists(Path.Combine(result.ResultDirectory, "SyncLoss-20", "trial-0001-recovered.afs")));
    }

    private sealed class FakeCodec : IAfsFrameCodec
    {
        public Task<byte[]> EncodeAsync(int toi, ReadOnlyMemory<byte> sb2Bits, ReadOnlyMemory<byte> sb3Bits, ReadOnlyMemory<byte> sb4Bits, CancellationToken token) => Task.FromResult(new byte[750]);
        public Task<AfsDecodedFrame> DecodeAsync(int toi, ReadOnlyMemory<byte> frame, CancellationToken token) =>
            Task.FromResult(new AfsDecodedFrame(new byte[1176], new byte[846], new byte[846], true, true, true));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SyncFakeCodec : IAfsFrameCodec
    {
        private static readonly byte[] Sync = [0xCC, 0x63, 0xF7, 0x45, 0x36, 0xF4, 0x9E, 0x04, 0xA0];
        public Task<byte[]> EncodeAsync(int toi, ReadOnlyMemory<byte> sb2Bits, ReadOnlyMemory<byte> sb3Bits, ReadOnlyMemory<byte> sb4Bits, CancellationToken token) { var frame = new byte[750]; Array.Copy(Sync, frame, Sync.Length); return Task.FromResult(frame); }
        public Task<AfsDecodedFrame> DecodeAsync(int toi, ReadOnlyMemory<byte> frame, CancellationToken token)
        {
            if (!frame.Span[..8].SequenceEqual(Sync.AsSpan(0, 8)) || (frame.Span[8] & 0xF0) != (Sync[8] & 0xF0)) throw new InvalidOperationException("AFS synchronization pattern mismatch");
            return Task.FromResult(new AfsDecodedFrame(TestBits(1176, 3), TestBits(846, 4), TestBits(846, 5), true, true, true));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        private static byte[] TestBits(int length, uint state) { var bits = new byte[length]; for (var i = 0; i < length; i++) { state ^= state << 13; state ^= state >> 17; state ^= state << 5; bits[i] = (byte)(state & 1); } return bits; }
    }
}
