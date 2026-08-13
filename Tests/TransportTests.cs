using System.Net;
using System.Net.Sockets;
using LnisAfsValidator.Core;
using LnisAfsValidator.Infrastructure;
namespace LnisAfsValidator.Tests;
/// <summary>TCP artifact 전송의 파일 내용, manifest, receipt와 결과 회신을 검증한다.</summary>
public sealed class TransportTests
{
    [Fact]
    public async Task ShaMismatchReturnsFailedReceiptWithoutWaitingForResult()
    {
        var root = Path.Combine(Path.GetTempPath(), "lnis-afs-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "bad.iq"); await File.WriteAllBytesAsync(source, new byte[20_000]); var id = Guid.NewGuid();
            var scenario = new TestScenario("bad", new(0,0,0), 1, 1); var manifest = new TransferManifest(id, "bad.iq", 20_000, new string('A',64), 4096, new("PocketSDR.INT8X2",12), scenario, new(new(0,0,0),DateTimeOffset.UnixEpoch,0,0,0));
            var port = FreePort(); var transport = new TcpArtifactTransport();
            var server = Task.Run(async () => { var r = await transport.ReceiveAsync("127.0.0.1", port, Path.Combine(root,"rx"), 20_000, TimeSpan.FromSeconds(5), null, default); await using var c = r.Connection; Assert.False(r.Receipt.Success); });
            var (receipt, result) = await transport.SendAsync("127.0.0.1", port, manifest, source, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), null, default);
            Assert.False(receipt.Success); Assert.Null(result); await server;
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task LocalhostTransferVerifiesCrcBytesAndSha()
    {
        var root = Path.Combine(Path.GetTempPath(), "lnis-afs-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source.iq"); var data = new byte[196_613]; Random.Shared.NextBytes(data); await File.WriteAllBytesAsync(source, data);
            var hash = await Hashing.Sha256Async(source, default); var id = Guid.NewGuid(); var scenario = new TestScenario("net", new(0,0,0), 1, 1);
            var manifest = new TransferManifest(id, "source.iq", data.Length, hash, 32_768, new("PocketSDR.INT8X2", 12), scenario, new(new(0,0,0), DateTimeOffset.UnixEpoch,0,0,0));
            var port = FreePort(); var transport = new TcpArtifactTransport();
            var server = Task.Run(async () =>
            {
                var r = await transport.ReceiveAsync("127.0.0.1", port, Path.Combine(root, "rx"), data.Length, TimeSpan.FromSeconds(5), null, default);
                await using var connection = r.Connection; Assert.True(r.Receipt.Success); Assert.Equal(hash, r.Receipt.Sha256);
                await r.SendResultAsync(new(id, Verdict.Inconclusive, DateTimeOffset.Now, []), default);
            });
            var (receipt, remoteResult) = await transport.SendAsync("127.0.0.1", port, manifest, source, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), null, default);
            Assert.True(receipt.Success); Assert.NotNull(remoteResult); await server;
        }
        finally { Directory.Delete(root, true); }
    }
    private static int FreePort() { var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); var p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p; }
}
