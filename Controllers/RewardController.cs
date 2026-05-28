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
using System.Data;

namespace apiplatform.Controllers;


[ApiController]
[Route("[controller]")]
public class RewardController : ControllerBase
{

    private readonly ILogger<RewardController> _logger;
    private readonly ServerSettings _serverSettings;
    private static readonly Random rnd = new Random();
    public RewardController(ILogger<RewardController> logger, ServerSettings serverSettings)
    {
        _logger = logger;
        _serverSettings = serverSettings;
    }
    [HttpGet()]
    public async Task<ActionResult<List<RewardModel>>> GetAllRewards(int offset = 0)
    {
        var rewards = new List<RewardModel>();
        if (offset > 0) return rewards;

        using var connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        await connection.OpenAsync();

        const string query = "SELECT reward_id, name, price, type, rarity, metadata, image_path, DATE_FORMAT(available_until, '%Y-%m-%dT%TZ') as available_until, description FROM rewards WHERE available_until > CURRENT_TIMESTAMP AND available_from < CURRENT_TIMESTAMP AND available_amount > 0";

        using var mySqlCommand = new MySqlCommand(query, connection);
        using var reader = await mySqlCommand.ExecuteReaderAsync();

        int idOrd = reader.GetOrdinal("reward_id");
        int nameOrd = reader.GetOrdinal("name");
        int priceOrd = reader.GetOrdinal("price");
        int typeOrd = reader.GetOrdinal("type");
        int rarityOrd = reader.GetOrdinal("rarity");
        int metaOrd = reader.GetOrdinal("metadata");
        int pathOrd = reader.GetOrdinal("image_path");
        int untilOrd = reader.GetOrdinal("available_until");
        int descOrd = reader.GetOrdinal("description");

        while (await reader.ReadAsync())
        {
            var newReward = new RewardModel(
                (uint)reader.GetInt64(idOrd),
                reader.GetString(nameOrd),
                (uint)reader.GetInt64(priceOrd),
                (ushort)reader.GetInt32(typeOrd),  // Use GetInt32 and cast for ushort
                (ushort)reader.GetInt32(rarityOrd), // Use GetInt32 and cast for ushort
                await reader.IsDBNullAsync(pathOrd) ? null : reader.GetString(pathOrd),
                reader.GetString(untilOrd),
                await reader.IsDBNullAsync(metaOrd) ? null : reader.GetString(metaOrd),
                reader.GetString(descOrd)
            );
            rewards.Add(newReward);
        }

        return rewards;
    }



    [Authorize]
    [HttpPost("claim/{rewardId}")]
    public async Task<StatusCodeResult> ClaimReward(uint rewardId, int rewardPrice, short rewardType)
    {
        (string? username, string? role) = ConfigUtil.GetUsernameAndRoleFromClaims(HttpContext.User.Identity as ClaimsIdentity);
        if (role == null || username == null)
        {
            _logger.LogError("User was not authorized to claim reward!");
            return StatusCode(403);
        }

        using var connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        await connection.OpenAsync();
        using var myTrans = await connection.BeginTransactionAsync();

        try
        {
            // 1. Check Balance
            using var cmdBalance = new MySqlCommand(
                "SELECT COALESCE(SUM(amount), 0) as balance FROM user_transactions WHERE available_from < CURRENT_TIMESTAMP AND username = @username",
                connection, myTrans);
            cmdBalance.Parameters.AddWithValue("@username", username);

            // ExecuteScalar returns object, MySql decimal usually maps to decimal
            object? result = await cmdBalance.ExecuteScalarAsync();
            decimal accountBalance = result != null ? Convert.ToDecimal(result) : 0m;

            if (accountBalance - rewardPrice < 0)
            {
                await myTrans.RollbackAsync();
                return StatusCode(406);
            }

            // 2. Update Reward Stock
            using var cmdUpdateStock = new MySqlCommand(
                "UPDATE rewards SET available_amount = available_amount - 1 WHERE reward_id = @reward_id AND available_until > CURRENT_TIMESTAMP AND available_from < CURRENT_TIMESTAMP AND available_amount > 0 AND price = @rewardPrice AND type = @rewardType",
                connection, myTrans);
            cmdUpdateStock.Parameters.AddWithValue("@reward_id", rewardId);
            cmdUpdateStock.Parameters.AddWithValue("@rewardType", rewardType);
            cmdUpdateStock.Parameters.AddWithValue("@rewardPrice", rewardPrice);

            if (await cmdUpdateStock.ExecuteNonQueryAsync() <= 0)
            {
                await myTrans.RollbackAsync();
                return StatusCode(406);
            }

            // 3. Insert Claimed Reward
            bool rewardIsTransferable = (rewardType == 0 || rewardType == 1 || rewardType == 2);
            using var cmdInsertClaim = new MySqlCommand(
                "INSERT INTO user_claimed_rewards (username, reward_id, transferable) VALUES (@username, @reward_id, @transferable)",
                connection, myTrans);
            cmdInsertClaim.Parameters.AddWithValue("@username", username);
            cmdInsertClaim.Parameters.AddWithValue("@reward_id", rewardId);
            cmdInsertClaim.Parameters.AddWithValue("@transferable", rewardIsTransferable);

            if (await cmdInsertClaim.ExecuteNonQueryAsync() <= 0)
            {
                await myTrans.RollbackAsync();
                return StatusCode(409);
            }

            // 4. Insert Transaction
            using var cmdInsertTx = new MySqlCommand(
                "INSERT INTO user_transactions (username, amount, transaction_type, additional_data, description_type) VALUES (@username, @amount, @transaction_type, @additional_data, @description_type)",
                connection, myTrans);
            cmdInsertTx.Parameters.AddWithValue("@username", username);
            cmdInsertTx.Parameters.AddWithValue("@amount", -rewardPrice);
            cmdInsertTx.Parameters.AddWithValue("@transaction_type", 2);
            cmdInsertTx.Parameters.AddWithValue("@additional_data", $"reward claim ({rewardId})");
            cmdInsertTx.Parameters.AddWithValue("@description_type", 4);

            if (await cmdInsertTx.ExecuteNonQueryAsync() <= 0)
            {
                await myTrans.RollbackAsync();
                return StatusCode(500);
            }

            await myTrans.CommitAsync();
            return StatusCode(200);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred when claiming reward, message: {Message}", ex.Message);
            await myTrans.RollbackAsync();
            return StatusCode(500);
        }
    }


