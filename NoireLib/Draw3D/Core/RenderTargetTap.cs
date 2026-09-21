using NoireLib.Hooking;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D.Core;

// Render-thread hooks on the game's D3D11 immediate context. Each hook stays disabled until something needs it.
internal sealed unsafe class RenderTargetTap : IDisposable
{
    // ID3D11DeviceContext vtable slots.
    private const int SlotDrawIndexed = 12;
    private const int SlotDraw = 13;
    private const int SlotDrawIndexedInstanced = 20;
    private const int SlotDrawInstanced = 21;
    private const int SlotOmSetRenderTargets = 33;
    private const int SlotRsSetViewports = 44;
    private const int MaxBinds = 640;
    private const int MaxMultiBinds = 32;
    private const int MaxTargetsPerBind = 8;
    // The G-buffer pass binds five. A three-target bind on the same depth carries a different layout.
    private const int GBufferMinTargets = 5;
    private const int CaptureWarmupFrames = 6;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void OmSetRenderTargetsFn(nint context, uint numViews, nint ppRenderTargetViews, nint pDepthStencilView);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void DrawIndexedFn(nint context, uint indexCount, uint startIndex, int baseVertex);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void DrawFn(nint context, uint vertexCount, uint startVertex);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void DrawIndexedInstancedFn(nint context, uint indexCountPerInstance, uint instanceCount, uint startIndex, int baseVertex, uint startInstance);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void DrawInstancedFn(nint context, uint vertexCountPerInstance, uint instanceCount, uint startVertex, uint startInstance);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void RsSetViewportsFn(nint context, uint numViewports, nint pViewports);

    private readonly record struct Bind(uint NumViews, nint Rtv0Resource, DXGI_FORMAT Format, uint Width, uint Height, bool HasDsv, bool IsBackbuffer, int DrawCount);

    private readonly record struct TargetInfo(nint Resource, DXGI_FORMAT Format, uint Width, uint Height);

    private NoireHook<OmSetRenderTargetsFn>? omHook;
    private NoireHook<DrawIndexedFn>? drawIndexedHook;
    private NoireHook<DrawFn>? drawHook;
    private NoireHook<DrawIndexedInstancedFn>? drawIndexedInstancedHook;
    private NoireHook<DrawInstancedFn>? drawInstancedHook;
    private NoireHook<RsSetViewportsFn>? rsSetViewportsHook;
    private RenderDevice? device;
    private OmSetRenderTargetsFn? omDetour;
    private DrawIndexedFn? drawIndexedDetour;
    private DrawFn? drawDetour;
    private DrawIndexedInstancedFn? drawIndexedInstancedDetour;
    private DrawInstancedFn? drawInstancedDetour;
    private RsSetViewportsFn? rsSetViewportsDetour;
    private nint gameContext;

    private readonly Bind[] binds = new Bind[MaxBinds];
    private int bindCount;

    private ShadowProbe? shadowProbe;

    private readonly int[] multiBindAt = new int[MaxMultiBinds];
    private readonly TargetInfo[] multiBindTargets = new TargetInfo[MaxMultiBinds * MaxTargetsPerBind];
    private readonly int[] multiBindCounts = new int[MaxMultiBinds];
    private int multiBindCount;
    private int drawCounter;
    private volatile int state; // 0 = idle, 1 = warming up, 2 = capturing
    private int warmupLeft;

    // Each dump is a full-resolution copy and a synchronous map. The frame stalls.
    private const int MaxFrameDumps = 16;
    private int dumpFrom = -1;
    private int dumpCount;
    private int dumpStride;
    private int dumpsWritten;
    private string dumpFolder = string.Empty;

    // A frame has no fixed length. Bind indices do not carry across runs.
    private int lastFrameBindCount;

    private readonly nint[] knownBackbuffers = new nint[8];
    private int knownBackbufferCount;

