using System.Net.Http.Headers;
using System.Text;

namespace Epi.Rendering;

/// <summary>
/// The print engine ADR-010 chose: Chromium behind Gotenberg, HTML in, PDF out.
/// </summary>
/// <remarks>
/// The only part of rendering that needs a container. Everything about what the output says, and
/// whether two renders agree, is decided before this is called and checked after it returns.
/// </remarks>
public sealed class GotenbergPrintEngine(HttpClient client) : IPrintEngine
{
    /// <summary>
    /// Chromium's HTML route. The form file must be named <c>index.html</c>; anything else is
    /// refused with a message that does not name the field, which is worth knowing once.
    /// </summary>
    private const string Route = "forms/chromium/convert/html";

    private readonly HttpClient _client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<byte[]> ToPdfAsync(string html, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(html);

        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(html));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        form.Add(content, "files", "index.html");

        using var response = await _client.PostAsync(Route, form, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // The engine's own message, because it names what it did not like. Swallowing it
            // would leave a render failing with a status code and nothing to act on.
            throw new PrintEngineException(
                $"The print engine answered {(int)response.StatusCode}: "
                + await response.Content.ReadAsStringAsync(cancellationToken));
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }
}

/// <summary>Raised when the print engine could not produce a document.</summary>
public sealed class PrintEngineException(string message) : Exception(message);
