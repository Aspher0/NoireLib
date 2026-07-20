using NoireLib.Hooking;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// Captures the exact camera constants the GPU rasterizes the world with, at the D3D boundary.<br/>
/// The world pixels are drawn from constant-buffer bytes the game uploads through the immediate context; any read of
/// the CPU camera struct - at any moment - is a guess about when the game copied that struct into the upload, and
/// under load the struct advances mid-frame, so every struct-timed snapshot drifts from the rasterized frame by
/// camera-velocity times an unknown dt. This class removes the guess: it taps the upload paths
/// (<c>UpdateSubresource</c>, <c>Map</c>/<c>Unmap</c>, the same sanctioned vtable-tap mechanism as
/// <see cref="RenderTargetTap"/>), discovers at runtime where the view-projection is uploaded, and each frame commits
/// the newest validated upload pending at the main scene pass. The overlay then projects with exactly the matrix the
/// pixels were drawn with - including any projection jitter the struct never exposes.<br/>
/// The lock identity is an (offset, layout) <b>family across a set of member buffers</b>, not a single buffer: the
/// game rotates its per-view camera writes round-robin over a small ring of physical cbuffers (for example, three
/// 64-byte buffers at VS b0), so a single-pointer lock can never hold. Windows are scored <b>at capture time
/// against a same-instant struct read</b>, which removes the temporal skew from the match itself; matching and
/// validation use the X/Y/W columns only, because the game's uploaded Z column differs from the struct's by design
/// (the render path rebuilds Z analytically anyway).<br/>
/// Self-calibrating and fail-soft: no shipped offsets (discovery re-runs after any game patch or buffer change by
/// construction), per-commit validation against the struct camera rejects foreign views (shadow, water, portrait),
/// and any failure falls back to the world-pass struct snapshot - the layer never goes dark because of this feature.
/// </summary>
internal sealed unsafe class CameraConstantCapture : IDisposable
{
    // ID3D11DeviceContext vtable slots (base interface; numbering validated by RenderTargetTap's working set).
    private const int SlotMap = 14;
    private const int SlotUnmap = 15;
    private const int SlotCopySubresourceRegion = 46;
    private const int SlotCopyResource = 47;
    private const int SlotUpdateSubresource = 48;

    // Discovery bounds. A per-view camera cbuffer is small; anything larger than TrackedBytes is counted for the
    // probe report but never copied. Buffers up to SmallScanBytes are scored per update (the camera family is
    // typically 64 B); larger tracked buffers keep the cheaper last-write scan at commit time as a backstop.
    private const int MaxTrackedBuffers = 48;
    private const int TrackedBytes = 4096;
    private const int MinTrackedBytes = 64;
    private const int SmallScanBytes = 512;
    private const int LearnBudgetPerFrame = 128;   // unknown-pointer QI+GetDesc calls allowed per frame while discovering
    private const int ScoreBudgetPerFrame = 4096;  // capture-time window scores allowed per frame while discovering
    private const int MaxMapPending = 8;
    private const int MaxFamilies = 32;
    private const int MaxMembers = 8;
    private const int PendingRingLength = 32;      // must exceed the member writes that can follow the main-view upload before its pass draws
    private const int VsSlotCount = 14;            // D3D11 common-shader cbuffer slots queried for the bound-confirm

    // Match thresholds (normalized RMS over the compared elements). CandidateErr admits a window into the family
    // table; LockErr keeps a winning streak alive; StrongErr must be reached at least once before locking; SteadyErr
    // is the per-commit validation gate once locked. The floors these gates sit above are temporal: the uploads
    // derive from the game's own view setup, up to a frame before any reference read, so under fast camera motion
    // the best achievable match is a fraction of the inter-frame camera delta (typically a few e-3 at ordinary
    // speeds). A foreign view's constants (shadow/water/portrait) differ by orders of magnitude, so the gates stay
    // far from ambiguity even this wide open.
    private const float CandidateErr = 2e-2f;
    private const float LockErr = 1e-2f;
    private const float StrongErr = 3e-3f;
    private const float SteadyErr = 5e-2f;
    private const int LockStreak = 12;
    private const int UnlockAfterInvalidCommits = 90;

    /// <summary>Window layouts the matcher can lock on.</summary>
    internal enum MatrixForm : byte
    {
        /// <summary>Row-major bytes match the row-vector View*Proj directly.</summary>
        ViewProj = 0,
        /// <summary>The window holds the transpose of the row-vector View*Proj.</summary>
        ViewProjTransposed = 1,
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int MapFn(nint context, nint resource, uint subresource, int mapType, uint mapFlags, nint mappedOut);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void UnmapFn(nint context, nint resource, uint subresource);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void UpdateSubresourceFn(nint context, nint dstResource, uint dstSubresource, nint dstBox, nint srcData, uint srcRowPitch, uint srcDepthPitch);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void CopyResourceFn(nint context, nint dstResource, nint srcResource);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void CopySubresourceRegionFn(nint context, nint dstResource, uint dstSubresource, uint dstX, uint dstY, uint dstZ, nint srcResource, uint srcSubresource, nint srcBox);

    /// <summary>
    /// A small cbuffer being observed during discovery: identity, payload, per-frame best match, probe bookkeeping.
    /// Only real constant buffers within the size bounds occupy a slot; everything else goes to the ignore ring, so
    /// junk resources can never squat the table and starve the camera buffers out of it.
    /// </summary>
    private struct TrackedBuffer
    {
        public nint Ptr;
        public int ByteWidth;
        public byte[]? Bytes;
        public int ValidBytes;
        public long UpdatesSeen;
        public byte Mechanisms;        // bit 1 = UpdateSubresource, bit 2 = Map/Unmap
        public bool UpdatedSinceScan;
        public long FullCaptures;      // whole-payload copies into Bytes; lets a reader tell fresh bytes from frozen ones
        public int LastBoundSlot;      // -1 until seen bound to the VS at a main-pass commit
        public float BestVpErr;        // best window error ever seen for either lock form (probe report)

        // Best-matching window since the last commit, scored at capture time (small buffers only). This is what
        // survives the round-robin overwrites: the main-view write is kept even when other views' writes land after
        // it in the same physical buffer before the commit looks.
        public bool HasBestSinceCommit;
        public float BestErrSinceCommit;
        public int BestOffsetSinceCommit;
        public MatrixForm BestFormSinceCommit;
        public Matrix4x4 BestMatrixSinceCommit;
    }

    private struct MapPendingEntry
    {
        public nint Resource;
        public nint Data;
    }

    /// <summary>
    /// A candidate lock target: an (offset, layout) window family and the member buffers it has been seen in. The
    /// per-view ring rotates the camera write across its member buffers, so streaks are per family, never per buffer.
    /// </summary>
    private struct Family
    {
        public int Offset;
        public MatrixForm Form;
        public int Streak;
        public int Hits;
        public float MinErr;
        public float LastErr;
        public long LastCommitSeen;
        public bool BoundSeen;
        public int MemberCount;

        [System.Runtime.CompilerServices.InlineArray(MaxMembers)]
        public struct MemberArray
        {
            private nint element0;
        }

        public MemberArray Members;

        public readonly bool HasMember(nint ptr)
        {
            for (var i = 0; i < MemberCount; i++)
            {
                if (Members[i] == ptr)
                    return true;
            }

            return false;
        }

        public void AddMember(nint ptr)
        {
            if (!HasMember(ptr) && MemberCount < MaxMembers)
                Members[MemberCount++] = ptr;
        }
    }

    private struct PendingMatrix
    {
        public Matrix4x4 Vp;
        public long Seq;
    }