    private nint presentBuffer;
    private nint candidatePresentBuffer;    // RTV seen right before a swapchain bind this frame
    private nint lastNonBackbufferRtv;
    private int presentBufferBinds;
    private int presentBufferBindsLastFrame;
    private bool injectedThisFrame;
    private bool sawDepthBindLastFrame;
    private int injectedAtBind;
    private int injectedAtBindLastFrame;
    private InjectRule injectedByLastFrame;
    private InjectRule injectedBy;
    private bool sawDepthBindThisFrame;
    private volatile bool injecting;

    // Snapshotted at the first main-scene bind and locked for the frame. A shadow-pass snapshot is provisional.
    private GameRenderSources.CameraData worldCamera;
    private volatile bool hasWorldCamera;
    private bool mainDepthSeen;
    private nint frameSceneDepthTex;

    // The callback draws into the game's own targets and must restore every pipeline state it changes.
    public Action? GBufferInjector { get; set; }

    public bool GBufferInjectionEnabled
    {
        get => gbufferInjectionEnabled;
        set
        {
            if (gbufferInjectionEnabled == value)
                return;

            gbufferInjectionEnabled = value;
            RefreshOmHookState();
        }
    }

    private bool gbufferInjectionEnabled;

    private bool gbufferPassArmed;
    private bool gbufferDoneThisFrame;

    // A group with no draws is a cached map. Drawing into it again stamps a second silhouette.
    public Action<nint>? ShadowInjector { get; set; }

    public bool ShadowInjectionEnabled
    {
        get => shadowInjectionEnabled;
        set
        {
            if (shadowInjectionEnabled == value)
                return;

            shadowInjectionEnabled = value;
            RefreshOmHookState();
        }
    }

    private bool shadowInjectionEnabled;

    public Action? ShadowFrameBoundary { get; set; }

    private bool shadowBindActive;
    private bool shadowBindSawDraw;

    public bool SuppressSelf;

    public bool IsInjecting => injecting;

    public CameraConstantCapture? Capture;

    public bool InjectionEnabled { get; private set; }

    public Func<nint, bool>? Injector { get; set; }

    // Still readable at present time, with the game's native UI drawn into it.
    public nint PresentBuffer => presentBuffer;

    public int PresentBufferBindsLastFrame => presentBufferBindsLastFrame;

    internal enum InjectRule : byte
    {
        None = 0,
        // The first present-buffer bind carrying a depth-stencil: the world-space UI pass.
        FirstDepthBind = 1,
        LastBind = 2,
        ForcedOrdinal = 3,
    }

    public string InjectionDescription => injectedByLastFrame == InjectRule.None
        ? $"not injected ({presentBufferBindsLastFrame} present-buffer binds)"
        : $"{injectedByLastFrame} at bind {injectedAtBindLastFrame} of {presentBufferBindsLastFrame}";

    // 0 places the layer automatically. The pass count changes with the upscaler, glare, dynamic resolution and group pose.
    public int InjectOrdinal { get; set; }

    public bool TryGetWorldCamera(out GameRenderSources.CameraData camera)
    {
        camera = worldCamera;
        return hasWorldCamera;
    }

    public bool Installed => omHook != null;

