using System.Net.Http.Headers;
using System.Text;
using Platform.Application.Reports;

namespace Platform.Infrastructure.Reports;

/// <summary>
/// HTML to PDF through Gotenberg (a Chromium-based PDF service, run on Railway next to the
/// databases - see docs/STAGING.md). The page and its fonts are posted together, so the
/// service never fetches anything from the internet while printing. Protected with basic auth.
/// </summary>
public class GotenbergPdfRenderer : IPdfRenderer
{
    // Inches, which is what Gotenberg takes by default.
    private const string A4Width = "8.27";
    private const string A4Height = "11.7";
    private const string A4Margin = "0.47"; // 12 mm
    private const string PhoneWidth = "4.3125"; // 414 CSS px, a large phone

    private static readonly string[] FontFiles =
        { "Tajawal-Regular.ttf", "Tajawal-Medium.ttf", "Tajawal-Bold.ttf", "Tajawal-ExtraBold.ttf" };

    private readonly HttpClient _http;
    private readonly ReportSettings _settings;

    public GotenbergPdfRenderer(HttpClient http, ReportSettings settings)
    {
        _http = http;
        _settings = settings;
    }

    public async Task<byte[]> RenderAsync(string html, bool phone, CancellationToken cancellationToken)
    {
        if (!_settings.IsPdfConfigured) throw new InvalidOperationException("The PDF service isn't set up.");

        using var form = new MultipartFormDataContent();
        var page = new ByteArrayContent(Encoding.UTF8.GetBytes(html));
        page.Headers.ContentType = new MediaTypeHeaderValue("text/html") { CharSet = "utf-8" };
        form.Add(page, "files", "index.html");

        var assembly = typeof(GotenbergPdfRenderer).Assembly;
        foreach (var font in FontFiles)
        {
            await using var stream = assembly.GetManifestResourceStream($"Fonts.{font}")
                ?? throw new InvalidOperationException($"Font {font} is missing from the build.");
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            var content = new ByteArrayContent(buffer.ToArray());
            content.Headers.ContentType = new MediaTypeHeaderValue("font/ttf");
            form.Add(content, "files", font);
        }

        void Field(string name, string value) => form.Add(new StringContent(value), name);
        Field("printBackground", "true");
        Field("preferCssPageSize", "false");
        if (phone)
        {
            // One long page the width of a phone screen: it scrolls like a web page, with no page breaks.
            Field("paperWidth", PhoneWidth);
            Field("paperHeight", A4Height);
            Field("singlePage", "true");
            foreach (var side in new[] { "marginTop", "marginBottom", "marginLeft", "marginRight" }) Field(side, "0");
        }
        else
        {
            Field("paperWidth", A4Width);
            Field("paperHeight", A4Height);
            foreach (var side in new[] { "marginTop", "marginBottom", "marginLeft", "marginRight" }) Field(side, A4Margin);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_settings.PdfServiceUrl!.TrimEnd('/')}/forms/chromium/convert/html")
        {
            Content = form,
        };
        if (!string.IsNullOrEmpty(_settings.PdfServiceUsername))
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.PdfServiceUsername}:{_settings.PdfServicePassword}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"PDF service answered {(int)response.StatusCode}: {(body.Length > 300 ? body[..300] : body)}");
        }
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }
}
