using Asp.Versioning;
using Kp.Ms.Sms.Services;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Kp.Ms.Sms.Entities.Request;
using Kp.Ms.Sms.Entities.Enums;
using Kp.Ms.Sms.Entities.Response;

namespace Kp.Ms.Sms.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[SwaggerTag("Тестовый контроллер")]
public class SmsController : ControllerBase
{
    private readonly SmsService _smsService;

    public SmsController(SmsService smsService)
    {
        _smsService = smsService;
    }

    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] SmsRequest request)
    {
        var result = await _smsService.SendSmsAsync(request);
        switch (result)
        {
            case nameof(StatusType.Success):
                return Ok(new { status = StatusType.Success });

            case nameof(StatusType.Queued):
                return Accepted(new { status = StatusType.Success });

            default:
                return StatusCode(500, new StatusResponse
                {
                    Status = StatusType.Failure.ToString(),
                    Error = result,
                });
        }
    }

    [HttpPost("switch")]
    public IActionResult Switch([FromBody] SmsSwitchRequest request)
    {
        if (_smsService.SwitchProvider(request.Provider))
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
        return Ok(new { activeProvider = _smsService.GetActiveProvider() });
    }

    [HttpGet("queue-status")]
    public IActionResult GetQueueStatus()
    {
        return Ok(new { queued = _smsService.GetQueueStatus() });
    }

    [HttpGet("queue-callback-status")]
    public IActionResult GetCallbackQueueStatus()
    {
        return Ok(new { queued = _smsService.GetCallbackQueueStatus() });
    }
}