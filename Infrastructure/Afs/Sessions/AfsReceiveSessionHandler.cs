using System.Diagnostics;
using System.Net;
using System.Text.Json;
using LnisAfsValidator.Core;

namespace LnisAfsValidator.Infrastructure;

/// <summary>AFS 수신 세션의 패킷 처리, 프레임 복호, RAW 복원과 Result 반환 순서를 실행한다.</summary>
internal sealed class AfsReceiveSessionHandler(
    AfsFrameService frameService,
    AfsTimeSynchronizer timeSynchronizer,
    AfsTestEvaluator evaluator,
    AfsResultWriter resultWriter)
{
    public async Task<AfsSessionResult> ReceiveAsync(
        AfsReceiverSettings test,
        AfsTransportSettings network,
        IProgress<AfsSessionProgress>? progress,
        CancellationToken token)
    {
        using var transport = new AfsUdpTransport(network.DataPort, enableBroadcast: false);
        await using var frames = frameService.CreateReceiver(test.CustomMessageType);
        await using var sampler = new ProcessResourceSampler();
        var started = Stopwatch.StartNew();

        AfsSessionManifest? manifest = null;
        var receivedFrameSequences = new HashSet<uint>();
        var syncFrames = new SortedDictionary<uint, AfsPacket>();
        var latencies = new List<double>();
        long duplicates = 0;
        long corruptDatagrams = 0;
        long probes = 0;
        long probeResponses = 0;
        IPEndPoint? sender = null;

        while (true)
        {
            AfsReceivedPacket received;
            try { received = await transport.ReceivePacketAsync(token); }
            catch (InvalidDataException) { corruptDatagrams++; continue; }
            var packet = received.Packet;

            if (await timeSynchronizer.TryRespondAsync(transport, received, token))
                continue;
            if (!transport.TryAccept(packet))
            {
                duplicates++;
                continue;
            }

            sender ??= received.RemoteEndPoint;
            if (packet.Kind == AfsPacketKind.SessionStart)
            {
                manifest = JsonSerializer.Deserialize<AfsSessionManifest>(packet.Payload)
                           ?? throw new InvalidDataException("Invalid session manifest.");
                var conditions = evaluator.TestConditions(manifest);
                progress?.Report(new(
                    "Receiving",
                    5,
                    $"{manifest.TestType}: {conditions}",
                    manifest.TestType,
                    conditions));
                continue;
            }
            if (manifest is null || packet.TestId != manifest.TestId) continue;

            if (packet.Kind == AfsPacketKind.Probe)
            {
                probes++;
                var response = new AfsPacket(
                    AfsPacketKind.ProbeResponse,
                    packet.TestId,
                    packet.Sequence,
                    0,
                    packet.Prn,
                    0,
                    0,
                    0,
                    DateTimeOffset.UtcNow.UtcTicks,
                    []);
                await transport.SendCopiesAsync(
                    received.RemoteEndPoint,
                    response,
                    1,
                    token);
                probeResponses++;
                continue;
            }

            if (packet.Kind == AfsPacketKind.Frame)
            {
                var latency = (
                    DateTimeOffset.UtcNow.UtcTicks -
                    packet.SentUtcTicks -
                    manifest.ClockOffsetTicks) / (double)TimeSpan.TicksPerMillisecond;
                if (latency >= 0) latencies.Add(latency);
                receivedFrameSequences.Add(packet.Sequence);

                // Test D만 연속 심볼에서 SP를 다시 찾기 위해 프레임을 종료 시점까지 보관한다.
                if (manifest.TestType == AfsEndToEndTestType.TestD_SyncRecovery)
                    syncFrames[packet.Sequence] = packet;
                else
                    await frames.DecodeAsync(packet.TimeOfInterval, packet.Payload, token);

                progress?.Report(new(
                    "Receiving",
                    10 + 70.0 * transport.AcceptedPacketCount / Math.Max(1, manifest.FrameCount),
                    $"Received frame {packet.Sequence}"));
                continue;
            }

            if (packet.Kind != AfsPacketKind.SessionEnd) continue;
            await Task.Delay(network.EndGraceMilliseconds, token);
            break;
        }

        if (manifest is null || sender is null)
            throw new InvalidDataException("AFS session ended without a manifest.");
        if (manifest.TestType == AfsEndToEndTestType.TestD_SyncRecovery)
            await frames.RecoverSynchronizedAsync(syncFrames.Values.ToArray(), token);

        var directory = resultWriter.CreateDirectory(test.ResultRoot, manifest.TestId, "rx");
        var completeRecords = frames.CompleteRecords();
        var reconstructed = await resultWriter.WriteReconstructedAsync(
            directory,
            completeRecords,
            token);
        var reconstructedHash = await Hashing.Sha256Async(reconstructed, token);
        var reconstructedInfo = new FileInfo(reconstructed);
        var integrity = new RawIntegrityResult(
            reconstructedHash == manifest.SourceSha256 &&
            reconstructedInfo.Length == manifest.SourceLength &&
            completeRecords.Count == manifest.RecordCount &&
            frames.IncompleteRecordCount == 0,
            manifest.SourceLength,
            reconstructedInfo.Length,
            manifest.SourceSha256,
            reconstructedHash,
            manifest.RecordCount,
            completeRecords.Count,
            frames.IncompleteRecordCount == 0
                ? "RAW comparison completed."
                : "Incomplete RAW records remain.");
        var counters = new AfsNetworkCounters(
            manifest.FrameCount,
            receivedFrameSequences.Count,
            manifest.FrameCount * network.RepeatCount - manifest.SimulatedDroppedDatagrams,
            transport.ReceivedDatagramCount,
            duplicates,
            corruptDatagrams + frames.CorruptFrames,
            Math.Max(1, probes),
            probeResponses,
            reconstructedInfo.Length,
            started.Elapsed,
            latencies,
            null,
            null,
            manifest.SimulatedDroppedDatagrams,
            manifest.SimulatedDropRatePercent);
        var samples = sampler.Samples;
        var wire = evaluator.CreateReceiverWireResult(
            manifest,
            counters,
            integrity,
            frames,
            samples,
            directory);
        var evaluation = evaluator.Evaluate(counters, test.Thresholds, wire);
        wire = wire with { Verdict = evaluation.Verdict };
        var result = new AfsSessionResult(
            manifest.TestId,
            evaluation.Verdict,
            DateTimeOffset.UtcNow,
            integrity,
            evaluation.Metrics,
            counters,
            directory,
            wire.Error);
        await resultWriter.WriteResultAsync(result, samples, token);

        var resultPayload = JsonSerializer.SerializeToUtf8Bytes(wire);
        if (resultPayload.Length > AfsPacketCodec.MaximumPayloadLength)
            throw new InvalidDataException("Compact result exceeds UDP protocol limit.");
        var resultPacket = new AfsPacket(
            AfsPacketKind.Result,
            manifest.TestId,
            0,
            0,
            (byte)manifest.Prn,
            0,
            0,
            0,
            DateTimeOffset.UtcNow.UtcTicks,
            resultPayload);
        await transport.SendCopiesAsync(
            sender,
            resultPacket,
            network.RepeatCount,
            token);
        return result;
    }
}
