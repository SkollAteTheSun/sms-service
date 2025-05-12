using Common.HttpClientWrapper;
using Kp.Ms.Sms.Config;
using Kp.Ms.Sms.Entities.Enums;
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
            var parameters = new Dictionary<string, object>
            {
                { "from", Settings.FromNumber },
                { "to", Convert.ToInt32(phone) },
                { "message", message }
            };

            var response = await Client.PostAsync<object, SmsResponse>($"{Settings.Url}/sms/v1/sms", parameters,
                headers: DefaultHeaders);
            response.ProviderName = ProviderNames.Megafon.ToString();
            return response;
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