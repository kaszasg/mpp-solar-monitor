using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Solar;

public class InverterClient(IOptions<SolarOptions> options, ILogger<InverterClient> logger)
{
    private readonly SolarOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _lastAccess = DateTime.MinValue;

    public static string CalculateChecksum(string query)
    {
        int csum = 0;
        foreach (char c in query)
            csum += c;
        return query + (csum % 256).ToString("D3");
    }

    public async Task<string?> SendCommandAsync(string command, bool addChecksum = false,
        CancellationToken ct = default)
    {
        if (addChecksum)
            command = CalculateChecksum(command);

        await _gate.WaitAsync(ct);
        try
        {
            var elapsed = DateTime.UtcNow - _lastAccess;
            if (elapsed < TimeSpan.FromSeconds(1))
                await Task.Delay(TimeSpan.FromSeconds(1) - elapsed, ct);

            return await SendCommandCoreAsync(command, ct);
        }
        finally
        {
            _lastAccess = DateTime.UtcNow;
            _gate.Release();
        }
    }

    private async Task<string?> SendCommandCoreAsync(string command, CancellationToken ct)
    {
        logger.LogInformation("ACCESS Query:[{Query}]", command);

        using var client = new TcpClient();
        client.ReceiveTimeout = 1000;
        client.SendTimeout = 1000;

        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(_options.InverterHost, _options.InverterPort, connectCts.Token);
        }
        catch (Exception ex)
        {
            logger.LogError("Connection error: {Error}", ex.Message);
            return null;
        }

        var stream = client.GetStream();

        try
        {
            var sendData = Encoding.ASCII.GetBytes(command + "\r");
            await stream.WriteAsync(sendData, ct);
            await stream.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError("Send error: {Error}", ex.Message);
            return null;
        }

        try
        {
            var response = new StringBuilder();
            var buffer = new byte[1];
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCts.CancelAfter(TimeSpan.FromSeconds(5));

            while (true)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(0, 1), readCts.Token);
                if (read == 0)
                    break;

                char c = (char)buffer[0];
                if (c == '\r')
                {
                    // Try to consume optional \n
                    if (stream.DataAvailable)
                    {
                        read = await stream.ReadAsync(buffer.AsMemory(0, 1), readCts.Token);
                        if (read > 0 && (char)buffer[0] != '\n')
                            response.Append((char)buffer[0]);
                    }
                    break;
                }

                if (c == '\n')
                    break;

                response.Append(c);
            }

            var result = response.ToString();
            if (string.IsNullOrEmpty(result))
            {
                logger.LogWarning("Empty response received");
                return null;
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            logger.LogError("Timeout reading response");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError("Receive error: {Error}", ex.Message);
            return null;
        }
    }

    public static double[]? ParseResponse(string line)
    {
        if (line.Length <= 3)
            return null;

        // Strip first char '(' and last 2 chars (checksum end)
        var content = line[1..^2];

        if (content == "NAK")
            return null;

        var parts = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var values = new double[parts.Length];

        for (int i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], System.Globalization.CultureInfo.InvariantCulture, out values[i]))
                values[i] = 0;
        }

        return values;
    }
}
