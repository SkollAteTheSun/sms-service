using Newtonsoft.Json;

namespace Kp.Ms.Sms.Entities.Response;

public class CallResponse : Versioning
{
    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("code")]
    public string? Code { get; set; }

    [JsonProperty("call_id")]
    public string? CallId { get; set; }

    [JsonProperty("cost")]
    public double? Cost { get; set; }

    [JsonProperty("balance")]
    public double? Balance { get; set; }

    [JsonProperty("status_code")]
    public int? StatusCode { get; set; }

    [JsonProperty("status_text")]
    public string? StatusText { get; set; }
}