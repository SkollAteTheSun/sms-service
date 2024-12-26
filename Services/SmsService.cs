using Kp.Ms.Sms.Entities.Entity;
using Kp.Ms.Sms.Entities.Request;
using Kp.Ms.Sms.Entities.Response;
using Kp.Ms.Sms.Extensions;
using Kp.Ms.Sms.Factories;
using Newtonsoft.Json;
using OpenSearch.Client;
using System.Collections.Concurrent;
using System.Text;

namespace Kp.Ms.Sms.Services;

public class SmsService
{
    private readonly ConcurrentQueue<SmsRequest> _smsQueue;
    private readonly ConcurrentQueue<(string CallbackUrl, object CallbackData, int Attempt)> _smsCallbackQueue;
    private readonly int _maxQueueSize;
    private readonly int _smsBatchSize;
    private readonly int _smsQueueIntervalMs;
    private readonly int _smsBatchIntervalMs;
    private readonly int _maxCallbackQueueSize;
    private readonly int _callbackBatchSize;
    private readonly int _callbackQueueIntervalMs;
    private readonly int _callbackBatchIntervalMs;
    private readonly int _maxCallbackAttempts;
    private System.Timers.Timer _queueTimer;
    private System.Timers.Timer _queueCallbackTimer;

    private readonly HttpClient _httpClient;
    private readonly OpenSearchClient _openSearchClient;
    private IConfiguration _configuration;

    private readonly ProviderFactory _providerFactory;
    private static string _activeProvider;


    public SmsService(ProviderFactory providerFactory, IConfiguration configuration, HttpClient httpClient, OpenSearchClient openSearchClient)
    {
        _configuration = configuration;
        _httpClient = httpClient;
        _openSearchClient = openSearchClient;
        _providerFactory = providerFactory;
        _activeProvider = configuration["ActiveSmsProvider"] ?? "smsru"; 

        _maxQueueSize = _configuration.GetValue<int?>("QueueSettings:SmsMaxSize") ?? throw new ArgumentNullException("QueueSettings:SmsMaxSize");
        _smsBatchSize = _configuration.GetValue<int?>("QueueSettings:SmsBatchSize") ?? throw new ArgumentNullException("QueueSettings:SmsBatchSize");
        _smsQueueIntervalMs = _configuration.GetValue<int?>("QueueSettings:SmsQueueIntervalMs") ?? throw new ArgumentNullException("QueueSettings:SmsQueueIntervalMs");
        _smsBatchIntervalMs = _configuration.GetValue<int?>("QueueSettings:SmsBatchIntervalMs") ?? throw new ArgumentNullException("QueueSettings:SmsBatchIntervalMs");

        _smsQueue = new ConcurrentQueue<SmsRequest>();

        _maxCallbackQueueSize = _configuration.GetValue<int?>("QueueSettings:SmsCallbackMaxSize") ?? throw new ArgumentNullException("QueueSettings:SmsCallbackMaxSize");
        _callbackBatchSize = _configuration.GetValue<int?>("QueueSettings:SmsCallbackBatchSize") ?? throw new ArgumentNullException("QueueSettings:SmsCallbackBatchSize");
        _callbackQueueIntervalMs = _configuration.GetValue<int?>("QueueSettings:SmsCallbackQueueIntervalMs") ?? throw new ArgumentNullException("QueueSettings:SmsCallbackQueueIntervalMs");
        _callbackBatchIntervalMs = _configuration.GetValue<int?>("QueueSettings:SmsCallbackBatchIntervalMs") ?? throw new ArgumentNullException("QueueSettings:SmsCallbackBatchIntervalMs");
        _maxCallbackAttempts = _configuration.GetValue<int?>("QueueSettings:MaxSmsAttempts") ?? throw new ArgumentNullException("QueueSettings:MaxSmsAttempts");

        _smsCallbackQueue = new ConcurrentQueue<(string CallbackUrl, object CallbackData, int Attempt)>();

        _queueTimer = new System.Timers.Timer(_smsQueueIntervalMs);
        _queueTimer.Elapsed += (sender, e) => ProcessQueue();
        _queueTimer.Start();

        _queueCallbackTimer = new System.Timers.Timer(_callbackQueueIntervalMs);
        _queueCallbackTimer.Elapsed += (sender, e) => ProcessCallbackQueue();
        _queueCallbackTimer.Start();
    }

    public async Task<string> SendSmsAsync(SmsRequest request)
    {
        var provider = _providerFactory.GetProvider(_activeProvider);
        request.MessId = GenerateMessageId();
        request.Phone = CleanPhoneNumber(request.Phone);

        if (!ValidPhoneNumber(request.Phone)) return "Error: Invalid phone nubmer";

        if (!string.IsNullOrEmpty(request.CallbackUrl) && !ValidUrl(request.CallbackUrl)) return "Error: Invalid callback URL";

        var response = await provider.SendSmsAsync(request.Phone, request.Message);

        // Отправка смс прошла успешна
        if (response.Status == "OK")
        {
            await EnqueueCallback(request.CallbackUrl, new
            {
                phone = request.Phone,
                message = request.Message,
                messId = request.MessId,
                status = response.Status,
                errorMessage = response.StatusText
            });

            await LogSmsToOpenSearch(request.Phone, request.Message, response.Status, _activeProvider, request.MessId, null);
            return "success";
        }

        // 220 Сервис временно недоступен, попробуйте чуть позже
        // 500 Ошибка на сервере. Повторите запрос
        if (response.StatusCode == 220 || response.StatusCode == 500)
        {
            // Если очередь переплнена, возвращаем ошибку
            if (!EnqueueSms(request))
            {
                await LogSmsToOpenSearch(request.Phone, request.Message, "failure", _activeProvider, request.MessId, "500: Queue limit reached");

                return "500: Queue limit reached";
            }

            // Есть место в очереди - добавляем в очередеь

            await EnqueueCallback(request.CallbackUrl, new
            {
                phone = request.Phone,
                message = request.Message,
                messId = request.MessId,
                status = response.Status,
                errorMessage = response.StatusText
            });

            await LogSmsToOpenSearch(request.Phone, request.Message, "queued", _activeProvider, request.MessId, "No route to host");
            return "queued";
        }

        // Непредвиденные ошибки
        await LogSmsToOpenSearch(request.Phone, request.Message, "error", _activeProvider, request.MessId, response.StatusText);
        return response.Status;
    }

