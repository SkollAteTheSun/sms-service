using Newtonsoft.Json;

namespace Kp.Ms.Sms.Entities.Response;

public class MegafonResponse
{
    [JsonProperty("result")]
    public MegafonResult Result { get; set; }
}
