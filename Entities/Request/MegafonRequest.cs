using Newtonsoft.Json;

namespace Kp.Ms.Sms.Entities.Request;

public class MegafonRequest
{
    [JsonProperty("from")]
    public string From { get; set; }

    [JsonProperty("to")]
    public long To { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }
}