    private HookWrapper<MapFn>? mapHook;
    private HookWrapper<UnmapFn>? unmapHook;
    private HookWrapper<UpdateSubresourceFn>? updateSubresourceHook;
    private HookWrapper<CopyResourceFn>? copyResourceHook;
    private HookWrapper<CopySubresourceRegionFn>? copySubresourceRegionHook;
    private MapFn? mapDetour;
    private UnmapFn? unmapDetour;
    private UpdateSubresourceFn? updateSubresourceDetour;
    private CopyResourceFn? copyResourceDetour;
    private CopySubresourceRegionFn? copySubresourceRegionDetour;
    private nint gameContext;
    private RenderTargetTap? tap;

    private volatile bool active;
    private int detourFaults;                 // a throwing detour body disables the feature (self-disable, logged once)
    private bool faultLogged;

    private readonly TrackedBuffer[] tracked = new TrackedBuffer[MaxTrackedBuffers];
    private int trackedCount;
    private int evictionCursor;
    private int learnBudget;
    private int scoreBudget;
    private long largeCbuffersSeen;           // distinct constant buffers above TrackedBytes (probe report)
    private long copiesIntoTracked;           // CopyResource/CopySubresourceRegion into a tracked/locked buffer (probe report)

    // Pointers checked once and found uninteresting (not a buffer, not a cbuffer, out of size bounds). A ring so a
    // very long session cannot grow it; overwritten entries are simply re-checked on next sight (budgeted). Sized
    // for the game's resource-upload churn - an undersized ring wraps and re-burns the learn budget every frame.
    private const int MaxIgnored = 1024;
    private readonly nint[] ignoredPtrs = new nint[MaxIgnored];
    private int ignoredCount;
    private int ignoredCursor;
    private long ignoredNotBuffer;
    private long ignoredNoCbufferFlag;
    private long ignoredTooLarge;
    private long ignoredTooSmall;

    // Learning-machinery counters for the probe report - the answer to "why is the table not filling".
    private long statLearns;
    private long statEvictions;
    private long statBudgetExhaustedFrames;

    private readonly MapPendingEntry[] mapPending = new MapPendingEntry[MaxMapPending];

    private readonly Family[] families = new Family[MaxFamilies];
    private int familyCount;
    private long commitFrames;                // main-pass commits seen (discovery clock)

    // Locked state. All writes happen on the render thread (detours and the main-pass commit run there); the
    // consumers (inject and present-time paths) run on the same thread, so plain fields are safe by construction.
    // Membership is a SIZE CLASS, not a pointer set: the game rotates its camera block round-robin across a ring
    // of same-size physical buffers (for example, five 1024 B buffers each written every ~5th frame), so a
    // pointer-set lock starves - most frames the main-view write lands in a buffer outside the set, the commit
    // goes stale, and the overlay projects with a frames-old camera. Extracting the locked window from every
    // tracked buffer of the locked byte-width lets validation decide which windows are the main view.
    private bool lockedOn;
    private int lockedOffset;
    private MatrixForm lockedForm;
    private int lockedByteWidth;
    private byte lockedMechanisms;
    private readonly PendingMatrix[] pendingRing = new PendingMatrix[PendingRingLength];
    private int pendingCursor;
    private long pendingSeq;
    private long lastCommittedSeq;
    private long validationCountHwm;
    private int invalidCommitStreak;
    private bool unlockLogged;

    private Matrix4x4 committedVp;
    private long commitPresentIndex = -1;
    private bool haveCommit;
    private long presentIndex;
    private volatile bool awaitingMainDraw;   // set at the main-pass OM bind; the commit runs at the pass's first draw
    private Matrix4x4 lastCommitRefVp;        // the previous commit's struct reference (capture-time scoring's second anchor)
    private bool hasLastCommitRef;

    // Counters surfaced through stats and the probe report.
    private long statCommits;
    private long statValidCommits;
    private long statValidationFails;
    private long statStaleSkips;
    private long statConsumedInject;
    private long statConsumedPresent;
    private long statLocks;

    private int probeFramesRemaining;
    private int fullCaptureFramesRemaining;

    /// <summary>
    /// Records every payload rather than only the last, for finding data the game writes many times per frame.
    /// Idle unless armed, and it never touches the camera path's state.
    /// </summary>
    private readonly ConstantWriteLog writeLog = new();
    private bool probeArmed;

    /// <summary>True once the upload-path hooks are installed (they may still be disabled).</summary>
    public bool Installed => updateSubresourceHook != null;

    /// <summary>Whether a camera constant window family is currently locked and producing per-frame commits.</summary>
    public bool IsLocked => lockedOn;

    /// <summary>
    /// Classifies the contents of every tracked constant buffer, for finding values the game uploads that this
    /// renderer has no other way to learn - its lighting above all.<br/>
    /// The buffers are already being tracked for the camera window; this reads the same bytes without disturbing
    /// that. Snapshots are copies, so two taken at different moments can be compared.
    /// </summary>
    /// <param name="lockedOnly">Restrict to buffers of the locked size class, which is where the camera lives and most likely the frame constants with it.</param>
    public IReadOnlyList<ConstantSnapshot> SnapshotConstants(bool lockedOnly = false)
    {
        var snapshots = new List<ConstantSnapshot>();

        for (var i = 0; i < trackedCount; i++)
        {
            ref var slot = ref tracked[i];
            if (slot.Bytes is null || slot.ValidBytes < MinTrackedBytes)
                continue;

            if (lockedOnly && (!lockedOn || slot.ByteWidth != lockedByteWidth))
                continue;

            snapshots.Add(LightConstantProbe.Classify(slot.Ptr, slot.Bytes, slot.ValidBytes, slot.FullCaptures));
        }

        return snapshots;
    }

    /// <summary>Whether whole payloads are currently being copied, which is what makes a constant snapshot live.</summary>
    public bool FullCaptureArmed => fullCaptureFramesRemaining > 0;

    /// <summary>
    /// Asks for whole upload payloads to be copied for the next <paramref name="frames"/> world frames.<br/>
    /// The tracker stops copying them once it locks, because the camera window is all it needs from then on.
    /// Reading those bytes for anything else - finding the game's lighting - requires turning the copies back on,
    /// and it is time-boxed because it costs a memcpy per upload of every tracked buffer.
    /// </summary>
    /// <param name="frames">How many world frames to keep copying for.</param>
    public void ArmFullCapture(int frames) => fullCaptureFramesRemaining = Math.Max(frames, 0);

    /// <summary>
    /// Records every payload written to tracked buffers for the next few frames, instead of only the last one
    /// per buffer.<br/>
    /// This is how data the game writes repeatedly within a frame becomes visible. A deferred renderer's light
    /// list is the case in point: one buffer, rewritten per light, which the tracking table can only ever show
    /// as a single value that changes constantly.
    /// </summary>
    /// <param name="frames">How many world frames to record.</param>
    /// <param name="byteWidth">When above zero, record only buffers of exactly this size.</param>
    public void ArmWriteLog(int frames, int byteWidth = 0)
    {
        // The log can only record payloads for buffers the table already knows, so whole-payload copying has to
        // be on for it to have anything to see.
        if (fullCaptureFramesRemaining <= 0)
            fullCaptureFramesRemaining = Math.Max(frames, 0) + 1;

        writeLog.Arm(frames, byteWidth);
    }

    /// <summary>Whether the write log hit its cap, meaning the end of the frame was never recorded.</summary>
    public bool WriteLogTruncated => writeLog.Truncated;

    /// <summary>The distinct buffer sizes currently tracked, for choosing what to restrict a write log to.</summary>
    public IReadOnlyList<int> TrackedSizes()
    {
        var sizes = new SortedSet<int>();
        for (var i = 0; i < trackedCount; i++)
        {
            if (tracked[i].Bytes is not null)
                sizes.Add(tracked[i].ByteWidth);
        }

        return new List<int>(sizes);
    }

    /// <summary>Whether the write log is currently recording.</summary>
    public bool WriteLogArmed => writeLog.Armed;

