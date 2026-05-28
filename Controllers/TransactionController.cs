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
public class TransactionController : ControllerBase
{
    private readonly ILogger<TransactionController> _logger;

    private readonly ServerSettings _serverSettings;

    public TransactionController(ILogger<TransactionController> logger, ServerSettings serverSettings)
    {
        _logger = logger;
        _serverSettings = serverSettings;
    }


    [Authorize]
    [HttpGet("{username}")]
    public async Task<ActionResult<List<TransactionModel>>> GetAllTransactions(string username, int count = 5, int offset = 0)
    {
        var response = new List<TransactionModel>();
        if (username == null)
        {
            _logger.LogError("Attempted to fetch transactions without username!");
            return response;
        }

        var role = ConfigUtil.VerifyUserNameFromClaimAndGetRole(username, HttpContext.User.Identity as ClaimsIdentity);
        if (role == null)
        {
            _logger.LogError("Attempted to fetch transactions with wrong username: {Username}", username);
            throw new UnauthorizedAccessException("Unauthorized!");
        }

        using var connectionStory = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        await connectionStory.OpenAsync();

        const string query = @"
        WITH fw_cte AS ( 
            SELECT transaction_id, amount, transaction_type, description_type, 
                   DATE_FORMAT(available_from, '%Y-%m-%dT%TZ') as available_from,  
                   additional_data, 
                   ROW_NUMBER() OVER (ORDER BY available_from DESC) as row_num 
            FROM user_transactions 
            WHERE username = @username AND available_from < CURRENT_TIMESTAMP
        ) 
        SELECT * FROM fw_cte WHERE row_num > @offset LIMIT @count";

        using var mySqlCommand = new MySqlCommand(query, connectionStory);
        mySqlCommand.Parameters.AddWithValue("@username", username);
        mySqlCommand.Parameters.AddWithValue("@offset", offset);
        mySqlCommand.Parameters.AddWithValue("@count", count);

        using var reader = await mySqlCommand.ExecuteReaderAsync();

        // Cache ordinals
        int idOrd = reader.GetOrdinal("transaction_id");
        int amountOrd = reader.GetOrdinal("amount");
        int typeOrd = reader.GetOrdinal("transaction_type");
        int descOrd = reader.GetOrdinal("description_type");
        int dateOrd = reader.GetOrdinal("available_from");
        int dataOrd = reader.GetOrdinal("additional_data");

        while (await reader.ReadAsync())
        {
            uint transactionId = (uint)reader.GetInt64(idOrd);
            int amount = reader.GetInt32(amountOrd);
            byte transactionType = reader.GetByte(typeOrd);
            ushort descriptionType = (ushort)reader.GetInt32(descOrd);
            string availableFrom = reader.GetString(dateOrd);
            string additionalData = reader.GetString(dataOrd);

            var transaction = new TransactionModel(
                transactionId,
                availableFrom,
                descriptionType,
                amount,
                transactionType,
                additionalData
            );
            response.Add(transaction);
        }

        return response;
    }

}