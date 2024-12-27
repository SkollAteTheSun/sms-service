namespace Kp.Ms.Sms.Entities.Response;

public class CallLogRequest
{
    public string? Phone { get; set; }
    public string Status { get; set; }
    public string? CallId { get; set; }
    public string? Code { get; set; }
    public string? ErrorMessage { get; set; }

    public CallLogRequest(string? phone, string status, string? callId, string? code, string? errorMessage)
    {
        Phone = phone;
        Status = status;
        CallId = callId;
        Code = code;
        ErrorMessage = errorMessage;
    }
}
