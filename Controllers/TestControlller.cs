using Microsoft.AspNetCore.Mvc;
using JWT.Builder;
using JWT.Algorithms;
using System.Text.Json;
using Slugify;
using System.Globalization;
using System.Text;
using System.Security.Cryptography;
using MySql.Data.MySqlClient;
using LanguageDetection;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Collections.Generic;
using System.Collections.ObjectModel;
namespace apiplatform.Controllers;


[ApiController]
[Route("[controller]")]
public class TestController : ControllerBase
{

    private readonly ILogger<TestController> _logger;

    private readonly ServerSettings _serverSettings;

    public TestController(ILogger<TestController> logger, ServerSettings serverSettings)
    {
        _logger = logger;
        _serverSettings = serverSettings;
    }

    [HttpGet]
    public async Task<IActionResult> GetDatabaseStatus()
    {
        using var connection = new MySqlConnection(_serverSettings.MysqlConnectionStoryPop);
        await connection.OpenAsync();
        using var command = new MySqlCommand("SELECT 1", connection);
        return Ok(new { versionCode = 2, databaseRunning = Convert.ToInt32(await command.ExecuteScalarAsync()) == 1 });
    }
}
