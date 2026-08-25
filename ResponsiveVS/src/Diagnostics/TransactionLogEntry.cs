namespace ResponsiveVS.Diagnostics;

public sealed class TransactionLogEntry
{
    public long TransactionId { get; set; }
    public string Classification { get; set; }
    public string Reason { get; set; }
    public string Source { get; set; }
    public string Target { get; set; }
    public long ElapsedMs { get; set; }

    public override string ToString()
    {
        return $"tx={TransactionId} class={Classification} reason={Reason} source={Source} target={Target} elapsedMs={ElapsedMs}";
    }
}
