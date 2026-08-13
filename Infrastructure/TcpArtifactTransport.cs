using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LnisAfsValidator.Core;
namespace LnisAfsValidator.Infrastructure;
/// <summary>IQ artifact와 manifest, 검증 결과를 길이 제한이 있는 TCP 프로토콜로 교환한다.</summary>
public sealed partial class TcpArtifactTransport : IArtifactTransport
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("LAFSIQ01");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const int MaxManifestBytes = 64 * 1024;

    public async Task<(TransferReceipt, ValidationResult?)> SendAsync(string host, int port, TransferManifest manifest, string filePath, TimeSpan connectTimeout, TimeSpan idleTimeout, IProgress<RunProgress>? progress, CancellationToken token)
    {
        using var client = new TcpClient(); using var connect = CancellationTokenSource.CreateLinkedTokenSource(token); connect.CancelAfter(connectTimeout);
        await client.ConnectAsync(host, port, connect.Token); await using var stream = client.GetStream();
        await stream.WriteAsync(Magic, token); await WriteJsonAsync(stream, manifest, token);
        await SendChunksAsync(stream, filePath, manifest, progress, token);
        var receipt = await ReadJsonAsync<TransferReceipt>(stream, idleTimeout, token);
        if (!receipt.Success) return (receipt, null);
        var result = await ReadJsonAsync<ValidationResult>(stream, TimeSpan.FromMinutes(60), token);
        return (receipt, result);
    }

    public async Task<ReceivedTransfer> ReceiveAsync(string bindAddress, int port, string directory, long maxBytes, TimeSpan idleTimeout, IProgress<RunProgress>? progress, CancellationToken token)
    {
        var address = bindAddress is "" or "0.0.0.0" ? IPAddress.Any : IPAddress.Parse(bindAddress);
        var listener = new TcpListener(address, port); listener.Start(); progress?.Report(new(RunState.Listening, 1, $"Listening on {address}:{port}"));
        TcpClient client;
        try { client = await listener.AcceptTcpClientAsync(token); } finally { listener.Stop(); }
        var connection = new TcpConnection(client); var stream = client.GetStream();
        try
        {
            var magic = new byte[Magic.Length]; await ReadExactlyAsync(stream, magic, idleTimeout, token);
            if (!magic.SequenceEqual(Magic)) throw new InvalidDataException("Invalid protocol magic or version.");
            var manifest = await ReadJsonAsync<TransferManifest>(stream, idleTimeout, token);
            ValidateManifest(manifest, maxBytes);
            Directory.CreateDirectory(directory); var partial = Path.Combine(directory, manifest.TestId.ToString("N") + ".partial");
            var receipt = await ReceiveChunksAsync(stream, partial, manifest, idleTimeout, progress, token);
            await WriteJsonAsync(stream, receipt, token);
            Func<ValidationResult, CancellationToken, Task> send = async (result, ct) => await WriteJsonAsync(stream, result, ct);
            return new(manifest, receipt, send, connection);
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static void ValidateManifest(TransferManifest m, long maxBytes)
    {
        if (m.FileLength <= 0 || m.FileLength > maxBytes) throw new InvalidDataException("File length is outside the allowed range.");
        if (m.ChunkSize is < 4096 or > 16 * 1024 * 1024) throw new InvalidDataException("Chunk size is outside the allowed range.");
        if (m.Sha256.Length != 64 || !m.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid SHA-256.");
        if (m.Format.FormatId != "PocketSDR.INT8X2") throw new InvalidDataException("Unsupported I/Q format.");
    }

    // 연결과 NetworkStream의 수명을 하나의 비동기 disposable로 관리한다.
    private sealed class TcpConnection(TcpClient client) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { client.Dispose(); return ValueTask.CompletedTask; }
    }
}
