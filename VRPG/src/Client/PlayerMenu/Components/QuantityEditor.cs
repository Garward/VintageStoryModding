using System;
using System.Globalization;

namespace VRPG.Client.PlayerMenu.Components;

/// <summary>Exact integer input with explicit culture and no silent correction of typed amounts.</summary>
public sealed class QuantityEditor
{
    private readonly CultureInfo culture;
    private long maximum;
    public string Text { get; private set; } = "1";
    public long? Value { get; private set; }
    public string ErrorCode { get; private set; } = "quantity-unavailable";
    public bool CanSubmit => ErrorCode.Length == 0;
    public QuantityEditor(CultureInfo culture) { this.culture = CultureInfo.ReadOnly((CultureInfo)culture.Clone()); }
    public void SetLimits(long available, long reserved, long destination)
    {
        maximum = Math.Min(Math.Max(0, destination), Math.Max(0, available) - Math.Min(Math.Max(0, available), Math.Max(0, reserved)));
        Validate();
    }
    public void Type(string text) { Text = text; Validate(); }
    public void Max() => Type(maximum.ToString(culture));
    public void Half() => Type((maximum == 0 ? 0 : Math.Max(1, maximum / 2)).ToString(culture));
    private void Validate()
    {
        Value = null;
        string text = Text.Trim();
        string separator = culture.NumberFormat.NumberGroupSeparator;
        bool grouped = separator.Length > 0 && text.Contains(separator, StringComparison.Ordinal);
        if (!long.TryParse(text, NumberStyles.AllowThousands, culture, out long value) || value <= 0
            || grouped && text != value.ToString("N0", culture))
        { ErrorCode = "quantity-integer"; return; }
        Value = value;
        ErrorCode = value > maximum ? "quantity-unavailable" : "";
    }
}
