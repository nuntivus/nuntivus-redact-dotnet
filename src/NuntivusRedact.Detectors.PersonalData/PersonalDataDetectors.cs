using System.Net;
using System.Net.Sockets;
using NuntivusRedact.Detectors.Regex;

namespace NuntivusRedact.Detectors.PersonalData;

/// <summary>Heuristic personal-data candidate detectors. Not exhaustive identity validation.</summary>
public static class PersonalDataDetectors
{
    /// <summary>Creates the default detector set with unique stable identifiers.</summary>
    public static IReadOnlyList<ISensitiveDataDetector> CreateDefault() => Array.AsReadOnly<ISensitiveDataDetector>(
        [Email(), Phone(), Ipv4(), Ipv6(), Iban(), Url(), CreditCard()]);

    /// <summary>Detects common ASCII email syntax; not every RFC mailbox or internationalized address.</summary>
    public static ISensitiveDataDetector Email() => new RegexDetector("email", DetectionCategories.Email,
        @"(?<![\w.!#$%&'*+/=?^`{|}~@-])[A-Z0-9.!#$%&'*+/=?^`{|}~-]+@[A-Z0-9](?:[A-Z0-9-]*[A-Z0-9])?(?:\.[A-Z0-9](?:[A-Z0-9-]*[A-Z0-9])?)+(?![\w@-])");
    /// <summary>Detects phone-like runs containing 7 through 15 digits; not a numbering-plan validator.</summary>
    public static ISensitiveDataDetector Phone() => new RegexDetector("phone", DetectionCategories.Phone,
        @"(?<!\w)\+?[0-9](?:[0-9 ().-]{5,28}[0-9])(?!\w)", 0.6,
        validator: value => value.Count(char.IsAsciiDigit) is >= 7 and <= 15);
    /// <summary>Detects dotted-decimal IPv4 candidates validated with IPAddress.</summary>
    public static ISensitiveDataDetector Ipv4() => new RegexDetector("ipv4", DetectionCategories.IpAddress,
        @"(?<![\w.])(?:[0-9]{1,3}\.){3}[0-9]{1,3}(?![\w.])", 0.95,
        validator: value => IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetwork);
    /// <summary>Detects IPv6 literals including compressed and IPv4-mapped syntax, without zone identifiers.</summary>
    public static ISensitiveDataDetector Ipv6() => new RegexDetector("ipv6", DetectionCategories.IpAddress,
        @"(?<![\w:])[0-9A-F:.]*:[0-9A-F:.]+(?![\w:%])", 0.95,
        validator: value => IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6);
    /// <summary>Detects contiguous IBAN candidates with mod97 validation; country-specific account rules are not checked.</summary>
    public static ISensitiveDataDetector Iban() => new RegexDetector("iban", DetectionCategories.Iban,
        @"\b[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}\b", 0.95, validator: IsIban);
    /// <summary>Detects absolute HTTP(S) URL candidates; trailing punctuation may be included.</summary>
    public static ISensitiveDataDetector Url() => new RegexDetector("url", DetectionCategories.Url,
        "\\bhttps?://[^\\s<>\"']+", 0.8,
        validator: value => Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host));
    /// <summary>Detects 13 through 19 digit card candidates with Luhn checks, not issuer validation.</summary>
    public static ISensitiveDataDetector CreditCard() => new RegexDetector("credit-card", DetectionCategories.CreditCard,
        @"(?<![0-9])(?<![0-9][ -])[0-9](?:[ -]?[0-9]){12,18}(?![ -]?[0-9])", 0.9, validator: IsCard);

    private static bool IsIban(string value)
    {
        int remainder = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char c = char.ToUpperInvariant(value[(i + 4) % value.Length]);
            if (char.IsAsciiDigit(c)) remainder = (remainder * 10 + c - '0') % 97;
            else remainder = (remainder * 100 + c - 'A' + 10) % 97;
        }
        return remainder == 1;
    }
    private static bool IsCard(string value)
    {
        int sum = 0;
        bool doubleDigit = false;
        bool nonzero = false;
        for (int i = value.Length - 1; i >= 0; i--)
        {
            if (!char.IsAsciiDigit(value[i])) continue;
            int digit = value[i] - '0';
            nonzero |= digit != 0;
            if (doubleDigit) { digit *= 2; if (digit > 9) digit -= 9; }
            sum += digit;
            doubleDigit = !doubleDigit;
        }
        return nonzero && sum % 10 == 0;
    }
}
