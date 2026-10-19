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
        using var httpClient = new HttpClient();
        var token = await GeneralUtil.StartGetNewTokenTask(httpClient, "", _serverSettings, _logger);
        if (string.IsNullOrEmpty(token)) throw new InvalidOperationException("Storage authentication failed.");
        return GetEncryptedString(token, _serverSettings.CDNAccessKey);
    }
}
