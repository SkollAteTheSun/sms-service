using Newtonsoft.Json;
using Kp.Ms.Sms.Entities.Response;
using Kp.Ms.Sms.Interfaces;

namespace Kp.Ms.Sms.Services;

public class SmsProviderBase : IProvider
{
    protected readonly IConfiguration _configuration;
    protected readonly HttpClient _client;
    protected readonly string _apiIdKey;
    protected readonly string _fromKey;
    protected readonly string _urlKey;

    protected SmsProviderBase(IConfiguration configuration, HttpClient client, string apiIdKey, string fromKey, string urlKey)
    {
        _configuration = configuration;
        _client = client;
        _apiIdKey = apiIdKey;
        _fromKey = fromKey;
        _urlKey = urlKey;
    }

    public async Task<SmsResponse> SendSmsAsync(string phone, string message)
    {
        var parameters = new Dictionary<string, string>
            {
                { "from", _configuration[_fromKey] ?? throw new InvalidOperationException($"{_fromKey} not set") },
                { "api_id", _configuration[_apiIdKey] ?? throw new InvalidOperationException($"{_apiIdKey} not set") },
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
        return JsonConvert.DeserializeObject<SmsResponse>(jsonString) ?? throw new Exception("No sms response");
    }

    public async Task<CallResponse> CallApiAsync(string phone, string userIp)
    {
        var apiId = _configuration[_apiIdKey];
        var url = $"{_configuration[_urlKey]}/code/call?phone={phone}&ip={userIp}&api_id={apiId}";

        var response = await _client.GetAsync(url);
        var data = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<CallResponse>(data);
    }
}