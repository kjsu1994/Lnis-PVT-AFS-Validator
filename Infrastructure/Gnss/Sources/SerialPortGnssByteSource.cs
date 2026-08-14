using System.IO.Ports;
using LnisAfsValidator.Core;

namespace LnisAfsValidator.Infrastructure;

/// <summary>운영체제의 현재 COM 포트 목록을 정렬하여 제공한다.</summary>
public sealed class SystemGnssSerialPortCatalog : IGnssSerialPortCatalog
{
    public IReadOnlyList<string> GetPortNames() => SerialPort.GetPortNames().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
}

/// <summary>GNSS 캡처 설정으로 Windows SerialPort 바이트 소스를 생성한다.</summary>
public sealed class SerialPortGnssByteSourceFactory : IGnssByteSourceFactory
{
    public ValueTask<IGnssByteSource> OpenAsync(GnssSerialCaptureSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var port = new SerialPort(settings.PortName, settings.BaudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            DtrEnable = settings.DtrEnable,
            RtsEnable = settings.RtsEnable,
            ReadTimeout = 1000,
            WriteTimeout = 1000
        };
        port.Open();
        return ValueTask.FromResult<IGnssByteSource>(new SerialPortGnssByteSource(port));
    }
}

/// <summary>열린 SerialPort의 BaseStream을 비동기 캡처 서비스에 노출한다.</summary>
public sealed class SerialPortGnssByteSource(SerialPort port) : IGnssByteSource
{
    public string Description => $"{port.PortName} @ {port.BaudRate:N0} bps";

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
        port.BaseStream.ReadAsync(buffer, cancellationToken);

    public ValueTask DisposeAsync()
    {
        if (port.IsOpen) port.Close();
        port.Dispose();
        return ValueTask.CompletedTask;
    }
}
