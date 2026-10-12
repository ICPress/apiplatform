using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace apiplatform.Controllers;

[ApiController]
[Route("[controller]")]
public class MarketController : ControllerBase
{
    private readonly ServerSettings _serverSettings;

    public MarketController(ServerSettings serverSettings)
    {
        _serverSettings = serverSettings;
    }


    // Returns the latest bars, optionally strictly before the cursor, oldest first.
    [Authorize]
    [HttpGet("bars/{stockTicker}")]
    public async Task<ActionResult<List<object[]>>> GetBars(
        [FromRoute] string stockTicker,
        [FromQuery] string? before = null,
        [FromQuery] string timeframe = "1D",
        [FromQuery] int limit = 300,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stockTicker) || stockTicker.Length > 32)
            return BadRequest("stockTicker must contain between 1 and 32 characters.");

        if (!string.Equals(timeframe, "1D", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only the 1D timeframe is supported.");

        DateTime beforeDate = default;
        if (before != null && !DateTime.TryParseExact(before, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out beforeDate))
            return BadRequest("before must be a valid date in yyyy-MM-dd format when provided.");

        if (limit < 1 || limit > 1000)
            return BadRequest("limit must be between 1 and 1000.");

        using var connection = new MySqlConnection(
            ConfigUtil.GetMysqlConnectionStringForDatabase(
                ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        await connection.OpenAsync(cancellationToken);

        const string query = @"
            SELECT trading_date, open_price, high_price, low_price, close_price, volume
            FROM stock_daily_ohlc
            WHERE stock_ticker_id = (
                SELECT stock_ticker_id
                FROM stock_ticker
                WHERE ticker_symbol = @stockTicker
                ORDER BY stock_ticker_id
                LIMIT 1
            ) AND (@before IS NULL OR trading_date < @before)
            ORDER BY trading_date DESC
            LIMIT @limit;";

        using var command = new MySqlCommand(query, connection);
        command.Parameters.Add("@stockTicker", MySqlDbType.VarChar, 32).Value = stockTicker;
        command.Parameters.Add("@before", MySqlDbType.Date).Value = before == null ? DBNull.Value : beforeDate;
        command.Parameters.Add("@limit", MySqlDbType.Int32).Value = limit;

        var bars = new List<object[]>(limit);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            bars.Add(new object[]
            {
                reader.GetDateTime(0).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetDecimal(1),
                reader.GetDecimal(2),
                reader.GetDecimal(3),
                reader.GetDecimal(4),
                reader.GetDecimal(5)
            });
        }

        bars.Reverse();
        return bars;
    }
}
