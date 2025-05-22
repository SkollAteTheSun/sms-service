using Newtonsoft.Json;

namespace Kp.Ms.Sms.Entities.Request;

public class BeelineRequest
{
    [JsonProperty("user")]
    public string User { get; set; }

    [JsonProperty("pass")]
    public string Password { get; set; }

    [JsonProperty("action")]
    public string Action { get; set; }

    [JsonProperty("sender")]
    public string Sender { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }

    [JsonProperty("target")]
    public string Phones { get; set; }

    [JsonProperty("gzip")]
    public string Gzip { get; set; } = "none";
}
