using Newtonsoft.Json;

namespace Kp.Ms.Sms.Entities.Response;

public class BeelineError
{
    [JsonProperty("code")]
    public string Code { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }
}
