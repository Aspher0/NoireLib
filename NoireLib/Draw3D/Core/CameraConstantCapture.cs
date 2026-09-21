using NoireLib.Hooking;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

// Captures the camera constants the game draws the world with from its cbuffer uploads.
internal sealed unsafe class CameraConstantCapture : IDisposable
{
    // ID3D11DeviceContext vtable slots (base interface numbering).
    private const int SlotMap = 14;
    private const int SlotUnmap = 15;
    private const int SlotCopySubresourceRegion = 46;
    private const int SlotCopyResource = 47;
    private const int SlotUpdateSubresource = 48;

    // Buffers above TrackedBytes are only counted. Buffers above SmallScanBytes are scanned at commit only.
    private const int MaxTrackedBuffers = 48;
    private const int TrackedBytes = 4096;
    private const int MinTrackedBytes = 64;
    private const int SmallScanBytes = 512;
    private const int LearnBudgetPerFrame = 128;
    private const int ScoreBudgetPerFrame = 4096;
    private const int MaxMapPending = 64;          // the game maps dozens of view buffers before unmapping any
    private const int MaxFamilies = 32;
    private const int MaxMembers = 8;
    private const int PendingRingLength = 32;      // must exceed the writes that follow the main-view upload before its pass draws
    private const int VsSlotCount = 14;

    private const float CandidateErr = 2e-2f;
    private const float LockErr = 1e-2f;
    private const float StrongErr = 3e-3f;
    private const int LockStreak = 12;
    private const int UnlockAfterInvalidCommits = 90;

    internal enum MatrixForm : byte
    {
        ViewProj = 0,
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

    private struct TrackedBuffer
    {
        public nint Ptr;
        public int ByteWidth;
        public byte[]? Bytes;
        public int ValidBytes;
        public long UpdatesSeen;
        public long LastUpdatePresent;
        public byte Mechanisms;        // bit 1 = UpdateSubresource, bit 2 = Map/Unmap
        public bool UpdatedSinceScan;
        public long FullCaptures;
        public int LastBoundSlot;
        public float BestVpErr;

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
        public long Stamp;
    }

    // Streaks are per family: the camera write rotates across the members.
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
        public long Present;
    }

    private NoireHook<MapFn>? mapHook;
    private NoireHook<UnmapFn>? unmapHook;
    private NoireHook<UpdateSubresourceFn>? updateSubresourceHook;
    private NoireHook<CopyResourceFn>? copyResourceHook;
    private NoireHook<CopySubresourceRegionFn>? copySubresourceRegionHook;
    private MapFn? mapDetour;
    private UnmapFn? unmapDetour;
    private UpdateSubresourceFn? updateSubresourceDetour;
    private CopyResourceFn? copyResourceDetour;
    private CopySubresourceRegionFn? copySubresourceRegionDetour;
    private nint gameContext;
    private RenderTargetTap? tap;

    private volatile bool active;
    private int detourFaults;
    private bool faultLogged;

    private readonly TrackedBuffer[] tracked = new TrackedBuffer[MaxTrackedBuffers];
    private int trackedCount;
    private int learnBudget;
    private int scoreBudget;
    private long largeCbuffersSeen;
    private long copiesIntoTracked;

    private const int MaxIgnored = 1024;
    private const int IgnoredForgetPresents = 600;
    private readonly nint[] ignoredPtrs = new nint[MaxIgnored];
    private int ignoredCount;
    private int ignoredCursor;
    private long ignoredNotBuffer;
    private long ignoredNoCbufferFlag;
    private long ignoredTooLarge;
    private long ignoredTooSmall;

    private long statLearns;
    private long statEvictions;
    private long statTableFullRefusals;
    private long statBudgetExhaustedFrames;

    private readonly MapPendingEntry[] mapPending = new MapPendingEntry[MaxMapPending];

    private readonly Family[] families = new Family[MaxFamilies];
    private int familyCount;
    private long commitFrames;

    // The camera block rotates round-robin across same-size buffers. The lock is a byte width.
    private float commitNearPlane = 0.1f;
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
    private volatile bool awaitingMainDraw;
    private Matrix4x4 lastCommitRefVp;
    private bool hasLastCommitRef;

