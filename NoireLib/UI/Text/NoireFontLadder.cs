using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace NoireLib.UI;

public sealed class NoireFontLadder : IDisposable
{
    private const float Epsilon = 0.0001f;

    private static readonly object LiveRoot = new();
    private static NoireFontLadder[] live = [];

    private sealed class Tier(float scale, float uiScale, NoireFontSet set)
    {
        public float Scale { get; } = scale;

        public float UiScale { get; } = uiScale;

        public NoireFontSet Set { get; } = set;
    }

    private readonly object syncRoot = new();
    private readonly string name;
    private readonly float[] steps;
    private readonly List<(NoireFont Face, float[] SizesPx)> faces = new();
    private readonly List<Tier> tiers = new();
    private readonly List<NoireFontSet> sets = new();
    private float prebuiltFor = float.NaN;
    private float droppedFor = float.NaN;
    private Tier? shown;
    private float wanted = 1f;
    private bool started;
    private bool parked;
    private bool disposed;

    public NoireFontLadder(string name, params float[] steps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(steps);

        this.name = name;
        this.steps = (float[])steps.Clone();
    }

    public float Scale
    {
        get
        {
            lock (syncRoot)
                return shown?.Scale ?? wanted;
        }
    }

    public bool Ready
    {
        get
        {
            lock (syncRoot)
                return shown != null;
        }
    }

    public bool IsBuilt
    {
        get
        {
            lock (syncRoot)
                return BuiltLocked();
        }
    }

    public bool Parked
    {
        get => parked;
        set
        {
            if (parked == value)
                return;

            parked = value;
            NoireScriptFonts.UpdateStage();
        }
    }

    public NoireFontLadder Add(NoireFont face, params float[] sizesPx)
    {
        ArgumentNullException.ThrowIfNull(face);
        ArgumentNullException.ThrowIfNull(sizesPx);

        lock (syncRoot)
        {
            if (started)
                throw new InvalidOperationException("Faces cannot be added to a font ladder once it is in use");

            faces.Add((face, (float[])sizesPx.Clone()));
        }

        return this;
    }

    public void Want(float scale)
    {
        lock (syncRoot)
        {
            if (disposed)
                return;

            wanted = scale;

            if (!started)
            {
                started = true;

                lock (LiveRoot)
                    live = [.. live, this];

                UiFontPump.Ensure();
            }

            AdvanceLocked();
        }
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
                return;

            disposed = true;

            foreach (var set in sets)
                set.Dispose();

            sets.Clear();
            tiers.Clear();
            shown = null;
        }

        lock (LiveRoot)
            live = Array.FindAll(live, ladder => !ReferenceEquals(ladder, this));

        NoireScriptFonts.UpdateStage();
    }

    internal static bool AllParked
    {
        get
        {
            var ladders = Volatile.Read(ref live);

            if (ladders.Length == 0)
                return false;

            foreach (var ladder in ladders)
            {
                if (!ladder.parked)
                    return false;
            }

            return true;
        }
    }

    internal static bool AllBuilt
    {
        get
        {
            foreach (var ladder in Volatile.Read(ref live))
            {
                if (!ladder.IsBuilt)
                    return false;
            }

            return true;
        }
    }

    internal static void TickAll()
    {
        foreach (var ladder in Volatile.Read(ref live))
        {
            lock (ladder.syncRoot)
            {
                if (!ladder.disposed)
                    ladder.AdvanceLocked();
            }
        }
    }

    private bool BuiltLocked()
    {
        if (disposed || tiers.Count == 0)
            return false;

        foreach (var set in sets)
        {
            if (!set.IsBuilt)
                return false;
        }

        return true;
    }

    private void AdvanceLocked()
    {
        if (faces.Count == 0)
            return;

        var uiScale = NoireUI.Scale;
        var tier = Find(wanted, uiScale) ?? Create([wanted], uiScale, $"{name} {(wanted * 100f).ToString("0", CultureInfo.InvariantCulture)}%");

        if (prebuiltFor != uiScale)
        {
            prebuiltFor = uiScale;
            PrebuildSteps(uiScale);
        }

        if (!ReferenceEquals(tier, shown) && tier.Set.IsBuilt)
            shown = tier;

        if (shown is not { } current || current.UiScale != uiScale || droppedFor == uiScale)
            return;

        droppedFor = uiScale;
        DropOtherUiScales(uiScale);
    }

    private Tier? Find(float scale, float uiScale)
    {
        foreach (var tier in tiers)
        {
            if (MathF.Abs(tier.Scale - scale) < Epsilon && MathF.Abs(tier.UiScale - uiScale) < Epsilon)
                return tier;
        }

        return null;
    }

    private Tier Create(List<float> scales, float uiScale, string setName)
    {
        var set = new NoireFontSet(setName);
        var sizes = new List<float>();

        foreach (var (face, sizesPx) in faces)
        {
            sizes.Clear();

            foreach (var scale in scales)
            {
                foreach (var size in sizesPx)
                    sizes.Add(size * scale);
            }

            set.Add(face, sizes.ToArray());
        }

        set.Build();
        sets.Add(set);

        Tier? first = null;

        foreach (var scale in scales)
        {
            var tier = new Tier(scale, uiScale, set);
            tiers.Add(tier);
            first ??= tier;
        }

        return first!;
    }

    private void PrebuildSteps(float uiScale)
    {
        var missing = new List<float>();

        foreach (var step in steps)
        {
            if (Find(step, uiScale) == null)
                missing.Add(step);
        }

        if (missing.Count > 0)
            Create(missing, uiScale, name + " other sizes");
    }

    private void DropOtherUiScales(float uiScale)
    {
        tiers.RemoveAll(tier => MathF.Abs(tier.UiScale - uiScale) >= Epsilon && !ReferenceEquals(tier, shown));

        for (var index = sets.Count - 1; index >= 0; index--)
        {
            var set = sets[index];

            if (tiers.Exists(tier => ReferenceEquals(tier.Set, set)))
                continue;

            set.Dispose();
            sets.RemoveAt(index);
        }
    }
}
