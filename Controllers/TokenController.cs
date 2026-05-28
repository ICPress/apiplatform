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
public class TokenController : ControllerBase
{

    static Random rnd = new Random();


    private readonly ILogger<TokenController> _logger;

    private readonly ServerSettings _serverSettings;

    public TokenController(ILogger<TokenController> logger, ServerSettings serverSettings)
    {
        _logger = logger;
        _serverSettings = serverSettings;
    }


    private static string validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    public static string GenerateNonce(int length)
    {
        var nonceString = new StringBuilder();
        for (int i = 0; i < length; i++)
        {
            nonceString.Append(validChars[rnd.Next(0, validChars.Length - 1)]);
        }

        return nonceString.ToString();
    }
    private string GetEncryptedString(string source, string cdnAccessKey)
    {
        var encoding = new UTF8Encoding();
        var key = Encoding.ASCII.GetBytes(cdnAccessKey);
        var sourceBytes = encoding.GetBytes(source);
        var tagSpan = new byte[AesGcm.TagByteSizes.MaxSize];
        var cipherSpan = new byte[sourceBytes.Length];
        var data = new ReadOnlySpan<byte>();
        var nonce = GenerateNonce(AesGcm.NonceByteSizes.MaxSize);
        using AesGcm aes = new AesGcm(key);
        aes.Encrypt(Encoding.ASCII.GetBytes(nonce), sourceBytes, cipherSpan, tagSpan, data);
        return nonce + "\n" + System.Convert.ToBase64String(tagSpan) + "\n" + System.Convert.ToBase64String(cipherSpan);

    }

    [Authorize]
    [HttpGet("{appName}")]
    public async Task<string> GetToken(string appName)
    {
        // return GetEncryptedString("randomText");
        using var client = Ignition.StartClient(ConfigUtil.GetIgniteConfiguration(_serverSettings));
        using var httpClient = new HttpClient();
        var generalCache = client.GetOrCreateCache<string, AuthCacheData>("generalCache");
        if (generalCache.ContainsKey(GeneralCacheKey.S3_AUTH_TOKEN))
        {
            var token = generalCache.Get(GeneralCacheKey.S3_AUTH_TOKEN);
            if (token.Expiration.AddHours(-1) <= DateTime.UtcNow)
            { //about to expire
                var newTokenTask = await GeneralUtil.StartGetNewTokenTask(httpClient, generalCache, token.AuthToken, _serverSettings, _logger);
                _logger.LogError("Fetched new token, received:" + newTokenTask);
                return GetEncryptedString(newTokenTask, _serverSettings.CDNAccessKey);
            }
            else
            {
                _logger.LogError("Reusing existing token in memory:" + token.AuthToken);
                return GetEncryptedString(token.AuthToken, _serverSettings.CDNAccessKey);
            }
        }
        else
        {
            var newTokenTask = await GeneralUtil.StartGetNewTokenTask(httpClient, generalCache, "", _serverSettings, _logger);
            _logger.LogError("no memory exists, fetching new token, received:" + newTokenTask);
            return GetEncryptedString(newTokenTask, _serverSettings.CDNAccessKey);
        }
    }


}