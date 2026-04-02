using System.Collections.Concurrent;
using AlgoBot.Configuration;
using AlgoBot.Interfaces;
using AlgoBot.Models;

namespace AlgoBot.Services;

public sealed class SessionStateStore : ISessionStateStore
{
    private readonly ConcurrentDictionary<string, SessionState> _sessions =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _sync = new();

    public SessionState EnsureSessionState(TradingSessionSettings session, DateOnly tradingDay)
    {
        lock (_sync)
        {
            var state = _sessions.GetOrAdd(
                session.Name,
                _ => new SessionState(session.Name, tradingDay));

            state.IsEnabled = session.Enabled;

            if (state.TradingDay != tradingDay)
            {
                state.ResetForNewTradingDay(tradingDay, session.Instruments);
            }

            SynchronizeInstruments(state, session.Instruments, tradingDay);

            return state;
        }
    }

    public IReadOnlyCollection<SessionState> GetAll()
    {
        return _sessions.Values.ToList().AsReadOnly();
    }

    private static void SynchronizeInstruments(
        SessionState state,
        IEnumerable<string> configuredInstruments,
        DateOnly tradingDay)
    {
        var configured = configuredInstruments
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingKeys = state.InstrumentStates.Keys.ToList();

        foreach (var existing in existingKeys)
        {
            if (!configured.Contains(existing))
            {
                state.InstrumentStates.TryRemove(existing, out _);
            }
        }

        foreach (var instrument in configured)
        {
            state.InstrumentStates.AddOrUpdate(
                instrument,
                _ => new InstrumentState(instrument, tradingDay),
                (_, current) =>
                {
                    if (current.TradingDay != tradingDay)
                    {
                        current.ResetForNewTradingDay(tradingDay);
                    }

                    return current;
                });
        }
    }
}