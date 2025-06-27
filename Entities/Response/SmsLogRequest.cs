namespace Kp.Ms.Sms.Entities.Response;

public class SmsLogRequest
{
    public string? Phone { get; set; }
    public string? TextMessage { get; set; }
    public string Status { get; set; }
    public string? MessId { get; set; }
    public string? ErrorMessage { get; set; }

    //public SmsLogRequest(string? phone, string? textMessage, string status, string? messId, string? errorMessage)
    //{
    //    Phone = phone;
    //    TextMessage = textMessage;
    //    Status = status;
    //    MessId = messId;
    //    ErrorMessage = errorMessage;
    //}
}