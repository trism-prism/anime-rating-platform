using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("admin")]
public class AdminController : ControllerBase
{
    [HttpPost("import")]
    public async Task<IActionResult> Import([FromServices] AniListImporter importer) => Ok(await importer.Import());
}
