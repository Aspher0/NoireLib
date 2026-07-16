namespace NoireLib.Draw3D;

/// <summary>
/// A point-in-time snapshot of the renderer's counters. Draw3D renders no stats UI itself (Law 11) -
/// consumers may display this anywhere they like, and <c>/noire3d stats</c> prints it to the log.
/// </summary>
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

    /// <summary>Frames the layer chose not to draw because the game UI was hidden and <see cref="NoireDraw3D.KeepDrawingWhenUiHidden"/> is off. The host's own windows are unaffected by that switch.</summary>
    public required long FramesSkippedUiHidden { get; init; }

    /// <summary>Frames rendered in depth-off mode (game depth unreadable).</summary>
    public required long DepthOffFrames { get; init; }

    /// <summary>Draws skipped because their mesh or texture was disposed.</summary>
    public required long DisposedAssetDraws { get; init; }

    /// <summary>Immediate-layer commands dropped (dynamic geometry budget exceeded).</summary>
    public required long ImCommandsDropped { get; init; }

    /// <summary>Draw calls issued last frame.</summary>
    public required int DrawCalls { get; init; }

    /// <summary>Instances drawn last frame (instanced batches).</summary>
    public required int Instances { get; init; }

    /// <summary>Triangles submitted last frame.</summary>
    public required int Triangles { get; init; }

    /// <summary>Batches (instanced groups + single draws) last frame.</summary>
    public required int Batches { get; init; }

    /// <summary>Items culled by the frustum last frame.</summary>
    public required int CulledItems { get; init; }

    /// <summary>Items that survived culling last frame.</summary>
    public required int VisibleItems { get; init; }

    /// <summary>Nameplate/HUD policy rects applied last frame (over-everything UI masking only; 0 otherwise).</summary>
    public required int ProtectRects { get; init; }

    /// <summary>Whether the game's depth buffer was readable last frame.</summary>
    public required bool DepthAvailable { get; init; }

    /// <summary>The active depth source (route + format), the live depth-calibration fit, and the UI-mask health - the one line that answers "why does occlusion/UI layering look wrong".</summary>
    public required string DepthSource { get; init; }

    /// <summary>Whether the wholesale VP camera fallback was active last frame.</summary>
    public required bool UsedFallbackCamera { get; init; }

    /// <summary>GPU time of the scene pass, in milliseconds (rolling, resolved a few frames late).</summary>
    public required float SceneGpuMs { get; init; }

    /// <summary>GPU time of the composite, in milliseconds (rolling, resolved a few frames late).</summary>
    public required float CompositeGpuMs { get; init; }

    /// <summary>Formats the snapshot as a readable multi-line report.</summary>
    public override string ToString() =>
        $"""
        Draw3D stats
          frames: rendered {FramesRendered}, skipped (disabled {FramesSkippedDisabled}, init {FramesSkippedInitPending}, device {FramesSkippedNoDevice}, camera {FramesSkippedNoCamera}, size {FramesSkippedZeroSize}, empty {FramesSkippedEmpty}, ui-hidden {FramesSkippedUiHidden})
          last frame: draws {DrawCalls}, batches {Batches}, instances {Instances}, tris {Triangles}, visible {VisibleItems}, culled {CulledItems}
          depth: available {DepthAvailable} ({DepthSource}), depth-off frames {DepthOffFrames} | camera fallback: {UsedFallbackCamera}
          protection rects: {ProtectRects} | disposed-asset draws: {DisposedAssetDraws} | Im dropped: {ImCommandsDropped}
          gpu: scene {SceneGpuMs:F3} ms, composite {CompositeGpuMs:F3} ms
        """;
}