    public bool Install(RenderDevice device)
    {
        if (omHook != null)
            return true;

        var ctx = device.Context;
        if (ctx == null)
            return false;

        gameContext = (nint)ctx;
        this.device = device;
        var vtable = *(void***)ctx;

        try
        {
            omDetour = OmDetour;
            omHook = new NoireHook<OmSetRenderTargetsFn>((nint)vtable[SlotOmSetRenderTargets], omDetour, DeviceHookOptions("Draw3D.OMSetRenderTargets"));
            drawIndexedDetour = DrawIndexedDetour;
            drawIndexedHook = new NoireHook<DrawIndexedFn>((nint)vtable[SlotDrawIndexed], drawIndexedDetour, DeviceHookOptions("Draw3D.DrawIndexed"));
            drawDetour = DrawDetour;
            drawHook = new NoireHook<DrawFn>((nint)vtable[SlotDraw], drawDetour, DeviceHookOptions("Draw3D.Draw"));
            drawIndexedInstancedDetour = DrawIndexedInstancedDetour;
            drawIndexedInstancedHook = new NoireHook<DrawIndexedInstancedFn>((nint)vtable[SlotDrawIndexedInstanced], drawIndexedInstancedDetour, DeviceHookOptions("Draw3D.DrawIndexedInstanced"));
            drawInstancedDetour = DrawInstancedDetour;
            drawInstancedHook = new NoireHook<DrawInstancedFn>((nint)vtable[SlotDrawInstanced], drawInstancedDetour, DeviceHookOptions("Draw3D.DrawInstanced"));
            rsSetViewportsDetour = RsSetViewportsDetour;
            rsSetViewportsHook = new NoireHook<RsSetViewportsFn>((nint)vtable[SlotRsSetViewports], rsSetViewportsDetour, DeviceHookOptions("Draw3D.RSSetViewports"));
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Draw3D: failed to install the render-thread hook (pre-UI features unavailable).", "Draw3D");
            Dispose();
            return false;
        }

        NoireLogger.LogInfo("Draw3D: render-thread hook installed (disabled until armed/enabled).", "Draw3D");
        return true;
    }

    public void SetInjection(bool enabled)
    {
        InjectionEnabled = enabled;
        RefreshOmHookState();
    }

    public void ArmShadowProbe()
    {
        if (omHook == null)
            return;

        shadowProbe ??= new ShadowProbe();
        shadowProbe.Arm();
    }

    public void ArmCapture()
    {
        if (omHook == null)
            return;

        bindCount = 0;
        multiBindCount = 0;
        warmupLeft = CaptureWarmupFrames;
        state = 1;
        dumpFrom = -1;
        dumpCount = 0;
        RefreshOmHookState();
    }

    public void ArmFrameDump(int from, int count, string folder)
    {
        if (omHook == null)
            return;

        ArmCapture();
        dumpFrom = Math.Max(0, from);
        dumpCount = Math.Clamp(count, 1, MaxFrameDumps);
        dumpStride = 0;
        dumpsWritten = 0;
        dumpFolder = folder;
    }

    public int ArmFrameSweep(int count, string folder)
    {
        if (omHook == null)
            return 0;

        ArmFrameDump(0, count, folder);

        const int AssumedFrameBinds = 128;
        var length = lastFrameBindCount > 0 ? lastFrameBindCount : AssumedFrameBinds;
        dumpStride = Math.Max(1, length / dumpCount);
        return dumpStride;
    }

    // Runs before the game's new bind is applied, the only moment the previous target's contents are final.
    private void DumpFinishedBind()
    {
        var finished = bindCount - 1;
        if (dumpFrom < 0 || finished < 0 || dumpsWritten >= dumpCount)
            return;

        var wanted = dumpStride > 0
            ? finished % dumpStride == 0
            : finished >= dumpFrom && finished < dumpFrom + dumpCount;

        if (!wanted)
            return;

        var resource = binds[finished].Rtv0Resource;
        if (resource == 0 || device is not { } dev)
            return;

        dumpsWritten++;

        try
        {
            var path = System.IO.Path.Combine(dumpFolder, $"frame_bind{finished:D3}.bmp");
            var note = GBufferProbe.Dump(dev, resource, path);
            NoireLogger.LogInfo($"[FrameDump] bind {finished}: {note}", "Draw3D");

            // The light volumes' stencil mark only exists between the geometry and lighting passes.
            if (GameRenderSources.TryGetDepthTexture(out var depth) && depth.Texture != 0)
            {
                var stencilPath = System.IO.Path.Combine(dumpFolder, $"frame_bind{finished:D3}_stencil.bmp");
                NoireLogger.LogInfo($"[FrameDump] bind {finished}: {GBufferProbe.DumpStencil(dev, depth.Texture, stencilPath)}", "Draw3D");
            }
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Draw3D: frame dump of bind {finished} failed.", "Draw3D");
        }
    }

