using Kp.Ms.Sms.Entities.Request;
using Kp.Ms.Sms.Entities.Response;
using Kp.Ms.Sms.Entities.Entity;
using Kp.Ms.Sms.Factories;
using Newtonsoft.Json;
using OpenSearch.Client;
using System.Collections.Concurrent;
using System.Text;
using Kp.Ms.Sms.Extensions;

namespace Kp.Ms.Sms.Services;

public class CallService
{
    private readonly ConcurrentQueue<CallRequest> _callQueue;
    private readonly int _maxQueueSize;
    private bool _isServiceAvailable = true;
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

        _queueTimer = new System.Timers.Timer(60000);
        _queueTimer.Elapsed += (sender, e) => ProcessQueue();
        _queueTimer.Start();
    }

    public async Task<CallResponse> InitiateCallAsync(CallRequest request)
    {
        request.Phone = CleanPhoneNumber(request.Phone);

        if (!ValidPhoneNumber(request.Phone) || !ValidIpAddress(request.UserIp))
            return new CallResponse
            {
                Status = "failure",
                StatusText = "Invalid phone number or IP address"
            };

        if (!string.IsNullOrEmpty(request.CallbackUrl) && !ValidUrl(request.CallbackUrl))
            return new CallResponse
            {
                Status = "failure",
                StatusText = "Invalid callback URL"
            };

        if (_isServiceAvailable)
        {
            var response = await CallApiAsync(request.Phone, request.UserIp);
            if (response.Status == "OK")
            {
                await HandleCallbackAndLogging(request.CallbackUrl, request, response);
                return response;
            }
            else if (response.Status == "ERROR" && response.StatusText?.Contains("Слишком много звонков") == true) 
                // чтобы не добавлять в очередь, если превышен лимит на день
            {
                await HandleCallbackAndLogging(request.CallbackUrl, request, response);
                return response;
            }
            else // если статус неуспешный
            {
                if (!EnqueueCall(request)) // если очередь заполнена
                {
                    await HandleCallbackAndLogging(request.CallbackUrl, request, response);
                    return new CallResponse
                    {
                        Status = "failure",
                        StatusText = "Queue limit reached"
                    };
                }

                // если в очереди есть место
                await HandleCallbackAndLogging(request.CallbackUrl, request, response);
                return new CallResponse
                {
                    Status = "queued",
                    StatusText = "Call has been queued"
                };
            }
        }

        if (!EnqueueCall(request)) //если сервис недоступен и очередь переполнена 
        {
            return new CallResponse
            {
                Status = "failure",
                StatusText = "Queue limit reached"
            };
        }

        return new CallResponse //если сервис недоступен и в очереди есть место
        {
            Status = "queued",
            StatusText = "Call has been queued"
        };
    }

    private async void ProcessQueue()
    {
        if (_callQueue.IsEmpty || !_isServiceAvailable) return;

        for (int i = 0; i < 5 && _callQueue.TryDequeue(out var request); i++)
        {
            var response = await CallApiAsync(request.Phone, request.UserIp);
            var status = response.Status == "OK" ? "success" : "failure";
            await SendCallback(request.Phone, request.Phone, response.Status, response.CallId, response.Code, response.StatusText);
            await LogCallToOpenSearch(request.Phone, status, response.CallId, response.Code, response.StatusText);
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

    public int GetQueueStatus() => _callQueue.IsEmpty ? 0 : _callQueue.Count;

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

    private async Task SendCallback(string callbackUrl, string? phone, string? status, string? callId, string? code, string? errorMessage)
    {
        if (string.IsNullOrEmpty(callbackUrl)) return;

        var callbackData = new
        {
            phone,
            callId,
            status,
            code,
            errorMessage
        };

        var jsonContent = new StringContent(JsonConvert.SerializeObject(callbackData), Encoding.UTF8, "application/json");
        try
        {
            var response = await _httpClient.PostAsync(callbackUrl, jsonContent);
            response.EnsureSuccessStatusCode();
        }
        catch
        {
            _isServiceAvailable = false;
        }
    }

    private async Task HandleCallbackAndLogging(string callbackUrl, CallRequest request, CallResponse response)
    {
        if (!string.IsNullOrEmpty(callbackUrl))
        {
            await SendCallback(request.Phone, request.Phone ?? null, response.Status ?? null, response.CallId ?? null, response.Code ?? null, response.StatusText ?? null);
        }
        await LogCallToOpenSearch(request.Phone, response.Status, response.CallId, response.Code, response.StatusText);
    }

    private string NormalizePhoneNumber(string phoneNumber)
    {
        var digitsOnly = new StringBuilder();

        foreach (char c in phoneNumber)
        {
            if (char.IsDigit(c))
            {
                digitsOnly.Append(c);
            }
        }

        return digitsOnly.ToString();
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
