using System.Globalization;
using System.Text.Json;

namespace Joblink.Services.Employer
{
    public sealed record GeocodedLocation(decimal Latitude, decimal Longitude);

    // Turns a job's free-text location into coordinates. Best-effort: publishing a job must
    // never fail just because geocoding did - a null result just means the job has no
    // coordinates yet.
    public interface IGeocodingService
    {
        Task<GeocodedLocation?> GeocodeAsync(string location, CancellationToken cancellationToken);
    }

    // Nominatim (OpenStreetMap) - free, no API key. Their usage policy requires a real,
    // identifying User-Agent (no default/blank one) - this app's posting volume is far below
    // their rate limit, so no extra throttling is needed here.
    public sealed class NominatimGeocodingService : IGeocodingService
    {
        private const string DefaultBaseUrl = "https://nominatim.openstreetmap.org";
        private const string UserAgent = "JobLink-Capstone/1.0 (+fuertessean07@gmail.com)";

        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<NominatimGeocodingService> _logger;
        private readonly string _baseUrl;

        public NominatimGeocodingService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<NominatimGeocodingService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;

            var baseUrl = configuration["Nominatim:BaseUrl"];

            _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim().TrimEnd('/');
        }

        public async Task<GeocodedLocation?> GeocodeAsync(string location, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(location))
                return null;

            var url = $"{_baseUrl}/search?q={Uri.EscapeDataString(location)}&format=json&limit=1";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(UserAgent);

            var client = _httpClientFactory.CreateClient();
            client.Timeout = RequestTimeout;

            HttpResponseMessage response;

            try
            {
                response = await client.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex, "Could not reach Nominatim to geocode {Location}.", location);
                return null;
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Nominatim returned {Status} geocoding {Location}.", (int)response.StatusCode, location);
                    return null;
                }

                try
                {
                    var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                    if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
                        return null;

                    var first = document.RootElement[0];

                    if (!first.TryGetProperty("lat", out var latProp) || !first.TryGetProperty("lon", out var lonProp))
                        return null;

                    if (decimal.TryParse(latProp.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
                        decimal.TryParse(lonProp.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
                        return new GeocodedLocation(lat, lon);
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException)
                {
                    _logger.LogWarning(ex, "Unexpected response from Nominatim geocoding {Location}.", location);
                }

                return null;
            }
        }
    }

    // Always "no coordinates" - for tests, so they never make a real network call.
    public sealed class NullGeocodingService : IGeocodingService
    {
        public Task<GeocodedLocation?> GeocodeAsync(string location, CancellationToken cancellationToken) =>
            Task.FromResult<GeocodedLocation?>(null);
    }
}
