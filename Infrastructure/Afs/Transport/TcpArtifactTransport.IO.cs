using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;
using LnisAfsValidator.Core;
namespace LnisAfsValidator.Infrastructure;
// TcpArtifactTransport에서 사용하는 고정 길이 숫자와 JSON 메시지 입출력을 구현한다.
public sealed partial class TcpArtifactTransport
{
    private static async Task WriteJsonAsync<T>(NetworkStream stream, T value, CancellationToken token)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(value, Json); if (data.Length > MaxManifestBytes) throw new InvalidDataException("JSON message is too large.");
        var size = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(size, data.Length); await stream.WriteAsync(size, token); await stream.WriteAsync(data, token);
    }
    private static async Task<T> ReadJsonAsync<T>(NetworkStream stream, TimeSpan idle, CancellationToken token)
    {
        var size = new byte[4]; await ReadExactlyAsync(stream, size, idle, token); var length = BinaryPrimitives.ReadInt32BigEndian(size);
        if (length <= 0 || length > MaxManifestBytes) throw new InvalidDataException("Invalid JSON message length.");
        var data = new byte[length]; await ReadExactlyAsync(stream, data, idle, token);
        return JsonSerializer.Deserialize<T>(data, Json) ?? throw new InvalidDataException("Invalid JSON message.");
    }
    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, TimeSpan idle, CancellationToken token)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(idle);
            var read = await stream.ReadAsync(buffer[offset..], timeout.Token); if (read == 0) throw new EndOfStreamException("Connection closed before the message completed."); offset += read;
        }
    }
}