    /// <summary>How many payloads the write log has recorded.</summary>
    public int WriteLogCount => writeLog.Count;

    /// <summary>Reports what the write log recorded, grouped by buffer and ordered by how varied each one is.</summary>
    public string DescribeWriteLog() => writeLog.Describe();

    /// <summary>
    /// The distinct payloads from the last write-log run, for comparing one run against another across a change
    /// made in the world.
    /// </summary>
    public List<byte[]> WriteLogPayloads() => writeLog.DistinctPayloads();

    /// <summary>The buffer size the last write-log run was restricted to, or zero for all of them.</summary>
    public int WriteLogSize => writeLog.SizeFilter;

    /// <summary>Installs the upload-path hooks (disabled) on the immediate context's vtable. One-time, fail-soft.</summary>
    public bool Install(RenderDevice device, RenderTargetTap ownerTap)
    {
        if (updateSubresourceHook != null)
            return true;

        var ctx = device.Context;
        if (ctx == null)
            return false;

        gameContext = (nint)ctx;
        tap = ownerTap;
        var vtable = *(void***)ctx;

        try
        {
            mapDetour = MapDetour;
            mapHook = new HookWrapper<MapFn>((nint)vtable[SlotMap], mapDetour, autoEnable: false, name: "Draw3D.CamCapture.Map");
            unmapDetour = UnmapDetour;
            unmapHook = new HookWrapper<UnmapFn>((nint)vtable[SlotUnmap], unmapDetour, autoEnable: false, name: "Draw3D.CamCapture.Unmap");
            updateSubresourceDetour = UpdateSubresourceDetour;
            updateSubresourceHook = new HookWrapper<UpdateSubresourceFn>((nint)vtable[SlotUpdateSubresource], updateSubresourceDetour, autoEnable: false, name: "Draw3D.CamCapture.UpdateSubresource");
            copyResourceDetour = CopyResourceDetour;
            copyResourceHook = new HookWrapper<CopyResourceFn>((nint)vtable[SlotCopyResource], copyResourceDetour, autoEnable: false, name: "Draw3D.CamCapture.CopyResource");
            copySubresourceRegionDetour = CopySubresourceRegionDetour;
            copySubresourceRegionHook = new HookWrapper<CopySubresourceRegionFn>((nint)vtable[SlotCopySubresourceRegion], copySubresourceRegionDetour, autoEnable: false, name: "Draw3D.CamCapture.CopySubresourceRegion");
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Draw3D: failed to install the camera-constant capture hooks (the layer projects with the struct snapshot instead).", "Draw3D");
            Dispose();
            return false;
        }

        NoireLogger.LogInfo("Draw3D: camera-constant capture installed (disabled until the injection point is armed).", "Draw3D");
        return true;
    }

    /// <summary>Enables or disables the upload-path taps. Driven by the render-target tap's injection state.</summary>
    public void SetActive(bool enabled)
    {
        if (updateSubresourceHook == null || detourFaults >= 3)
            enabled = false;

        if (active == enabled)
            return;

        active = enabled;
        mapHook?.SetEnabled(enabled);
        unmapHook?.SetEnabled(enabled);
        updateSubresourceHook?.SetEnabled(enabled);
        copyResourceHook?.SetEnabled(enabled);
        copySubresourceRegionHook?.SetEnabled(enabled);
    }

    /// <summary>
    /// Per-present frame boundary (render thread, from <see cref="RenderTargetTap.OnPresent"/>). Advances the
    /// present index the commit-freshness rules compare against and resets the per-frame budgets.
    /// </summary>
    public void OnFrameBoundary()
    {
        presentIndex++;
        awaitingMainDraw = false; // a frame whose main pass never drew must not commit on an unrelated later draw
        if (fullCaptureFramesRemaining > 0)
            fullCaptureFramesRemaining--;

        writeLog.OnFrameBoundary();
        if (learnBudget <= 0 && !lockedOn)
            statBudgetExhaustedFrames++;
        learnBudget = LearnBudgetPerFrame;
        scoreBudget = ScoreBudgetPerFrame;
        Array.Clear(mapPending); // a mapping never legitimately spans a present; dropping strays keeps the table free
    }

    /// <summary>
    /// Arms the discovery probe: unlocks, re-runs discovery for <paramref name="frames"/> main-pass frames while
    /// accumulating the full observation table, then logs the report (and re-locks on the way if a winner emerges).
    /// </summary>
    public void ArmProbe(int frames)
    {
        probeFramesRemaining = Math.Clamp(frames, 30, 6000);
        probeArmed = true;
        Unlock("probe armed");
        ResetDiscovery();
    }

    /// <summary>Whether the tap should keep its draw hooks enabled: the commit runs at the main pass's first draw.</summary>
    public bool WantsDrawSignal => active;

    /// <summary>Draws a locked commit may retry across while the frame's fresh main-view upload has not landed yet.</summary>
    private const int CommitRetryDraws = 96;
    private int commitRetriesLeft;

    /// <summary>
    /// Signal from the tap's OM detour at the first main-scene-pass bind: arms the commit for the pass's draws.
    /// Committing at the bind itself would be wrong: the game binds and uploads its camera block between the OM
    /// bind and the pass's draws, so at the bind the VS slots still hold the previous pass's buffers and the
    /// newest camera upload may not exist yet.
    /// </summary>
    public void OnMainPassBind()
    {
        if (!active)
            return;

        awaitingMainDraw = true;
        commitRetriesLeft = CommitRetryDraws;
    }

    /// <summary>
    /// Signal from the tap's draw detours (render thread, every game draw while active). Locked, the commit runs
    /// at the first draw at-or-after the frame's fresh main-view upload: a small fraction of frames put a draw
    /// (clear, sky, query) between the pass bind and the camera upload (typically a couple of percent of frames,
    /// each recovering within the same frame), so a failed attempt keeps retrying on subsequent draws, bounded by
    /// <see cref="CommitRetryDraws"/>. Discovering, the first draw learns the VS-bound buffers and advances the
    /// lock state machine. Fast no-op on every other draw.
    /// </summary>
    public void OnGameDraw(nint context)
    {
        if (!awaitingMainDraw || context != gameContext || tap is not { SuppressSelf: false, IsInjecting: false })
            return;

        try
        {
            if (!GameRenderSources.TryGetCamera(out var cam) || !cam.HasRenderCamera)
            {
                awaitingMainDraw = false;
                return;
            }

            var refVp = cam.View * cam.Proj;

            if (!lockedOn)
            {
                awaitingMainDraw = false;
                commitFrames++;
                LearnBoundBuffers((ID3D11DeviceContext*)context);
                AdvanceDiscovery(in refVp);
                FinishCommitFrame(in refVp);
                return;
            }

            var firstAttempt = commitRetriesLeft == CommitRetryDraws;
            if (firstAttempt)
            {
                commitFrames++;
                statCommits++;
            }

            if (TryCommitLocked(in refVp, firstAttempt))
            {
                awaitingMainDraw = false;
                invalidCommitStreak = 0;
                statValidCommits++;
                FinishCommitFrame(in refVp);
                return;
            }

            if (--commitRetriesLeft > 0)
                return; // fresh upload not landed yet - the next draw retries

            awaitingMainDraw = false;
            invalidCommitStreak++;
            if (invalidCommitStreak >= UnlockAfterInvalidCommits)
            {
                Unlock($"no valid upload for {UnlockAfterInvalidCommits} main-pass frames");
                ResetDiscovery();
            }

            FinishCommitFrame(in refVp);
        }
        catch (Exception ex)
        {
            awaitingMainDraw = false;
            OnDetourFault(ex);
        }
    }

    /// <summary>Once-per-frame commit epilogue: the phase reference for the next frame's scoring/validation, and the probe clock.</summary>
    private void FinishCommitFrame(in Matrix4x4 refVp)
    {
        lastCommitRefVp = refVp;
        hasLastCommitRef = true;

        if (probeArmed && --probeFramesRemaining <= 0)
        {
            probeArmed = false;
            ReportProbe();
        }
    }

