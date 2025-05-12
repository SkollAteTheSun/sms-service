using Common.HttpClientWrapper;
using Kp.Ms.Sms.Config;
using Kp.Ms.Sms.Entities.Enums;
using Kp.Ms.Sms.Entities.Request;
using Kp.Ms.Sms.Entities.Response;
using System.Net.Mime;

namespace Kp.Ms.Sms.Providers;

public class SmsRuProvider : Provider
{
    public SmsRuProvider(
        IHttpClientWrapper client,
        ProviderSettings settings) : base(client, settings)
    {
    }

    public override async Task<SmsResponse> SendSmsAsync(string phone, string message)
    {
        try
        {
            var parameters = new SmsRuRequest()
            {
                From = Settings.FromNumber,
                ApiId = Settings.Password,
                Json = "1",
                To = phone,
                Message = message
            };

            var response = await Client.PostAsync<SmsRuRequest, SmsResponse>($"{Settings.Url}/sms/send", parameters, MediaTypeNames.Application.FormUrlEncoded);
            response.ProviderName = ProviderNames.SmsRu.ToString();
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