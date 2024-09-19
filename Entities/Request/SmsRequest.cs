namespace Kp.Ms.Sms.Entities.Request;

public class SmsRequest
{
    public string Phone { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? CallbackUrl { get; set; }
}