    /// <summary>
    /// The committed GPU view-projection for the frame the caller is compositing, or false when there is none fresh.
    /// The inject path runs before this frame's present boundary, so its commit carries the current present index;
    /// the present-time path runs after the boundary advanced, so its commit carries the previous one.
    /// </summary>
    /// <param name="presentTimePath">True for the present-time composite, false for the pre-UI inject path.</param>
    /// <param name="viewProj">Receives the captured view-projection (Z column as uploaded; the render path rebuilds it).</param>
    public bool TryGetCommitted(bool presentTimePath, out Matrix4x4 viewProj)
    {
        viewProj = default;
        if (!lockedOn || !haveCommit || !IsCommitFresh(commitPresentIndex, presentIndex, presentTimePath))
            return false;

        viewProj = committedVp;
        if (presentTimePath)
            statConsumedPresent++;
        else
            statConsumedInject++;
        return true;
    }

    /// <summary>One-line capture state for stats and the camtrace report.</summary>
    public string Describe()
    {
        if (updateSubresourceHook == null)
            return "not installed";
        if (detourFaults >= 3)
            return "self-disabled (detour faults)";
        if (!active)
            return "off (injection point disabled)";
        if (!lockedOn)
            return $"discovering ({trackedCount} buffers observed, {familyCount} families, {commitFrames} main-pass frames)";

        return $"locked {lockedByteWidth} B ring @ offset {lockedOffset} {FormName(lockedForm)} via {MechanismName(lockedMechanisms)}; "
               + $"commits {statValidCommits}/{statCommits} valid, stale skips {statStaleSkips}, foreign rejects {statValidationFails}, "
               + $"fresh at inject {statConsumedInject} / present {statConsumedPresent}, locks {statLocks}";
    }

    // ---------------------------------------------------------------- detours

    private bool Relevant(nint context) => active && context == gameContext && tap is { SuppressSelf: false, IsInjecting: false };

    private int MapDetour(nint context, nint resource, uint subresource, int mapType, uint mapFlags, nint mappedOut)
    {
        var hr = mapHook!.Original(context, resource, subresource, mapType, mapFlags, mappedOut);

        // Write-intent maps only (2 = WRITE, 3 = READ_WRITE, 4 = WRITE_DISCARD, 5 = WRITE_NO_OVERWRITE).
        if (hr >= 0 && mapType >= 2 && subresource == 0 && mappedOut != 0 && Relevant(context))
        {
            try
            {
                // Learning keeps running while locked (the budget is otherwise idle): a ring buffer reallocated on
                // a zone change joins the tracked table and its size-class updates feed the locked extraction.
                if (FindOrLearn(resource) >= 0)
                {
                    var data = *(nint*)mappedOut;
                    if (data != 0)
                        RememberMapping(resource, data);
                }
            }
            catch (Exception ex)
            {
                OnDetourFault(ex);
            }
        }

        return hr;
    }

    private void UnmapDetour(nint context, nint resource, uint subresource)
    {
        if (subresource == 0 && Relevant(context))
        {
            try
            {
                var data = TakeMapping(resource);
                if (data != 0)
                    CapturePayload(resource, data, sourceOffset: 0, sourceLength: int.MaxValue, mechanism: 2);
            }
            catch (Exception ex)
            {
                OnDetourFault(ex);
            }
        }

        unmapHook!.Original(context, resource, subresource);
    }

    private void UpdateSubresourceDetour(nint context, nint dstResource, uint dstSubresource, nint dstBox, nint srcData, uint srcRowPitch, uint srcDepthPitch)
    {
        updateSubresourceHook!.Original(context, dstResource, dstSubresource, dstBox, srcData, srcRowPitch, srcDepthPitch);

        // The caller's source memory stays valid for the whole call, including this epilogue.
        if (dstSubresource == 0 && srcData != 0 && Relevant(context))
        {
            try
            {
                // Box-less updates carry the whole buffer; a box carries exactly [left, right) bytes and the
                // source allocation is only that long - never read past it.
                var offset = 0;
                var length = int.MaxValue;
                if (dstBox != 0)
                {
                    var box = (D3D11_BOX*)dstBox;
                    offset = (int)box->left;
                    length = (int)box->right - (int)box->left;
                    if (length <= 0)
                        return;
                }

                if (FindOrLearn(dstResource) >= 0)
                    CapturePayload(dstResource, srcData, offset, length, mechanism: 1);
            }
            catch (Exception ex)
            {
                OnDetourFault(ex);
            }
        }
    }

    private void CopyResourceDetour(nint context, nint dstResource, nint srcResource)
    {
        if (Relevant(context) && IsObservedBuffer(dstResource))
            copiesIntoTracked++;

        copyResourceHook!.Original(context, dstResource, srcResource);
    }

    private void CopySubresourceRegionDetour(nint context, nint dstResource, uint dstSubresource, uint dstX, uint dstY, uint dstZ, nint srcResource, uint srcSubresource, nint srcBox)
    {
        if (Relevant(context) && IsObservedBuffer(dstResource))
            copiesIntoTracked++;

        copySubresourceRegionHook!.Original(context, dstResource, dstSubresource, dstX, dstY, dstZ, srcResource, srcSubresource, srcBox);
    }

    private void OnDetourFault(Exception ex)
    {
        detourFaults++;
        if (!faultLogged)
        {
            faultLogged = true;
            NoireLogger.LogError(ex, "Draw3D: camera-constant capture faulted; after 3 faults it self-disables (struct snapshot fallback).", "Draw3D");
        }

        if (detourFaults >= 3)
            SetActive(false);
    }

    // ---------------------------------------------------------------- payload tracking

    private void RememberMapping(nint resource, nint data)
    {
        for (var i = 0; i < mapPending.Length; i++)
        {
            if (mapPending[i].Resource == 0 || mapPending[i].Resource == resource)
            {
                mapPending[i].Resource = resource;
                mapPending[i].Data = data;
                return;
            }
        }
    }

    private nint TakeMapping(nint resource)
    {
        for (var i = 0; i < mapPending.Length; i++)
        {
            if (mapPending[i].Resource == resource)
            {
                var data = mapPending[i].Data;
                mapPending[i] = default;
                return data;
            }
        }

        return 0;
    }

    /// <summary>Whether the pointer is a tracked buffer (no learning). Copy-counter check.</summary>
    private bool IsObservedBuffer(nint ptr)
    {
        for (var i = 0; i < trackedCount; i++)
        {
            if (tracked[i].Ptr == ptr)
                return true;
        }

        return false;
    }

    private bool IsIgnored(nint ptr)
    {
        for (var i = 0; i < ignoredCount; i++)
        {
            if (ignoredPtrs[i] == ptr)
                return true;
        }

        return false;
    }

    private void AddIgnored(nint ptr)
    {
        ignoredPtrs[ignoredCursor] = ptr;
        ignoredCursor = (ignoredCursor + 1) % MaxIgnored;
        if (ignoredCount < MaxIgnored)
            ignoredCount++;
    }

