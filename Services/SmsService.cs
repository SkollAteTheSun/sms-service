using Kp.Ms.Sms.Entities.Entity;
using Kp.Ms.Sms.Entities.Request;
using Kp.Ms.Sms.Entities.Response;
using Kp.Ms.Sms.Extensions;
using Kp.Ms.Sms.Factories;
using Newtonsoft.Json;
using OpenSearch.Client;
using System;
using System.Collections.Concurrent;
using System.Text;

namespace Kp.Ms.Sms.Services;

public class SmsService
{
    private readonly SmsProviderFactory _smsProviderFactory;
    private static string _activeProvider;
    private readonly ConcurrentQueue<SmsRequest> _smsQueue;
    private const int MaxQueueSize = 500; // в 2 раза меньше чем смс
    private bool _isUrlAvailable = true;
    private readonly HttpClient _httpClient;
    private System.Timers.Timer _queueTimer;
    private IConfiguration _configuration;

    private readonly OpenSearchClient _openSearchClient;

    public SmsService(SmsProviderFactory smsProviderFactory, IConfiguration configuration, HttpClient httpClient, OpenSearchClient openSearchClient)
    {
        _configuration = configuration;
        _smsProviderFactory = smsProviderFactory;
        _activeProvider = configuration["ActiveSmsProvider"] ?? "smsru";
        _smsQueue = new ConcurrentQueue<SmsRequest>();
        _httpClient = httpClient;
        _openSearchClient = openSearchClient;
        _queueTimer = new System.Timers.Timer(60000);
        _queueTimer.Elapsed += (sender, e) => SendFromQueue();
        _queueTimer.Start();
    }

    public async Task<string> SendSmsAsync(SmsRequest smsRequest)
    {
        var provider = _smsProviderFactory.GetProvider(_activeProvider);
        smsRequest.MessId = GenerateMessageId();
        smsRequest.Phone = CleanPhoneNumber(smsRequest.Phone);

        if (!ValidPhoneNumber(smsRequest.Phone)) return "Error: Invalid phone nubmer";

        if (!string.IsNullOrEmpty(smsRequest.CallbackUrl) && !ValidUrl(smsRequest.CallbackUrl)) return "Error: Invalid callback URL";

        if (_isUrlAvailable)
        {
            var response = await provider.SendSmsAsync(smsRequest.Phone, smsRequest.Message);
            var dateTime = DateTime.UtcNow;
            if (response.Status == "OK")
            {
                if (smsRequest.CallbackUrl != null)
                {
                    await SendCallback(smsRequest.CallbackUrl, smsRequest.Phone, "success", smsRequest.MessId);
                }
                await LogSmsToOpenSearch(dateTime, smsRequest.Phone, smsRequest.Message, response.Status, _activeProvider, smsRequest.MessId);
                return "success";
            }
            else
            {
                if (smsRequest.CallbackUrl != null)
                {
                    await SendCallback(smsRequest.CallbackUrl, smsRequest.Phone, response.Status, smsRequest.MessId, response.StatusText);
                }
                await LogSmsToOpenSearch(dateTime, smsRequest.Phone, smsRequest.Message, response.Status, _activeProvider, smsRequest.MessId, response.StatusText);
                return response.StatusText;
            }
        }

        if (_smsQueue.Count >= MaxQueueSize)
        {
            return "500: Queue limit reached";
        }

        _smsQueue.Enqueue(smsRequest);
        await SendCallback(smsRequest.CallbackUrl, smsRequest.Phone, "queued", smsRequest.MessId, "No route to host");
        return "queued";
    }

    private async Task SendCallback(string callbackUrl, string phone, string status, string messId, string reason = null)
    {
        if (string.IsNullOrEmpty(callbackUrl)) return;

        var callbackData = new
        {
            phone,
            messId,
            status,
            reason
        };

        var jsonContent = new StringContent(JsonConvert.SerializeObject(callbackData), Encoding.UTF8, "application/json");
        try
        {
            var response = await _httpClient.PostAsync(callbackUrl, jsonContent);
            response.EnsureSuccessStatusCode();
        }
        catch
        {
            _isUrlAvailable = false;
        }
    }

