using System;

namespace NoireLib.Configuration;

public abstract partial class NoireConfigBase
{
    // Brings every property back inside its [Range], [StringLength] or [MaxLength] after a load. Returns whether anything changed.
    private bool EnforceRules()
    {
        var corrected = false;

        foreach (var rule in ConfigRules.For(GetType()))
        {
            try
            {
                var value = rule.Property.GetValue(this);
                var applied = ConfigRules.Apply(rule.Limits, value);

                if (Equals(applied, value))
                    continue;

                rule.Property.SetValue(this, applied);
                corrected = true;
                NoireLogger.LogWarning<NoireConfigBase>($"{GetType().Name}.{rule.Property.Name} was {value}, outside its rules. Set to {applied}.");
            }
            catch (Exception ex)
            {
                NoireLogger.LogError<NoireConfigBase>(ex, $"Could not apply the rules of {GetType().Name}.{rule.Property.Name}.");
            }
        }

        return corrected;
    }
}
