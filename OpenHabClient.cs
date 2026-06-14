using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Solar;

public class OpenHabClient(HttpClient httpClient, IOptions<SolarOptions> options, ILogger<OpenHabClient> logger)
{
    private readonly string _baseUrl = options.Value.OpenHabBaseUrl.TrimEnd('/');

    public async Task<bool> PostStateAsync(string item, double value, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/{item}";
        var content = new StringContent(value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Encoding.UTF8, "text/plain");

        try
        {
            var response = await httpClient.PostAsync(url, content, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("REST failed for {Item}: {Status}", item, response.StatusCode);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning("REST error for {Item}: {Error}", item, ex.Message);
            return false;
        }
    }
}
