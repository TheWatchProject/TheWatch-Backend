using System;

namespace TheWatch.Infrastructure.Security;

[AttributeUsage(AttributeTargets.Property)]
public class PiiSensitiveAttribute : Attribute
{
    public string MaskType { get; set; } = "Generic";

    public PiiSensitiveAttribute(string maskType = "Generic")
    {
        MaskType = maskType;
    }

    public static string MaskValue(string? value, string maskType = "Generic")
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.Length <= 4) return "****";
        return $"{value.Substring(0, 2)}****{value.Substring(value.Length - 2)}";
    }
}