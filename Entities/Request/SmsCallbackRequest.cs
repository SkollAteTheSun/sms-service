namespace Kp.Ms.Sms.Entities.Request;

public class SmsCallbackRequest
{
    public string CallbackUrl { get; set; }
    public string Phone { get; set; }
    public string Status { get; set; }
    public string MessId { get; set; }
    public string? Reason { get; set; }
}