    private long statCommits;
    private long statValidCommits;
    private long statValidationFails;
    private readonly long[] statShapeRejects = new long[5];
    private ViewShape lastRejectedShape;
    private ViewShapeMismatch lastRejectedReason;
    private long statCaptureShapeRejects;
    private long statPriorFrameSkips;
    private long statLateCommits;
    private long statUntrackedCaptures;
    private long statMapTableOverflows;
    private long statMappingsDroppedAtPresent;
    private long mapStamp;
    private long priorFrameCountHwm;
    private int reuseStreak;
    private ViewShape captureRefShape;
    private bool hasCaptureRefShape;
    private long statStaleSkips;
    private long statConsumedInject;
    private long statConsumedPresent;
    private long statLocks;

    private long statMissNoUpload;
    private long statMissRejected;
    private long statMissLateArrival;
    private long missOpenPresent = -1;
    private long missOpenSeqHwm;

    private int probeFramesRemaining;
    private int fullCaptureFramesRemaining;

    private readonly ConstantWriteLog writeLog = new();
    private bool probeArmed;

    public bool Installed => updateSubresourceHook != null;

    public bool IsLocked => lockedOn;

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

    public bool FullCaptureArmed => fullCaptureFramesRemaining > 0;

    public void ArmFullCapture(int frames) => fullCaptureFramesRemaining = Math.Max(frames, 0);

    public void ArmWriteLog(int frames, int byteWidth = 0)
    {
        if (fullCaptureFramesRemaining <= 0)
            fullCaptureFramesRemaining = Math.Max(frames, 0) + 1;

        writeLog.Arm(frames, byteWidth);
    }

    public bool WriteLogTruncated => writeLog.Truncated;

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

    public bool WriteLogArmed => writeLog.Armed;

    public int WriteLogCount => writeLog.Count;

    public string DescribeWriteLog() => writeLog.Describe();

    public List<byte[]> WriteLogPayloads() => writeLog.DistinctPayloads();

