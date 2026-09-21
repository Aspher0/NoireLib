using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace NoireLib.UI;

internal sealed class TourRun
{
    internal TourRun(string id, IReadOnlyList<TourStep> steps, TourOptions options)
    {
        Id = id;
        Steps = steps;
        Options = options;
        Counters = new string[steps.Count];

        for (var index = 0; index < steps.Count; index++)
            Counters[index] = string.Create(CultureInfo.InvariantCulture, $"{index + 1} / {steps.Count}");
    }

    public string Id { get; }

    public IReadOnlyList<TourStep> Steps { get; }

    public TourOptions Options { get; }

    public string[] Counters { get; }

    public int Index { get; set; }

    public bool Entered { get; set; }

    public Vector2 CardSize { get; set; }

    public Vector4 CardRect { get; set; }

    public int AdvanceAtFrame { get; set; }

    public bool Settling { get; set; }

    public float SettleSince { get; set; }

    public int SettleStamp { get; set; }

    public float SettleFraction { get; set; }

    public int EnteredAtFrame { get; set; }

    public TourStep? CurrentStep => Index >= 0 && Index < Steps.Count ? Steps[Index] : null;

    public string Counter => Index >= 0 && Index < Counters.Length ? Counters[Index] : string.Empty;

    public bool IsLastStep => Index >= Steps.Count - 1;
}
