// Presentation metadata lives in application configuration, not stock_ticker.
public sealed class MarketOverviewEntry
{
    public string Symbol { get; set; } = "";
    public string CountryCode { get; set; } = "";
    // First matching exchange wins when the database contains multiple listings.
    public string[] ExchangeCodes { get; set; } = Array.Empty<string>();
    public string TradingViewSymbol { get; set; } = "";

    public bool Matches(string symbol, string exchange, string country) =>
        string.Equals(Symbol, symbol, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(CountryCode, country, StringComparison.OrdinalIgnoreCase) &&
        ExchangeRank(exchange) >= 0;

    public int ExchangeRank(string exchange) => Array.FindIndex(ExchangeCodes,
        code => string.Equals(code, exchange, StringComparison.OrdinalIgnoreCase));
}