    public int WriteLogSize => writeLog.SizeFilter;

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
            mapHook = new NoireHook<MapFn>((nint)vtable[SlotMap], mapDetour, DeviceHookOptions("Draw3D.CamCapture.Map"));
            unmapDetour = UnmapDetour;
            unmapHook = new NoireHook<UnmapFn>((nint)vtable[SlotUnmap], unmapDetour, DeviceHookOptions("Draw3D.CamCapture.Unmap"));
            updateSubresourceDetour = UpdateSubresourceDetour;
            updateSubresourceHook = new NoireHook<UpdateSubresourceFn>((nint)vtable[SlotUpdateSubresource], updateSubresourceDetour, DeviceHookOptions("Draw3D.CamCapture.UpdateSubresource"));
            copyResourceDetour = CopyResourceDetour;
            copyResourceHook = new NoireHook<CopyResourceFn>((nint)vtable[SlotCopyResource], copyResourceDetour, DeviceHookOptions("Draw3D.CamCapture.CopyResource"));
            copySubresourceRegionDetour = CopySubresourceRegionDetour;
            copySubresourceRegionHook = new NoireHook<CopySubresourceRegionFn>((nint)vtable[SlotCopySubresourceRegion], copySubresourceRegionDetour, DeviceHookOptions("Draw3D.CamCapture.CopySubresourceRegion"));
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Draw3D: failed to install the camera-constant capture hooks (the layer projects with the fallback camera instead).", "Draw3D");
            Dispose();
            return false;
        }

        NoireLogger.LogInfo("Draw3D: camera-constant capture installed (disabled until the injection point is armed).", "Draw3D");
        return true;
    }

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

    public void OnFrameBoundary()
    {
        presentIndex++;
        awaitingMainDraw = false;
        if (fullCaptureFramesRemaining > 0)
            fullCaptureFramesRemaining--;

        writeLog.OnFrameBoundary();
        if (learnBudget <= 0 && !lockedOn)
            statBudgetExhaustedFrames++;
        learnBudget = LearnBudgetPerFrame;
        scoreBudget = ScoreBudgetPerFrame;

        if (presentIndex % IgnoredForgetPresents == 0)
        {
            ignoredCount = 0;
            ignoredCursor = 0;
        }
        for (var i = 0; i < mapPending.Length; i++)
        {
            if (mapPending[i].Resource != 0)
                statMappingsDroppedAtPresent++;
        }

        Array.Clear(mapPending);
    }

    public void ArmProbe(int frames)
    {
        probeFramesRemaining = Math.Clamp(frames, 30, 6000);
        probeArmed = true;
        Unlock("probe armed");
        ResetDiscovery();
    }

    public bool WantsDrawSignal => active;

    private const int CommitRetryDraws = 96;
    private int commitRetriesLeft;

    // The camera block is uploaded after the OM bind and before the first draw.
    public void OnMainPassBind()
    {
        if (!active)
            return;

        awaitingMainDraw = true;
        commitRetriesLeft = CommitRetryDraws;
    }

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
            commitNearPlane = cam.NearPlane;
            hasCaptureRefShape = TryReadViewShape(in refVp, out captureRefShape) && captureRefShape.WScale >= 1e-4f;

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

                // An upload stamped with the missed frame but sequenced after its watch ended arrived too late.
                if (missOpenPresent >= 0)
                {
                    for (var i = 0; i < PendingRingLength; i++)
                    {
                        if (pendingRing[i].Present == missOpenPresent && pendingRing[i].Seq > missOpenSeqHwm)
                        {
                            statMissLateArrival++;
                            break;
                        }
                    }

                    missOpenPresent = -1;
                }
            }

            if (TryCommitLocked(firstAttempt))
            {
                awaitingMainDraw = false;
                invalidCommitStreak = 0;
                statValidCommits++;
                FinishCommitFrame(in refVp);
                return;
            }

            if (--commitRetriesLeft > 0)
                return;

            awaitingMainDraw = false;
            invalidCommitStreak++;

            var freshSeen = false;
            for (var i = 0; i < PendingRingLength; i++)
            {
                if (pendingRing[i].Seq > lastCommittedSeq && pendingRing[i].Present == presentIndex)
                {
                    freshSeen = true;
                    break;
                }
            }

            if (freshSeen)
                statMissRejected++;
            else
                statMissNoUpload++;
            missOpenPresent = presentIndex;
            missOpenSeqHwm = pendingSeq;

            // A frame with no upload keeps the last commit for one frame. A longer run means uploads are lost.
            if (!freshSeen && haveCommit && reuseStreak < 1)
            {
                reuseStreak++;
                statStaleSkips++;
                commitPresentIndex = presentIndex;
                FinishCommitFrame(in refVp);
                return;
            }

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

    public bool TryGetCommitted(bool presentTimePath, out Matrix4x4 viewProj)
    {
        viewProj = default;
        if (!presentTimePath && lockedOn)
            CommitNewestUpload();

        if (!lockedOn || !haveCommit || !IsCommitFresh(commitPresentIndex, presentIndex, presentTimePath))
            return false;

        viewProj = committedVp;
        if (presentTimePath)
            statConsumedPresent++;
        else
            statConsumedInject++;
        return true;
    }

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
               + $"commits {statValidCommits}/{statCommits} valid, reused {statStaleSkips}, prior-frame uploads skipped {statPriorFrameSkips}, committed at consumption {statLateCommits}, "
               + $"rejects {statValidationFails} unusable + {statCaptureShapeRejects} other views (projection {statShapeRejects[1]}, aspect {statShapeRejects[2]}, skew {statShapeRejects[3]}, mirrored {statShapeRejects[4]}; "
               + $"last {lastRejectedReason}: w {lastRejectedShape.WScale:0.###} sx {lastRejectedShape.ScaleX:0.###} sy {lastRejectedShape.ScaleY:0.###}), "
               + $"misses noUpload {statMissNoUpload} / rejected {statMissRejected} / arrived-late {statMissLateArrival}, "
               + $"map-table overflows {statMapTableOverflows}, captures from unlisted buffers {statUntrackedCaptures}, width queries {statWidthQueries}, mappings open at present {statMappingsDroppedAtPresent}, learns {statLearns} / evictions {statEvictions} / table-full refusals {statTableFullRefusals}, fresh at inject {statConsumedInject} / present {statConsumedPresent}, locks {statLocks}";
    }

    private bool Relevant(nint context) => active && context == gameContext && tap is { SuppressSelf: false, IsInjecting: false };

    private int MapDetour(nint context, nint resource, uint subresource, int mapType, uint mapFlags, nint mappedOut)
    {
        var hr = mapHook!.Original(context, resource, subresource, mapType, mapFlags, mappedOut);

        // 2 = WRITE, 3 = READ_WRITE, 4 = WRITE_DISCARD, 5 = WRITE_NO_OVERWRITE.
        if (hr >= 0 && mapType >= 2 && subresource == 0 && mappedOut != 0 && Relevant(context))
        {
            try
            {
                // A mapped row pitch is the buffer's byte width.
                var admitted = lockedOn
                    ? WidthOf(resource, (int)((D3D11_MAPPED_SUBRESOURCE*)mappedOut)->RowPitch) == lockedByteWidth
                    : FindOrLearn(resource) >= 0;
                if (admitted)
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

        if (dstSubresource == 0 && srcData != 0 && Relevant(context))
        {
            try
            {
                // With a box the source is only [left, right) bytes long. Reading past right faults.
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

                if (lockedOn ? WidthOf(dstResource) == lockedByteWidth : FindOrLearn(dstResource) >= 0)
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
            NoireLogger.LogError(ex, "Draw3D: camera-constant capture faulted; after 3 faults it self-disables (fallback camera meanwhile).", "Draw3D");
        }

        if (detourFaults >= 3)
            SetActive(false);
    }

    private void RememberMapping(nint resource, nint data)
    {
        var free = -1;
        for (var i = 0; i < mapPending.Length; i++)
        {
            if (mapPending[i].Resource == resource)
            {
                mapPending[i].Data = data;
                mapPending[i].Stamp = ++mapStamp;
                return;
            }

            if (free < 0 && mapPending[i].Resource == 0)
                free = i;
        }

        if (free < 0)
        {
            statMapTableOverflows++;
            free = 0;
            for (var i = 1; i < mapPending.Length; i++)
            {
                if (mapPending[i].Stamp < mapPending[free].Stamp)
                    free = i;
            }
        }

        mapPending[free].Resource = resource;
        mapPending[free].Data = data;
        mapPending[free].Stamp = ++mapStamp;
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

    private const int WidthCacheSize = 8192;
    private const int WidthCacheProbe = 16;
    private const long WidthRecheckPresents = 240;
    private readonly nint[] widthKeys = new nint[WidthCacheSize];
    private readonly int[] widthValues = new int[WidthCacheSize];
    private readonly long[] widthStamps = new long[WidthCacheSize];
    private long statWidthQueries;

    private int WidthOf(nint ptr, int widthHint = -1)
    {
        var home = (int)((((ulong)ptr >> 4) * 0x9E3779B97F4A7C15UL) >> 51) & (WidthCacheSize - 1);
        var slot = -1;
        for (var k = 0; k < WidthCacheProbe; k++)
        {
            var i = (home + k) & (WidthCacheSize - 1);
            if (widthKeys[i] == ptr)
            {
                var fresh = presentIndex - widthStamps[i] <= WidthRecheckPresents;
                var contradicted = widthHint >= 0 && widthHint == lockedByteWidth && widthValues[i] != widthHint;
                if (fresh && !contradicted)
                    return widthValues[i];
                slot = i;
                break;
            }

            if (slot < 0 && (widthKeys[i] == 0 || presentIndex - widthStamps[i] > WidthRecheckPresents))
                slot = i;
        }

        if (slot < 0)
            slot = home;

        var width = -1;
        statWidthQueries++;
        if (ComPtrUtil.TryQi<ID3D11Buffer>((IUnknown*)ptr, out var buffer))
        {
            D3D11_BUFFER_DESC desc;
            buffer.Get()->GetDesc(&desc);
            buffer.Dispose();
            if ((desc.BindFlags & (uint)D3D11_BIND_FLAG.D3D11_BIND_CONSTANT_BUFFER) != 0)
                width = (int)desc.ByteWidth;
        }

        widthKeys[slot] = ptr;
        widthValues[slot] = width;
        widthStamps[slot] = presentIndex;
        return width;
    }

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
            largeCbuffersSeen++;
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

    // Eviction must work on a full table, or a buffer reallocated later (a zone change) never gets a slot.
    private int AcquireTrackedSlot()
    {
        if (trackedCount < MaxTrackedBuffers)
            return trackedCount;

        var victim = -1;
        var victimLocked = true;
        var victimLast = long.MaxValue;
        for (var i = 0; i < trackedCount; i++)
        {
            var isLockedWidth = lockedOn && tracked[i].ByteWidth == lockedByteWidth;
            var last = tracked[i].UpdatesSeen == 0 ? long.MinValue : tracked[i].LastUpdatePresent;
            if (victim < 0 || (victimLocked && !isLockedWidth) || (victimLocked == isLockedWidth && last < victimLast))
            {
                victim = i;
                victimLocked = isLockedWidth;
                victimLast = last;
            }
        }

        if (victim < 0 || (victimLast >= presentIndex && victimLast != long.MinValue))
        {
            statTableFullRefusals++;
            return -1;
        }

        statEvictions++;
        return victim;
    }

    private int Learn(int slotIdx, nint ptr, int byteWidth)
    {
        ref var slot = ref tracked[slotIdx];
        var pooled = slot.Bytes;
        slot = default;
        slot.Ptr = ptr;
        slot.ByteWidth = byteWidth;
        slot.LastBoundSlot = -1;
        slot.BestVpErr = float.MaxValue;
        slot.Bytes = pooled ?? new byte[TrackedBytes];

        if (slotIdx == trackedCount)
            trackedCount++;
        statLearns++;
        return slotIdx;
    }

    private void CapturePayload(nint resource, nint data, int sourceOffset, int sourceLength, byte mechanism)
    {
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
            var ti = -1;
            for (var i = 0; i < trackedCount; i++)
            {
                if (tracked[i].Ptr == resource)
                {
                    ti = i;
                    break;
                }
            }

            var windowInSource = lockedOffset - sourceOffset;
            if (windowInSource < 0 || (long)windowInSource + 64 > sourceLength)
                return;

            var floats = (float*)(data + windowInSource);
            var vp = ExtractMatrix(new ReadOnlySpan<float>(floats, 16), lockedForm == MatrixForm.ViewProjTransposed);
            if (ti >= 0)
            {
                tracked[ti].UpdatesSeen++;
                tracked[ti].LastUpdatePresent = presentIndex;
            }
            else
            {
                statUntrackedCaptures++;
            }

            // Environment probes and shadow cascades share this byte width every frame. Their view shape rejects them.
            if (hasCaptureRefShape)
            {
                var mismatch = TryReadViewShape(in vp, out var shape) ? CompareViewShape(in shape, in captureRefShape) : ViewShapeMismatch.Projection;
                if (mismatch != ViewShapeMismatch.None)
                {
                    statCaptureShapeRejects++;
                    statShapeRejects[(int)mismatch]++;
                    lastRejectedShape = shape;
                    lastRejectedReason = mismatch;
                    return;
                }
            }

            pendingRing[pendingCursor] = new PendingMatrix { Vp = vp, Seq = ++pendingSeq, Present = presentIndex };
            pendingCursor = (pendingCursor + 1) % PendingRingLength;
            lockedMechanisms |= mechanism;

            if (ti >= 0 && fullCaptureFramesRemaining > 0)
                CopyPayloadBytes(ref tracked[ti], data, sourceOffset, sourceLength, mechanism);

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
            slot.LastUpdatePresent = presentIndex;

            // Each physical buffer is overwritten several times per frame.
            if (slot.ByteWidth <= SmallScanBytes)
                ScoreBufferNow(ref slot);
            return;
        }
    }

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

    private void ScoreBufferNow(ref TrackedBuffer slot)
    {
        var windows = (Math.Min(slot.ValidBytes, slot.ByteWidth) - 64) / 16 + 1;
        if (windows <= 0)
            return;

        if (slot.LastBoundSlot < 0)
        {
            if (scoreBudget < windows * 2)
                return;

            scoreBudget -= windows * 2;
        }

        if (!GameRenderSources.TryGetCamera(out var cam) || !cam.HasRenderCamera)
            return;

        // The struct camera runs a frame ahead of the uploads.
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

        // A stale entry for a reused address would hide the camera for the rest of the session.
        ignoredCount = 0;
        ignoredCursor = 0;
        ignoredNotBuffer = 0;
        ignoredNoCbufferFlag = 0;
        ignoredTooLarge = 0;
        ignoredTooSmall = 0;
        statLearns = 0;
        statEvictions = 0;
        statTableFullRefusals = 0;
        statBudgetExhaustedFrames = 0;
        largeCbuffersSeen = 0;
        copiesIntoTracked = 0;
        commitFrames = 0;
    }

    // At the OM bind the VS slots still hold the previous pass's buffers.
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
                    largeCbuffersSeen++;
                }
            }

            if (idx >= 0)
                tracked[idx].LastBoundSlot = s;

            b->Release();
        }
    }

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

            if (slot.HasBestSinceCommit)
            {
                slot.HasBestSinceCommit = false;
                var f = UpsertFamily(slot.BestOffsetSinceCommit, slot.BestFormSinceCommit, slot.BestErrSinceCommit, slot.Ptr, boundHere);
                ConsiderWinner(f, slot.BestErrSinceCommit, boundHere, ref bestIdx, ref bestErr, ref bestBound);
            }

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

        // The same matrix is stored twice per upload. An incumbent keeps the win within a factor of two, or the lock never settles.
        if (bestIdx >= 0)
        {
            for (var i = 0; i < familyCount; i++)
            {
                if (i == bestIdx || families[i].LastCommitSeen != commitFrames)
                    continue;

                if (families[i].Streak > families[bestIdx].Streak
                    && families[i].Offset <= families[bestIdx].Offset
                    && families[i].LastErr < bestErr * 2f
                    && (families[i].BoundSeen || !bestBound))
                {
                    bestIdx = i;
                }
            }
        }

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

            // Excludes camera-shaped uploads the world pass never reads.
            if (winner.Streak >= LockStreak && winner.MinErr < StrongErr && winner.BoundSeen)
                LockOn(in winner);
        }
    }

    private void ConsiderWinner(int familyIdx, float err, bool bound, ref int bestIdx, ref float bestErr, ref bool bestBound)
    {
        if (familyIdx < 0)
            return;

        // The world is drawn with the block's first view-projection (offset 96). The copy at 512 is Control's and lags it.
        const float tieErr = 1e-4f;
        var tie = bestIdx >= 0 && bound == bestBound && MathF.Abs(err - bestErr) <= tieErr;
        var better = bestIdx < 0
                     || (bound && !bestBound)
                     || (tie ? families[familyIdx].Offset < families[bestIdx].Offset : bound == bestBound && err < bestErr);
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
        missOpenPresent = -1;
        if (!unlockLogged)
        {
            unlockLogged = true;
            NoireLogger.LogInfo($"Draw3D: camera-constant lock released ({reason}) - rediscovering; fallback camera meanwhile.", "Draw3D");
        }
    }

    // A matrix invertible before the reversed-Z rebuild can be singular after it.
    private static bool IsUsableCamera(in Matrix4x4 m, float nearPlane)
    {
        var sum = MathF.Abs(m.M11) + MathF.Abs(m.M22) + MathF.Abs(m.M41) + MathF.Abs(m.M42) + MathF.Abs(m.M44);
        if (!float.IsFinite(sum) || sum <= 1e-6f)
            return false;

        if (!float.IsFinite(m.M12) || !float.IsFinite(m.M21) || !float.IsFinite(m.M43) || !float.IsFinite(m.M14))
            return false;

        var rebuilt = m;
        rebuilt.M13 = 0f;
        rebuilt.M23 = 0f;
        rebuilt.M33 = 0f;
        rebuilt.M43 = nearPlane > 1e-6f ? nearPlane : 0.1f;

        return Matrix4x4.Invert(rebuilt, out _);
    }

    private const float ViewShapeTolerance = 2e-2f;

    internal struct ViewShape
    {
        public float ScaleX;

        public float ScaleY;

        // 1 for perspective, 0 for orthographic.
        public float WScale;

        public float Skew;

        public int Handedness;

        public Vector3 Eye;
    }

    // In a perspective row-vector view-projection the W column is forward. X and Y are right and up plus a jitter term along forward.
    internal static bool TryReadViewShape(in Matrix4x4 m, out ViewShape shape)
    {
        shape = default;
        var c1 = new Vector3(m.M11, m.M21, m.M31);
        var c2 = new Vector3(m.M12, m.M22, m.M32);
        var c4 = new Vector3(m.M14, m.M24, m.M34);
        if (!float.IsFinite(c1.X + c1.Y + c1.Z + c2.X + c2.Y + c2.Z + c4.X + c4.Y + c4.Z))
            return false;

        var w = c4.Length();
        shape.WScale = w;
        if (w < 1e-4f)
            return true;

        var forward = c4 / w;
        var right = c1 - (Vector3.Dot(c1, forward) * forward);
        var up = c2 - (Vector3.Dot(c2, forward) * forward);
        shape.ScaleX = right.Length();
        shape.ScaleY = up.Length();
        if (shape.ScaleX < 1e-6f || shape.ScaleY < 1e-6f)
            return false;

        shape.Skew = Vector3.Dot(right, up) / (shape.ScaleX * shape.ScaleY);
        shape.Handedness = Vector3.Dot(Vector3.Cross(right, up), forward) >= 0f ? 1 : -1;

        // The eye is the point that lands on clip X = Y = W = 0.
        var basis = new Matrix4x4(
            m.M11, m.M12, m.M14, 0f,
            m.M21, m.M22, m.M24, 0f,
            m.M31, m.M32, m.M34, 0f,
            0f, 0f, 0f, 1f);
        if (Matrix4x4.Invert(basis, out var inverse))
            shape.Eye = Vector3.Transform(-new Vector3(m.M41, m.M42, m.M44), inverse);

        return true;
    }

    internal enum ViewShapeMismatch : byte
    {
        None = 0,
        Projection = 1,
        AspectRatio = 2,
        Skew = 3,
        Handedness = 4,
    }

    internal static ViewShapeMismatch CompareViewShape(in ViewShape candidate, in ViewShape reference)
    {
        if (reference.WScale < 1e-4f || candidate.WScale < 1e-4f
            || MathF.Abs(candidate.WScale - reference.WScale) > ViewShapeTolerance * reference.WScale)
            return ViewShapeMismatch.Projection;

        // The field of view changes (zoom, GPose) and the struct camera is a frame ahead. Only the aspect ratio is comparable.
        var candidateAspect = candidate.ScaleY / candidate.ScaleX;
        var referenceAspect = reference.ScaleY / reference.ScaleX;
        if (MathF.Abs(candidateAspect - referenceAspect) > ViewShapeTolerance * referenceAspect)
            return ViewShapeMismatch.AspectRatio;

        if (MathF.Abs(candidate.Skew) > ViewShapeTolerance)
            return ViewShapeMismatch.Skew;

        return candidate.Handedness != reference.Handedness ? ViewShapeMismatch.Handedness : ViewShapeMismatch.None;
    }

    // Some sessions upload the camera after the main pass's first draw.
    private void CommitNewestUpload()
    {
        var bestSeq = -1L;
        var bestIdx = -1;
        for (var i = 0; i < PendingRingLength; i++)
        {
            ref var entry = ref pendingRing[i];
            // Where the upload falls against the present counter varies per session. The stamp is only an age bound.
            if (entry.Seq <= bestSeq || entry.Present < presentIndex - 1)
                continue;

            if (!IsUsableCamera(in entry.Vp, commitNearPlane))
                continue;

            bestSeq = entry.Seq;
            bestIdx = i;
        }

        if (bestIdx < 0)
            return;

        if (bestSeq != lastCommittedSeq || commitPresentIndex != presentIndex)
            statLateCommits += bestSeq > lastCommittedSeq ? 1 : 0;

        committedVp = pendingRing[bestIdx].Vp;
        commitPresentIndex = presentIndex;
        haveCommit = true;
        lastCommittedSeq = Math.Max(lastCommittedSeq, bestSeq);
        reuseStreak = 0;
        invalidCommitStreak = 0;
    }

    // Uploads stamped with an earlier present are the previous frame's camera uploaded again.
    private bool TryCommitLocked(bool firstAttempt)
    {
        var bestSeq = -1L;
        var bestIdx = -1;
        for (var i = 0; i < PendingRingLength; i++)
        {
            var seq = pendingRing[i].Seq;
            if (seq == 0)
                continue;

            if (!IsUsableCamera(in pendingRing[i].Vp, commitNearPlane))
            {
                if (seq > validationCountHwm)
                {
                    statValidationFails++;
                    validationCountHwm = seq;
                }

                continue;
            }

            if (pendingRing[i].Present != presentIndex)
            {
                if (seq > priorFrameCountHwm)
                {
                    statPriorFrameSkips++;
                    priorFrameCountHwm = seq;
                }

                continue;
            }

            if (seq > bestSeq)
            {
                bestSeq = seq;
                bestIdx = i;
            }
        }

        if (bestIdx < 0)
            return false;

        if (bestSeq <= lastCommittedSeq)
        {
            commitPresentIndex = presentIndex;
            return true;
        }

        reuseStreak = 0;
        committedVp = pendingRing[bestIdx].Vp;
        commitPresentIndex = presentIndex;
        haveCommit = true;
        lastCommittedSeq = bestSeq;
        return true;
    }

    // Skips the Z column and removes TAA jitter from both sides. The drawn camera is jittered and the struct is not.
    internal static float WindowError(ReadOnlySpan<float> window, in Matrix4x4 reference, bool transposed, bool skipZColumn)
    {
        if (skipZColumn)
        {
            for (var k = 0; k < 16; k++)
            {
                if (!float.IsFinite(window[k]))
                    return float.NaN;
            }

            var candidate = RemoveTemporalJitter(ExtractMatrix(window, transposed), out _);
            var centredRef = RemoveTemporalJitter(in reference, out _);
            Span<float> cv = stackalloc float[16]
            {
                candidate.M11, candidate.M12, candidate.M13, candidate.M14,
                candidate.M21, candidate.M22, candidate.M23, candidate.M24,
                candidate.M31, candidate.M32, candidate.M33, candidate.M34,
                candidate.M41, candidate.M42, candidate.M43, candidate.M44,
            };
            return RawWindowError(cv, in centredRef, transposed: false, skipZColumn: true);
        }

        return RawWindowError(window, in reference, transposed, skipZColumn);
    }

    private static float RawWindowError(ReadOnlySpan<float> window, in Matrix4x4 reference, bool transposed, bool skipZColumn)
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

    internal static float MatrixError(in Matrix4x4 a, in Matrix4x4 b, bool skipZColumn)
    {
        Span<float> av = stackalloc float[16]
        {
            a.M11, a.M12, a.M13, a.M14, a.M21, a.M22, a.M23, a.M24,
            a.M31, a.M32, a.M33, a.M34, a.M41, a.M42, a.M43, a.M44,
        };
        return WindowError(av, in b, transposed: false, skipZColumn);
    }

    // The game rasterizes half a pixel right and down of its camera constants (measured 0.3 cm error with it, 1.4 cm without).
    internal static Matrix4x4 ApplyPixelOffset(in Matrix4x4 m, Vector2 pixels, Vector2 displaySize)
    {
        if (displaySize.X <= 0f || displaySize.Y <= 0f)
            return m;

        var dx = 2f * pixels.X / displaySize.X;
        var dy = -2f * pixels.Y / displaySize.Y;
        var r = m;
        r.M11 += dx * m.M14;
        r.M21 += dx * m.M24;
        r.M31 += dx * m.M34;
        r.M41 += dx * m.M44;
        r.M12 += dy * m.M14;
        r.M22 += dy * m.M24;
        r.M32 += dy * m.M34;
        r.M42 += dy * m.M44;
        return r;
    }

    // Only a layer composited after the TAA resolve wants the jitter removed.
    internal static Matrix4x4 RemoveTemporalJitter(in Matrix4x4 m, out Vector2 jitter)
    {
        var c4 = new Vector3(m.M14, m.M24, m.M34);
        var w2 = Vector3.Dot(c4, c4);
        if (w2 < 1e-8f)
        {
            jitter = Vector2.Zero;
            return m;
        }

        var jx = Vector3.Dot(new Vector3(m.M11, m.M21, m.M31), c4) / w2;
        var jy = Vector3.Dot(new Vector3(m.M12, m.M22, m.M32), c4) / w2;
        jitter = new Vector2(jx, jy);

        var r = m;
        r.M11 -= jx * m.M14;
        r.M21 -= jx * m.M24;
        r.M31 -= jx * m.M34;
        r.M41 -= jx * m.M44;
        r.M12 -= jy * m.M14;
        r.M22 -= jy * m.M24;
        r.M32 -= jy * m.M34;
        r.M42 -= jy * m.M44;
        return r;
    }

    internal static Matrix4x4 ExtractMatrix(ReadOnlySpan<float> window, bool transposed)
    {
        var m = new Matrix4x4(
            window[0], window[1], window[2], window[3],
            window[4], window[5], window[6], window[7],
            window[8], window[9], window[10], window[11],
            window[12], window[13], window[14], window[15]);
        return transposed ? Matrix4x4.Transpose(m) : m;
    }

    internal static bool IsCommitFresh(long commitIndex, long presentIndex, bool presentTimePath)
        => commitIndex >= 0 && commitIndex == (presentTimePath ? presentIndex - 1 : presentIndex);

    private static string FormName(MatrixForm form) => form == MatrixForm.ViewProj ? "VP" : "VP-transposed";

    private static string MechanismName(byte mechanisms) => mechanisms switch
    {
        1 => "UpdateSubresource",
        2 => "Map/Unmap",
        3 => "UpdateSubresource+Map",
        _ => "none observed",
    };

    private void ReportProbe()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Draw3D cbprobe: {commitFrames} main-pass frames observed, {trackedCount} small cbuffers tracked.");
        sb.AppendLine($"  state now: {Describe()}");
        sb.AppendLine($"  learning: {statLearns} learns ({statEvictions} evictions, {statTableFullRefusals} refused with the table full), budget-exhausted frames {statBudgetExhaustedFrames}; copies into observed buffers: {copiesIntoTracked}.");
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

        NoireLogger.PrintToChat($"Draw3D cbprobe: {(lockedOn ? "LOCKED - " + Describe() : $"{familyCount} families, not locked")} (details in log).");
        NoireLogger.LogInfo(sb.ToString(), "Draw3D");
    }

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

    // These run thousands of times per frame on unnamed vtable slots.
    private static HookOptions DeviceHookOptions(string name) => new()
    {
        Name = name,
        AutoEnable = false,
        Guard = HookGuardMode.None,
        Verification = HookVerificationPolicy.Ignore,
        CollectStats = false,
    };

}
