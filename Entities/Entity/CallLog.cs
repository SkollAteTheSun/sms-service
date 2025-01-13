using Newtonsoft.Json;

namespace Kp.Ms.Sms.Entities.Entity;

public class CallLog
{
    [JsonProperty("call_id")]
    public string? CallId { get; set; }

    [JsonProperty("phone")]
    public string? Phone { get; set; }

    [JsonProperty("code")]
    public string? Code { get; set; }

    [JsonProperty("date")]
    public DateTime Date { get; set; }

    [JsonProperty("provider")]
    public string Provider { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("error_message")]
    public string? ErrorMessage { get; set; }
}