    /// <summary>
    /// Finds the tracked slot for a buffer, learning it (QI + GetDesc, budgeted per frame) on first sight while
    /// discovering. Only real constant buffers within the size bounds enter the table; everything else goes to the
    /// ignore ring. Returns -1 for unknown, over-budget, or ignored pointers.
    /// </summary>
    private int FindOrLearn(nint ptr)
    {
        for (var i = 0; i < trackedCount; i++)
        {
            if (tracked[i].Ptr == ptr)
                return i;
        }

        if (IsIgnored(ptr) || learnBudget <= 0)
            return -1;

        learnBudget--;
        if (!ComPtrUtil.TryQi<ID3D11Buffer>((IUnknown*)ptr, out var buffer))
        {
            ignoredNotBuffer++;
            AddIgnored(ptr);
            return -1;
        }

        D3D11_BUFFER_DESC desc;
        buffer.Get()->GetDesc(&desc);
        buffer.Dispose();

        if ((desc.BindFlags & (uint)D3D11_BIND_FLAG.D3D11_BIND_CONSTANT_BUFFER) == 0)
        {
            ignoredNoCbufferFlag++;
            AddIgnored(ptr);
            return -1;
        }

        if (desc.ByteWidth > TrackedBytes)
        {
            largeCbuffersSeen++; // visible in the probe report - the signal for a ring-allocation scheme
            ignoredTooLarge++;
            AddIgnored(ptr);
            return -1;
        }

        if (desc.ByteWidth < MinTrackedBytes)
        {
            ignoredTooSmall++;
            AddIgnored(ptr);
            return -1;
        }

        var slotIdx = AcquireTrackedSlot();
        if (slotIdx < 0)
            return -1;

        return Learn(slotIdx, ptr, (int)desc.ByteWidth);
    }

    /// <summary>
    /// A tracked slot for a new buffer: appends while capacity remains, else evicts an entry with no update this
    /// discovery (a freed buffer's stale pointer), round-robin so successive learns never thrash one slot while
    /// others sit stale. -1 when every slot is live.
    /// </summary>
    private int AcquireTrackedSlot()
    {
        if (trackedCount < MaxTrackedBuffers)
            return trackedCount;

        for (var step = 0; step < trackedCount; step++)
        {
            var i = (evictionCursor + step) % trackedCount;
            if (tracked[i].UpdatesSeen == 0)
            {
                evictionCursor = (i + 1) % trackedCount;
                statEvictions++;
                return i;
            }
        }

        return -1;
    }

    private int Learn(int slotIdx, nint ptr, int byteWidth)
    {
        ref var slot = ref tracked[slotIdx];
        var pooled = slot.Bytes; // the payload buffer is pooled and survives slot reuse
        slot = default;
        slot.Ptr = ptr;
        slot.ByteWidth = byteWidth;
        slot.LastBoundSlot = -1;
        slot.BestVpErr = float.MaxValue;
        slot.Bytes = pooled ?? new byte[TrackedBytes]; // one-time per slot

        if (slotIdx == trackedCount)
            trackedCount++;
        statLearns++;
        return slotIdx;
    }

    /// <summary>
    /// Copies an upload payload into the tracker (discovery) or extracts the locked window (steady state).
    /// <paramref name="sourceLength"/> is the number of valid bytes at <paramref name="data"/>
    /// (<see cref="int.MaxValue"/> for a whole-buffer source, whose length is the buffer's).
    /// </summary>
    private void CapturePayload(nint resource, nint data, int sourceOffset, int sourceLength, byte mechanism)
    {
        // Recorded before anything else, and independently of the tracking table: a buffer written once per
        // light is exactly the case the table cannot represent, since it only ever holds the final write.
        if (writeLog.Armed)
        {
            for (var i = 0; i < trackedCount; i++)
            {
                if (tracked[i].Ptr == resource)
                {
                    writeLog.Record(resource, tracked[i].ByteWidth, data, sourceLength);
                    break;
                }
            }
        }

        if (lockedOn)
        {
            // Size-class membership: any tracked buffer of the locked byte-width is a potential ring member (the
            // camera write rotates across the whole ring, so a fixed pointer set starves). Validation at commit
            // decides which extracted windows are actually the main view.
            for (var i = 0; i < trackedCount; i++)
            {
                if (tracked[i].Ptr != resource)
                    continue;

                if (tracked[i].ByteWidth != lockedByteWidth)
                    return;

                // The update must fully cover the locked 64-byte window; the source allocation is only
                // sourceLength bytes, so a partial box that clips the window is skipped, never over-read.
                var windowInSource = lockedOffset - sourceOffset;
                if (windowInSource < 0 || (long)windowInSource + 64 > sourceLength)
                    return;

                var floats = (float*)(data + windowInSource);
                var vp = ExtractMatrix(new ReadOnlySpan<float>(floats, 16), lockedForm == MatrixForm.ViewProjTransposed);
                pendingRing[pendingCursor] = new PendingMatrix { Vp = vp, Seq = ++pendingSeq };
                pendingCursor = (pendingCursor + 1) % PendingRingLength;
                lockedMechanisms |= mechanism;
                tracked[i].UpdatesSeen++; // keeps live ring members protected from slot eviction

                // Once locked the tracker has no reason to keep whole payloads, so it stops copying them and the
                // stored bytes freeze at whatever discovery last saw. Anything reading those bytes for another
                // purpose - the light search - needs them live, and asks for it by arming a window.
                if (fullCaptureFramesRemaining > 0)
                    CopyPayloadBytes(ref tracked[i], data, sourceOffset, sourceLength, mechanism);

                return;
            }

            return;
        }

        for (var i = 0; i < trackedCount; i++)
        {
            if (tracked[i].Ptr != resource)
                continue;

            ref var slot = ref tracked[i];
            if (!CopyPayloadBytes(ref slot, data, sourceOffset, sourceLength, mechanism))
                return;

            slot.UpdatesSeen++;

            // Small buffers are scored at capture time against a same-instant struct read: the per-view ring
            // overwrites each physical buffer several times per frame, so only scoring every write can see the
            // main-view upload - the commit-time last-write scan would usually find another view's constants.
            if (slot.ByteWidth <= SmallScanBytes)
                ScoreBufferNow(ref slot);
            return;
        }
    }

    /// <summary>
    /// Copies an upload payload into a tracked buffer's byte store. Returns false when there was nothing to copy.
    /// </summary>
    private bool CopyPayloadBytes(ref TrackedBuffer slot, nint data, int sourceOffset, int sourceLength, byte mechanism)
    {
        if (slot.Bytes == null || sourceOffset < 0 || sourceOffset >= TrackedBytes)
            return false;

        var copyLength = Math.Min(sourceLength, Math.Min(slot.ByteWidth - sourceOffset, TrackedBytes - sourceOffset));
        if (copyLength <= 0)
            return false;

        fixed (byte* dst = slot.Bytes)
            Buffer.MemoryCopy((void*)data, dst + sourceOffset, TrackedBytes - sourceOffset, copyLength);

        slot.ValidBytes = Math.Max(slot.ValidBytes, sourceOffset + copyLength);
        slot.Mechanisms |= mechanism;
        slot.UpdatedSinceScan = true;
        slot.FullCaptures++;
        return true;
    }