    public void OnPresent(nint backbufferTexture)
    {
        RememberBackbuffer(backbufferTexture);
        Capture?.OnFrameBoundary();
        shadowProbe?.OnFrameBoundary();
        if (shadowInjectionEnabled)
            ShadowFrameBoundary?.Invoke();

        if (candidatePresentBuffer != 0)
            presentBuffer = candidatePresentBuffer;
        candidatePresentBuffer = 0;
        lastNonBackbufferRtv = 0;
        presentBufferBindsLastFrame = presentBufferBinds;
        presentBufferBinds = 0;
        sawDepthBindLastFrame = sawDepthBindThisFrame;
        sawDepthBindThisFrame = false;
        injectedByLastFrame = injectedThisFrame ? injectedBy : InjectRule.None;
        injectedAtBindLastFrame = injectedThisFrame ? injectedAtBind : 0;
        injectedThisFrame = false;
        injectedBy = InjectRule.None;
        injectedAtBind = 0;
        hasWorldCamera = false;
        mainDepthSeen = false;

        gbufferPassArmed = false;
        gbufferDoneThisFrame = false;
        shadowBindActive = false;
        shadowBindSawDraw = false;
        frameSceneDepthTex = GameRenderSources.TryGetDepthTexture(out var sceneDepth) ? sceneDepth.Texture : 0;

        switch (state)
        {
            case 1:
                if (--warmupLeft <= 0)
                {
                    bindCount = 0;
                    multiBindCount = 0;
                    drawCounter = 0;
                    state = 2;
                    RefreshOmHookState();
                }

                break;
            case 2:
                Flush();
                state = 0;
                RefreshOmHookState();
                break;
        }
    }

    private void RefreshOmHookState()
    {
        var wanted = InjectionEnabled || state == 2 || shadowInjectionEnabled;
        if (omHook != null && omHook.IsEnabled != wanted)
            omHook.SetEnabled(wanted);

        rsSetViewportsHook?.SetEnabled(shadowInjectionEnabled);

        Capture?.SetActive(InjectionEnabled);
        RefreshDrawHookState();
    }

    private void RefreshDrawHookState()
        => SetDrawHooksEnabled(state == 2 || GBufferInjectionEnabled || shadowInjectionEnabled || (Capture?.WantsDrawSignal ?? false));

    private void SetDrawHooksEnabled(bool enabled)
    {
        drawIndexedHook?.SetEnabled(enabled);
        drawHook?.SetEnabled(enabled);
        drawIndexedInstancedHook?.SetEnabled(enabled);
        drawInstancedHook?.SetEnabled(enabled);
    }

    private void RememberBackbuffer(nint texture)
    {
        if (texture == 0)
            return;

        for (var i = 0; i < knownBackbufferCount; i++)
        {
            if (knownBackbuffers[i] == texture)
                return;
        }

        if (knownBackbufferCount < knownBackbuffers.Length)
            knownBackbuffers[knownBackbufferCount++] = texture;
    }

    private bool IsBackbuffer(nint resource)
    {
        for (var i = 0; i < knownBackbufferCount; i++)
        {
            if (knownBackbuffers[i] == resource)
                return true;
        }

        return false;
    }

    private bool Counting(nint context) => state == 2 && !SuppressSelf && !injecting && context == gameContext;

