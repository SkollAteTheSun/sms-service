using System.Text.Json.Serialization;

namespace Kp.Ms.Sms.Entities.Response;

public class SmsResponse
{
    public string? Status { get; set; }

    [JsonPropertyName("status_code")]
    public int? StatusCode { get; set; }

    [JsonPropertyName("status_text")]
    public string? StatusText { get; set; }
}
