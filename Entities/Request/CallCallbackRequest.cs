namespace Kp.Ms.Sms.Entities.Request;

public class CallCallbackRequest
{
    public string Code { get; set; }
    public string Phone { get; set; }
    public string Status { get; set; }
    public string CallId { get; set; }
    public string Reason { get; set; }
}
