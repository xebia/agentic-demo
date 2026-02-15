using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Web.Services;

namespace Ticketing.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = "Bearer")]
public class OperationsController(IAlertService alertService, ILogger<OperationsController> logger) : ControllerBase
{
    [HttpPost("alerts")]
    public IActionResult PostAlert([FromBody] OperationsAlertDto alert)
    {
        alertService.AddAlert(alert);
        logger.LogInformation("Alert added: {Severity} - {Title} from {Source}", alert.Severity, alert.Title, alert.Source);
        return Created($"/api/operations/alerts/{alert.Id}", alert);
    }

    [HttpGet("alerts")]
    public IActionResult GetAlerts()
    {
        return Ok(alertService.Alerts);
    }

    [HttpPost("alerts/{id}/acknowledge")]
    public IActionResult AcknowledgeAlert(string id)
    {
        alertService.AcknowledgeAlert(id);
        return NoContent();
    }
}
