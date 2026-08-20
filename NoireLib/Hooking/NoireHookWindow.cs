using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

namespace NoireLib.Hooking;

/// <summary>
/// A live table of every hook in the registry: what it is, where it landed, whether its delegate matched, and what
/// its detour has been doing. The shared instance is constructed and registered only on the first
/// <see cref="NoireHook.ShowWindow"/> or <see cref="NoireHook.ToggleWindow"/>; construct one directly to place it in
/// another window system.
/// </summary>
public sealed class NoireHookWindow : Window
{
    /// <summary>The window's default title, and the id its position is remembered under.</summary>
    public const string DefaultName = "NoireLib Hooks";

    private const string DisposeKey = "NoireLib.NoireHookWindow";
    private const float StatsRefreshSeconds = 0.25f;

    private static readonly Vector4 WarnColour = new(0.93f, 0.72f, 0.35f, 1f);
    private static readonly Vector4 FailColour = new(0.86f, 0.36f, 0.38f, 1f);
    private static readonly Vector4 GoodColour = new(0.44f, 0.78f, 0.51f, 1f);
    private static readonly Vector4 DimColour = new(0.62f, 0.62f, 0.66f, 1f);

    private static readonly object InstanceLock = new();

    private static NoireHookWindow? shared;

    private readonly List<HookRow> rows = [];

    private int lastVersion = -1;
    private long lastStatsRefresh;
    private string filter = string.Empty;

    /// <summary>Creates the window.</summary>
    /// <param name="name">The window title.</param>
    public NoireHookWindow(string name = DefaultName)
        : base(name)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560f, 240f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        Size = new Vector2(880f, 420f);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    internal static bool IsSharedOpen => shared is { IsOpen: true };

    /// <summary>
    /// Whether the shared window has been constructed, which reading it must never cause.
    /// </summary>
    internal static bool HasSharedInstance => shared != null;

    /// <summary>Draws the window, called by the window system it was added to.</summary>
    public override void Draw()
    {
        DrawControls();
        ImGui.Separator();

        Refresh();

        if (rows.Count == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, DimColour);
            ImGui.TextUnformatted("No hooks are registered.");
            ImGui.PopStyleColor();
            return;
        }

