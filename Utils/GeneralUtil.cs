using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Apache.Ignite.Core;
using Apache.Ignite.Core.Client.Cache;
using MySql.Data.MySqlClient;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;

public static class GeneralUtil
{
    public static async Task PreloadBadgeDataAsync<T>(MySqlConnection connectionStory, List<T> authorItems) where T : IAuthorEntity, IAuthorEntityPublished
    {
        if (authorItems == null || authorItems.Count == 0) return;

        var distinctAuthors = authorItems.Select(x => x.AuthorName).Distinct().ToList();

        var mySqlCommandBadge = new MySql.Data.MySqlClient.MySqlCommand();
        mySqlCommandBadge.Connection = connectionStory;
        mySqlCommandBadge.CommandText = $"SELECT username, profile_icon FROM users WHERE username IN ({string.Join(",", distinctAuthors.Select(_ => "?"))})";

        foreach (var name in distinctAuthors)
        {
            mySqlCommandBadge.Parameters.Add(new MySqlParameter("", name));
        }

        await using var reader = await mySqlCommandBadge.ExecuteReaderAsync();

        // Cache the ordinals before entering the loop for better performance
        int userOrdinal = reader.GetOrdinal("username");
        int iconOrdinal = reader.GetOrdinal("profile_icon");

        while (await reader.ReadAsync())
        {
            string username = reader.GetString(userOrdinal);
            string? authorBadgeMetadata = await reader.IsDBNullAsync(iconOrdinal)
                ? null
                : reader.GetString(iconOrdinal);

            if (authorBadgeMetadata == null) continue;

            foreach (var authorItem in authorItems.Where(x => x.AuthorName == username))
            {
                authorItem.AuthorBadge = authorBadgeMetadata;
            }
        }
    }



    public static RSAParameters ToRSAParameters(RsaPrivateCrtKeyParameters privKey)
    {
        RSAParameters rp = new RSAParameters();
        rp.Modulus = privKey.Modulus.ToByteArrayUnsigned();
        rp.Exponent = privKey.PublicExponent.ToByteArrayUnsigned();
        rp.P = privKey.P.ToByteArrayUnsigned();
        rp.Q = privKey.Q.ToByteArrayUnsigned();
        rp.D = ConvertRSAParametersField(privKey.Exponent, rp.Modulus.Length);
        rp.DP = ConvertRSAParametersField(privKey.DP, rp.P.Length);
        rp.DQ = ConvertRSAParametersField(privKey.DQ, rp.Q.Length);
        rp.InverseQ = ConvertRSAParametersField(privKey.QInv, rp.Q.Length);
        return rp;
    }

    private static byte[] ConvertRSAParametersField(BigInteger n, int size)
    {
        byte[] bs = n.ToByteArrayUnsigned();
        if (bs.Length == size)
            return bs;
        if (bs.Length > size)
            throw new ArgumentException("Specified size too small", "size");
        byte[] padded = new byte[size];
        Array.Copy(bs, 0, padded, size - bs.Length, bs.Length);
        return padded;
    }


    public static async Task<string> StartGetNewTokenTask(HttpClient httpClient, Apache.Ignite.Core.Client.Cache.ICacheClient<string, AuthCacheData> generalCache, string oldToken, ServerSettings serverSettings, ILogger logger)
    {
        try
        {
            var useTempAuth = serverSettings.SwiftTempAuthUser != "";
            var authJson = useTempAuth ? serverSettings.SwiftTempAuthUser : JsonSerializer.Serialize<OpenStackAuth>(serverSettings.SwiftKeyStoneAuth);
            HttpResponseMessage res;
            if (useTempAuth)
            {
                httpClient.DefaultRequestHeaders.Add("X-Auth-User", serverSettings.SwiftTempAuthUser);
                httpClient.DefaultRequestHeaders.Add("X-Auth-Key", serverSettings.SwiftTempAuthKey);
                res = await httpClient.GetAsync(serverSettings.SwiftAuthEndpoint);
            }
            else
            {
                res = await httpClient.PostAsync(serverSettings.SwiftAuthEndpoint,
                            new StringContent(authJson, Encoding.UTF8, "application/json"));
            }

            if (!res.IsSuccessStatusCode)
            {
                logger.LogError($"Did not receive new swift auth token, statusCode: {res.StatusCode}, response:{await res.Content.ReadAsStringAsync()}");
                return oldToken; // returning old token, something went wrong
            }
            if (res.Headers.Contains("X-Auth-Token"))
            {
                var authTokenNew = new AuthCacheData();
                authTokenNew.AuthToken = res.Headers.GetValues("X-Auth-Token").First();
                if (res.Headers.Contains("X-Auth-Token-Expires"))
                {
                    authTokenNew.Expiration = DateTime.UtcNow.AddSeconds(long.Parse(res.Headers.GetValues("X-Auth-Token-Expires").First()));
                }
                else
                {
                    logger.LogWarning("Did not find X-Auth-Token-Expires header when getting new auth token, setting default expires value at 24 hours");
                    authTokenNew.Expiration = DateTime.UtcNow.AddHours(24);
                }
                authTokenNew.Issued = DateTime.UtcNow;
                generalCache.Put(GeneralCacheKey.S3_AUTH_TOKEN, authTokenNew);
                return authTokenNew.AuthToken;
            }
            if (res.Headers.Contains("X-Subject-Token"))
            {
                var newToken = JsonSerializer.Deserialize<TokenResponse>(res.Content.ReadAsStringAsync().Result);
                if (newToken != null && newToken.Token != null)
                {
                    var authTokenNew = new AuthCacheData();
                    authTokenNew.AuthToken = res.Headers.GetValues("X-Subject-Token").First();
                    authTokenNew.Expiration = newToken.Token.ExpiresAt;
                    authTokenNew.Issued = newToken.Token.IssuedAt;
                    generalCache.Put(GeneralCacheKey.S3_AUTH_TOKEN, authTokenNew);
                    return authTokenNew.AuthToken;
                }
                else logger.LogError("swift auth object not deserialized!");
                return oldToken; // returning old token, something went wrong
            }
            else logger.LogError("Swift Token header missing in auth response!");
            return oldToken; // returning old token, something went wrong
        }
        catch (Exception ex)
        {
            logger.LogError("Exception occured when trying to fetch new Swift Auth Token:" + ex.Message, ex);
            throw;
        }
    }

