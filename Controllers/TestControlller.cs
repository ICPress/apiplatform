using Microsoft.AspNetCore.Mvc;
using Apache.Ignite.Core;
using Apache.Ignite.Core.Cache.Query;
using Apache.Ignite.Core.Client.Cache;
using Apache.Ignite.Core.Cache.Configuration;
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
    public Version GetClusterStatus()
    {
        using var client = Ignition.StartClient(ConfigUtil.GetIgniteConfiguration(_serverSettings));
        var cluster = client.GetCluster();
        if (!cluster.IsActive())
        {
            client.GetCluster().SetActive(true);
            return new Version(false);
        }
        else return new Version(true);

    }
}