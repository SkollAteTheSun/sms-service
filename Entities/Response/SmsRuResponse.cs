using Newtonsoft.Json;

namespace Kp.Ms.Sms.Entities.Response;

public class SmsRuResponse
{
    public string? Status { get; set; }

    [JsonProperty("status_code")]
    public int? StatusCode { get; set; }

    [JsonProperty("status_text")]
    public string? StatusText { get; set; }
}
