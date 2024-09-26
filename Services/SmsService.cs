using Kp.Ms.Sms.Entities.Entity;
using Kp.Ms.Sms.Entities.Request;
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
    private const int MaxQueueSize = 1000;
    private bool _isUrlAvailable = true;
    private readonly HttpClient _httpClient;
    private System.Timers.Timer _queueTimer;
    private IConfiguration _configuration;

    private readonly OpenSearchClient _openSearchClient;
    private readonly TimeZoneInfo _utc3TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");

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
            var dateTime = TimeZoneInfo.ConvertTime(DateTime.UtcNow, _utc3TimeZone);
            if (response.Status == "OK")
            {
                if (smsRequest.CallbackUrl != null)
                {
                    await SendCallback(smsRequest.CallbackUrl, smsRequest.Phone, "success", smsRequest.MessId);
                }
                await LogSmsToOpenSearch(dateTime, response.Status, _activeProvider, smsRequest.MessId);
                return "success";
            }
            else
            {
                if (smsRequest.CallbackUrl != null)
                {
                    await SendCallback(smsRequest.CallbackUrl, smsRequest.Phone, response.Status, smsRequest.MessId, response.StatusText);
                }
                await LogSmsToOpenSearch(dateTime, response.Status, _activeProvider, smsRequest.MessId, response.StatusText);
                return response.StatusText;
            }
        }

        if (_smsQueue.Count >= MaxQueueSize)
        {
            return "500: Queue limit reached";
        }

        _smsQueue.Enqueue(smsRequest);
        await SendCallback(smsRequest.CallbackUrl, smsRequest.Phone, "queued", "No route to host");
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

    private async void SendFromQueue()
    {
        if (_smsQueue.IsEmpty || !_isUrlAvailable) return;

        var provider = _smsProviderFactory.GetProvider(_activeProvider);
        for (int i = 0; i < 10 && _smsQueue.TryDequeue(out SmsRequest smsRequest); i++)
        {
            var response = await provider.SendSmsAsync(smsRequest.Phone, smsRequest.Message);
            var status = response.Status == "OK" ? "success" : "failure";
            await SendCallback(smsRequest.CallbackUrl, smsRequest.Phone, status, smsRequest.MessId);
            var dateTime = TimeZoneInfo.ConvertTime(DateTime.UtcNow, _utc3TimeZone);
            await LogSmsToOpenSearch(dateTime, status, _activeProvider, response.StatusText);
        }
    }

    public int GetQueueStatus()
    {
        return _smsQueue.Count;
    }

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

    private async Task LogSmsToOpenSearch(DateTime timestamp, string status, string providerCode, string messId, string errorMessage = null)
    {
        SmsLog smsLog = new SmsLog
        {
            MessId = messId, 
            Date = TimeZoneInfo.ConvertTime(timestamp, _utc3TimeZone),
            Status = status,
            Provider = providerCode,
            ErrorMessage = errorMessage
        };

        var indexName = $"sms-logs-{DateTime.UtcNow:yyyy-MM-dd}";

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

    private bool ValidPhoneNumber(string phoneNumber)
    {
        if (phoneNumber.Length == 11 && (phoneNumber.StartsWith("7") || phoneNumber.StartsWith("8")))
            return true;
        else
            return false;
    }

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
}