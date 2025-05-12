using Common.HttpClientWrapper;
using Kp.Ms.Sms.Config;
using Kp.Ms.Sms.Entities.Enums;
using Kp.Ms.Sms.Entities.Request;
using Kp.Ms.Sms.Entities.Response;
using System.Text;

namespace Kp.Ms.Sms.Providers;

public class MegafonProvider : Provider
{
    public MegafonProvider(
        IHttpClientWrapper client,
        ProviderSettings settings) : base(client, settings)
    {
        var bytes = Encoding.UTF8.GetBytes($"{settings.Username}:{settings.Password}");
        var authString = Convert.ToBase64String(bytes);
        DefaultHeaders = new Dictionary<string, string>()
        {
            { "Authorization", $"Basic {authString}" }
        };
    }

    public override async Task<SmsResponse> SendSmsAsync(string phone, string message)
    {
        try { 
            var parameters = new MegafonRequest()
            {
                From = Settings.FromNumber,
                To = Convert.ToInt64(phone),
                Message = message
            };

            var response = await Client.PostAsync<MegafonRequest, MegafonResponse>($"{Settings.Url}/sms/v1/sms", parameters,
                headers: DefaultHeaders);

            return new SmsResponse()
            {
                ProviderName = ProviderNames.Megafon.ToString(),
                Status = SmsRuResponseStatus.OK.ToString(),
                StatusCode = response?.Result?.Status?.Code,
                StatusText = response?.Result?.Status?.Description
            };
        }
        catch (Exception ex)
        {
            return new SmsResponse()
            {
                Status = SmsRuResponseStatus.ERROR.ToString(),
                StatusText = ex.Message
            };
        }
    }

    public override async Task<CallResponse> CallApiAsync(string phone, string? userIp)
    {
        var ip = string.IsNullOrEmpty(userIp) ? DefaultUserIp : userIp;

        var apiId = Settings.Password;
        var url = $"{Settings.Url}/code/call?phone={phone}&ip={ip}&api_id={apiId}";

        var response = await Client.GetAsync<CallResponse>(url);
        return response;
    }
}