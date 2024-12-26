using Kp.Ms.Sms.Entities.Request;
using Kp.Ms.Sms.Entities.Response;
using Kp.Ms.Sms.Entities.Entity;
using Newtonsoft.Json;
using OpenSearch.Client;
using System.Collections.Concurrent;
using System.Text;
using Kp.Ms.Sms.Extensions;
using Kp.Ms.Sms.Factories;

namespace Kp.Ms.Sms.Services;

public class CallService
{
    private readonly ConcurrentQueue<CallRequest> _callQueue;
    private readonly ConcurrentQueue<(string CallbackUrl, object CallbackData, int Attempt)> _callbackQueue;
    private readonly int _maxQueueSize;
    private readonly int _callBatchSize;
    private readonly int _callQueueIntervalMs;
    private readonly int _callBatchIntervalMs;
    private readonly int _maxCallbackQueueSize;
    private readonly int _callbackBatchSize;
    private readonly int _callbackQueueIntervalMs;
    private readonly int _callbackBatchIntervalMs;
    private readonly int _maxCallbackAttempts;
    private readonly System.Timers.Timer _queueTimer;
    private readonly System.Timers.Timer _queueCallbackTimer;

    private readonly HttpClient _httpClient;
    private readonly OpenSearchClient _openSearchClient;
    private readonly IConfiguration _configuration;

    private readonly ProviderFactory _providerFactory;
    private static string _activeProvider;

    public CallService(ProviderFactory providerFactory, IConfiguration configuration, HttpClient httpClient, OpenSearchClient openSearchClient)
    {
        _configuration = configuration;
        _httpClient = httpClient;
        _openSearchClient = openSearchClient;
        _providerFactory = providerFactory;
        _activeProvider = configuration["ActiveSmsProvider"] ?? "smsru";

        _maxQueueSize = _configuration.GetValue<int?>("QueueSettings:CallMaxSize") ?? throw new ArgumentNullException("QueueSettings:CallMaxSize");
        _callBatchSize = _configuration.GetValue<int?>("QueueSettings:CallBatchSize") ?? throw new ArgumentNullException("QueueSettings:CallBatchSize");
        _callQueueIntervalMs = _configuration.GetValue<int?>("QueueSettings:CallQueueIntervalMs") ?? throw new ArgumentNullException("QueueSettings:CallQueueIntervalMs");
        _callBatchIntervalMs = _configuration.GetValue<int?>("QueueSettings:CallBatchIntervalMs") ?? throw new ArgumentNullException("QueueSettings:CallBatchIntervalMs");

        _callQueue = new ConcurrentQueue<CallRequest>();

        _maxCallbackQueueSize = _configuration.GetValue<int?>("QueueSettings:CallCallbackMaxSize") ?? throw new ArgumentNullException("QueueSettings:CallCallbackMaxSize");
        _callbackBatchSize = _configuration.GetValue<int?>("QueueSettings:CallCallbackBatchSize") ?? throw new ArgumentNullException("QueueSettings:CallCallbackBatchSize");
        _callbackQueueIntervalMs = _configuration.GetValue<int?>("QueueSettings:CallCallbackQueueIntervalMs") ?? throw new ArgumentNullException("QueueSettings:CallCallbackQueueIntervalMs");
        _callbackBatchIntervalMs = _configuration.GetValue<int?>("QueueSettings:CallCallbackBatchIntervalMs") ?? throw new ArgumentNullException("QueueSettings:CallCallbackBatchIntervalMs");
        _maxCallbackAttempts = _configuration.GetValue<int?>("QueueSettings:MaxCallAttempts") ?? throw new ArgumentNullException("QueueSettings:MaxCallAttempts");

        _callbackQueue = new ConcurrentQueue<(string CallbackUrl, object CallbackData, int Attempt)>();

        _queueTimer = new System.Timers.Timer(_callQueueIntervalMs);
        _queueTimer.Elapsed += (sender, e) => ProcessQueue();
        _queueTimer.Start();

        _queueCallbackTimer = new System.Timers.Timer(_callbackQueueIntervalMs);
        _queueCallbackTimer.Elapsed += (sender, e) => ProcessCallbackQueue();
        _queueCallbackTimer.Start();
    }

