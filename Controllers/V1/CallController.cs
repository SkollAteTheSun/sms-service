using Asp.Versioning;
using Kp.Ms.Sms.Entities.Enums;
using Kp.Ms.Sms.Entities.Request;
using Kp.Ms.Sms.Entities.Response;
using Kp.Ms.Sms.Services;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Kp.Ms.Sms.Controllers.V1;
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[SwaggerTag("Контроллер для звонков")]
public class CallController : ControllerBase
{
    private readonly CallService _callService;

    public CallController(CallService callService)
    {
        _callService = callService;
    }

    [HttpPost("send")]
    public async Task<IActionResult> InitiateCall([FromBody] CallRequest request)
    {
        var response = await _callService.InitiateCallAsync(request);

        switch (response.Status)
        {
            case nameof(StatusType.Success):
                return Ok(response);

            case nameof(StatusType.Failure):
                if (response.StatusText.Contains("Invalid phone number or IP address") ||
                    response.StatusText.Contains("Invalid callback URL"))
                {
                    return BadRequest(response);
                }
                return StatusCode(500, response);

            case nameof(StatusType.Queued):
                return Accepted(response);

            default:
                return StatusCode(500, new CallResponse
                {
                    Status = StatusType.Failure.ToString(),
                    StatusText = response.StatusText,
                });
        }
    }

    [HttpPost("switch")]
    public IActionResult Switch([FromBody] SmsSwitchRequest request)
    {
        if (_callService.SwitchProvider(request.Provider))
            return Ok(new { status = StatusType.Success });

        return BadRequest(new StatusResponse
        {
            Status = StatusType.Failure.ToString(),
            Error = "Invalid provider code",
        });
    }

    [HttpGet("active-provider")]
    public IActionResult GetActiveProvider()
    {
        return Ok(new { activeProvider = _callService.GetActiveProvider() });
    }

    [HttpGet("queue-status")]
    public IActionResult GetQueueStatus()
    {
        return Ok(new { queued = _callService.GetCallQueueStatus() });
    }

    [HttpGet("queue-callback-status")]
    public IActionResult GetCallbackQueueStatus()
    {
        return Ok(new { queued = _callService.GetCallbackQueueStatus() });
    }
}