    private async void ProcessQueue()
    {
        var provider = _providerFactory.GetProvider(_activeProvider);
        var batch = new List<SmsRequest>();
        for (int i = 0; i < _smsBatchSize && _smsQueue.TryDequeue(out SmsRequest request); i++)
        {
            batch.Add(request);
        }

        if (batch.Count == 0)
        {
            return;
        }

        foreach (var request in batch)
        {
            var response = await provider.SendSmsAsync(request.Phone, request.Message);

            //var status = response.Status == "OK" ? "success" : "failure";
            if (response.Status == "OK")
            {

                await EnqueueCallback(request.CallbackUrl, new
                {
                    phone = request.Phone,
                    message = request.Message,
                    messId = request.MessId,
                    status = response.Status,
                    errorMessage = response.StatusText
                });

                await LogSmsToOpenSearch(request.Phone, request.Message, response.Status, _activeProvider, request.MessId, "Successful sending of call from queue");
            }
            else
            {
                if (response.StatusCode == 500 || response.StatusCode == 220)
                {
                    if (!EnqueueSms(request))
                    {
                        await LogSmsToOpenSearch(request.Phone, request.Message, response.Status, _activeProvider, request.MessId, "500: Queue limit reached");
                    }
                }
                else
                {
                    await LogSmsToOpenSearch(request.Phone, request.Message, "failure", _activeProvider, request.MessId, response.StatusText);
                }
            }

            await Task.Delay(_smsBatchIntervalMs);
        }
    }

    private bool EnqueueSms(SmsRequest request)
    {
        if (_smsQueue.Count >= _maxQueueSize)
        {
            return false;
        }
        _smsQueue.Enqueue(request);
        return true;
    }

    private async Task ProcessCallbackQueue()
    {
        var batch = new List<(string CallbackUrl, object CallbackData, int Attempt)>();

        for (int i = 0; i < _smsBatchSize && _smsCallbackQueue.TryDequeue(out var callbackItem); i++)
        {
            batch.Add(callbackItem);
        }

        if (batch.Count == 0)
        {
            return;
        }

        var tasks = batch.Select(async item =>
        {
            var (callbackUrl, callbackData, attempt) = item;

            if (attempt >= _maxCallbackAttempts)
            {
                await LogSmsToOpenSearch(null, null, "failure", _activeProvider, null, $"Callback to url: {callbackUrl} failed after {attempt} attempts. Removing from queue.");
                return;
            }

            var (success, statusText) = await SendCallback(callbackUrl, callbackData, attempt);

            if (!success)
            {
                if (_smsCallbackQueue.Count >= _maxCallbackQueueSize)
                {
                    await LogSmsToOpenSearch(null, null, "failure", _activeProvider, null, $"The callback queue is full! Callback queue size: {_smsCallbackQueue.Count}, callback url: {callbackUrl}");
                    return;
                }
            }
            else
            {
                await LogSmsToOpenSearch(null, null, "success", _activeProvider, null, $"Sending callback from queue to {callbackUrl} was successful.");
            }

            await Task.Delay(_smsBatchIntervalMs);
        });

        await Task.WhenAll(tasks);
    }

    private async Task EnqueueCallback(string callbackUrl, object callbackData)
    {
        if (string.IsNullOrEmpty(callbackUrl))
            return;

        if (_smsCallbackQueue.Count >= _maxCallbackQueueSize)
        {
            await LogSmsToOpenSearch(null, null, "failure", _activeProvider, null, $"The callback queue is full! Callback queue size: {_smsCallbackQueue.Count}, callback url: {callbackUrl}");
            return;
        }
        _smsCallbackQueue.Enqueue((callbackUrl, callbackData, 1));
    }

    private async Task<(bool Success, string? StatusText)> SendCallback(string callbackUrl, object callbackData, int attempt)
    {
        var jsonContent = new StringContent(JsonConvert.SerializeObject(callbackData), Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(callbackUrl, jsonContent);
            response.EnsureSuccessStatusCode();
            return (true, null);
        }
        catch (Exception ex)
        {
            if (_smsCallbackQueue.Count >= _maxCallbackQueueSize)
            {
                return (false, "Callback queue limit reached");
            }
            _smsCallbackQueue.Enqueue((callbackUrl, callbackData, attempt + 1));
            return (false, $"Callback failed and added to queue: {ex.Message}");
        }
    }

    private async Task LogSmsToOpenSearch(string? phone, string? textMessage, string status, string providerCode, string? messId, string? errorMessage)
    {
        SmsLog smsLog = new SmsLog
        {
            MessId = messId,
            Phone = phone,
            TextMessage = textMessage,
            Date = DateTime.UtcNow,
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
}