    public async Task<CallResponse> InitiateCallAsync(CallRequest request)
    {
        var provider = _providerFactory.GetProvider(_activeProvider);
        request.Phone = CleanPhoneNumber(request.Phone);

        if (!ValidPhoneNumber(request.Phone) || !ValidIpAddress(request.UserIp))
        {
            return new CallResponse
            {
                Status = "failure",
                StatusText = "Invalid phone number or IP address"
            };
        }

        if (!string.IsNullOrEmpty(request.CallbackUrl) && !ValidUrl(request.CallbackUrl))
        {
            return new CallResponse
            {
                Status = "failure",
                StatusText = "Invalid callback URL"
            };
        }

        var response = await provider.CallApiAsync(request.Phone, request.UserIp);

        // Звонок совершен успешно
        if (response.Status == "OK")
        {
            await EnqueueCallback(request.CallbackUrl, new
            {
                phone = request.Phone,
                callId = response.CallId,
                status = "success",
                code = response.Code,
                errorMessage = response.StatusText
            });

            await LogCallToOpenSearch(request.Phone, "success", response.CallId, response.Code, response.StatusText, _activeProvider);
            return response;
        }

        // 220 Сервис временно недоступен, попробуйте чуть позже
        // 500 Ошибка на сервере. Повторите запрос
        if (response.Code == "220" || response.Code == "500")
        {
            // Если очередь переплнена, возвращаем ошибку
            if (!EnqueueCall(request))
            {
                var queueErrorResponse = new CallResponse
                {
                    Status = "failure",
                    StatusText = "500: Queue limit reached"
                };

                await LogCallToOpenSearch(request.Phone, queueErrorResponse.Status, response.CallId, response.Code, queueErrorResponse.StatusText, _activeProvider);

                return queueErrorResponse;
            }

            // Есть место в очереди - добавляем в очередеь
            var queuedResponse = new CallResponse
            {
                Status = "queued",
                StatusText = "Call has been queued"
            };

            await EnqueueCallback(request.CallbackUrl, new
            {
                phone = request.Phone,
                callId = response.CallId,
                status = response.Status,
                code = response.Code,
                errorMessage = response.StatusText
            });

            await LogCallToOpenSearch(request.Phone, queuedResponse.Status, response.CallId, response.Code, queuedResponse.StatusText, _activeProvider);
            return queuedResponse;
        }

        // Непредвиденные ошибки
        await LogCallToOpenSearch(request.Phone, response.Status, response.CallId, response.Code, response.StatusText, _activeProvider);
        return response;
    }


    private async void ProcessQueue()
    {
        var provider = _providerFactory.GetProvider(_activeProvider);
        var batch = new List<CallRequest>();
        for (int i = 0; i < _callBatchSize && _callQueue.TryDequeue(out var request); i++)
        {
            batch.Add(request);
        }

        if (batch.Count == 0)
        {
            return;
        }

        foreach (var request in batch)
        {
            var response = await provider.CallApiAsync(request.Phone, request.UserIp);
            var (callbackSuccess, statusText) = await SendCallback(request.CallbackUrl, new
            {
                phone = request.Phone,
                callId = response.CallId,
                status = response.Status,
                code = response.Code,
                errorMessage = response.StatusText
            }, 1);

            await LogCallToOpenSearch(request.Phone, response.Status == "OK" ? "success" : "failure", response.CallId, response.Code, response.StatusText, _activeProvider);
            await Task.Delay(_callBatchIntervalMs);
        }
    }

    private bool EnqueueCall(CallRequest request)
    {
        if (_callQueue.Count >= _maxQueueSize)
        {
            return false;
        }
        _callQueue.Enqueue(request);
        return true;
    }

    private async Task ProcessCallbackQueue()
    {
        var batch = new List<(string CallbackUrl, object CallbackData, int Attempt)>();

        for (int i = 0; i < _callbackBatchSize && _callbackQueue.TryDequeue(out var callbackItem); i++)
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

                await LogCallToOpenSearch(null, "failure", null, null, $"Callback to url: {callbackUrl} failed after attempts: {attempt}. Removing from queue.", _activeProvider);
                return;
            }

            var (success, statusText) = await SendCallback(callbackUrl, callbackData, attempt);

            if (!success)
            {
                
                if (_callbackQueue.Count >= _maxCallbackQueueSize)
                {
                    await LogCallToOpenSearch(null, "failure", null, null, $"The callback queue is full! Callback queue size: {_callbackQueue.Count}, callback url: {callbackUrl}", _activeProvider);
                    return;
                }
            }
            else
            {
                await LogCallToOpenSearch(null, "success", null, null, $"Sending callback from queue to {callbackUrl} was successful.", _activeProvider);
            }
            await Task.Delay(_callbackBatchIntervalMs);
        });

        await Task.WhenAll(tasks);
    }

    private async Task EnqueueCallback(string callbackUrl, object callbackData)
    {
        if (string.IsNullOrEmpty(callbackUrl))
            return;

        if (_callbackQueue.Count >= _maxCallbackQueueSize)
        {
            await LogCallToOpenSearch(null, "failure", null, null, $"The callback queue is full! Callback queue size: {_callbackQueue.Count}, callback url: {callbackUrl}", _activeProvider);
            return;
        }
        _callbackQueue.Enqueue((callbackUrl, callbackData, 1));
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
            if (_callbackQueue.Count >= _maxCallbackQueueSize)
            {
                return (false, "Callback queue limit reached");
            }
            _callbackQueue.Enqueue((callbackUrl, callbackData, attempt + 1));

            return (false, $"Callback failed and added to queue: {ex.Message}");
        }
    }

    private async Task LogCallToOpenSearch(string? phone, string status, string? callId, string? code, string? errorMessage, string provider)
    {
        var log = new CallLog
        {
            CallId = callId,
            Phone = phone,
            Code = code,
            Date = DateTime.UtcNow,
            Provider = provider,
            Status = status,
            ErrorMessage = errorMessage ?? null,
        };

        var response = await _openSearchClient.IndexAsync(log, idx => idx.Index(_configuration.GetCallStorageName()));

        if (!response.IsValid)
        {
            Console.WriteLine("Error logging call to OpenSearch: " + response.ServerError.Error.Reason);
        }
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

    private bool ValidIpAddress(string ipAddress) => !string.IsNullOrEmpty(ipAddress);

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

    public int GetCallQueueStatus() => _callQueue.IsEmpty ? 0 : _callQueue.Count;
    public int GetCallbackQueueStatus() => _callbackQueue.IsEmpty ? 0 : _callbackQueue.Count;

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