    private void OmDetour(nint context, uint numViews, nint ppRtvs, nint pDsv)
    {
        if (state == 2 && dumpFrom >= 0 && !injecting && !SuppressSelf && context == gameContext)
            DumpFinishedBind();

        // The shadow group's state stays bound until Original applies the new targets.
        TryInjectShadowAtGroupEnd(context);

        if (!injecting && context == gameContext)
        {
            shadowBindActive = false;
            shadowBindSawDraw = false;
        }

        omHook!.Original(context, numViews, ppRtvs, pDsv);

        if (injecting || SuppressSelf || context != gameContext)
            return;

        var rtv0 = ResolveRtv0Resource(numViews, ppRtvs);

        if (shadowProbe is { Armed: true } && rtv0 == 0 && pDsv != 0)
            shadowProbe.OnDepthOnlyBind(pDsv, IsMainSceneDepth(pDsv));

        shadowBindActive = shadowInjectionEnabled && rtv0 == 0 && pDsv != 0 && !IsMainSceneDepth(pDsv);

        // The present-composition buffer is the RTV bound right before a swapchain backbuffer bind.
        if (rtv0 != 0)
        {
            if (IsBackbuffer(rtv0))
            {
                if (lastNonBackbufferRtv != 0)
                    candidatePresentBuffer = lastNonBackbufferRtv;
            }
            else
            {
                lastNonBackbufferRtv = rtv0;
            }
        }

        // First bind only. Transparency, water and post-fx re-bind scene depth with a newer camera.
        if (InjectionEnabled && !mainDepthSeen && pDsv != 0 && rtv0 != 0 && !IsBackbuffer(rtv0))
        {
            if (IsMainSceneDepth(pDsv) && GameRenderSources.TryGetCamera(out var mainSnap))
            {
                worldCamera = mainSnap;
                hasWorldCamera = true;
                mainDepthSeen = true;

                // The game uploads its camera block between this bind and the pass's first draw.
                Capture?.OnMainPassBind();
            }
            else if (!hasWorldCamera && GameRenderSources.TryGetCamera(out var provisionalSnap))
            {
                worldCamera = provisionalSnap;
                hasWorldCamera = true;
            }
        }

        // Post-process passes bind multiple targets too, never with the scene's depth-stencil.
        if (GBufferInjectionEnabled && !gbufferDoneThisFrame && numViews >= GBufferMinTargets && pDsv != 0 && IsMainSceneDepth(pDsv))
            gbufferPassArmed = true;

        if (state == 2 && bindCount < MaxBinds)
            Record(numViews, rtv0, pDsv, ppRtvs);

        if (InjectionEnabled && presentBuffer != 0 && rtv0 == presentBuffer && Injector != null)
        {
            presentBufferBinds++;

            // The first depth-bearing present-buffer bind starts the world-space UI. Without one, the last bind is used.
            var rule = InjectRule.None;
            if (!injectedThisFrame)
            {
                if (InjectOrdinal > 0)
                {
                    if (presentBufferBinds == InjectOrdinal)
                        rule = InjectRule.ForcedOrdinal;
                }
                else if (pDsv != 0)
                {
                    rule = InjectRule.FirstDepthBind;
                }
                else if (!sawDepthBindLastFrame && presentBufferBindsLastFrame > 0 && presentBufferBinds == presentBufferBindsLastFrame)
                {
                    rule = InjectRule.LastBind;
                }
            }

            if (pDsv != 0)
                sawDepthBindThisFrame = true;

            if (rule != InjectRule.None)
            {
                injectedThisFrame = true;
                injectedBy = rule;
                injectedAtBind = presentBufferBinds;
                injecting = true;
                try
                {
                    Injector(presentBuffer);
                }
                catch (Exception ex)
                {
                    NoireLogger.LogError(ex, "Draw3D: native-UI injection callback threw.", "Draw3D");
                }
                finally
                {
                    injecting = false;
                }
            }
        }
    }

    // The camera commit runs first. Injected geometry uses the committed camera.
    private void OnDraw(nint context)
    {
        Capture?.OnGameDraw(context);

        if (shadowProbe is { Armed: true } && !injecting && !SuppressSelf && context == gameContext)
            shadowProbe.OnGameDraw((TerraFX.Interop.DirectX.ID3D11DeviceContext*)context);

        if (shadowBindActive && !injecting && !SuppressSelf && context == gameContext)
            shadowBindSawDraw = true;

        if (!gbufferPassArmed || GBufferInjector is not { } injector)
            return;

        // Disarmed before the call. A throwing callback is not retried on every remaining draw.
        gbufferPassArmed = false;
        gbufferDoneThisFrame = true;

        injecting = true;
        try
        {
            injector();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Draw3D: G-buffer injection callback threw - injection disabled for safety.", "Draw3D");
            GBufferInjectionEnabled = false;
        }
        finally
        {
            injecting = false;
        }
    }

