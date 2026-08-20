namespace NoireLib.Draw3D;

/// <summary>A point-in-time snapshot of the renderer's counters, also printed by <c>/noire3d stats</c>.</summary>
public readonly struct Draw3DStats
{
    /// <summary>Frames actually rendered since the last counter reset.</summary>
    public required long FramesRendered { get; init; }

    /// <summary>Frames skipped, by reason, since the last counter reset.</summary>
    public required long FramesSkippedDisabled { get; init; }

    /// <inheritdoc cref="FramesSkippedDisabled"/>
    public required long FramesSkippedInitPending { get; init; }

    /// <inheritdoc cref="FramesSkippedDisabled"/>
    public required long FramesSkippedNoDevice { get; init; }

    /// <inheritdoc cref="FramesSkippedDisabled"/>
    public required long FramesSkippedNoCamera { get; init; }

    /// <inheritdoc cref="FramesSkippedDisabled"/>
    public required long FramesSkippedZeroSize { get; init; }

    /// <inheritdoc cref="FramesSkippedDisabled"/>
    public required long FramesSkippedEmpty { get; init; }

    /// <summary>Frames not drawn because the game UI was hidden and <see cref="NoireDraw3D.KeepDrawingWhenUiHidden"/> is off.</summary>
    public required long FramesSkippedUiHidden { get; init; }

    /// <summary>Frames rendered in depth-off mode, with the game depth unreadable.</summary>
    public required long DepthOffFrames { get; init; }

    /// <summary>Draws skipped because their mesh or texture was disposed.</summary>
    public required long DisposedAssetDraws { get; init; }

    /// <summary>Immediate-layer commands dropped for exceeding the dynamic geometry budget.</summary>
    public required long ImCommandsDropped { get; init; }

    /// <summary>Draw calls issued last frame.</summary>
    public required int DrawCalls { get; init; }

    /// <summary>Instances drawn last frame across the instanced batches.</summary>
    public required int Instances { get; init; }

    /// <summary>Triangles submitted last frame.</summary>
    public required int Triangles { get; init; }

    /// <summary>Instanced groups plus single draws last frame.</summary>
    public required int Batches { get; init; }

    /// <summary>Object constant-buffer uploads in the scene pass last frame, the cost <see cref="Draw3DPerformance.BatchedObjectConstants"/> collapses.</summary>
    public required int ObjectCbUpdates { get; init; }

    /// <summary>Items culled by the frustum last frame.</summary>
    public required int CulledItems { get; init; }

    /// <summary>Items that survived culling last frame.</summary>
    public required int VisibleItems { get; init; }

    /// <summary>Nameplate and HUD policy rects applied last frame, non-zero only under over-everything UI masking.</summary>
    public required int ProtectRects { get; init; }

    /// <summary>Whether the game's depth buffer was readable last frame.</summary>
    public required bool DepthAvailable { get; init; }

    /// <summary>The active depth route and format, the live depth-calibration fit, and the UI-mask health.</summary>
    public required string DepthSource { get; init; }

    /// <summary>Whether the wholesale VP camera fallback was active last frame.</summary>
    public required bool UsedFallbackCamera { get; init; }

    /// <summary>Whether last frame projected with the captured GPU camera constants.</summary>
    public required bool UsedGpuCamera { get; init; }

    /// <summary>Frames projected with the captured GPU camera constants since the last counter reset.</summary>
    public required long GpuCameraFrames { get; init; }

    /// <summary>Camera-constant capture state: locked identity and health, or why it is inactive.</summary>
    public required string CameraCapture { get; init; }

    /// <summary>Rolling GPU time of the scene pass in milliseconds, resolved a few frames late.</summary>
    public required float SceneGpuMs { get; init; }

    /// <summary>Rolling GPU time of the composite in milliseconds, resolved a few frames late.</summary>
    public required float CompositeGpuMs { get; init; }

    /// <summary>CPU time of the most recent <see cref="NoireDraw3D.Pick"/>, in microseconds.</summary>
    public required int LastPickMicros { get; init; }

    /// <summary>Nodes visited by the most recent pick.</summary>
    public required int LastPickNodes { get; init; }

    /// <summary>Meshes the most recent pick refined to exact triangles through a BVH query.</summary>
    public required int LastPickRefined { get; init; }

    /// <summary>Formats the snapshot as a multi-line report.</summary>
    /// <returns>The report text.</returns>
    public override string ToString() =>
        $"""
        Draw3D stats
          frames: rendered {FramesRendered}, skipped (disabled {FramesSkippedDisabled}, init {FramesSkippedInitPending}, device {FramesSkippedNoDevice}, camera {FramesSkippedNoCamera}, size {FramesSkippedZeroSize}, empty {FramesSkippedEmpty}, ui-hidden {FramesSkippedUiHidden})
          last frame: draws {DrawCalls}, batches {Batches}, instances {Instances}, tris {Triangles}, visible {VisibleItems}, culled {CulledItems}, objectCb updates {ObjectCbUpdates}
          depth: available {DepthAvailable} ({DepthSource}), depth-off frames {DepthOffFrames} | camera fallback: {UsedFallbackCamera}
          camera capture: {CameraCapture} | gpu-camera last frame: {UsedGpuCamera}, frames {GpuCameraFrames}
          protection rects: {ProtectRects} | disposed-asset draws: {DisposedAssetDraws} | Im dropped: {ImCommandsDropped}
          gpu: scene {SceneGpuMs:F3} ms, composite {CompositeGpuMs:F3} ms
          last pick: {LastPickMicros} us, {LastPickNodes} nodes, {LastPickRefined} refined
        """;
}
