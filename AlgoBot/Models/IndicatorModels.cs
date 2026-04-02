namespace AlgoBot.Models;

public sealed class MacdSnapshot
{
    public decimal CurrentMacd { get; init; }
    public decimal CurrentSignal { get; init; }
    public decimal CurrentHistogram { get; init; }
    public decimal PreviousMacd { get; init; }
    public decimal PreviousSignal { get; init; }
    public decimal PreviousHistogram { get; init; }
}