    // An atlas shadow map renders each slice as its own viewport group with its own constants.
    private void TryInjectShadowAtGroupEnd(nint context)
    {
        if (!shadowBindActive || !shadowBindSawDraw || !shadowInjectionEnabled || injecting || SuppressSelf
            || context != gameContext || ShadowInjector is not { } shadowInjector)
            return;

        shadowBindSawDraw = false;

        injecting = true;
        try
        {
            shadowInjector(context);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Draw3D: shadow injection callback threw - shadow casting disabled for safety.", "Draw3D");
            ShadowInjectionEnabled = false;
        }
        finally
        {
            injecting = false;
        }
    }

    private void RsSetViewportsDetour(nint context, uint numViewports, nint pViewports)
    {
        TryInjectShadowAtGroupEnd(context);

        rsSetViewportsHook!.Original(context, numViewports, pViewports);
    }

    private void DrawIndexedDetour(nint context, uint indexCount, uint startIndex, int baseVertex)
    {
        if (Counting(context))
            drawCounter++;
        OnDraw(context);

        drawIndexedHook!.Original(context, indexCount, startIndex, baseVertex);
    }

    private void DrawDetour(nint context, uint vertexCount, uint startVertex)
    {
        if (Counting(context))
            drawCounter++;
        OnDraw(context);

        drawHook!.Original(context, vertexCount, startVertex);
    }

    private void DrawIndexedInstancedDetour(nint context, uint indexCountPerInstance, uint instanceCount, uint startIndex, int baseVertex, uint startInstance)
    {
        if (Counting(context))
            drawCounter++;
        OnDraw(context);

        drawIndexedInstancedHook!.Original(context, indexCountPerInstance, instanceCount, startIndex, baseVertex, startInstance);
    }

    private void DrawInstancedDetour(nint context, uint vertexCountPerInstance, uint instanceCount, uint startVertex, uint startInstance)
    {
        if (Counting(context))
            drawCounter++;
        OnDraw(context);

        drawInstancedHook!.Original(context, vertexCountPerInstance, instanceCount, startVertex, startInstance);
    }

    private nint ResolveRtv0Resource(uint numViews, nint ppRtvs)
    {
        if (numViews == 0 || ppRtvs == 0)
            return 0;

        var rtv = ((ID3D11RenderTargetView**)ppRtvs)[0];
        if (rtv == null)
            return 0;

        ID3D11Resource* resource = null;
        rtv->GetResource(&resource);
        if (resource == null)
            return 0;

        var res = (nint)resource;
        resource->Release();
        return res;
    }

    private bool IsMainSceneDepth(nint pDsv)
    {
        if (frameSceneDepthTex == 0 || pDsv == 0)
            return false;

        ID3D11Resource* resource = null;
        ((ID3D11DepthStencilView*)pDsv)->GetResource(&resource);
        if (resource == null)
            return false;

        var match = (nint)resource == frameSceneDepthTex;
        resource->Release();
        return match;
    }

    private void Record(uint numViews, nint rtv0, nint pDsv, nint ppRtvs)
    {
        uint w = 0, h = 0;
        if (rtv0 != 0 && ComPtrUtil.TryQi<ID3D11Texture2D>((IUnknown*)rtv0, out var tex))
        {
            D3D11_TEXTURE2D_DESC desc;
            tex.Get()->GetDesc(&desc);
            w = desc.Width;
            h = desc.Height;
            tex.Dispose();
        }

        var format = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;
        if (numViews > 0 && ppRtvs != 0)
        {
            var view = ((ID3D11RenderTargetView**)ppRtvs)[0];
            if (view != null)
            {
                D3D11_RENDER_TARGET_VIEW_DESC viewDesc;
                view->GetDesc(&viewDesc);
                format = viewDesc.Format;
            }
        }

        if (numViews > 1)
            RecordMultiTarget(numViews, ppRtvs);

        binds[bindCount++] = new Bind(numViews, rtv0, format, w, h, pDsv != 0, IsBackbuffer(rtv0), drawCounter);
    }

