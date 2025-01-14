using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Kp.Ms.Sms.Entities.Request;

public class CallRequest
{
    [DefaultValue(89991002233)]
    public string Phone { get; set; } = string.Empty;

    [DefaultValue(null)]
    public string? CallbackUrl { get; set; }

    [DefaultValue(null)]
    public string? UserIp { get; set; } = string.Empty;

    [JsonIgnore]
    public string CallId { get; set; } = string.Empty;
}