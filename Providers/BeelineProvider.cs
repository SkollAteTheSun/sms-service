using Common.HttpClientWrapper;
using Kp.Ms.Sms.Config;
using Kp.Ms.Sms.Entities.Enums;
using Kp.Ms.Sms.Entities.Request;
using Kp.Ms.Sms.Entities.Response;
using System.Text;

namespace Kp.Ms.Sms.Providers;

public class BeelineProvider : Provider
{
    public BeelineProvider(
        IHttpClientWrapper client,
        ProviderSettings settings) : base(client, settings)
    {
    }

    public override async Task<SmsResponse> SendSmsAsync(string phone, string message)
    {
        try
        {
            var parameters = new BeelineRequest()
            {
                User = Settings.Username,
                Password = Settings.Password,
                Action = "post_sms",
                Sender = Settings.Sender,
                Phones = phone,
                Message = message
            };

            var result = await Client.PostAsync<BeelineRequest, BeelineResponse>($"{Settings.Url}/proto/http/rest", parameters);

            var response = new SmsResponse()
            {
                ProviderName = ProviderNames.Beeline.ToString()
            };

            if (result.Error == null)
            {
                response.Status = SmsResponseStatus.OK.ToString();
            }
            else
            {
                response.Status = SmsResponseStatus.ERROR.ToString();
                response.StatusCode = Convert.ToInt32(result.Error?.Code);
                response.StatusText = result.Error?.Message;
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
