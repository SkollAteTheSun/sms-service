using Kp.Ms.Sms.Entities.Request;
using Kp.Ms.Sms.Entities.Response;
using Kp.Ms.Sms.Entities.Entity;
using Newtonsoft.Json;
using OpenSearch.Client;
using System.Collections.Concurrent;
using System.Text;
using Kp.Ms.Sms.Extensions;

namespace Kp.Ms.Sms.Services;

public class CallService
{
    private readonly ConcurrentQueue<CallRequest> _callQueue;
    private readonly ConcurrentQueue<(string CallbackUrl, object CallbackData)> _callbackQueue;
    private readonly int _maxQueueSize;
    private readonly HttpClient _httpClient;
    private readonly OpenSearchClient _openSearchClient;
    private readonly IConfiguration _configuration;
    private readonly System.Timers.Timer _queueTimer;

    public CallService(IConfiguration configuration, HttpClient httpClient, OpenSearchClient openSearchClient)
    {
        _configuration = configuration;
        _callQueue = new ConcurrentQueue<CallRequest>();
        _httpClient = httpClient;
        _openSearchClient = openSearchClient;
        _maxQueueSize = _configuration.GetValue<int>("QueueSettings:CallMaxSize");
        _callQueue = new ConcurrentQueue<CallRequest>();
        _callbackQueue = new ConcurrentQueue<(string CallbackUrl, object CallbackData)>();

        _queueTimer = new System.Timers.Timer(60000);
        _queueTimer.Elapsed += (sender, e) => ProcessQueue(); // звонки
        _queueTimer.Elapsed += (sender, e) => ProcessCallbackQueue(); // callback-и
        _queueTimer.Start();
    }

    public async Task<CallResponse> InitiateCallAsync(CallRequest request)
    {
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

        var response = await CallApiAsync(request.Phone, request.UserIp);

        // Звонок совершен успешно
        if (response.Status == "OK")
        {
            await HandleCallbackAndLogging(request.CallbackUrl, request, response);
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

                await LogCallToOpenSearch(request.Phone, queueErrorResponse.Status, response.CallId, response.Code, queueErrorResponse.StatusText);

                return queueErrorResponse;
            }

            // Есть место в очереди - добавляем в очередеь
            var queuedResponse = new CallResponse
            {
                Status = "queued",
                StatusText = "Call has been queued"
            };

            await HandleCallbackAndLogging(request.CallbackUrl, request, queuedResponse);
            return queuedResponse;
        }

        // Непредвиденные ошибки
        await LogCallToOpenSearch(request.Phone, response.Status, response.CallId, response.Code, response.StatusText);
        return response;
    }


    private async void ProcessQueue()
    {
        while (!_callQueue.IsEmpty)
        {
            var batch = new List<CallRequest>();
            for (int i = 0; i < 10 && _callQueue.TryDequeue(out var request); i++)
            {
                batch.Add(request);
            }

            foreach (var request in batch)
            {
                var response = await CallApiAsync(request.Phone, request.UserIp);
                var(callbackSuccess, statusText) = await SendCallback(request.CallbackUrl, new
                {
                    phone = request.Phone,
                    callId = response.CallId,
                    status = response.Status,
                    code = response.Code,
                    errorMessage = response.StatusText
                });

                await LogCallToOpenSearch(request.Phone, response.Status == "OK" ? "success" : "failure", response.CallId, response.Code, response.StatusText);
            }

            await Task.Delay(1000);
        }
    }

    private async Task<CallResponse> CallApiAsync(string phone, string userIp)
    {
        var apiId = _configuration["SmsRu:ApiId"];
        var url = $"{_configuration["SmsRu:Url"]}/code/call?phone={phone}&ip={userIp}&api_id={apiId}";
        var response = await _httpClient.GetAsync(url);
        var data = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<CallResponse>(data);
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

    public int GetCallQueueStatus() => _callQueue.IsEmpty ? 0 : _callQueue.Count;
    public int GetCallbackQueueStatus() => _callbackQueue.IsEmpty ? 0 : _callbackQueue.Count;

    private async Task ProcessCallbackQueue()
    {
        while (!_callbackQueue.IsEmpty)
        {
            var batch = new List<(string CallbackUrl, object CallbackData)>();
            for (int i = 0; i < 10 && _callbackQueue.TryDequeue(out var callbackItem); i++)
            {
                batch.Add(callbackItem);
            }

            foreach (var (callbackUrl, callbackData) in batch)
            {
                var (success, statusText) = await SendCallback(callbackUrl, callbackData);

                if (!success)
                {
                    _callbackQueue.Enqueue((callbackUrl, callbackData));
                }
            }

            await Task.Delay(1000);
        }
    }

    private async Task LogCallToOpenSearch(string phone, string status, string callId, string code, string? errorMessage)
     {
        var log = new CallLog
        {
            CallId = callId,
            Phone = phone,
            Code = code,
            Date = DateTime.UtcNow,
            Status = status,
            ErrorMessage = errorMessage ?? null
        };

        var response = await _openSearchClient.IndexAsync(log, idx => idx.Index(_configuration.GetCallStorageName()));

        if (!response.IsValid)
        {
            Console.WriteLine("Error logging call to OpenSearch: " + response.ServerError.Error.Reason);
        }
    }

    private async Task<(bool Success, string? StatusText)> SendCallback(string callbackUrl, object callbackData)
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
            if (_callbackQueue.Count >= _maxQueueSize)
            {
                return (false, "Callback queue limit reached");
            }
            _callbackQueue.Enqueue((callbackUrl, callbackData));
            return (false, $"Callback failed and added to queue: {ex.Message}");
        }
    }

    private async Task<(bool Success, string? StatusText)> HandleCallbackAndLogging(string? callbackUrl, CallRequest request, CallResponse response)
    {
        if (!string.IsNullOrEmpty(callbackUrl))
        {
            var callbackData = new
            {
                phone = request.Phone,
                callId = response.CallId,
                status = response.Status,
                code = response.Code,
                errorMessage = response.StatusText
            };

            var (callbackSuccess, statusText) = await SendCallback(callbackUrl, callbackData);
            if (!callbackSuccess)
            {
                return (false, statusText);
            }
        }

        await LogCallToOpenSearch(request.Phone, response.Status, response.CallId, response.Code, response.StatusText);
        return (true, null);
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
}