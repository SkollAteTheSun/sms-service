using System.Text;

namespace Kp.Ms.Sms.Services;

public class ValidationService
{
    public bool ValidPhoneNumber(string phoneNumber)
        => phoneNumber.Length == 11 && (phoneNumber.StartsWith("7") || phoneNumber.StartsWith("8"));

    public bool ValidIpAddress(string ipAddress)
        => !string.IsNullOrEmpty(ipAddress);

    public bool ValidUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;

        if (Uri.TryCreate(url, UriKind.Absolute, out var uriResult) &&
            (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
        {
            return true;
        }

        return false;
    }

    public string CleanPhoneNumber(string phoneNumber)
    {
        var cleanedNumber = new StringBuilder();

        foreach (char c in phoneNumber)
        {
            if (char.IsDigit(c))
            {
                cleanedNumber.Append(c);
            }
        }
        return cleanedNumber.ToString();
    }
}