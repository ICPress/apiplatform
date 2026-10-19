using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Data.Common;
using System.Globalization;
using Platform.Instruments;

namespace apiplatform.Controllers;

[ApiController]
[Route("Instrument")]
public class InstrumentController(ServerSettings settings) : ControllerBase
{
    private const string Columns = "stock_ticker_id, ticker_symbol, exchange_code, country_code, entity_name, has_sec_filings";
    private sealed record InstrumentSummary(string Id, string Symbol, string Exchange,
        string CountryCode, string Name, string Type, string? TradingViewSymbol, bool HasSecFilings);

    private InstrumentSummary Read(DbDataReader r)
    {
        var symbol = r.GetString(1).Trim();
        var exchange = r.GetString(2).Trim();
        var country = r.GetString(3).Trim().ToUpperInvariant();
        var overview = settings.MarketOverview.FirstOrDefault(item => item.Matches(symbol, exchange, country));
        var listingKey = $"{country}|{exchange}|{symbol}".ToUpperInvariant();
        string? chartSymbol;
        if (settings.TradingViewSymbolOverrides.TryGetValue(listingKey, out var specificSymbol))
            chartSymbol = specificSymbol;
        else if (overview != null && !string.IsNullOrWhiteSpace(overview.TradingViewSymbol))
            chartSymbol = overview.TradingViewSymbol;
        else if (settings.TradingViewExchanges.TryGetValue($"{country}|{exchange}".ToUpperInvariant(), out var chartExchange))
            chartSymbol = TradingViewSymbols.Resolve(symbol, chartExchange, country);
        else
            chartSymbol = TradingViewSymbols.Resolve(symbol, exchange, country);
        // Unknown listings are labeled generically; has_sec_filings is not an ETF classifier.
        return new InstrumentSummary(r.GetValue(0).ToString()!, symbol, exchange, country,
            r.IsDBNull(4) ? symbol : r.GetString(4), overview == null ? "instrument" : "etf", chartSymbol, !r.IsDBNull(5) && r.GetBoolean(5));
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(string? q, CancellationToken cancellationToken)
    {
        q = q?.Trim();
        if (string.IsNullOrEmpty(q)) return Ok(Array.Empty<object>());
        if (q.Length > 100) return BadRequest("Search is limited to 100 characters.");
        using var connection = new MySqlConnection(settings.MysqlConnectionStoryPop);
        await connection.OpenAsync(cancellationToken);
        using var command = new MySqlCommand($@"SELECT {Columns} FROM stock_ticker
            WHERE ticker_symbol LIKE @prefix ESCAPE '=' OR entity_name LIKE @contains ESCAPE '='
            ORDER BY (ticker_symbol = @q) DESC, (ticker_symbol LIKE @prefix ESCAPE '=') DESC, ticker_symbol, exchange_code, country_code LIMIT 12", connection);
        var escaped = q.Replace("=", "==").Replace("%", "=%").Replace("_", "=_");
        command.Parameters.AddWithValue("@q", q);
        command.Parameters.AddWithValue("@prefix", escaped + "%");
        command.Parameters.AddWithValue("@contains", "%" + escaped + "%");
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<object>();
        while (await reader.ReadAsync(cancellationToken)) items.Add(Read(reader));
        return Ok(items);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken cancellationToken)
    {
        if (id <= 0) return NotFound();
        using var connection = new MySqlConnection(settings.MysqlConnectionStoryPop);
        await connection.OpenAsync(cancellationToken);
        using var command = new MySqlCommand($"SELECT {Columns} FROM stock_ticker WHERE stock_ticker_id = @id", connection);
        command.Parameters.AddWithValue("@id", id);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Ok(Read(reader)) : NotFound();
    }

    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken cancellationToken)
    {
        var configured = settings.MarketOverview
            .Where(item => !string.IsNullOrWhiteSpace(item.Symbol) &&
                !string.IsNullOrWhiteSpace(item.CountryCode) && item.ExchangeCodes.Length > 0)
            .Take(20).ToList();
        if (configured.Count == 0) return Ok(Array.Empty<InstrumentSummary>());
        using var connection = new MySqlConnection(settings.MysqlConnectionStoryPop);
        await connection.OpenAsync(cancellationToken);
        using var command = new MySqlCommand { Connection = connection };
        var predicates = new List<string>();
        for (var i = 0; i < configured.Count; i++)
        {
            var item = configured[i];
            var exchangeParameters = new List<string>();
            for (var j = 0; j < item.ExchangeCodes.Length; j++)
            {
                var parameter = $"@exchange{i}_{j}";
                exchangeParameters.Add(parameter);
                command.Parameters.AddWithValue(parameter, item.ExchangeCodes[j]);
            }
            predicates.Add($"(ticker_symbol = @symbol{i} AND country_code = @country{i} AND exchange_code IN ({string.Join(",", exchangeParameters)}))");
            command.Parameters.AddWithValue($"@symbol{i}", item.Symbol);
            command.Parameters.AddWithValue($"@country{i}", item.CountryCode);
        }
        command.CommandText = $"SELECT {Columns} FROM stock_ticker WHERE {string.Join(" OR ", predicates)}";
        var listings = new List<InstrumentSummary>();
        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) listings.Add(Read(reader));

        // Configuration controls order; IDs always come from the existing database listings.
        var result = new List<InstrumentSummary>();
        foreach (var item in configured)
        {
            var listing = listings.Where(row => item.Matches(row.Symbol, row.Exchange, row.CountryCode))
                .OrderBy(row => item.ExchangeRank(row.Exchange))
                .ThenBy(row => ulong.Parse(row.Id, CultureInfo.InvariantCulture)).FirstOrDefault();
            if (listing != null && result.All(row => row.Id != listing.Id)) result.Add(listing);
        }
        return Ok(result);
    }
}