    [Authorize]
    [HttpGet("claimed/{username}")]
    public async Task<ActionResult<List<RewardClaimedModel>>> GetClaimed(string username, int offset = 0)
    {
        var rewards = new List<RewardClaimedModel>();
        var role = ConfigUtil.VerifyUserNameFromClaimAndGetRole(username, HttpContext.User.Identity as ClaimsIdentity);
        if (role == null || username == null)
        {
            _logger.LogError("User was not authorized, returning empty result!");
            return rewards;
        }

        if (offset > 0) return rewards;

        using var connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        await connection.OpenAsync();

        const string query = @"
        SELECT claim_id, rr.reward_id, name, price, type, rarity, metadata, image_path, 
               ur.transferable, DATE_FORMAT(transfered_at, '%Y-%m-%dT%TZ') as transfered_at, 
               transfer_request_id, description 
        FROM user_claimed_rewards ur 
        INNER JOIN rewards rr ON rr.reward_id = ur.reward_id 
        WHERE ur.username = @username";

        using var mySqlCommand = new MySqlCommand(query, connection);
        mySqlCommand.Parameters.AddWithValue("@username", username);

        using var reader = await mySqlCommand.ExecuteReaderAsync();

        // Cache ordinals
        int claimIdOrd = reader.GetOrdinal("claim_id");
        int rewardIdOrd = reader.GetOrdinal("reward_id");
        int nameOrd = reader.GetOrdinal("name");
        int priceOrd = reader.GetOrdinal("price");
        int rarityOrd = reader.GetOrdinal("rarity");
        int typeOrd = reader.GetOrdinal("type");
        int metaOrd = reader.GetOrdinal("metadata");
        int pathOrd = reader.GetOrdinal("image_path");
        int transOrd = reader.GetOrdinal("transferable");
        int dateOrd = reader.GetOrdinal("transfered_at");
        int reqIdOrd = reader.GetOrdinal("transfer_request_id");
        int descOrd = reader.GetOrdinal("description");

        while (await reader.ReadAsync())
        {
            uint claimId = (uint)reader.GetInt64(claimIdOrd);
            uint rewardId = (uint)reader.GetInt64(rewardIdOrd);
            string rewardName = reader.GetString(nameOrd);
            uint rewardPrice = (uint)reader.GetInt64(priceOrd);
            ushort rewardRarity = (ushort)reader.GetInt32(rarityOrd);
            ushort rewardType = (ushort)reader.GetInt32(typeOrd);

            string? rewardMetadata = await reader.IsDBNullAsync(metaOrd) ? null : reader.GetString(metaOrd);
            string? imagePath = await reader.IsDBNullAsync(pathOrd) ? null : reader.GetString(pathOrd);
            bool walletTransferable = reader.GetBoolean(transOrd);
            string? transferedDate = await reader.IsDBNullAsync(dateOrd) ? null : reader.GetString(dateOrd);

            uint? transferRequestId = await reader.IsDBNullAsync(reqIdOrd)
                ? null
                : (uint)reader.GetInt64(reqIdOrd);

            string description = reader.GetString(descOrd);

            var newReward = new RewardModel(
                rewardId, rewardName, rewardPrice, rewardType,
                rewardRarity, imagePath, DateTime.UtcNow.ToString(),
                rewardMetadata, description
            );

            var claimedReward = new RewardClaimedModel(
                newReward, walletTransferable, transferedDate,
                claimId, transferRequestId
            );

            rewards.Add(claimedReward);
        }

        return rewards;
    }