    private async void SendFromQueue() //по 5 штук
    {
        if (_smsQueue.IsEmpty || !_isUrlAvailable) return;

        var provider = _smsProviderFactory.GetProvider(_activeProvider);
        for (int i = 0; i < 5 && _smsQueue.TryDequeue(out SmsRequest smsRequest); i++)
        {
            var response = await provider.SendSmsAsync(smsRequest.Phone, smsRequest.Message);
            var status = response.Status == "OK" ? "success" : "failure";
            await SendCallback(smsRequest.CallbackUrl, smsRequest.Phone, status, smsRequest.MessId);
            var dateTime = DateTime.UtcNow;
            await LogSmsToOpenSearch(dateTime, smsRequest.Phone, smsRequest.Message, status, _activeProvider, smsRequest.MessId, response.StatusText);
        }
    }

    public int GetQueueStatus() => _smsQueue.IsEmpty ? 0 : _smsQueue.Count;

    public bool SwitchProvider(string methodCode)
    {
        var allowedProviders = new[] { "smsru", "smsru2" };
        if (!allowedProviders.Contains(methodCode.ToLower()))
            return false;

        _activeProvider = methodCode.ToLower();
        return true;
    }

    public string GetActiveProvider()
    {
        return _activeProvider;
    }

    private async Task LogSmsToOpenSearch(DateTime timestamp, string phone, string textMessage, string status, string providerCode, string messId, string errorMessage = null)
    {
        SmsLog smsLog = new SmsLog
        {
            MessId = messId, 
            Phone = phone,
            TextMessage = textMessage,
            Date = timestamp,
            Status = status,
            Provider = providerCode,
            ErrorMessage = errorMessage
        };

        var response = await _openSearchClient.IndexAsync(smsLog, idx => idx.Index(_configuration.GetSmsStorageName()));

        if (!response.IsValid)
        {
            Console.WriteLine("Error logging SMS to OpenSearch: " + response.ServerError.Error.Reason);
        }
    }

    public static string GenerateMessageId()
    {
        DateTime now = DateTime.UtcNow;
        // Получаем таймстамп в миллисекундах 

        long timestamp = (long)(now - new DateTime(1970, 1, 1)).TotalMilliseconds;

        // Получаем миллисекунды текущего времени
        int milliseconds = now.Millisecond;

        // Форматируем идентификатор с учетом 5 знаков миллисекунд
        string messageId = $"{timestamp:D13}{milliseconds:D3}";

        return messageId;
    }

    private string CleanPhoneNumber(string phoneNumber)
    {
        var cleanedNumber = new StringBuilder();

        foreach (char c in phoneNumber)
        {
            if (char.IsDigit(c))
            {
                cleanedNumber.Append(c);
            }
        }
        return cleanedNumber.ToString();
    }

    private bool ValidPhoneNumber(string phoneNumber) => phoneNumber.Length == 11 && (phoneNumber.StartsWith("7") || phoneNumber.StartsWith("8"));

    private bool ValidUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return false;

        if (Uri.TryCreate(url, UriKind.Absolute, out var uriResult) &&
            (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
        {
            return true;
        }
        return false;
    }

    // пока чисто копия send sms, только для звонка
    public async Task<string> CallUserAsync(string phone, string userIp, string callbackUrl = null)
    {
        var apiId = _configuration["SmsRu:ApiId"];
        var requestUrl = $"{_configuration["SmsRu:Url"]}/code/call?phone={phone}&ip={userIp}&api_id={apiId}";

        try
        {
            var response = await _httpClient.GetAsync(requestUrl);
            var responseData = await response.Content.ReadAsStringAsync();
            var callResponse = JsonConvert.DeserializeObject<CallResponse>(responseData);

            if (callResponse.Status == "OK")
            {
                if (callbackUrl != null)
                    await SendCallback(callbackUrl, phone, "success", callResponse.CallId);

                return "success";
            }
            else
            {
                // Обработка ошибки и отправка callback
                if (callbackUrl != null)
                    await SendCallback(callbackUrl, phone, "failure", null, callResponse.StatusText);

                // Добавление в очередь, если вызов не удался
                if (_smsQueue.Count < MaxQueueSize)
                    _smsQueue.Enqueue(new SmsRequest { Phone = phone, CallbackUrl = callbackUrl });
                return "queued";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error calling user: {ex.Message}");
            return "failure";
        }
    }
}