    /// <summary>
    /// Scores every window of a freshly-captured small-buffer payload against the struct camera read now, keeping
    /// the buffer's best match since the last commit. Budgeted per frame; skipped silently when the struct camera
    /// is unavailable (the merge at commit simply sees no small-path result that frame).
    /// </summary>
    private void ScoreBufferNow(ref TrackedBuffer slot)
    {
        var windows = (Math.Min(slot.ValidBytes, slot.ByteWidth) - 64) / 16 + 1;
        if (windows <= 0)
            return;

        // Buffers seen bound to the VS at the main pass are scored unconditionally: they are the only lock
        // candidates (the bound gate) and there are few of them. Unbound buffers spend the per-frame budget: the
        // per-draw upload stream is large enough to drain any budget every frame before the camera writes arrive,
        // so the bound set must never compete with it for scoring.
        if (slot.LastBoundSlot < 0)
        {
            if (scoreBudget < windows * 2)
                return;

            scoreBudget -= windows * 2;
        }

        if (!GameRenderSources.TryGetCamera(out var cam) || !cam.HasRenderCamera)
            return;

        // Two references, best fit wins: the uploads derive from the game's own view setup, up to a frame before
        // this capture instant, so under motion the same-instant read alone carries a frame of skew; the previous
        // commit's reference brackets it from the other side. At rest the two coincide.
        var refVp = cam.View * cam.Proj;
        var floats = MemoryMarshal.Cast<byte, float>(new ReadOnlySpan<byte>(slot.Bytes, 0, Math.Min(slot.ValidBytes, slot.ByteWidth)));
        for (var offset = 0; offset + 16 <= floats.Length; offset += 4)
        {
            var window = floats.Slice(offset, 16);
            for (var f = 0; f < 2; f++)
            {
                var form = (MatrixForm)f;
                var transposed = form == MatrixForm.ViewProjTransposed;
                var err = WindowError(window, in refVp, transposed, skipZColumn: true);
                if (hasLastCommitRef)
                {
                    var errPrev = WindowError(window, in lastCommitRefVp, transposed, skipZColumn: true);
                    if (!float.IsNaN(errPrev) && (float.IsNaN(err) || errPrev < err))
                        err = errPrev;
                }

                if (float.IsNaN(err) || err >= CandidateErr)
                    continue;

                slot.BestVpErr = MathF.Min(slot.BestVpErr, err);
                if (!slot.HasBestSinceCommit || err < slot.BestErrSinceCommit)
                {
                    slot.HasBestSinceCommit = true;
                    slot.BestErrSinceCommit = err;
                    slot.BestOffsetSinceCommit = offset * 4;
                    slot.BestFormSinceCommit = form;
                    slot.BestMatrixSinceCommit = ExtractMatrix(window, form == MatrixForm.ViewProjTransposed);
                }
            }
        }
    }

    // ---------------------------------------------------------------- discovery

    private void ResetDiscovery()
    {
        familyCount = 0;
        for (var i = 0; i < trackedCount; i++)
        {
            tracked[i].UpdatesSeen = 0;
            tracked[i].Mechanisms = 0;
            tracked[i].UpdatedSinceScan = false;
            tracked[i].LastBoundSlot = -1;
            tracked[i].BestVpErr = float.MaxValue;
            tracked[i].HasBestSinceCommit = false;
        }

        // The ignore ring is cleared so a pointer the allocator reused for a real cbuffer gets re-checked; the
        // verdicts are cheap to re-earn (budgeted QI) and a stale ignore would hide the camera forever.
        ignoredCount = 0;
        ignoredCursor = 0;
        ignoredNotBuffer = 0;
        ignoredNoCbufferFlag = 0;
        ignoredTooLarge = 0;
        ignoredTooSmall = 0;
        statLearns = 0;
        statEvictions = 0;
        statBudgetExhaustedFrames = 0;
        largeCbuffersSeen = 0;
        copiesIntoTracked = 0;
        commitFrames = 0;
    }

    /// <summary>
    /// Learns and marks the buffers bound to the VS at the main pass's first draw (discovery only). This is the
    /// primary discovery source, not just ranking: the camera constants must be bound here to be read by the world
    /// pass at all, so enrolling the bound buffers directly makes discovery immune to upload-stream noise starving
    /// the learn budget. The draw moment matters as much as the enrollment: at the OM bind, the VS slots still hold
    /// the previous pass's buffers, since the game binds its camera block between the OM bind and the first draw;
    /// sampling there would mislabel those leftovers as bound and hide the real camera family.
    /// </summary>
    private void LearnBoundBuffers(ID3D11DeviceContext* ctx)
    {
        var bound = stackalloc ID3D11Buffer*[VsSlotCount];
        ctx->VSGetConstantBuffers(0, VsSlotCount, bound);
        for (var s = 0; s < VsSlotCount; s++)
        {
            var b = bound[s];
            if (b == null)
                continue;

            var ptr = (nint)b;
            var idx = -1;
            for (var i = 0; i < trackedCount; i++)
            {
                if (tracked[i].Ptr == ptr)
                {
                    idx = i;
                    break;
                }
            }

            if (idx < 0)
            {
                // Bound to the VS = a constant buffer by construction; only the size gate applies, no budget.
                D3D11_BUFFER_DESC desc;
                b->GetDesc(&desc);
                if (desc.ByteWidth >= MinTrackedBytes && desc.ByteWidth <= TrackedBytes)
                {
                    var slotIdx = AcquireTrackedSlot();
                    if (slotIdx >= 0)
                        idx = Learn(slotIdx, ptr, (int)desc.ByteWidth);
                }
                else if (desc.ByteWidth > TrackedBytes)
                {
                    largeCbuffersSeen++; // a large cbuffer bound at the main pass is the ring-scheme signal
                }
            }

            if (idx >= 0)
                tracked[idx].LastBoundSlot = s;

            b->Release(); // VSGetConstantBuffers AddRef'd each returned buffer
        }
    }

    /// <summary>
    /// One discovery step (main-pass commit frame): merges the capture-time best matches (small buffers) and a
    /// last-write scan (larger buffers) into the family table, advances the winning family's streak, and locks when
    /// a stable winner with a strong match emerges. Family identity is (offset, layout); the physical buffer a
    /// match lands in only extends the family's member set, so the per-view ring rotation cannot reset a streak.
    /// </summary>
    private void AdvanceDiscovery(in Matrix4x4 refVp)
    {
        var bestIdx = -1;
        var bestErr = float.MaxValue;
        var bestBound = false;

        for (var i = 0; i < trackedCount; i++)
        {
            ref var slot = ref tracked[i];
            if (slot.Bytes == null)
                continue;

            var boundHere = slot.LastBoundSlot >= 0;

            // Small path: the best window scored at capture time since the last commit.
            if (slot.HasBestSinceCommit)
            {
                slot.HasBestSinceCommit = false;
                var f = UpsertFamily(slot.BestOffsetSinceCommit, slot.BestFormSinceCommit, slot.BestErrSinceCommit, slot.Ptr, boundHere);
                ConsiderWinner(f, slot.BestErrSinceCommit, boundHere, ref bestIdx, ref bestErr, ref bestBound);
            }

            // Commit-moment backstop for bound small buffers: score the current bytes against this frame's
            // reference as well. Covers uploads the capture-time path missed (activation mid-frame, an untapped
            // write route) at negligible cost - the bound set is a handful of small buffers.
            if (boundHere && slot.ByteWidth <= SmallScanBytes && slot.ValidBytes >= 64)
            {
                var boundFloats = MemoryMarshal.Cast<byte, float>(new ReadOnlySpan<byte>(slot.Bytes, 0, Math.Min(slot.ValidBytes, slot.ByteWidth)));
                for (var offset = 0; offset + 16 <= boundFloats.Length; offset += 4)
                {
                    var window = boundFloats.Slice(offset, 16);
                    for (var form = 0; form < 2; form++)
                    {
                        var err = WindowError(window, in refVp, form == 1, skipZColumn: true);
                        if (float.IsNaN(err) || err >= CandidateErr)
                            continue;

                        slot.BestVpErr = MathF.Min(slot.BestVpErr, err);
                        var f = UpsertFamily(offset * 4, (MatrixForm)form, err, slot.Ptr, bound: true);
                        ConsiderWinner(f, err, bound: true, ref bestIdx, ref bestErr, ref bestBound);
                    }
                }
            }

            // Large-path backstop: scan the last-written payload of bigger buffers against this frame's struct VP.
            // Steady discovery only pays for bound ones (the lock candidates); an armed probe scans them all so
            // the report can still expose a camera hiding in an unbound buffer if the bound assumption ever broke.
            if (slot.ByteWidth > SmallScanBytes && slot.UpdatedSinceScan && (slot.LastBoundSlot >= 0 || probeArmed))
            {
                slot.UpdatedSinceScan = false;
                var floats = MemoryMarshal.Cast<byte, float>(new ReadOnlySpan<byte>(slot.Bytes, 0, Math.Min(slot.ValidBytes, slot.ByteWidth)));
                for (var offset = 0; offset + 16 <= floats.Length; offset += 4)
                {
                    var window = floats.Slice(offset, 16);
                    for (var form = 0; form < 2; form++)
                    {
                        var err = WindowError(window, in refVp, form == 1, skipZColumn: true);
                        if (float.IsNaN(err) || err >= CandidateErr)
                            continue;

                        slot.BestVpErr = MathF.Min(slot.BestVpErr, err);
                        var f = UpsertFamily(offset * 4, (MatrixForm)form, err, slot.Ptr, boundHere);
                        ConsiderWinner(f, err, boundHere, ref bestIdx, ref bestErr, ref bestBound);
                    }
                }
            }
        }

        // Stability: two families can tie within noise (the same matrix stored twice per upload). An incumbent
        // already carrying a streak keeps the win while it stays within a factor of this frame's best - either
        // window holds the correct matrix; only alternation between them would prevent the lock.
        if (bestIdx >= 0)
        {
            for (var i = 0; i < familyCount; i++)
            {
                if (i == bestIdx || families[i].LastCommitSeen != commitFrames)
                    continue;

                if (families[i].Streak > families[bestIdx].Streak
                    && families[i].LastErr < bestErr * 2f
                    && (families[i].BoundSeen || !bestBound))
                {
                    bestIdx = i;
                }
            }
        }

        // Streaks: only this frame's winning family advances; other families that matched this frame reset. A
        // family with no match this frame keeps its streak (the camera family is written every world frame, so a
        // genuine winner is present at every commit).
        for (var i = 0; i < familyCount; i++)
        {
            if (families[i].LastCommitSeen != commitFrames)
                continue;

            if (i == bestIdx && families[i].LastErr < LockErr)
                families[i].Streak++;
            else
                families[i].Streak = 0;
        }

        if (bestIdx >= 0)
        {
            ref var winner = ref families[bestIdx];

            // BoundSeen is a hard lock requirement: the family must have been seen bound to the VS at the game's
            // main-pass bind. Beyond ranking, this is the guard against locking onto a camera-shaped upload that
            // the world pass never reads - in particular another NoireLib instance in the same process uploading
            // its own frame constants (its own uploads are invisible to itself, not to a sibling instance).
            if (winner.Streak >= LockStreak && winner.MinErr < StrongErr && winner.BoundSeen)
                LockOn(in winner);
        }
    }

