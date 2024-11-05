using System.ComponentModel;

namespace Kp.Ms.Sms.Entities.Request;

public class CallRequest
{
    [DefaultValue(89046331311)]
    public string Phone { get; set; } = string.Empty;

    [DefaultValue(null)]
    public string? CallbackUrl { get; set; }

    [DefaultValue(-1)]
    public string UserIp { get; set; } = string.Empty;
}
