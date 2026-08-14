using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LnisAfsValidator.Core;

namespace LnisAfsValidator.Infrastructure;

/// <summary>
/// Test A/E의 시간 동기, 세션 제어, AFS 프레임 송수신, RAW 재조립과 결과 반환을 담당한다.
/// Frame만 선택적으로 드롭하며 세션 제어 패킷은 시험 종료를 위해 항상 전송한다.
/// </summary>
public sealed class AfsUdpSessionService(Func<IAfsFrameCodec>? codecFactory = null) : IAfsSessionService
{
    private readonly Func<IAfsFrameCodec> codecFactory = codecFactory ?? (() => new AfsNativeCodec());
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public async Task<AfsSessionResult> SendAsync(AfsSenderSettings test, AfsTransportSettings network, IProgress<AfsSessionProgress>? progress, CancellationToken token)
    {
        // 원본 레코드 두 조각을 SB3/SB4에 배치하므로 한 AFS 프레임이 최대 두 fragment를 운반한다.
        Validate(test, network, true); var testId = Guid.NewGuid(); var records = await ReadRecordsAsync(test.CapturePath, token);
        var sourceHash = await Hashing.Sha256Async(test.CapturePath, token); var sourceLength = new FileInfo(test.CapturePath).Length;
        var (week, itow, toi) = TimeFrom(records);
        var blocks = records.SelectMany((record, index) => AfsRawFragmentCodec.Fragment(checked((uint)index), record)).ToArray();
        var frames = new List<(ushort Week, ushort Itow, byte Toi, byte[] Frame)>(); await using var codec = codecFactory();
        for (var i = 0; i < blocks.Length; i += 2)
        {
            token.ThrowIfCancellationRequested(); var second = i + 1 < blocks.Length ? blocks[i + 1] : blocks[i];
            var sb2 = AfsSb2Builder.BuildValidationPattern(week, itow); var sb3 = AfsRawFragmentCodec.ToSbBits(blocks[i]); var sb4 = AfsRawFragmentCodec.ToSbBits(second);
            frames.Add((week, itow, toi, await codec.EncodeAsync(toi, sb2, sb3, sb4, token))); Advance(ref week, ref itow, ref toi);
            progress?.Report(new("Encoding", 35.0 * (i + 2) / Math.Max(1, blocks.Length), $"Encoded {frames.Count} AFS frames"));
        }

        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, network.ResultPort)); udp.EnableBroadcast = true;
        var destination = new IPEndPoint(IPAddress.Parse(network.BroadcastAddress), network.DataPort);
        var offset = await SynchronizeAsync(udp, destination, testId, test.Prn, token);
        var plannedDrops = frames.SelectMany((_, frameIndex) => Enumerable.Range(0, network.RepeatCount)
            .Select(copyIndex => AfsPacketDropSimulator.ShouldDrop((uint)frameIndex, copyIndex, network.SimulatedDropRatePercent, network.SimulatedDropSeed)))
            .LongCount(drop => drop);
        var manifest = new AfsSessionManifest(testId, 1, test.Prn, test.CustomMessageType, sourceLength, sourceHash, records.Count, frames.Count,
            frames[0].Week, frames[0].Itow, frames[0].Toi, offset, network.SimulatedDropRatePercent, network.SimulatedDropSeed, plannedDrops);
        await SendCopiesAsync(udp, destination, new(AfsPacketKind.SessionStart, testId, 0, 0, (byte)test.Prn, frames[0].Week, frames[0].Itow, frames[0].Toi, DateTimeOffset.UtcNow.UtcTicks, JsonSerializer.SerializeToUtf8Bytes(manifest)), network.RepeatCount, token);
        await SendCopiesAsync(udp, destination, new(AfsPacketKind.Probe, testId, 0, 0, (byte)test.Prn, 0, 0, 0, DateTimeOffset.UtcNow.UtcTicks, []), network.RepeatCount, token);
        var stopwatch = Stopwatch.StartNew(); var nextProbe = TimeSpan.FromMilliseconds(network.ProbeIntervalMilliseconds); uint probeSequence = 1;
        long frameDatagramsSent = 0;
        for (var i = 0; i < frames.Count; i++)
        {
            var f = frames[i]; var packet = new AfsPacket(AfsPacketKind.Frame, testId, checked((uint)i), 0, (byte)test.Prn, f.Week, f.Itow, f.Toi, DateTimeOffset.UtcNow.UtcTicks, f.Frame);
            frameDatagramsSent += await SendFrameCopiesAsync(udp, destination, packet, network, token);
            if (stopwatch.Elapsed >= nextProbe) { await SendCopiesAsync(udp, destination, new(AfsPacketKind.Probe, testId, probeSequence++, 0, (byte)test.Prn, 0, 0, 0, DateTimeOffset.UtcNow.UtcTicks, []), network.RepeatCount, token); nextProbe += TimeSpan.FromMilliseconds(network.ProbeIntervalMilliseconds); }
            progress?.Report(new("Transmitting", 35 + 45.0 * (i + 1) / frames.Count, $"Sent {i + 1}/{frames.Count} logical frames"));
        }
        await SendCopiesAsync(udp, destination, new(AfsPacketKind.SessionEnd, testId, checked((uint)frames.Count), 0, (byte)test.Prn, 0, 0, 0, DateTimeOffset.UtcNow.UtcTicks, []), network.RepeatCount, token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(network.ResultTimeoutSeconds));
        while (true)
        {
            var received = await udp.ReceiveAsync(timeout.Token); AfsPacket packet;
            try { packet = AfsPacketCodec.Decode(received.Buffer); } catch (InvalidDataException) { continue; }
            if (packet.Kind != AfsPacketKind.Result || packet.TestId != testId) continue;
            var wire = JsonSerializer.Deserialize<WireResult>(packet.Payload) ?? throw new InvalidDataException("Empty AFS result.");
            var counters = new AfsNetworkCounters(wire.ExpectedFrames, wire.ReceivedFrames, frameDatagramsSent, wire.ReceivedDatagrams, wire.Duplicates, wire.Corrupt, wire.Probes, wire.ProbeResponses, sourceLength, stopwatch.Elapsed, [], wire.AverageLatency, wire.MaximumLatency, plannedDrops, network.SimulatedDropRatePercent);
            var metrics = AfsPerformanceCalculator.Calculate(counters, test.Thresholds).Concat(SystemMetrics(wire)).ToArray();
            var verdict = wire.Verdict == Verdict.Fail || metrics.Any(x => x.Status == MetricStatus.Fail) ? Verdict.Fail : wire.Verdict;
            var localDirectory = Path.Combine(test.ResultRoot, $"{DateTime.Now:yyyyMMdd-HHmmss}-{testId:N}-afs-tx"); Directory.CreateDirectory(localDirectory);
            var result = new AfsSessionResult(testId, verdict, DateTimeOffset.UtcNow, wire.Integrity, metrics, counters, localDirectory, wire.Error);
            await File.WriteAllTextAsync(Path.Combine(localDirectory, "result.json"), JsonSerializer.Serialize(result, Json), token); await WriteCsvAsync(localDirectory, metrics, [], token); return result;
        }
    }

    public async Task<AfsSessionResult> ReceiveAsync(AfsReceiverSettings test, AfsTransportSettings network, IProgress<AfsSessionProgress>? progress, CancellationToken token)
    {
        // 수신 측은 SessionStart의 원본 메타데이터를 기준으로 재조립 결과와 최종 해시를 검증한다.
        Validate(test, network, false); using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, network.DataPort));
        AfsSessionManifest? manifest = null; var dedup = new AfsPacketDeduplicator(); var reassembler = new AfsRawReassembler();
        var latencies = new List<double>(); long datagrams = 0, duplicates = 0, corrupt = 0, probes = 0, probeResponses = 0; IPEndPoint? sender = null;
        await using var codec = codecFactory(); await using var sampler = new ProcessResourceSampler(); var started = Stopwatch.StartNew();
        while (true)
        {
            var received = await udp.ReceiveAsync(token); datagrams++; AfsPacket packet;
            try { packet = AfsPacketCodec.Decode(received.Buffer); } catch (InvalidDataException) { corrupt++; continue; }
            if (packet.Kind == AfsPacketKind.TimeSyncRequest)
            {
                var t2 = DateTimeOffset.UtcNow.UtcTicks; var payload = new byte[24]; BinaryPrimitives.WriteInt64BigEndian(payload, packet.SentUtcTicks); BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(8), t2);
                var t3 = DateTimeOffset.UtcNow.UtcTicks; BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(16), t3);
                await SendCopiesAsync(udp, received.RemoteEndPoint, new(AfsPacketKind.TimeSyncResponse, packet.TestId, packet.Sequence, 0, packet.Prn, 0, 0, 0, t3, payload), 1, token); continue;
            }
            if (!dedup.TryAccept(packet)) { duplicates++; continue; }
            sender ??= received.RemoteEndPoint;
            if (packet.Kind == AfsPacketKind.SessionStart) { manifest = JsonSerializer.Deserialize<AfsSessionManifest>(packet.Payload) ?? throw new InvalidDataException("Invalid session manifest."); progress?.Report(new("Receiving", 5, $"Session {manifest.TestId:N}")); continue; }
            if (manifest is null || packet.TestId != manifest.TestId) continue;
            if (packet.Kind == AfsPacketKind.Probe) { probes++; await SendCopiesAsync(udp, received.RemoteEndPoint, new(AfsPacketKind.ProbeResponse, packet.TestId, packet.Sequence, 0, packet.Prn, 0, 0, 0, DateTimeOffset.UtcNow.UtcTicks, []), 1, token); probeResponses++; continue; }
            if (packet.Kind == AfsPacketKind.Frame)
            {
                var latency = (DateTimeOffset.UtcNow.UtcTicks - packet.SentUtcTicks - manifest.ClockOffsetTicks) / (double)TimeSpan.TicksPerMillisecond; if (latency >= 0) latencies.Add(latency);
                var decoded = await codec.DecodeAsync(packet.TimeOfInterval, packet.Payload, token); if (!decoded.Sb3Valid || !decoded.Sb4Valid) { corrupt++; continue; }
                reassembler.Add(AfsRawFragmentCodec.DecodeBlock(AfsRawFragmentCodec.FromSbBits(decoded.Sb3Bits, manifest.CustomMessageType)));
                reassembler.Add(AfsRawFragmentCodec.DecodeBlock(AfsRawFragmentCodec.FromSbBits(decoded.Sb4Bits, manifest.CustomMessageType)));
                progress?.Report(new("Receiving", 10 + 70.0 * dedup.Count / Math.Max(1, manifest.FrameCount), $"Received frame {packet.Sequence}")); continue;
            }
            if (packet.Kind != AfsPacketKind.SessionEnd) continue;
            await Task.Delay(network.EndGraceMilliseconds, token); break;
        }

        if (manifest is null || sender is null) throw new InvalidDataException("AFS session ended without a manifest.");
        var directory = Path.Combine(test.ResultRoot, $"{DateTime.Now:yyyyMMdd-HHmmss}-{manifest.TestId:N}-afs-rx"); Directory.CreateDirectory(directory);
        var reconstructed = Path.Combine(directory, "reconstructed.graw"); var complete = reassembler.CompleteRecords();
        await using (var stream = new FileStream(reconstructed, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 64 * 1024, true))
            foreach (var pair in complete) { var size = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(size, checked((uint)pair.Record.Length)); await stream.WriteAsync(size, token); await stream.WriteAsync(pair.Record, token); }
        var hash = await Hashing.Sha256Async(reconstructed, token); var info = new FileInfo(reconstructed); var integrity = new RawIntegrityResult(hash == manifest.SourceSha256 && info.Length == manifest.SourceLength && complete.Count == manifest.RecordCount && reassembler.IncompleteRecords().Count == 0,
            manifest.SourceLength, info.Length, manifest.SourceSha256, hash, manifest.RecordCount, complete.Count, reassembler.IncompleteRecords().Count == 0 ? "RAW comparison completed." : "Incomplete RAW records remain.");
        var frameCount = dedup.Count; var counters = new AfsNetworkCounters(manifest.FrameCount, Math.Min(manifest.FrameCount, latencies.Count), manifest.FrameCount * network.RepeatCount - manifest.SimulatedDroppedDatagrams, datagrams, duplicates, corrupt, Math.Max(1, probes), probeResponses, info.Length, started.Elapsed, latencies, null, null, manifest.SimulatedDroppedDatagrams, manifest.SimulatedDropRatePercent);
        var samples = sampler.Samples; var wire = new WireResult(integrity.Success ? Verdict.Pass : Verdict.Fail, integrity, manifest.FrameCount, counters.ReceivedLogicalFrames, datagrams, duplicates, corrupt, counters.ProbeAttempts, counters.ProbeResponses, latencies.Count == 0 ? null : latencies.Average(), latencies.Count == 0 ? null : latencies.Max(), samples.Count == 0 ? 0 : samples.Average(x => x.CpuPercent), samples.Count == 0 ? 0 : samples.Max(x => x.CpuPercent), samples.Count == 0 ? 0 : samples.Average(x => x.WorkingSetBytes), samples.Count == 0 ? 0 : samples.Max(x => x.WorkingSetBytes), directory, integrity.Success ? null : integrity.Detail);
        var metrics = AfsPerformanceCalculator.Calculate(counters, test.Thresholds).Concat(SystemMetrics(wire)).ToArray(); var finalVerdict = wire.Verdict == Verdict.Fail || metrics.Any(x => x.Status == MetricStatus.Fail) ? Verdict.Fail : wire.Verdict; var result = new AfsSessionResult(manifest.TestId, finalVerdict, DateTimeOffset.UtcNow, integrity, metrics, counters, directory, wire.Error); wire = wire with { Verdict = finalVerdict };
        await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(result, Json), token); await WriteCsvAsync(directory, metrics, samples, token);
        var resultPayload = JsonSerializer.SerializeToUtf8Bytes(wire); if (resultPayload.Length > AfsPacketCodec.MaximumPayloadLength) throw new InvalidDataException("Compact result exceeds UDP protocol limit.");
        await SendCopiesAsync(udp, sender, new(AfsPacketKind.Result, manifest.TestId, 0, 0, (byte)manifest.Prn, 0, 0, 0, DateTimeOffset.UtcNow.UtcTicks, resultPayload), network.RepeatCount, token); return result;
    }

    private static async Task<long> SynchronizeAsync(UdpClient udp, IPEndPoint destination, Guid testId, int prn, CancellationToken token)
    {
        var offsets = new List<long>();
        for (uint i = 0; i < 8; i++)
        {
            var t1 = DateTimeOffset.UtcNow.UtcTicks; await SendCopiesAsync(udp, destination, new(AfsPacketKind.TimeSyncRequest, testId, i, 0, (byte)prn, 0, 0, 0, t1, []), 1, token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(300);
            try { var r = await udp.ReceiveAsync(timeout.Token); var p = AfsPacketCodec.Decode(r.Buffer); var t4 = DateTimeOffset.UtcNow.UtcTicks; if (p.Kind == AfsPacketKind.TimeSyncResponse && p.Sequence == i && p.Payload.Length == 24) offsets.Add((BinaryPrimitives.ReadInt64BigEndian(p.Payload.AsSpan(8)) - t1 + BinaryPrimitives.ReadInt64BigEndian(p.Payload.AsSpan(16)) - t4) / 2); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        }
        if (offsets.Count == 0) return 0; offsets.Sort(); return offsets[offsets.Count / 2];
    }

    private static async Task SendCopiesAsync(UdpClient udp, IPEndPoint destination, AfsPacket packet, int copies, CancellationToken token)
    { for (var i = 0; i < copies; i++) { var bytes = AfsPacketCodec.Encode(packet with { CopyIndex = checked((byte)i), SentUtcTicks = packet.Kind == AfsPacketKind.Frame ? DateTimeOffset.UtcNow.UtcTicks : packet.SentUtcTicks }); await udp.SendAsync(bytes, destination, token); } }
    private static async Task<int> SendFrameCopiesAsync(UdpClient udp, IPEndPoint destination, AfsPacket packet, AfsTransportSettings network, CancellationToken token)
    {
        var sent = 0;
        for (var copyIndex = 0; copyIndex < network.RepeatCount; copyIndex++)
        {
            // Test E에서는 Frame 데이터그램만 제거한다. SessionStart/End와 결과 패킷은
            // 시험 제어가 중단되지 않도록 항상 전송한다.
            if (AfsPacketDropSimulator.ShouldDrop(packet.Sequence, copyIndex, network.SimulatedDropRatePercent, network.SimulatedDropSeed)) continue;
            var bytes = AfsPacketCodec.Encode(packet with { CopyIndex = checked((byte)copyIndex), SentUtcTicks = DateTimeOffset.UtcNow.UtcTicks });
            await udp.SendAsync(bytes, destination, token); sent++;
        }
        return sent;
    }
    private static void Advance(ref ushort week, ref ushort itow, ref byte toi) { if (++toi < 100) return; toi = 0; if (++itow < 504) return; itow = 0; week++; }
    private static (ushort Week, ushort Itow, byte Toi) TimeFrom(IReadOnlyList<byte[]> records)
    {
        var codec = new GnssRawBinaryCodec(); foreach (var record in records) { var e = codec.Decode(record); if (e.Message is ObservationEpochMessage o) return NextTime(o.Week, o.ReceiverTowSeconds); }
        var first = codec.Decode(records[0]); var gps = first.CapturedAtUtc.AddSeconds(18) - new DateTimeOffset(1980, 1, 6, 0, 0, 0, TimeSpan.Zero); return NextTime(checked((ushort)(gps.TotalDays / 7)), gps.TotalSeconds % 604800);
        static (ushort Week, ushort Itow, byte Toi) NextTime(ushort week, double sow) { var itow = (ushort)(sow / 1200); var toi = (int)(sow % 1200) / 12 + 1; if (toi < 100) return (week, itow, (byte)toi); toi = 0; if (++itow < 504) return (week, itow, 0); return (checked((ushort)(week + 1)), 0, 0); }
    }
    private static async Task<List<byte[]>> ReadRecordsAsync(string path, CancellationToken token)
    { var result = new List<byte[]>(); await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true); var size = new byte[4]; while (stream.Position < stream.Length) { await stream.ReadExactlyAsync(size, token); var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(size)); if (length is <= 0 or > 1048704) throw new InvalidDataException("Invalid capture.graw record length."); var record = new byte[length]; await stream.ReadExactlyAsync(record, token); result.Add(record); } if (result.Count == 0) throw new InvalidDataException("capture.graw is empty."); return result; }
    private static IEnumerable<PerformanceMetric> SystemMetrics(WireResult w) { yield return new(PerformanceCategory.System, "CpuAverage", "라우팅 및 PVT 처리 시 프로세서 평균 사용량", "%", w.CpuAverage, MetricStatus.Measured); yield return new(PerformanceCategory.System, "CpuMaximum", "라우팅 및 PVT 처리 시 프로세서 최대 사용량", "%", w.CpuMaximum, MetricStatus.Measured); yield return new(PerformanceCategory.System, "MemoryAverage", "번들 저장 및 처리 시 평균 메모리 사용량", "byte", w.MemoryAverage, MetricStatus.Measured); yield return new(PerformanceCategory.System, "MemoryMaximum", "번들 저장 및 처리 시 최대 메모리 사용량", "byte", w.MemoryMaximum, MetricStatus.Measured); yield return new(PerformanceCategory.System, "LogStorageRate", "시험데이터 기록 성공률", "%", 100, MetricStatus.Measured); }
    private static async Task WriteCsvAsync(string directory, IReadOnlyList<PerformanceMetric> metrics, IReadOnlyList<ResourceSample> samples, CancellationToken token) { await File.WriteAllLinesAsync(Path.Combine(directory, "metrics-summary.csv"), new[] { "Category,Name,Description,Value,Unit,Status" }.Concat(metrics.Select(x => $"{x.Category},{x.Name},\"{x.Description}\",{x.Value},{x.Unit},{x.Status}")), Encoding.UTF8, token); await File.WriteAllLinesAsync(Path.Combine(directory, "metrics-timeseries.csv"), new[] { "Timestamp,CpuPercent,WorkingSetBytes" }.Concat(samples.Select(x => $"{x.Timestamp:O},{x.CpuPercent},{x.WorkingSetBytes}")), Encoding.UTF8, token); }
    private static void Validate(AfsSenderSettings test, AfsTransportSettings network, bool sender) { if (!File.Exists(test.CapturePath)) throw new FileNotFoundException("capture.graw 파일을 찾을 수 없습니다.", test.CapturePath); ValidateCommon(test.ResultRoot, test.Prn, test.CustomMessageType, network); }
    private static void Validate(AfsReceiverSettings test, AfsTransportSettings network, bool sender) => ValidateCommon(test.ResultRoot, test.Prn, test.CustomMessageType, network);
    private static void ValidateCommon(string resultRoot, int prn, int customMessageType, AfsTransportSettings network)
    {
        if (prn != 8 || customMessageType != 63) throw new ArgumentException("AFS v1은 PRN 8과 Custom Type 63만 지원합니다.");
        if (network.DataPort is < 1 or > 65535 || network.ResultPort is < 1 or > 65535 || network.DataPort == network.ResultPort) throw new ArgumentException("데이터 포트와 결과 포트는 서로 다른 1~65535 값이어야 합니다.");
        if (network.RepeatCount is < 1 or > 20) throw new ArgumentException("중복 송신 횟수는 1~20 범위여야 합니다.");
        if (network.SimulatedDropRatePercent is < 0 or > 100) throw new ArgumentException("의도적 UDP Drop Rate는 0~100% 범위여야 합니다.");
        Directory.CreateDirectory(resultRoot);
    }
    private sealed record WireResult(Verdict Verdict, RawIntegrityResult Integrity, long ExpectedFrames, long ReceivedFrames, long ReceivedDatagrams, long Duplicates, long Corrupt, long Probes, long ProbeResponses, double? AverageLatency, double? MaximumLatency, double CpuAverage, double CpuMaximum, double MemoryAverage, double MemoryMaximum, string ResultDirectory, string? Error);
}
