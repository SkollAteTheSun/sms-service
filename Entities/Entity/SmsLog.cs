using Newtonsoft.Json;

namespace Kp.Ms.Sms.Entities.Entity;

public class SmsLog
{
    [JsonProperty("mess_id")]
    public string MessId { get; set; }

    [JsonProperty("date")]
    public DateTime Date { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("provider")]
    public string Provider { get; set; }

    [JsonProperty("error_message")]
    public string? ErrorMessage { get; set; }
}