using System.Diagnostics;
using System.Net;
using System.Text.Json;
using LnisAfsValidator.Core;

namespace LnisAfsValidator.Infrastructure;

/// <summary>AFS 송신 세션의 SessionStart, Frame, SessionEnd와 Result 수신 순서를 실행한다.</summary>
internal sealed class AfsSendSessionHandler(
    AfsFrameService frameService,
    AfsTimeSynchronizer timeSynchronizer,
    AfsTestEvaluator evaluator,
    AfsResultWriter resultWriter)
{
    public async Task<AfsSessionResult> SendAsync(
        AfsSenderSettings test,
        AfsTransportSettings network,
        IProgress<AfsSessionProgress>? progress,
        CancellationToken token)
    {
        var testId = Guid.NewGuid();
        var prepared = await frameService.PrepareAsync(test, progress, token);

        using var transport = new AfsUdpTransport(network.ResultPort, enableBroadcast: true);
        var destination = new IPEndPoint(
            IPAddress.Parse(network.BroadcastAddress),
            network.DataPort);
        var offset = await timeSynchronizer.SynchronizeAsync(
            transport,
            destination,
            testId,
            test.Prn,
            token);
        var dropRate = test.TestType == AfsEndToEndTestType.TestE_UdpDrop
            ? network.SimulatedDropRatePercent
            : 0;
        var plannedDrops = prepared.Frames
            .SelectMany((_, frameIndex) => Enumerable.Range(0, network.RepeatCount)
                .Select(copyIndex => AfsPacketDropSimulator.ShouldDrop(
                    checked((uint)frameIndex),
                    copyIndex,
                    dropRate,
                    network.SimulatedDropSeed)))
            .LongCount(drop => drop);
        var first = prepared.Frames[0];
        var manifest = new AfsSessionManifest(
            testId,
            1,
            test.Prn,
            test.CustomMessageType,
            prepared.SourceLength,
            prepared.SourceSha256,
            prepared.RecordCount,
            prepared.Frames.Count,
            first.Week,
            first.IntervalOfWeek,
            first.TimeOfInterval,
            offset,
            dropRate,
            network.SimulatedDropSeed,
            plannedDrops,
            test.TestType,
            test.ErrorCount,
            test.ErrorSeed,
            test.SyncDamageInterval,
            prepared.InjectedFrameCount);

        var sessionStart = new AfsPacket(
            AfsPacketKind.SessionStart,
            testId,
            0,
            0,
            (byte)test.Prn,
            first.Week,
            first.IntervalOfWeek,
            first.TimeOfInterval,
            DateTimeOffset.UtcNow.UtcTicks,
            JsonSerializer.SerializeToUtf8Bytes(manifest));
        await transport.SendCopiesAsync(
            destination,
            sessionStart,
            network.RepeatCount,
            token);

        var initialProbe = new AfsPacket(
            AfsPacketKind.Probe,
            testId,
            0,
            0,
            (byte)test.Prn,
            0,
            0,
            0,
            DateTimeOffset.UtcNow.UtcTicks,
            []);
        await transport.SendCopiesAsync(
            destination,
            initialProbe,
            network.RepeatCount,
            token);

        var stopwatch = Stopwatch.StartNew();
        var nextProbe = TimeSpan.FromMilliseconds(network.ProbeIntervalMilliseconds);
        uint probeSequence = 1;
        long frameDatagramsSent = 0;
        for (var frameIndex = 0; frameIndex < prepared.Frames.Count; frameIndex++)
        {
            var frame = prepared.Frames[frameIndex];
            var packet = new AfsPacket(
                AfsPacketKind.Frame,
                testId,
                checked((uint)frameIndex),
                0,
                (byte)test.Prn,
                frame.Week,
                frame.IntervalOfWeek,
                frame.TimeOfInterval,
                DateTimeOffset.UtcNow.UtcTicks,
                frame.Payload);
            frameDatagramsSent += await transport.SendFrameCopiesAsync(
                destination,
                packet,
                network,
                dropRate,
                token);

            if (stopwatch.Elapsed >= nextProbe)
            {
                var probe = new AfsPacket(
                    AfsPacketKind.Probe,
                    testId,
                    probeSequence++,
                    0,
                    (byte)test.Prn,
                    0,
                    0,
                    0,
                    DateTimeOffset.UtcNow.UtcTicks,
                    []);
                await transport.SendCopiesAsync(
                    destination,
                    probe,
                    network.RepeatCount,
                    token);
                nextProbe += TimeSpan.FromMilliseconds(network.ProbeIntervalMilliseconds);
            }

            progress?.Report(new(
                "Transmitting",
                35 + 45.0 * (frameIndex + 1) / prepared.Frames.Count,
                $"Sent {frameIndex + 1}/{prepared.Frames.Count} logical frames"));
        }

        var sessionEnd = new AfsPacket(
            AfsPacketKind.SessionEnd,
            testId,
            checked((uint)prepared.Frames.Count),
            0,
            (byte)test.Prn,
            0,
            0,
            0,
            DateTimeOffset.UtcNow.UtcTicks,
            []);
        await transport.SendCopiesAsync(
            destination,
            sessionEnd,
            network.RepeatCount,
            token);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(network.ResultTimeoutSeconds));
        while (true)
        {
            AfsReceivedPacket received;
            try { received = await transport.ReceivePacketAsync(timeout.Token); }
            catch (InvalidDataException) { continue; }
            var packet = received.Packet;
            if (packet.Kind != AfsPacketKind.Result || packet.TestId != testId) continue;

            var wire = JsonSerializer.Deserialize<AfsWireResult>(packet.Payload)
                       ?? throw new InvalidDataException("Empty AFS result.");
            var counters = new AfsNetworkCounters(
                wire.ExpectedFrames,
                wire.ReceivedFrames,
                frameDatagramsSent,
                wire.ReceivedDatagrams,
                wire.Duplicates,
                wire.Corrupt,
                wire.Probes,
                wire.ProbeResponses,
                prepared.SourceLength,
                stopwatch.Elapsed,
                [],
                wire.AverageLatency,
                wire.MaximumLatency,
                plannedDrops,
                dropRate);
            var evaluation = evaluator.Evaluate(counters, test.Thresholds, wire);
            var directory = resultWriter.CreateDirectory(test.ResultRoot, testId, "tx");
            var result = new AfsSessionResult(
                testId,
                evaluation.Verdict,
                DateTimeOffset.UtcNow,
                wire.Integrity,
                evaluation.Metrics,
                counters,
                directory,
                wire.Error);
            await resultWriter.WriteResultAsync(result, [], token);
            return result;
        }
    }
}
