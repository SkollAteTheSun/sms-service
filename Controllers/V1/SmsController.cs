using Asp.Versioning;
using Kp.Ms.Sms.Factories;
using Kp.Ms.Sms.Services;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Kp.Ms.Sms.Entities.Request;
using OpenSearch.Client;

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
        if (result == "success")
            return Ok(new { status = "success" });
        if (result == "queued")
            return Accepted(new { status = "success" });

        return StatusCode(500, new { status = "failure", reason = result });
    }

    [HttpPost("switch")]
    public IActionResult Switch([FromBody] SmsSwitchRequest request)
    {
        if (_smsService.SwitchProvider(request.MethodCode))
            return Ok(new { status = "success" });

        return BadRequest(new { status = "failure", reason = "Invalid provider code" });
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
}