    private void ConsiderWinner(int familyIdx, float err, bool bound, ref int bestIdx, ref float bestErr, ref bool bestBound)
    {
        if (familyIdx < 0)
            return;

        // Bound beats unbound at any error; among equals the smaller error wins.
        var better = bestIdx < 0
                     || (bound && !bestBound)
                     || (bound == bestBound && err < bestErr);
        if (better)
        {
            bestIdx = familyIdx;
            bestErr = err;
            bestBound = bound;
        }
    }

    private int UpsertFamily(int byteOffset, MatrixForm form, float err, nint memberPtr, bool bound)
    {
        var free = -1;
        var worst = -1;
        var worstErr = -1f;
        for (var i = 0; i < MaxFamilies; i++)
        {
            if (i < familyCount)
            {
                ref var f = ref families[i];
                if (f.Offset == byteOffset && f.Form == form)
                {
                    f.Hits++;
                    // Within one commit frame keep the family's best error; a later, worse member match (another
                    // view's constants in a sibling ring buffer) must not overwrite the main view's score.
                    if (f.LastCommitSeen != commitFrames || err < f.LastErr)
                        f.LastErr = err;
                    f.MinErr = MathF.Min(f.MinErr, err);
                    f.LastCommitSeen = commitFrames;
                    f.BoundSeen |= bound;
                    f.AddMember(memberPtr);
                    return i;
                }

                if (f.MinErr > worstErr)
                {
                    worstErr = f.MinErr;
                    worst = i;
                }
            }
            else if (free < 0)
            {
                free = i;
            }
        }

        var idx = free >= 0 ? free : (err < worstErr ? worst : -1);
        if (idx < 0)
            return -1;

        if (free >= 0)
            familyCount++;

        families[idx] = new Family
        {
            Offset = byteOffset,
            Form = form,
            Streak = 0,
            Hits = 1,
            MinErr = err,
            LastErr = err,
            LastCommitSeen = commitFrames,
            BoundSeen = bound,
        };
        families[idx].AddMember(memberPtr);
        return idx;
    }

    private void LockOn(in Family winner)
    {
        lockedOn = true;
        lockedOffset = winner.Offset;
        lockedForm = winner.Form;
        lockedMechanisms = 0;
        lockedByteWidth = 0;

        // The locked identity is the SIZE CLASS of the family's members (see the locked-state comment): resolve it
        // from any member's tracked entry. The camera write rotates across every same-size ring buffer, so the
        // extraction matches on byte-width, never on the pointers the family happened to accumulate.
        for (var i = 0; i < trackedCount; i++)
        {
            if (winner.HasMember(tracked[i].Ptr))
            {
                lockedByteWidth = tracked[i].ByteWidth;
                lockedMechanisms |= tracked[i].Mechanisms;
            }
        }

        invalidCommitStreak = 0;
        unlockLogged = false;
        pendingSeq = 0;
        lastCommittedSeq = 0;
        validationCountHwm = 0;
        Array.Clear(pendingRing);
        pendingCursor = 0;
        statLocks++;

        if (lockedByteWidth <= 0)
        {
            // No member resolved to a tracked entry (evicted between upsert and lock) - the lock cannot extract.
            lockedOn = false;
            return;
        }

        NoireLogger.LogInfo(
            $"Draw3D: camera constants locked - {lockedByteWidth} B ring, offset {lockedOffset}, {FormName(lockedForm)}, "
            + $"via {MechanismName(lockedMechanisms)}, best err {winner.MinErr:E2}. "
            + "The layer now projects with the exact GPU camera constants.", "Draw3D");
    }

    private void Unlock(string reason)
    {
        if (!lockedOn)
            return;

        lockedOn = false;
        haveCommit = false;
        if (!unlockLogged)
        {
            unlockLogged = true;
            NoireLogger.LogInfo($"Draw3D: camera-constant lock released ({reason}) - rediscovering; struct-snapshot fallback meanwhile.", "Draw3D");
        }
    }

    // ---------------------------------------------------------------- locked commit

    /// <summary>
    /// One locked-commit attempt: promotes the newest FRESH pending upload that validates against the struct camera
    /// to this frame's commit; false when none exists yet (the caller retries on the next draw). Newest wins by
    /// design: the game uploads the other views first and the main view last, before the pass draws.
    /// Validation runs against BOTH the draw-moment reference and the previous commit's reference: the uploads
    /// carry the previous frame's camera phase, so the captured constants equal the struct camera one frame back,
    /// and under extreme motion the draw-moment reference alone is a full frame ahead and would reject the true
    /// main view. Foreign views (shadow/water/portrait) differ by orders of magnitude from either.
    /// A pending already committed once is never re-committed: a frame whose fresh upload never arrives produces
    /// NO commit and falls back to the struct snapshot. Projecting a frames-old camera, which is what a starved
    /// pointer-set lock would do, is the one failure mode worse than the fallback.
    /// </summary>
    private bool TryCommitLocked(in Matrix4x4 refVp, bool firstAttempt)
    {
        var bestSeq = -1L;
        var bestIdx = -1;
        for (var i = 0; i < PendingRingLength; i++)
        {
            var seq = pendingRing[i].Seq;
            if (seq == 0 || seq <= bestSeq)
                continue;

            var err = MatrixError(in pendingRing[i].Vp, in refVp, skipZColumn: true);
            if (hasLastCommitRef)
            {
                var errPrev = MatrixError(in pendingRing[i].Vp, in lastCommitRefVp, skipZColumn: true);
                if (!float.IsNaN(errPrev) && (float.IsNaN(err) || errPrev < err))
                    err = errPrev;
            }

            if (err < SteadyErr)
            {
                bestSeq = seq;
                bestIdx = i;
            }
            else if (seq > validationCountHwm)
            {
                statValidationFails++; // a foreign view's constants (shadow/water/portrait), or a torn read
                validationCountHwm = seq; // an entry lingering in the ring is counted once, not once per commit
            }
        }

        if (bestIdx >= 0 && bestSeq <= lastCommittedSeq)
        {
            if (firstAttempt)
                statStaleSkips++;
            bestIdx = -1; // already projected with once - wait for the frame's fresh upload instead
        }

        if (bestIdx < 0)
            return false;

        committedVp = pendingRing[bestIdx].Vp;
        commitPresentIndex = presentIndex;
        haveCommit = true;
        lastCommittedSeq = bestSeq;
        return true;
    }

