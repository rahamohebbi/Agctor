using System.Net.Http.Headers;
using System.Text;

namespace AgctorSDK.Core.Rag.Transport;

/// <summary>Default <see cref="IRestRagTransport"/> using injected <see cref="HttpClient"/>.</summary>
public sealed class RestRagTransport : IRestRagTransport
{
    private readonly HttpClient _http;

    public RestRagTransport(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc />
    public async Task<RagRestResponse> SendAsync(RagRestCall call, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(call.Method, call.Url);
        if (call.Headers != null)
        {
            foreach (var (key, value) in call.Headers)
                request.Headers.TryAddWithoutValidation(key, value);
        }

        if (!string.IsNullOrEmpty(call.JsonBody))
        {
            request.Content = new StringContent(call.JsonBody, Encoding.UTF8, "application/json");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new RagRestResponse((int)response.StatusCode, body, response.IsSuccessStatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            // Sidecar down / wrong port — adapters map status 0 to Unavailable instead of crashing the Host UI.
            return new RagRestResponse(0, ex.Message, false);
        }
        catch (OperationCanceledException ex)
        {
            // HttpClient timeout (not user cancellation).
            return new RagRestResponse(0, ex.Message ?? "Request timed out.", false);
        }
    }
}
