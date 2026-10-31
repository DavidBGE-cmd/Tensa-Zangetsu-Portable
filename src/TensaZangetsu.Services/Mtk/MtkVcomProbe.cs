using System.Buffers.Binary;
using TensaZangetsu.Transport.SerialPort;

namespace TensaZangetsu.Services.Mtk;

/// <summary>
/// Performs only the legacy MTK VCOM BROM identification exchange.
/// It intentionally does not upload DA, authenticate or write device storage.
/// </summary>
internal static class MtkVcomProbe
{
    private static ReadOnlySpan<byte> StartCommand => [0xA0, 0x0A, 0x50, 0x05];

    public static ushort Connect(string portName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        cancellationToken.ThrowIfCancellationRequested();

        using var transport = SerialPortTransportFactory.Create(
            portName,
            readTimeout: 5000,
            writeTimeout: 5000);

        transport.Open();
        try
        {
            transport.Flush();
            cancellationToken.ThrowIfCancellationRequested();
            transport.Write(StartCommand);

            Span<byte> response = stackalloc byte[sizeof(ushort)];
            int bytesRead = transport.ReadExact(response, 5000);
            if (bytesRead != response.Length)
                throw new TimeoutException(
                    $"The MTK VCOM HW_CODE response was incomplete ({bytesRead}/{response.Length} bytes).");

            return BinaryPrimitives.ReadUInt16BigEndian(response);
        }
        finally
        {
            transport.Close();
        }
    }
}
