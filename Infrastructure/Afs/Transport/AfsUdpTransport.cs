using System.Net;
using System.Net.Sockets;
using LnisAfsValidator.Core;

namespace LnisAfsValidator.Infrastructure;

/// <summary>수신한 AFS 패킷과 실제 UDP 송신자 주소를 함께 전달한다.</summary>
public sealed record AfsReceivedPacket(AfsPacket Packet, IPEndPoint RemoteEndPoint);

/// <summary>AFS 패킷의 UDP 송수신, 복제 전송, 의도적 Drop과 중복 판정을 담당한다.</summary>
public sealed class AfsUdpTransport : IDisposable
{
    private readonly UdpClient udp;
    private readonly AfsPacketDeduplicator deduplicator = new();
    public long ReceivedDatagramCount { get; private set; }

    public AfsUdpTransport(int localPort, bool enableBroadcast)
    {
        udp = new UdpClient(new IPEndPoint(IPAddress.Any, localPort));
        udp.EnableBroadcast = enableBroadcast;
    }

    public async Task<AfsReceivedPacket> ReceivePacketAsync(CancellationToken token)
    {
        var received = await udp.ReceiveAsync(token);
        ReceivedDatagramCount++;
        return new(AfsPacketCodec.Decode(received.Buffer), received.RemoteEndPoint);
    }

    public bool TryAccept(AfsPacket packet) => deduplicator.TryAccept(packet);
    public int AcceptedPacketCount => deduplicator.Count;

    public async Task SendCopiesAsync(
        IPEndPoint destination,
        AfsPacket packet,
        int copies,
        CancellationToken token)
    {
        for (var copyIndex = 0; copyIndex < copies; copyIndex++)
        {
            var copy = packet with
            {
                CopyIndex = checked((byte)copyIndex),
                SentUtcTicks = packet.Kind == AfsPacketKind.Frame
                    ? DateTimeOffset.UtcNow.UtcTicks
                    : packet.SentUtcTicks
            };
            await udp.SendAsync(AfsPacketCodec.Encode(copy), destination, token);
        }
    }

    public async Task<int> SendFrameCopiesAsync(
        IPEndPoint destination,
        AfsPacket packet,
        AfsTransportSettings settings,
        double dropRate,
        CancellationToken token)
    {
        var sent = 0;
        for (var copyIndex = 0; copyIndex < settings.RepeatCount; copyIndex++)
        {
            // Test E에서는 Frame 데이터그램만 제거하고 세션 제어 패킷은 항상 전송한다.
            if (AfsPacketDropSimulator.ShouldDrop(packet.Sequence, copyIndex, dropRate, settings.SimulatedDropSeed))
                continue;

            var copy = packet with
            {
                CopyIndex = checked((byte)copyIndex),
                SentUtcTicks = DateTimeOffset.UtcNow.UtcTicks
            };
            await udp.SendAsync(AfsPacketCodec.Encode(copy), destination, token);
            sent++;
        }
        return sent;
    }

    public void Dispose() => udp.Dispose();
}
