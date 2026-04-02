using AlgoBot.Configuration;
using AlgoBot.Models;

namespace AlgoBot.Helpers;

public static class TradingTimeHelper
{
    public static DateOnly GetSessionTradingDay(
        TradingSessionSettings session,
        DateTimeOffset nowLocal)
    {
        var today = DateOnly.FromDateTime(nowLocal.DateTime);

        if (!CrossesMidnight(session))
            return today;

        // Example: 22:00 -> 02:00
        // If current local time is 00:30, the session actually started yesterday.
        return nowLocal.TimeOfDay <= session.EndTime
            ? today.AddDays(-1)
            : today;
    }

    public static SessionWindow GetSessionWindow(
        TradingSessionSettings session,
        DateOnly tradingDay,
        TimeZoneInfo timeZone)
    {
        var sessionStartLocal = tradingDay.ToDateTime(TimeOnly.MinValue).Add(session.StartTime);

        var sessionEndLocal = CrossesMidnight(session)
            ? tradingDay.ToDateTime(TimeOnly.MinValue).AddDays(1).Add(session.EndTime)
            : tradingDay.ToDateTime(TimeOnly.MinValue).Add(session.EndTime);

        var orbEndLocal = sessionStartLocal.AddMinutes(session.OrbMinutes);
        if (orbEndLocal > sessionEndLocal)
        {
            orbEndLocal = sessionEndLocal;
        }

        var sessionStartUtc = TimeZoneInfo.ConvertTimeToUtc(sessionStartLocal, timeZone);
        var sessionEndUtc = TimeZoneInfo.ConvertTimeToUtc(sessionEndLocal, timeZone);
        var orbEndUtc = TimeZoneInfo.ConvertTimeToUtc(orbEndLocal, timeZone);

        return new SessionWindow
        {
            TradingDay = tradingDay,
            SessionStartLocal = sessionStartLocal,
            SessionEndLocal = sessionEndLocal,
            OrbEndLocal = orbEndLocal,
            SessionStartUtc = sessionStartUtc,
            SessionEndUtc = sessionEndUtc,
            OrbEndUtc = orbEndUtc
        };
    }

    public static TimeSpan GetTimeframeSpan(string timeframe)
    {
        return timeframe.Trim().ToUpperInvariant() switch
        {
            "M1" or "1M" => TimeSpan.FromMinutes(1),
            "M2" or "2M" => TimeSpan.FromMinutes(2),
            "M3" or "3M" => TimeSpan.FromMinutes(3),
            "M4" or "4M" => TimeSpan.FromMinutes(4),
            "M5" or "5M" => TimeSpan.FromMinutes(5),
            "M6" or "6M" => TimeSpan.FromMinutes(6),
            "M10" or "10M" => TimeSpan.FromMinutes(10),
            "M12" or "12M" => TimeSpan.FromMinutes(12),
            "M15" or "15M" => TimeSpan.FromMinutes(15),
            "M20" or "20M" => TimeSpan.FromMinutes(20),
            "M30" or "30M" => TimeSpan.FromMinutes(30),
            "H1" or "1H" => TimeSpan.FromHours(1),
            "H2" or "2H" => TimeSpan.FromHours(2),
            "H3" or "3H" => TimeSpan.FromHours(3),
            "H4" or "4H" => TimeSpan.FromHours(4),
            "H6" or "6H" => TimeSpan.FromHours(6),
            "H8" or "8H" => TimeSpan.FromHours(8),
            "H12" or "12H" => TimeSpan.FromHours(12),
            "D1" or "1D" => TimeSpan.FromDays(1),
            _ => throw new ArgumentOutOfRangeException(nameof(timeframe), timeframe, "Unsupported timeframe.")
        };
    }

    public static bool CrossesMidnight(TradingSessionSettings session)
    {
        return session.EndTime <= session.StartTime;
    }
}