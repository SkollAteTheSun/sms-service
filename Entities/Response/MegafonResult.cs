using Newtonsoft.Json;

namespace Kp.Ms.Sms.Entities.Response;

public class MegafonResult
{
    [JsonProperty("msg_id")]
    public string MessageId { get; set; }

    [JsonProperty("status")]
    public MegafonStatus Status { get; set; }
}
