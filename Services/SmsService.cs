using Kp.Ms.Sms.Entities.Entity;
using Kp.Ms.Sms.Entities.Request;
using Kp.Ms.Sms.Extensions;
using Kp.Ms.Sms.Factories;
using Newtonsoft.Json;
using OpenSearch.Client;
using System.Collections.Concurrent;
using System.Text;

namespace Kp.Ms.Sms.Services;

public class SmsService
{
    private readonly SmsProviderFactory _smsProviderFactory;
    private static string _activeProvider;
    private readonly ConcurrentQueue<SmsRequest> _smsQueue;
    private readonly ConcurrentQueue<SmsCallbackRequest> _smsCallbackQueue;
    private readonly int _maxQueueSize;
    private readonly int _smsBatchSize;
    private readonly int _smsQueueIntervalMs;
    private readonly int _smsBatchIntervalMs;
    private readonly HttpClient _httpClient;
    private System.Timers.Timer _queueTimer;
    private IConfiguration _configuration;

    private readonly OpenSearchClient _openSearchClient;

    public SmsService(SmsProviderFactory smsProviderFactory, IConfiguration configuration, HttpClient httpClient, OpenSearchClient openSearchClient)
    {
        _configuration = configuration;
        _smsProviderFactory = smsProviderFactory;
        _activeProvider = configuration["ActiveSmsProvider"] ?? "smsru"; 
        _maxQueueSize = _configuration.GetValue<int?>("QueueSettings:SmsMaxSize") ?? throw new ArgumentNullException("QueueSettings:SmsMaxSize");
        _smsBatchSize = _configuration.GetValue<int?>("QueueSettings:SmsBatchSize") ?? throw new ArgumentNullException("QueueSettings:SmsBatchSize");
        _smsQueueIntervalMs = _configuration.GetValue<int?>("QueueSettings:SmsQueueIntervalMs") ?? throw new ArgumentNullException("QueueSettings:SmsQueueIntervalMs");
        _smsBatchIntervalMs = _configuration.GetValue<int?>("QueueSettings:SmsBatchIntervalMs") ?? throw new ArgumentNullException("QueueSettings:SmsBatchIntervalMs");

        _smsQueue = new ConcurrentQueue<SmsRequest>();
        _smsCallbackQueue = new ConcurrentQueue<SmsCallbackRequest>();

        _httpClient = httpClient;
        _openSearchClient = openSearchClient;

        _queueTimer = new System.Timers.Timer(_smsQueueIntervalMs);
        _queueTimer.Elapsed += (sender, e) => SendFromQueue(); // смс
        _queueTimer.Elapsed += (sender, e) => SendFromCallbackQueue(); // callback-и
        _queueTimer.Start();
    }

    public async Task<string> SendSmsAsync(SmsRequest smsRequest)
    {
        var provider = _smsProviderFactory.GetProvider(_activeProvider);
        smsRequest.MessId = GenerateMessageId();
        smsRequest.Phone = CleanPhoneNumber(smsRequest.Phone);

        if (!ValidPhoneNumber(smsRequest.Phone)) return "Error: Invalid phone nubmer";

        if (!string.IsNullOrEmpty(smsRequest.CallbackUrl) && !ValidUrl(smsRequest.CallbackUrl)) return "Error: Invalid callback URL";


        var response = await provider.SendSmsAsync(smsRequest.Phone, smsRequest.Message);
        var dateTime = DateTime.UtcNow;

        // Отправка смс прошла успешна
        if (response.Status == "OK")
        {
            if (smsRequest.CallbackUrl != null)
            {
                 await SendCallback(smsRequest.CallbackUrl, smsRequest.Phone, "success", smsRequest.MessId);
            }
            await LogSmsToOpenSearch(dateTime, smsRequest.Phone, smsRequest.Message, response.Status, _activeProvider, smsRequest.MessId);
            return "success";
        }

        // 220 Сервис временно недоступен, попробуйте чуть позже
        // 500 Ошибка на сервере. Повторите запрос
        if (response.StatusCode == 220 || response.StatusCode == 500)
        {
            // Если очередь переплнена, возвращаем ошибку
            if (_smsQueue.Count >= _maxQueueSize)
            {
                return "500: Queue limit reached";
            }

            // Есть место в очереди - добавляем в очередеь
            _smsQueue.Enqueue(smsRequest);

            if (smsRequest.CallbackUrl != null)
            {
                await SendCallback(smsRequest.CallbackUrl, smsRequest.Phone, "success", smsRequest.MessId);
            }
            await LogSmsToOpenSearch(dateTime, smsRequest.Phone, smsRequest.Message, "queued", _activeProvider, smsRequest.MessId, "No route to host");
            return "queued";
        }

        // Непредвиденные ошибки
        await LogSmsToOpenSearch(dateTime, smsRequest.Phone, smsRequest.Message, "error", _activeProvider, smsRequest.MessId, response.Status);
        return response.Status;
    }

