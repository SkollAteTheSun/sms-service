using Kp.Ms.Sms.Entities.Response;

namespace Kp.Ms.Sms.Interfaces;

public interface ISmsProvider
{
    Task<SmsResponse> SendSmsAsync(string phone, string message);
}