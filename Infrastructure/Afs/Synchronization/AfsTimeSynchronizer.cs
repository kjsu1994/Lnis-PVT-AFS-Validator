using System.Buffers.Binary;
using System.Net;
using LnisAfsValidator.Core;

namespace LnisAfsValidator.Infrastructure;

/// <summary>AFS 시험 PC 사이의 UDP 왕복 시각 교환과 중앙값 기반 시계 오프셋 추정을 담당한다.</summary>
public sealed class AfsTimeSynchronizer
{
    public async Task<long> SynchronizeAsync(
        AfsUdpTransport transport,
        IPEndPoint destination,
        Guid testId,
        int prn,
        CancellationToken token)
    {
        var offsets = new List<long>();
        for (uint sequence = 0; sequence < 8; sequence++)
        {
            var t1 = DateTimeOffset.UtcNow.UtcTicks;
            var request = new AfsPacket(
                AfsPacketKind.TimeSyncRequest,
                testId,
                sequence,
                0,
                (byte)prn,
                0,
                0,
                0,
                t1,
                []);
            await transport.SendCopiesAsync(destination, request, 1, token);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(300);
            try
            {
                var received = await transport.ReceivePacketAsync(timeout.Token);
                var packet = received.Packet;
                var t4 = DateTimeOffset.UtcNow.UtcTicks;
                if (packet.Kind == AfsPacketKind.TimeSyncResponse &&
                    packet.Sequence == sequence &&
                    packet.Payload.Length == 24)
                {
                    offsets.Add((
                        BinaryPrimitives.ReadInt64BigEndian(packet.Payload.AsSpan(8)) - t1 +
                        BinaryPrimitives.ReadInt64BigEndian(packet.Payload.AsSpan(16)) - t4) / 2);
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        }

        if (offsets.Count == 0) return 0;
        offsets.Sort();
        return offsets[offsets.Count / 2];
    }

    public async Task<bool> TryRespondAsync(
        AfsUdpTransport transport,
        AfsReceivedPacket received,
        CancellationToken token)
    {
        var request = received.Packet;
        if (request.Kind != AfsPacketKind.TimeSyncRequest) return false;

        var t2 = DateTimeOffset.UtcNow.UtcTicks;
        var payload = new byte[24];
        BinaryPrimitives.WriteInt64BigEndian(payload, request.SentUtcTicks);
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(8), t2);
        var t3 = DateTimeOffset.UtcNow.UtcTicks;
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(16), t3);

        var response = new AfsPacket(
            AfsPacketKind.TimeSyncResponse,
            request.TestId,
            request.Sequence,
            0,
            request.Prn,
            0,
            0,
            0,
            t3,
            payload);
        await transport.SendCopiesAsync(received.RemoteEndPoint, response, 1, token);
        return true;
    }
}