    public static async Task CheckDependencyStartup(ServerSettings serverSettings, List<string> buckets, ILogger logger)
    {

        using HttpClient httpClient = new HttpClient();
        ICacheClient<string, AuthCacheData> generalCache;
        try
        {
            using var client = Ignition.StartClient(ConfigUtil.GetIgniteConfiguration(serverSettings!));
            var cluster = client.GetCluster();
            if (!cluster.IsActive())
            {
                client.GetCluster().SetActive(true); // activate ignite cluster if not active
            }
            generalCache = client.GetOrCreateCache<string, AuthCacheData>("generalCache");
            try
            {
                await CheckCreateSwiftBuckets(httpClient, serverSettings, generalCache, buckets, logger);
            }
            catch (Exception ex)
            {
                logger.LogError($"Checking & creating swift buckets failed: {ex.Message}", ex);
            }
        }
        catch (Exception igniteException)
        {
            logger.LogError($"Connecting to Ignite failed: {igniteException.Message}", igniteException);
            throw;
        }

    }



    public static async Task<bool> SetContainerPublicReadAsync(HttpClient client, string containerUri, string authToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, containerUri);

        if (!request.Headers.Contains("X-Auth-Token")) request.Headers.Add("X-Auth-Token", authToken);

        // 3. Set the Read ACL to ".r:*" to allow public read access (no token required for GET)
        // Note: To also allow public directory listing, use ".r:*,.rlistings"
        request.Headers.Add("X-Container-Read", ".r:*");

        HttpResponseMessage response = await client.SendAsync(request);
        return response.IsSuccessStatusCode;
    }


    public static async Task CheckCreateSwiftBuckets(HttpClient httpClient, ServerSettings serverSettings, ICacheClient<string, AuthCacheData> igniteCache, List<string> buckets, ILogger logger)
    {
        var token = await GeneralUtil.StartGetNewTokenTask(httpClient, igniteCache, "", serverSettings!, logger);
        httpClient.DefaultRequestHeaders.Add("X-Auth-Token", token);
        foreach (string bucket in buckets)
        {
            var res = await httpClient.GetAsync(bucket);
            if (!res.IsSuccessStatusCode)
            {
                logger.LogWarning($"Could not access swift bucket at endpoint {bucket}, statusCode: {res.StatusCode}, response:{await res.Content.ReadAsStringAsync()}, trying to create a new bucket");
                res = await httpClient.PutAsync(bucket,
                        new StringContent("", Encoding.UTF8, "application/json"));
                if (res.IsSuccessStatusCode)
                {
                    logger.LogInformation($"Created switft bucket at endpoint {bucket}, statusCode: {res.StatusCode}");
                }
                else
                {
                    logger.LogError($"Creating swift bucket failed for {bucket}, statusCode: {res.StatusCode}, message: {await res.Content.ReadAsStringAsync()}");
                }
            }
            var isPublic = await SetContainerPublicReadAsync(httpClient, bucket, token);
            if (!isPublic) logger.LogInformation($"Failed to set public access for bucket {bucket}, statusCode: {res.StatusCode}");
        }
    }

    public static MySql.Data.MySqlClient.MySqlCommand CreateNotificationMySQLCommand(MySqlConnection connection, MySqlTransaction? myTrans,
    string targetUsername, string metaData, TransactionDescriptionType descriptionType, DateTime availableFrom, NotificationType notificationType)
    {
        var mySqlCommand6 = new MySql.Data.MySqlClient.MySqlCommand();
        mySqlCommand6.CommandText = "INSERT INTO user_notification (username,type,additional_data, transaction_description_type, available_from) VALUES(@username, @type,@additional_data,@description_type,@available_from)";
        mySqlCommand6.Parameters.AddWithValue("@username", targetUsername);
        mySqlCommand6.Parameters.AddWithValue("@type", notificationType);
        mySqlCommand6.Parameters.AddWithValue("@additional_data", metaData);
        mySqlCommand6.Parameters.AddWithValue("@description_type", (int)descriptionType);
        mySqlCommand6.Parameters.AddWithValue("@available_from", availableFrom);
        mySqlCommand6.Connection = connection;
        if (myTrans != null) mySqlCommand6.Transaction = myTrans;
        return mySqlCommand6;
    }
}