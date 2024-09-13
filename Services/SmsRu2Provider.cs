using Kp.Ms.Sms.Entities.Response;
using Kp.Ms.Sms.Interfaces;
using Newtonsoft.Json;

namespace Kp.Ms.Sms.Services;

public class SmsRu2Provider : ISmsProvider
{
    private readonly IConfiguration _configuration;
    private readonly HttpClient _client;

    public SmsRu2Provider(IConfiguration configuration, HttpClient client)
    {
        _configuration = configuration;
        _client = client; // BaseAddress уже установлен через AddHttpClient
    }

    public async Task<SmsRuResponse> SendSmsAsync(string phone, string message)
    {
        var parameters = new Dictionary<string, string>
            {
                { "from", _configuration["SmsRu2:From"] ?? throw new InvalidOperationException("SmsRu2 from not set") },
                { "api_id", _configuration["SmsRu2:ApiId"] ?? throw new InvalidOperationException("SmsRu2 api id not set") },
                { "json", "1" },
                { "to", phone },
                { "msg", message }
            };

        var encodedContent = new FormUrlEncodedContent(parameters);
        var request = new HttpRequestMessage(HttpMethod.Post, "/sms/send")
        {
            Content = encodedContent
        };

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var jsonString = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<SmsRuResponse>(jsonString) ?? throw new Exception("No sms ru2 response");
    }
}