    // ---------------------------------------------------------------- pure logic (unit-tested)

    /// <summary>
    /// Normalized RMS error between a 16-float cbuffer window and a reference row-vector matrix, in the given
    /// layout. With <paramref name="skipZColumn"/> the reference's third column is excluded: the game's uploaded Z
    /// column legitimately differs from the struct-composed one (the render path rebuilds Z analytically), so the
    /// camera identity lives entirely in the X/Y/W columns. NaN when the window holds non-finite values.
    /// </summary>
    internal static float WindowError(ReadOnlySpan<float> window, in Matrix4x4 reference, bool transposed, bool skipZColumn)
    {
        Span<float> r = stackalloc float[16]
        {
            reference.M11, reference.M12, reference.M13, reference.M14,
            reference.M21, reference.M22, reference.M23, reference.M24,
            reference.M31, reference.M32, reference.M33, reference.M34,
            reference.M41, reference.M42, reference.M43, reference.M44,
        };

        double sumSq = 0, refSq = 0;
        var n = 0;
        for (var row = 0; row < 4; row++)
        {
            for (var col = 0; col < 4; col++)
            {
                if (skipZColumn && col == 2)
                    continue;

                var w = transposed ? window[col * 4 + row] : window[row * 4 + col];
                if (!float.IsFinite(w))
                    return float.NaN;

                var refv = r[row * 4 + col];
                var d = w - refv;
                sumSq += (double)d * d;
                refSq += (double)refv * refv;
                n++;
            }
        }

        if (n == 0)
            return float.NaN;

        return (float)(Math.Sqrt(sumSq / n) / (Math.Sqrt(refSq / n) + 1e-6));
    }

    /// <summary>Same error metric between two matrices (locked-commit validation path).</summary>
    internal static float MatrixError(in Matrix4x4 a, in Matrix4x4 b, bool skipZColumn)
    {
        Span<float> av = stackalloc float[16]
        {
            a.M11, a.M12, a.M13, a.M14, a.M21, a.M22, a.M23, a.M24,
            a.M31, a.M32, a.M33, a.M34, a.M41, a.M42, a.M43, a.M44,
        };
        return WindowError(av, in b, transposed: false, skipZColumn);
    }

    /// <summary>Reads a 16-float window as a row-vector matrix, transposing when the window holds the transpose.</summary>
    internal static Matrix4x4 ExtractMatrix(ReadOnlySpan<float> window, bool transposed)
    {
        var m = new Matrix4x4(
            window[0], window[1], window[2], window[3],
            window[4], window[5], window[6], window[7],
            window[8], window[9], window[10], window[11],
            window[12], window[13], window[14], window[15]);
        return transposed ? Matrix4x4.Transpose(m) : m;
    }

    /// <summary>
    /// Whether a commit made at <paramref name="commitIndex"/> belongs to the frame the caller is compositing. The
    /// inject path runs before its frame's present boundary (same index); the present-time path runs after the
    /// boundary advanced (previous index).
    /// </summary>
    internal static bool IsCommitFresh(long commitIndex, long presentIndex, bool presentTimePath)
        => commitIndex >= 0 && commitIndex == (presentTimePath ? presentIndex - 1 : presentIndex);

    // ---------------------------------------------------------------- probe report

    private static string FormName(MatrixForm form) => form == MatrixForm.ViewProj ? "VP" : "VP-transposed";

    private static string MechanismName(byte mechanisms) => mechanisms switch
    {
        1 => "UpdateSubresource",
        2 => "Map/Unmap",
        3 => "UpdateSubresource+Map",
        _ => "none observed",
    };

    /// <summary>Logs the full discovery observation table (armed by <c>/noire3d cbprobe</c>).</summary>
    private void ReportProbe()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Draw3D cbprobe: {commitFrames} main-pass frames observed, {trackedCount} small cbuffers tracked.");
        sb.AppendLine($"  state now: {Describe()}");
        sb.AppendLine($"  learning: {statLearns} learns ({statEvictions} evictions), budget-exhausted frames {statBudgetExhaustedFrames}; copies into observed buffers: {copiesIntoTracked}.");
        sb.AppendLine($"  ignored: not-a-buffer {ignoredNotBuffer}, no-cbuffer-flag {ignoredNoCbufferFlag}, > {TrackedBytes} B {ignoredTooLarge} (large cbuffers {largeCbuffersSeen}), < {MinTrackedBytes} B {ignoredTooSmall}.");
        sb.AppendLine("  tracked buffers (cbuffers 64 B..4 KB seen on the upload paths):");
        sb.AppendLine("    buffer | size | updates | via | VS slot | best VP window err");
        for (var i = 0; i < trackedCount; i++)
        {
            ref var t = ref tracked[i];
            var bound = t.LastBoundSlot >= 0 ? $"b{t.LastBoundSlot}" : "-";
            var best = t.BestVpErr < float.MaxValue ? t.BestVpErr.ToString("E2") : "-";
            sb.AppendLine($"    0x{t.Ptr:X} | {t.ByteWidth,4} | {t.UpdatesSeen,7} | {MechanismName(t.Mechanisms),-22} | {bound,7} | {best}");
        }

        sb.AppendLine("  window families (err = normalized RMS vs the struct VP on the X/Y/W columns, scored per upload; lower = closer; locking requires a VS-bound family):");
        sb.AppendLine("    offset | form | members | hits | streak | min err | last err | bound");
        for (var i = 0; i < familyCount; i++)
        {
            ref var f = ref families[i];
            sb.AppendLine($"    {f.Offset,6} | {FormName(f.Form),-13} | {f.MemberCount,7} | {f.Hits,4} | {f.Streak,6} | {f.MinErr:E2} | {f.LastErr:E2} | {(f.BoundSeen ? "yes" : "-")}");
        }

        if (familyCount == 0)
        {
            sb.AppendLine("    (none - no tracked upload matched the camera. If 'large cbuffers' above is non-zero the game may");
            sb.AppendLine("     use a ring-allocated scheme.)");
        }

        if (lockedOn && haveCommit)
        {
            sb.AppendLine($"  committed Z column (uploaded, informational): ({committedVp.M13:F5}, {committedVp.M23:F5}, {committedVp.M33:F5}, {committedVp.M43:F5})");
        }

        DiagnosticChat.Print($"Draw3D cbprobe: {(lockedOn ? "LOCKED - " + Describe() : $"{familyCount} families, not locked")} (details in log).");
        NoireLogger.LogInfo(sb.ToString(), "Draw3D");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        active = false;
        mapHook?.Dispose();
        unmapHook?.Dispose();
        updateSubresourceHook?.Dispose();
        copyResourceHook?.Dispose();
        copySubresourceRegionHook?.Dispose();
        mapHook = null;
        unmapHook = null;
        updateSubresourceHook = null;
        copyResourceHook = null;
        copySubresourceRegionHook = null;
        mapDetour = null;
        unmapDetour = null;
        updateSubresourceDetour = null;
        copyResourceDetour = null;
        copySubresourceRegionDetour = null;
        tap = null;
    }
}
