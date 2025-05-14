using Common.HttpClientWrapper;
using Kp.Ms.Sms.Config;
using Kp.Ms.Sms.Entities.Enums;
using Kp.Ms.Sms.Entities.Request;
using Kp.Ms.Sms.Entities.Response;
using System.Text;

namespace Kp.Ms.Sms.Providers;

public class SmsCProvider : Provider
{
    public SmsCProvider(
        IHttpClientWrapper client,
        ProviderSettings settings) : base(client, settings)
    {
    }

    public override async Task<SmsResponse> SendSmsAsync(string phone, string message)
    {
        try
        {
            var parameters = new SmsCRequest()
            {
                ApiKey = Settings.Password,
                Phones = phone,
                Message = message,
                CharSet = "utf-8"
            };

            var result = await Client.PostAsync<SmsCRequest, SmsCResponse>($"{Settings.Url}/rest/send/", parameters,
                headers: DefaultHeaders);

            var response = new SmsResponse()
            {
                ProviderName = ProviderNames.SMSC.ToString()
            };

            if (result.Error == null && result.ErrorCode == null)
            {
                response.Status = SmsResponseStatus.OK.ToString();
            }
            else
            {
                response.Status = SmsResponseStatus.ERROR.ToString();
                response.StatusText = result.Error;
                response.StatusCode = result.ErrorCode;
            }

            return response;
        }
        catch (Exception ex)
        {
            return new SmsResponse()
            {
                Status = SmsResponseStatus.ERROR.ToString(),
                StatusText = ex.Message
            };
        }
    }

    public override async Task<CallResponse> CallApiAsync(string phone, string? userIp)
    {
        throw new NotImplementedException();
    }
}
