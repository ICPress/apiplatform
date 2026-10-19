using System.Text.RegularExpressions;

namespace Platform.Instruments;

// Kept identical in siteplatform and apiplatform so a site deployment also works
// with older API responses that do not include a TradingView symbol.
public static class TradingViewSymbols
{
    private static readonly Dictionary<string, string> Exchanges = new(StringComparer.OrdinalIgnoreCase)
    {
        ["XNAS"] = "NASDAQ", ["NASDAQGS"] = "NASDAQ", ["NASDAQGM"] = "NASDAQ",
        ["NASDAQCM"] = "NASDAQ", ["NMS"] = "NASDAQ", ["NGM"] = "NASDAQ",
        ["NCM"] = "NASDAQ", ["NASDAQ GLOBAL SELECT MARKET"] = "NASDAQ", ["NASDAQ GLOBAL SELECT"] = "NASDAQ",
        ["NASDAQ GLOBAL MARKET"] = "NASDAQ", ["NASDAQ CAPITAL MARKET"] = "NASDAQ",
        ["XNYS"] = "NYSE", ["NYQ"] = "NYSE", ["NEW YORK STOCK EXCHANGE"] = "NYSE",
        ["NYSEARCA"] = "AMEX", ["NYSE ARCA"] = "AMEX", ["ARCA"] = "AMEX",
        ["ARCX"] = "AMEX", ["PCX"] = "AMEX", ["ASE"] = "AMEX", ["XASE"] = "AMEX", ["NYSE AMERICAN"] = "AMEX",
        ["XSTO"] = "OMXSTO", ["ST"] = "OMXSTO", ["STOCKHOLM"] = "OMXSTO",
        ["NASDAQ STOCKHOLM"] = "OMXSTO", ["XHEL"] = "OMXHEX", ["HE"] = "OMXHEX",
        ["HELSINKI"] = "OMXHEX", ["XCSE"] = "OMXCOP", ["CO"] = "OMXCOP",
        ["COPENHAGEN"] = "OMXCOP", ["XOSL"] = "OSL", ["OL"] = "OSL", ["OSLO"] = "OSL",
        ["XLON"] = "LSE", ["LONDON"] = "LSE", ["XETRA"] = "XETR",
        ["XFRA"] = "FWB", ["FRANKFURT"] = "FWB", ["XPAR"] = "EURONEXT",
        ["XAMS"] = "EURONEXT", ["XBRU"] = "EURONEXT", ["XLIS"] = "EURONEXT",
        ["XSWX"] = "SIX", ["XTSE"] = "TSX", ["XTSX"] = "TSXV",
        ["XASX"] = "ASX", ["XHKG"] = "HKEX", ["XTKS"] = "TSE"
    };

    public static string? Resolve(string? symbol, string? exchange, string? country)
    {
        var ticker = (symbol ?? "").Trim().ToUpperInvariant();
        var venue = (exchange ?? "").Trim().ToUpperInvariant();
        var region = (country ?? "").Trim().ToUpperInvariant();
        if (ticker.Length == 0) return null;
        // Already-qualified symbols are usable without any optional configuration.
        if (ticker.Contains(':')) return Safe(ticker);
        if (Exchanges.TryGetValue(venue, out var mapped)) venue = mapped;

        // Strip a data-vendor suffix only for the matching market, never an
        // arbitrary period (which may identify a share class such as BRK.B).
        var suffix = region switch
        {
            "US" => ".US", "SE" => ".ST", "FI" => ".HE", "DK" => ".CO",
            "NO" => ".OL", "GB" => ".L", "AU" => ".AX", "HK" => ".HK", _ => ""
        };
        if (suffix.Length > 0 && ticker.EndsWith(suffix, StringComparison.Ordinal))
            ticker = ticker[..^suffix.Length];

        if ((region == "US" || venue is "NASDAQ" or "NYSE" or "AMEX") &&
            venue is "" or "US" or "NASDAQ" or "NYSE" or "AMEX")
        {
            var etfVenue = ticker switch
            {
                "QQQ" or "SMH" or "IBIT" or "TLT" => "NASDAQ",
                "SPY" or "DIA" or "IWM" or "XLK" or "GLD" or "USO" or "EEM" or "VGK" => "AMEX",
                _ => null
            };
            if (etfVenue != null) venue = etfVenue;
        }
        if (venue is "OMXSTO" or "OMXHEX" or "OMXCOP")
            ticker = ticker.Replace('-', '_').Replace(' ', '_').Replace('.', '_');
        else if (venue is "NASDAQ" or "NYSE" or "AMEX")
            ticker = ticker.Replace('-', '.');

        // A generic US exchange code does not identify a venue. Let TradingView
        // resolve the ticker rather than incorrectly forcing every stock to NASDAQ.
        if (venue is "" or "US") return Safe(ticker);
        return Safe($"{venue}:{ticker}");
    }

    private static string? Safe(string value) =>
        Regex.IsMatch(value, @"^[A-Z0-9_:-]+(?:\.[A-Z0-9_-]+)*$") ? value : null;
}
