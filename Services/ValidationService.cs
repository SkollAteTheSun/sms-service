using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Kp.Ms.Sms.Services;

public class ValidationService
{
    public bool ValidPhoneNumber(string phoneNumber, out string cleanedNumber)
    {
        var regex = new Regex(@"[^0-9]");
        cleanedNumber = regex.Replace(phoneNumber, "");

        return cleanedNumber.Length == 11 && (cleanedNumber.StartsWith("7") || cleanedNumber.StartsWith("8"));
    }

    public bool ValidIp(string? ip)
    {
        if (string.IsNullOrEmpty(ip)) return false;

        return IPAddress.TryParse(ip, out var ipAddress) && ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
    }

    public bool ValidUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;

        var regex = new Regex(@"(https:\/\/www\.|http:\/\/www\.|https:\/\/|http:\/\/)?[a-zA-Z]{2,}(\.[a-zA-Z]{2,})(\.[a-zA-Z]{2,})?\/[a-zA-Z0-9]{2,}|((https:\/\/www\.|http:\/\/www\.|https:\/\/|http:\/\/)?[a-zA-Z]{2,}(\.[a-zA-Z]{2,})(\.[a-zA-Z]{2,})?)|(https:\/\/www\.|http:\/\/www\.|https:\/\/|http:\/\/)?[a-zA-Z0-9]{2,}\.[a-zA-Z0-9]{2,}\.[a-zA-Z0-9]{2,}(\.[a-zA-Z0-9]{2,})?");
        return regex.IsMatch(url);
    }
}