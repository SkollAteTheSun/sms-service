using Newtonsoft.Json;

namespace Kp.Ms.Sms.Entities.Response;

public class CallResponse
{
    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("code", NullValueHandling = NullValueHandling.Ignore)]
    public string? Code { get; set; }

    [JsonProperty("call_id", NullValueHandling = NullValueHandling.Ignore)]
    public string? CallId { get; set; }

    [JsonProperty("cost", NullValueHandling = NullValueHandling.Ignore)]
    public double? Cost { get; set; }

    [JsonProperty("balance", NullValueHandling = NullValueHandling.Ignore)]
    public double? Balance { get; set; }

    [JsonProperty("status_text", NullValueHandling = NullValueHandling.Ignore)]
    public string? StatusText { get; set; }
}