    private async Task<(bool Success, string? StatusText)> SendCallback(string callbackUrl, string phone, string status, string messId, string reason = null)
    {
        if (string.IsNullOrEmpty(callbackUrl))
        {
            return (false, "Callback URL is null or empty");
        }

        var callbackData = new SmsCallbackRequest
        {
            CallbackUrl = callbackUrl,
            Phone = phone,
            Status = status,
            MessId = messId,
            Reason = reason
        };

        var jsonContent = new StringContent(JsonConvert.SerializeObject(callbackData), Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(callbackUrl, jsonContent);
            response.EnsureSuccessStatusCode();
            return (true, "Callback successfully sent");
        }
        catch (Exception ex)
        {
            if (_smsCallbackQueue.Count >= _maxQueueSize)
            {
                return (false, "Callback queue limit reached");
            }
            _smsCallbackQueue.Enqueue(callbackData);
            return (false, $"Callback failed and added to queue: {ex.Message}");
        }
    }

    private async void SendFromQueue()
    {
        var provider = _smsProviderFactory.GetProvider(_activeProvider);

        var batch = new List<SmsRequest>();
        for (int i = 0; i < _smsBatchSize && _smsQueue.TryDequeue(out SmsRequest smsRequest); i++)
        {
            batch.Add(smsRequest);
        }

        if (batch.Count == 0)
        {
            return;
        }

        foreach (var smsRequest in batch)
        {
            var response = await provider.SendSmsAsync(smsRequest.Phone, smsRequest.Message);
            var status = response.Status == "OK" ? "success" : "failure";

            if (response.Status == "OK" && smsRequest.CallbackUrl != null)
            {
                await SendCallback(smsRequest.CallbackUrl, smsRequest.Phone, status, smsRequest.MessId);
            }

            var dateTime = DateTime.UtcNow;
            await LogSmsToOpenSearch(dateTime, smsRequest.Phone, smsRequest.Message, status, _activeProvider, smsRequest.MessId, response.StatusText);
            await Task.Delay(_smsBatchIntervalMs);
        }
    }

    private async void SendFromCallbackQueue()
    {
        var batch = new List<SmsCallbackRequest>();

        for (int i = 0; i < _smsBatchSize && _smsCallbackQueue.TryDequeue(out SmsCallbackRequest callbackRequest); i++)
        {
            batch.Add(callbackRequest);
        }

        if (batch.Count == 0)
        {
            return;
        }

        foreach (var callbackRequest in batch)
        {
            var (success, statusText) = await SendCallback(callbackRequest.CallbackUrl, callbackRequest.Phone, callbackRequest.Status, callbackRequest.MessId, callbackRequest.Reason);
            await Task.Delay(_smsBatchIntervalMs);
        }
    }

    public int GetQueueStatus() => _smsQueue.IsEmpty ? 0 : _smsQueue.Count;
    public int GetCallbackQueueStatus() => _smsCallbackQueue.IsEmpty ? 0 : _smsCallbackQueue.Count;


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
}