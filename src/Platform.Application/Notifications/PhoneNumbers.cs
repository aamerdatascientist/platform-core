namespace Platform.Application.Notifications;

/// <summary>Mobile numbers as WhatsApp wants them: international digits, no "+".</summary>
public static class PhoneNumbers
{
    /// <summary>
    /// "0551234567", "+966 55 123 4567", "00966551234567" and "966551234567" all become
    /// "966551234567". A local number starting 05 is taken as Saudi. Null for blank input;
    /// throws for anything that isn't 8-15 digits once cleaned up.
    /// </summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var text = input.Trim();
        var plus = text.StartsWith('+');
        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length != text.Count(c => !(c is ' ' or '-' or '(' or ')' or '+')))
            throw new FormatException("A phone number can only hold digits, spaces, dashes and a leading +.");

        if (!plus && digits.StartsWith("00")) digits = digits[2..];
        else if (!plus && digits.Length == 10 && digits.StartsWith("05")) digits = "966" + digits[1..];

        if (digits.Length is < 8 or > 15)
            throw new FormatException("Enter the full mobile number, e.g. 0551234567 or +966551234567.");
        return digits;
    }
}
