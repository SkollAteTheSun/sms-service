using Newtonsoft.Json;

namespace Kp.Ms.Sms.Entities.Response;

public class MegafonStatus
{
    [JsonProperty("code")]
    public int Code { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }
}
