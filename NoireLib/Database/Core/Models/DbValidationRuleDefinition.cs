using System;
using System.Collections.Generic;

namespace NoireLib.Database;

/// <summary>
/// Defines a validation rule with optional parameters for a model column.
/// </summary>
public sealed record DbValidationRuleDefinition
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DbValidationRuleDefinition"/> record.
    /// </summary>
    /// <param name="rule">The validation rule to apply.</param>
    /// <param name="parameters">Optional parameters for the validation rule.</param>
    public DbValidationRuleDefinition(DbValidationRule rule, IReadOnlyList<string>? parameters = null)
    {
        Rule = rule;
        Parameters = parameters ?? Array.Empty<string>();
    }

    /// <summary>
    /// Gets the validation rule.
    /// </summary>
    public DbValidationRule Rule { get; }

    /// <summary>
    /// Gets the validation rule parameters.
    /// </summary>
    public IReadOnlyList<string> Parameters { get; }
}
