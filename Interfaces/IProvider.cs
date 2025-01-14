using Kp.Ms.Sms.Entities.Response;

namespace Kp.Ms.Sms.Interfaces;

public interface IProvider
{
    Task<SmsResponse> SendSmsAsync(string phone, string message);
    Task<CallResponse> CallApiAsync(string phone, string? userIp);
}