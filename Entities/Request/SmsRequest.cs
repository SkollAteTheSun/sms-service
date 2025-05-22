using System.Text.Json.Serialization;

namespace Kp.Ms.Sms.Entities.Request;

public class SmsRequest
{
    public string OrganizationName { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? CallbackUrl { get; set; }

    [JsonIgnore]
    public string MessId { get; set; } = string.Empty;
}