        DrawTable();
    }

    internal static void SetSharedOpen(bool open)
    {
        if (!open)
        {
            if (shared != null)
                shared.IsOpen = false;

            return;
        }

        Shared().IsOpen = true;
    }

    private static NoireHookWindow Shared()
    {
        lock (InstanceLock)
        {
            if (shared != null)
                return shared;

            shared = new NoireHookWindow();
            NoireService.NoireWindowSystem?.AddWindow(shared);
            NoireLibMain.RegisterOnDispose(DisposeKey, Release);

            return shared;
        }
    }

    private static void Release()
    {
        lock (InstanceLock)
        {
            if (shared == null)
                return;

            NoireService.NoireWindowSystem?.RemoveWindow(shared);
            shared = null;
        }
    }

    private static void DrawCell(string text, Vector4? colour = null)
    {
        ImGui.TableNextColumn();

        if (colour == null)
        {
            ImGui.TextUnformatted(text);
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.Text, colour.Value);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    private void DrawControls()
    {
        ImGui.TextUnformatted($"{NoireHook.Count} hook(s)");

        ImGui.SameLine(0f, 14f);
        if (ImGui.SmallButton("Enable all"))
            NoireHook.EnableAll();

        ImGui.SameLine(0f, 6f);
        if (ImGui.SmallButton("Disable all"))
            NoireHook.DisableAll();

        ImGui.SameLine(0f, 14f);
        if (ImGui.SmallButton("Count all"))
        {
            foreach (var hook in NoireHook.All)
                hook.CollectsStats = true;
        }

        ImGui.SameLine(0f, 14f);
        ImGui.SetNextItemWidth(200f);
        ImGui.InputTextWithHint("##NoireHookFilter", "filter by name, group or function", ref filter, 128);
    }

    private void DrawTable()
    {
        const ImGuiTableFlags Flags = ImGuiTableFlags.RowBg
            | ImGuiTableFlags.BordersInnerV
            | ImGuiTableFlags.Resizable
            | ImGuiTableFlags.ScrollY
            | ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable("###NoireHookTable", 8, Flags))
            return;

        try
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Hook", ImGuiTableColumnFlags.WidthStretch, 2.2f);
            ImGui.TableSetupColumn("Group", ImGuiTableColumnFlags.WidthStretch, 1.1f);
            ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("Address", ImGuiTableColumnFlags.WidthStretch, 1.7f);
            ImGui.TableSetupColumn("Function", ImGuiTableColumnFlags.WidthStretch, 2.6f);
            ImGui.TableSetupColumn("Delegate", ImGuiTableColumnFlags.WidthStretch, 1.3f);
            ImGui.TableSetupColumn("Calls", ImGuiTableColumnFlags.WidthStretch, 1.4f);
            ImGui.TableSetupColumn("##toggle", ImGuiTableColumnFlags.WidthStretch, 0.9f);
            ImGui.TableHeadersRow();

            foreach (var row in rows)
                DrawRow(row);
        }
        finally
        {
            ImGui.EndTable();
        }
    }

    private void DrawRow(HookRow row)
    {
        ImGui.TableNextRow();

        DrawCell(row.Name, row.Unguarded ? WarnColour : null);
        if (row.Unguarded && ImGui.IsItemHovered())
            ImGui.SetTooltip("A fault guard could not be generated for this signature, so the detour runs unguarded.");

        DrawCell(row.Group, DimColour);
        DrawCell(row.State, row.StateColour);
        DrawCell(row.Address, DimColour);
        DrawCell(row.Function, row.Function.Length == 0 ? DimColour : null);
        DrawCell(row.Verification, row.VerificationColour);

        if (row.Mismatch.Length > 0 && ImGui.IsItemHovered())
            ImGui.SetTooltip(row.Mismatch);

        if (row.Counting)
        {
            DrawCell(row.Stats, DimColour);
        }
        else
        {
            ImGui.TableNextColumn();

            if (ImGui.SmallButton($"Count##{row.Id}"))
                row.Hook.CollectsStats = true;

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Start counting this hook's calls. Timings stay empty unless the hook was created with CollectStats already set.");
        }

        ImGui.TableNextColumn();

        if (row.Hook.State != HookState.Installed)
            return;

        if (ImGui.SmallButton(row.Hook.IsEnabled ? $"Disable##{row.Id}" : $"Enable##{row.Id}"))
            row.Hook.Toggle();
    }

    private void Refresh()
    {
        var version = NoireHook.Version;
        var elapsed = Stopwatch.GetElapsedTime(lastStatsRefresh).TotalSeconds;

        if (version == lastVersion && elapsed < StatsRefreshSeconds)
            return;

        lastVersion = version;
        lastStatsRefresh = Stopwatch.GetTimestamp();

        rows.Clear();

        foreach (var hook in NoireHook.All)
        {
            if (hook.IsDisposed || !Matches(hook))
                continue;

            rows.Add(BuildRow(hook));
        }
    }

    private bool Matches(INoireHook hook)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        return hook.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || (hook.Group?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
            || (hook.Identity?.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private HookRow BuildRow(INoireHook hook)
    {
        var stats = hook.Stats;

        // Read from the option, not the counters: a hook that counts but has not been called reads zero exactly like
        // one that never counts.
        var statsText = hook.CollectsStats
            ? $"{stats.CallCount} call(s), {stats.FaultCount} fault(s)"
            : "not counting";

        return new HookRow
        {
            Hook = hook,
            Id = hook.GetHashCode(),
            Name = hook.Name,
            Group = hook.Group ?? "-",
            State = hook.State == HookState.Installed ? (hook.IsEnabled ? "Enabled" : "Installed") : hook.State.ToString(),
            StateColour = hook.State switch
            {
                HookState.Failed => FailColour,
                HookState.Pending => WarnColour,
                HookState.Installed when hook.IsEnabled => GoodColour,
                _ => DimColour,
            },
            Address = NoireHook.DescribeAddress(hook.Address),
            Function = hook.Identity?.Name ?? hook.Target.Describe(),
            Verification = hook.Verification.Status switch
            {
                HookVerificationStatus.Matched => "matched",
                HookVerificationStatus.Mismatched => "MISMATCH",
                HookVerificationStatus.Unverifiable => "unknown",
                _ => "skipped",
            },
            VerificationColour = hook.Verification.Status switch
            {
                HookVerificationStatus.Mismatched => FailColour,
                HookVerificationStatus.Matched => GoodColour,
                _ => DimColour,
            },
            Mismatch = hook.Verification.IsMismatch ? hook.Verification.Describe() : string.Empty,
            Stats = statsText,
            Counting = hook.CollectsStats,
            Unguarded = hook.State == HookState.Installed && !hook.IsGuarded,
        };
    }

    private sealed class HookRow
    {
        public INoireHook Hook = null!;

        public int Id;

        public string Name = string.Empty;

        public string Group = string.Empty;

        public string State = string.Empty;

        public Vector4 StateColour;

        public string Address = string.Empty;

        public string Function = string.Empty;

        public string Verification = string.Empty;

        public Vector4 VerificationColour;

        public string Mismatch = string.Empty;

        public string Stats = string.Empty;

        public bool Counting;

        public bool Unguarded;
    }
}