    [Authorize]
    [HttpPost("transfer")]
    public async Task<StatusCodeResult> CreateTransfer(TransferRequestModel transferRequest)
    {
        if (transferRequest?.DeviceUUID == null || transferRequest.WalletAddress == null || transferRequest.Items == null)
        {
            _logger.LogError("Device UUID or wallet address was not provided! Wallet: {0}, DeviceUUID: {1}, items: {2}",
                transferRequest?.WalletAddress, transferRequest?.DeviceUUID, transferRequest?.Items?.Count);
            return StatusCode(406);
        }

        var (username, role) = ConfigUtil.GetUsernameAndRoleFromClaims(HttpContext.User.Identity as ClaimsIdentity);
        if (role == null || username == null)
        {
            _logger.LogError("User was not authorized, returning bad request!");
            return StatusCode(406);
        }

        using var connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        await connection.OpenAsync();
        using var myTrans = await connection.BeginTransactionAsync();

        try
        {
            // 1. Create Transfer Request Header
            using var cmdHeader = new MySqlCommand(
                "INSERT INTO claim_transfer_request (device_uuid, ip_address, wallet_address) " +
                "VALUES (UUID_TO_BIN(@device_uuid), INET6_ATON(@ip_address), @walletAddress); " +
                "SELECT LAST_INSERT_ID()",
                connection, myTrans);

            cmdHeader.Parameters.AddWithValue("@device_uuid", transferRequest.DeviceUUID);
            cmdHeader.Parameters.AddWithValue("@ip_address", Request.HttpContext.Connection.RemoteIpAddress?.ToString());
            cmdHeader.Parameters.AddWithValue("@walletAddress", transferRequest.WalletAddress);

            var result = await cmdHeader.ExecuteScalarAsync();
            uint transferRequestId = result != null ? Convert.ToUInt32(result) : 0;

            if (transferRequestId <= 0)
            {
                _logger.LogError("Could not create transaction for wallet transfer {0}, returning bad request!", transferRequest.WalletAddress);
                return StatusCode(406);
            }

            // 2. Process Items
            foreach (var item in transferRequest.Items)
            {
                // Insert Request Item
                using var cmdItem = new MySqlCommand(
                    "INSERT INTO claim_transfer_request_item (transfer_request_id, claim_id, reward_id) " +
                    "VALUES (@transfer_request_id, @claim_id, @reward_id)",
                    connection, myTrans);

                cmdItem.Parameters.AddWithValue("@transfer_request_id", transferRequestId);
                cmdItem.Parameters.AddWithValue("@claim_id", item.ClaimId);
                cmdItem.Parameters.AddWithValue("@reward_id", item.RewardId);

                if (await cmdItem.ExecuteNonQueryAsync() <= 0)
                {
                    await myTrans.RollbackAsync();
                    _logger.LogError("Could not create transaction items for wallet transfer {0}, returning bad request!", transferRequest.WalletAddress);
                    return StatusCode(406);
                }

                // Update Claimed Reward Status
                using var cmdUpdateClaim = new MySqlCommand(
                    "UPDATE user_claimed_rewards SET transferable = 0, transfer_request_id = @transfer_request_id " +
                    "WHERE claim_id = @claim_id AND username = @username AND reward_id = @reward_id AND transfer_request_id IS NULL",
                    connection, myTrans);

                cmdUpdateClaim.Parameters.AddWithValue("@username", username);
                cmdUpdateClaim.Parameters.AddWithValue("@transfer_request_id", transferRequestId);
                cmdUpdateClaim.Parameters.AddWithValue("@claim_id", item.ClaimId);
                cmdUpdateClaim.Parameters.AddWithValue("@reward_id", item.RewardId);

                if (await cmdUpdateClaim.ExecuteNonQueryAsync() <= 0)
                {
                    await myTrans.RollbackAsync();
                    _logger.LogError("Could not create transaction items for wallet transfer {0}, matching items missing, returning bad request!", transferRequest.WalletAddress);
                    return StatusCode(406);
                }
            }

            await myTrans.CommitAsync();
            return StatusCode(201);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred during transfer request for user {Username}", username);
            await myTrans.RollbackAsync();
            return StatusCode(500);
        }
    }


}