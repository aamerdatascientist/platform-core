using System.Text;

namespace Platform.Application.Files;

/// <summary>
/// Confirms the bytes actually look like the declared content type, rather than trusting
/// the client-supplied Content-Type header alone - that header is just whatever the
/// browser/caller says it is, trivially spoofable, and UploadFileCommandValidator's
/// allow-list check on its own only ever validated that string, never the file itself.
/// Deliberately just magic-byte sniffing, not a full parse - enough to catch "this isn't
/// really a JPEG" without pulling in an image-processing dependency for a security check.
/// </summary>
public static class FileSignatureValidator
{
    // ISO base media file format (HEIC, and also MP4/HEIF variants) - the brand at offset 8
    // varies by encoder, so this checks the family of brands real HEIC files actually use
    // rather than one fixed signature.
    private static readonly HashSet<string> HeicBrands = new(StringComparer.Ordinal)
    {
        "heic", "heix", "heim", "heis", "hevc", "hevx", "hevm", "hevs", "mif1", "msf1",
    };

    public static bool MatchesDeclaredContentType(ReadOnlySpan<byte> header, string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/jpeg" => header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
        "image/png" => header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        "image/webp" => header.Length >= 12
            && Encoding.ASCII.GetString(header[..4]) == "RIFF"
            && Encoding.ASCII.GetString(header[8..12]) == "WEBP",
        "application/pdf" => header.Length >= 5 && Encoding.ASCII.GetString(header[..5]) == "%PDF-",
        "image/heic" => header.Length >= 12
            && Encoding.ASCII.GetString(header[4..8]) == "ftyp"
            && HeicBrands.Contains(Encoding.ASCII.GetString(header[8..12])),
        _ => false
    };
}