    public List<nint> GBufferTargets()
    {
        var result = new List<nint>();
        var best = -1;
        var bestTargets = 0;
        var bestWidth = 0u;
        var bestDraws = -1;

        for (var i = 0; i < multiBindCount; i++)
        {
            var at = multiBindAt[i];
            if (at + 1 >= bindCount || !binds[at].HasDsv)
                continue;

            var targets = multiBindCounts[i];
            var width = multiBindTargets[i * MaxTargetsPerBind].Width;
            var draws = binds[at + 1].DrawCount - binds[at].DrawCount;

            // Draws alone picks a two-target post-process bind. Resolution rejects the half- and quarter-resolution ones.
            if (targets < bestTargets)
                continue;

            if (targets == bestTargets)
            {
                if (width < bestWidth || (width == bestWidth && draws <= bestDraws))
                    continue;
            }

            bestTargets = targets;
            bestWidth = width;
            bestDraws = draws;
            best = i;
        }

        if (best < 0)
            return result;

        for (var t = 0; t < multiBindCounts[best]; t++)
            result.Add(multiBindTargets[(best * MaxTargetsPerBind) + t].Resource);

        return result;
    }

    private void AppendMultiTargets(StringBuilder sb)
    {
        if (multiBindCount == 0)
        {
            sb.AppendLine();
            sb.AppendLine("No multi-target binds this frame. With only one target ever bound the renderer is forward.");
            return;
        }

        sb.AppendLine();
        sb.AppendLine($"Multi-target binds ({multiBindCount}). The G-buffer is the full-resolution one followed by a burst of draws:");

        for (var i = 0; i < multiBindCount; i++)
        {
            var count = multiBindCounts[i];
            var at = multiBindAt[i];

            var following = at + 1 < bindCount ? binds[at + 1].DrawCount - binds[at].DrawCount : 0;

            sb.AppendLine($"  idx {at,3}: {count} target(s), {following} draw(s) follow");
            for (var t = 0; t < count; t++)
            {
                var info = multiBindTargets[(i * MaxTargetsPerBind) + t];
                sb.AppendLine($"      rtv{t} | {FormatName(info.Format),-28} | {info.Width,4}x{info.Height,-4} | 0x{info.Resource:X}");
            }
        }
    }

    private static string FormatName(DXGI_FORMAT format) => format switch
    {
        DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM => "R8G8B8A8_UNORM",
        DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM_SRGB => "R8G8B8A8_UNORM_SRGB",
        DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_TYPELESS => "R8G8B8A8_TYPELESS",
        DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM => "B8G8R8A8_UNORM",
        DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM_SRGB => "B8G8R8A8_UNORM_SRGB",
        DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM => "R10G10B10A2_UNORM",
        DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT => "R16G16B16A16_FLOAT",
        DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_UNORM => "R16G16B16A16_UNORM",
        DXGI_FORMAT.DXGI_FORMAT_R11G11B10_FLOAT => "R11G11B10_FLOAT",
        DXGI_FORMAT.DXGI_FORMAT_R16G16_FLOAT => "R16G16_FLOAT",
        DXGI_FORMAT.DXGI_FORMAT_R16G16_UNORM => "R16G16_UNORM",
        DXGI_FORMAT.DXGI_FORMAT_R8G8_UNORM => "R8G8_UNORM",
        DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT => "R32_FLOAT",
        DXGI_FORMAT.DXGI_FORMAT_R16_FLOAT => "R16_FLOAT",
        DXGI_FORMAT.DXGI_FORMAT_R8_UNORM => "R8_UNORM",
        _ => $"format {(int)format}",
    };

