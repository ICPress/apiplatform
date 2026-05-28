using JWT.Builder;
using JWT.Algorithms;
using JWT.Exceptions;
using JWT.Serializers;
using JWT.Extensions.AspNetCore.Factories;
using JWT.Extensions.AspNetCore;
using System.Security.Claims;
using Apache.Ignite.Core.Client;
using Microsoft.Extensions.Options;

public static class ConfigUtil
{
    public const string TEMP_TOKEN_STORE_IGNITE = "TEMPTOKENSTORE";

    public const string TEMP_TOKEN_PREFIX = "@temp_";
    public enum TargetDatabase
    {
        GORSE, STORYPOP
    }
    public const string JWT_USERNAME_KEY = "JWT_USERNAME";
    public const string JWT_ROLE_KEY = "JWT_ROLE";

    public const string JWT_DEFAULT_ROLE = "APP_USER";
    public const string JWT_TEST_ROLE = "TEST_USER";
    public const string JWT_ADMIN_ROLE = "ADMIN_USER";
    public const int SIGN_IN_TOKEN_EXPIRATION_MINUTES = 30;

    public static IgniteClientConfiguration GetIgniteConfiguration(ServerSettings settings)
    {
        return new IgniteClientConfiguration
        {
            Endpoints = new[] { settings.IgniteEndpoint }
        };
    }


    public static string GetNewToken(ServerSettings _serverSettings, string username, DateTime? expiration = null, string? tokenRole = null)
    { //VERIFY USER BEFORE ISSUING TOKEN!!
        var isAdminUser = _serverSettings.AdminUsername.Equals(username, StringComparison.OrdinalIgnoreCase);
        var payload = new Dictionary<string, object>
{
    {ConfigUtil.JWT_USERNAME_KEY,  username.ToLower()},
    { ConfigUtil.JWT_ROLE_KEY, tokenRole ?? (isAdminUser ? ConfigUtil.JWT_ADMIN_ROLE : ConfigUtil.JWT_DEFAULT_ROLE)}
};
        string secret = _serverSettings.JWTSecret;
        var json = JwtBuilder.Create()
                     .WithAlgorithm(new HMACSHA256Algorithm()) // symmetric
                     .WithSecret(secret)
                     .ExpirationTime(expiration ?? DateTime.Now.AddDays(30))
                     .AddClaims(payload)
                     .Encode();
        return json;

    }

    public static void ConfigureServices(IServiceCollection services, ServerSettings settings)
    {
        services.AddAuthentication(options =>
                     {
                         options.DefaultAuthenticateScheme = JwtAuthenticationDefaults.AuthenticationScheme;
                         options.DefaultChallengeScheme = JwtAuthenticationDefaults.AuthenticationScheme;
                     })
                .AddJwt(options =>
                     {
                         // secrets, required only for symmetric algorithms
                         options.Keys = new[] { settings.JWTSecret };

                         // optionally; disable throwing an exception if JWT signature is invalid
                         // options.VerifySignature = false;
                     });
        services.AddSingleton<IAlgorithmFactory>(new DelegateAlgorithmFactory(new HMACSHA256Algorithm()));

        // or use the generic version AddJwt<TFactory() if you have a custom implementation of IAlgorithmFactory
        // AddJwt<MyCustomAlgorithmFactory(options => ...);
    }
    public static string GetMysqlConnectionStringForDatabase(TargetDatabase targetDatabase, ServerSettings serverSettings)
    {
        if (targetDatabase == TargetDatabase.GORSE)
            return serverSettings.MysqlConnectionGorse;
        else return serverSettings.MysqlConnectionStoryPop;
    }

    public static string? VerifyUserNameFromClaimAndGetRole(string username, ClaimsIdentity? claimsIdentity)
    {
        if (claimsIdentity != null)
        {
            IEnumerable<Claim> claims = claimsIdentity.Claims;
            // or
            if (claimsIdentity.HasClaim(x => x.Type == JWT_USERNAME_KEY) && claimsIdentity.HasClaim(x => x.Type == JWT_ROLE_KEY))
            {
                var jwtUserName = claimsIdentity.FindFirst(x => x.Type == JWT_USERNAME_KEY)?.Value;
                if (jwtUserName == username.ToLower())
                {
                    var value = claimsIdentity.FindFirst(x => x.Type == JWT_ROLE_KEY)?.Value;
                    if (value != null)
                        return value;
                    else return null;
                }

            }

        }
        return null;
    }

    public static (string?, string?) GetUsernameAndRoleFromClaims(ClaimsIdentity? claimsIdentity)
    {
        if (claimsIdentity != null)
        {
            IEnumerable<Claim> claims = claimsIdentity.Claims;
            if (claimsIdentity.HasClaim(x => x.Type == JWT_USERNAME_KEY) && claimsIdentity.HasClaim(x => x.Type == JWT_ROLE_KEY))
            {
                var jwtUserName = claimsIdentity.FindFirst(x => x.Type == JWT_USERNAME_KEY)?.Value;
                var jwtRole = claimsIdentity.FindFirst(x => x.Type == JWT_ROLE_KEY)?.Value;
                return (jwtUserName, jwtRole);
            }

        }
        return (null, null);
    }
}