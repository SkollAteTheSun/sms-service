using Newtonsoft.Json;

namespace Kp.Ms.Sms.Entities.Request;

[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class SmsCRequest
{
    [JsonProperty("login")]
    public string Login { get; set; }

    [JsonProperty("psw")]
    public string Password { get; set; }

    [JsonProperty("apikey")]
    public string ApiKey { get; set; }

    [JsonProperty("phones")]
    public string Phones { get; set; }

    [JsonProperty("mes")]
    public string Message { get; set; }

    [JsonProperty("sender")]
    public string Sender { get; set; }

    [JsonProperty("charset")]
    public string CharSet { get; set; }
}