    private void RecordMultiTarget(uint numViews, nint ppRtvs)
    {
        if (multiBindCount >= MaxMultiBinds || ppRtvs == 0)
            return;

        var slot = multiBindCount;
        var written = 0;
        var views = (ID3D11RenderTargetView**)ppRtvs;

        for (var i = 0; i < numViews && i < MaxTargetsPerBind; i++)
        {
            var view = views[i];
            if (view == null)
                continue;

            // A typeless texture is viewed as UNORM by one pass and SRGB by another. The view's format is the one written.
            D3D11_RENDER_TARGET_VIEW_DESC viewDesc;
            view->GetDesc(&viewDesc);

            ID3D11Resource* resource = null;
            view->GetResource(&resource);
            if (resource == null)
                continue;

            uint w = 0, h = 0;
            if (ComPtrUtil.TryQi<ID3D11Texture2D>((IUnknown*)resource, out var tex))
            {
                D3D11_TEXTURE2D_DESC desc;
                tex.Get()->GetDesc(&desc);
                w = desc.Width;
                h = desc.Height;
                tex.Dispose();
            }

            multiBindTargets[(slot * MaxTargetsPerBind) + written] = new TargetInfo((nint)resource, viewDesc.Format, w, h);
            written++;

            resource->Release();
        }

        if (written == 0)
            return;

        multiBindAt[slot] = bindCount;
        multiBindCounts[slot] = written;
        multiBindCount++;
    }

    private void Flush()
    {
        lastFrameBindCount = bindCount;

        var sb = new StringBuilder();
        var bbList = new StringBuilder();
        for (var i = 0; i < knownBackbufferCount; i++)
            bbList.Append($"0x{knownBackbuffers[i]:X} ");
        sb.AppendLine($"Draw3D RT-bind sequence, one frame ({bindCount} binds, {drawCounter} draws; {knownBackbufferCount} backbuffers: {bbList}; present buffer 0x{presentBuffer:X})");

        var bbIdx = new StringBuilder();
        for (var i = 0; i < bindCount; i++)
        {
            if (binds[i].IsBackbuffer)
                bbIdx.Append(i).Append(binds[i].HasDsv ? "(+dsv) " : " ");
        }

        sb.AppendLine($"  backbuffer binds at idx: {(bbIdx.Length == 0 ? "(none learned - re-run)" : bbIdx.ToString())}");
        sb.AppendLine("  'draws' = draw calls made into the PREVIOUS row's target (1 = a blit; a burst = a real pass, e.g. the UI).");
        sb.AppendLine("  A single-target two-channel float at half the display size is the shape of a velocity buffer.");
        sb.AppendLine("  idx | draws | #rtv | backbuffer | dsv |  size    | format                       | rtv0 resource");
        for (var i = 0; i < bindCount; i++)
        {
            var b = binds[i];
            var draws = i == 0 ? b.DrawCount : b.DrawCount - binds[i - 1].DrawCount;
            sb.AppendLine($"  {i,3} | {draws,5} |  {b.NumViews,2}  |    {(b.IsBackbuffer ? "YES" : " - ")}    | {(b.HasDsv ? "yes" : " - ")} | {b.Width,4}x{b.Height,-4} | {FormatName(b.Format),-28} | 0x{b.Rtv0Resource:X}");
        }

        AppendMultiTargets(sb);

        NoireLogger.LogInfo(sb.ToString(), "Draw3D");
        NoireLogger.PrintToChat($"Draw3D: captured {bindCount} binds / {drawCounter} draws this frame.");
    }

    public void Dispose()
    {
        InjectionEnabled = false;
        Injector = null;
        ShadowInjector = null;
        ShadowFrameBoundary = null;
        Capture = null;
        shadowProbe?.Dispose();
        shadowProbe = null;

        omHook?.Dispose();
        drawIndexedHook?.Dispose();
        drawHook?.Dispose();
        drawIndexedInstancedHook?.Dispose();
        drawInstancedHook?.Dispose();
        rsSetViewportsHook?.Dispose();
        omHook = null;
        drawIndexedHook = null;
        drawHook = null;
        drawIndexedInstancedHook = null;
        drawInstancedHook = null;
        rsSetViewportsHook = null;
        omDetour = null;
        drawIndexedDetour = null;
        drawDetour = null;
        drawIndexedInstancedDetour = null;
        drawInstancedDetour = null;
        rsSetViewportsDetour = null;
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
