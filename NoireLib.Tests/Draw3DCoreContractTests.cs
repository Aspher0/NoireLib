using FluentAssertions;
using NoireLib.Draw3D;
using NoireLib.Draw3D.Assets;
using NoireLib.Draw3D.Core;
using NoireLib.Draw3D.Geometry;
using NoireLib.Draw3D.Scene;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the trap doors of the Draw3D core: constant-buffer packing sizes (a drive-by field addition
/// must fail the build, not the visuals), scene-graph semantics (dirty flags, cycles, destruction),
/// disposed-scene semantics (a dead scene rejects new content instead of silently destroying it),
/// steady-state allocation of the frame body's device-free half, and Law 11 - zero ImGui anywhere under
/// NoireLib/Draw3D (executable policy, not convention).
/// </summary>
public class Draw3DCoreContractTests
{
    // ---------------------------------------------------------------- constant-buffer packing

    [Fact]
    public void FrameCB_Is256Bytes() => Unsafe.SizeOf<FrameCBData>().Should().Be(256); // + WorldHeightRegion (decal height-map)

    [Fact]
    public void ObjectCB_Is192Bytes() => Unsafe.SizeOf<ObjectCBData>().Should().Be(192); // + Params2 (ground-decal projection mode)

    [Fact]
    public void CompositeCB_Is4112Bytes() => Unsafe.SizeOf<CompositeCBData>().Should().Be(4112); // header + 128 rects + 128 factors

    [Fact]
    public void Vertex3D_Is48Bytes() => Unsafe.SizeOf<Vertex3D>().Should().Be(48);

    [Fact]
    public void InstanceData_Is80Bytes() => Unsafe.SizeOf<InstanceData>().Should().Be(80);

    // ---------------------------------------------------------------- scene graph

    [Fact]
    public void SceneGraph_ParentMove_UpdatesChildWorldMatrix()
    {
        var scene = new Scene3D("test");
        var parent = scene.CreateNode("parent");
        var child = parent.CreateChild("child");
        child.LocalPosition = new Vector3(1f, 0f, 0f);

        child.WorldMatrix.Translation.Should().Be(new Vector3(1f, 0f, 0f));

        parent.LocalPosition = new Vector3(0f, 5f, 0f);
        child.WorldMatrix.Translation.Should().Be(new Vector3(1f, 5f, 0f), "the parent move must dirty the child's cached world matrix");
    }

    [Fact]
    public void SceneGraph_ScaleRotateTranslate_ComposeInSrtOrder()
    {
        var scene = new Scene3D("test");
        var node = scene.CreateNode();
        node.LocalScale = new Vector3(2f, 2f, 2f);
        node.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        node.LocalPosition = new Vector3(10f, 0f, 0f);

        // v · S·R·T: local +X (1,0,0) → scaled (2,0,0) → rotated 90° around Y (0,0,-2) → translated.
        var world = node.WorldMatrix;
        var p = Vector3.Transform(new Vector3(1f, 0f, 0f), world);
        p.X.Should().BeApproximately(10f, 1e-4f);
        p.Y.Should().BeApproximately(0f, 1e-4f);
        p.Z.Should().BeApproximately(-2f, 1e-4f);
    }

    [Fact]
    public void SceneGraph_ReparentCycle_IsRejected()
    {
        var scene = new Scene3D("test");
        var a = scene.CreateNode("a");
        var b = a.CreateChild("b");
        var c = b.CreateChild("c");

        var act = () => a.SetParent(c);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SceneGraph_Destroy_RemovesSubtreeAndCounts()
    {
        var scene = new Scene3D("test");
        var a = scene.CreateNode("a");
        a.CreateChild("b").CreateChild("c");
        scene.CreateNode("d");
        scene.NodeCount.Should().Be(4);

        a.Destroy();
        scene.NodeCount.Should().Be(1);
        a.Destroyed.Should().BeTrue();
    }

    [Fact]
    public void SceneGraph_Clear_EmptiesTheScene()
    {
        var scene = new Scene3D("test");
        scene.CreateNode().CreateChild();
        scene.CreateNode();
        scene.Clear();
        scene.NodeCount.Should().Be(0);
    }

    [Fact]
    public void SceneGraph_Reparent_MovesAcrossScenes()
    {
        var sceneA = new Scene3D("a");
        var sceneB = new Scene3D("b");
        var node = sceneA.CreateNode("n");
        var target = sceneB.CreateNode("t");

        node.SetParent(target);
        node.Scene.Should().BeSameAs(sceneB);
        sceneA.NodeCount.Should().Be(0);
        sceneB.NodeCount.Should().Be(2);
    }

    // ---------------------------------------------------------------- disposed-scene semantics

    [Fact]
    public void AdoptRoot_OnDisposedScene_Throws()
    {
        var source = new Scene3D("source");
        var node = source.CreateNode("n");
        var target = new Scene3D("target");
        target.Dispose();

        var act = () => target.AdoptRoot(node);
        act.Should().Throw<ObjectDisposedException>(
            "a disposed scene has already run its teardown, so a root adopted afterwards would never be freed - CreateNode already rejects this");
    }

    [Fact]
    public void AddModel_OnDisposedScene_ThrowsAndLeavesTheCallersModelIntact()
    {
        var host = new Scene3D("host");
        var model = new Model3D(host.CreateNode("root"), new List<Mesh>(), new List<GpuTexture>());
        var target = new Scene3D("target");
        target.Dispose();

        var act = () => target.AddModel(model);
        act.Should().Throw<ObjectDisposedException>(
            "Own frees anything handed to a dead scene, so adopting there would hand back a model whose GPU buffers are gone and which draws nothing without erroring");
        model.Root.Destroyed.Should().BeFalse("a rejected AddModel must not destroy the model the caller still owns");
    }

    // ---------------------------------------------------------------- prepare phase

    [Fact]
    public void FirePrepare_FeatureThatThrows_IsDetachedAndTheOthersStillRun()
    {
        var scene = new Scene3D("features");
        var survivor = new CountingFeature();
        scene.AddFeature(new ThrowingFeature());
        scene.AddFeature(survivor);
        var frame = MakeFrame();

        scene.FirePrepare(in frame);
        survivor.Calls.Should().Be(1, "a feature that throws must not stop the features after it");
        scene.FeatureList.Should().ContainSingle().Which.Should().BeSameAs(survivor, "the throwing feature is detached (self-disable rung 2)");

        scene.FirePrepare(in frame);
        survivor.Calls.Should().Be(2);
    }

    [Fact]
    public void FirePrepare_FeatureRegisteredDuringPrepare_RunsFromTheNextFrameOn()
    {
        var scene = new Scene3D("features");
        var late = new CountingFeature();
        scene.AddFeature(new FeatureAdder(late));
        var frame = MakeFrame();

        // The prepare loop runs off a snapshot taken under the lock: a feature registered from inside a feature must
        // neither run this frame nor disturb the loop already in flight.
        scene.FirePrepare(in frame);
        late.Calls.Should().Be(0);

        scene.FirePrepare(in frame);
        late.Calls.Should().Be(1);
    }

    // ---------------------------------------------------------------- steady-state allocation

    [Fact]
    public void FirePrepare_HandlerAndFeatures_AllocateNothingAfterWarmup()
    {
        var scene = new Scene3D("prepare-alloc");
        var handlerCalls = 0;
        scene.OnPrepareFrame += _ => handlerCalls++;
        var first = new CountingFeature();
        var second = new CountingFeature();
        scene.AddFeature(first);
        scene.AddFeature(second);
        var frame = MakeFrame();

        scene.FirePrepare(in frame); // warm-up (JIT, snapshot buffer growth)

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 8; i++)
            scene.FirePrepare(in frame);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        handlerCalls.Should().Be(9);
        first.Calls.Should().Be(9);
        second.Calls.Should().Be(9);
        allocated.Should().Be(0, "the per-frame prepare phase must not allocate, snapshotting its feature list included (Law 9)");
    }

    [Fact]
    public void WorldResolutionCullingAndKeys_AllocateNothingAfterWarmup()
    {
        var scene = new Scene3D("alloc");
        var nodes = new SceneNode[1000];
        for (var i = 0; i < nodes.Length; i++)
        {
            nodes[i] = scene.CreateNode();
            nodes[i].LocalPosition = new Vector3(i % 30, 0, i / 30f);
        }

        var proj = new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0.1f, 0);
        var frustum = FrustumPlanes.FromViewProj(proj);
        var sphere = new BoundingSphere(Vector3.Zero, 0.5f);

        void Pass()
        {
            for (var i = 0; i < nodes.Length; i++)
            {
                nodes[i].LocalPosition = nodes[i].LocalPosition with { Y = i * 0.001f };
                var world = nodes[i].WorldMatrix;
                var bounds = sphere.Transform(world);
                _ = frustum.Intersects(bounds);
                _ = SortKey.MakeGrouped(0, 0, 1, 42, SortKey.QuantizeDistance(bounds.Center.Length()), i);
            }
        }

        Pass(); // warm-up (JIT, lazy internals)

        var before = GC.GetAllocatedBytesForCurrentThread();
        Pass();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        allocated.Should().Be(0, "steady-state transform resolution, culling and key building must not allocate (Law 9)");
    }

    // ---------------------------------------------------------------- Law 11

    [Fact]
    public void Law11_NoImGuiInTheRendererCore()
    {
        // Law 11 protects the Draw3D *rendering core* - the pure D3D11 renderer that works and must not be destabilised
        // by input/UI concerns. That core reads no input and never touches ImGui (not for rendering, not for
        // diagnostics). The one sanctioned exception is the interaction layer under NoireLib/Draw3D/Interaction/
        // (NoireInteract + the gizmo, incl. its ImGuizmo backend), which is allowed to read ImGui IO and drive
        // ImGui/ImGuizmo - it sits above the renderer and never touches the render pipeline. Everything else stays pure.
        var draw3dDir = FindDraw3DSourceDirectory();
        var offenders = new System.Collections.Generic.List<string>();

        foreach (var file in Directory.EnumerateFiles(draw3dDir, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(draw3dDir, file);
            if (relative.StartsWith("Interaction" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                continue; // the sanctioned input layer - allowed to read ImGui IO

            var text = File.ReadAllText(file);
            if (text.Contains("Dalamud.Bindings.ImGui", StringComparison.Ordinal))
                offenders.Add(Path.GetFileName(file));
        }

        offenders.Should().BeEmpty("Law 11: the Draw3D renderer core must never reference the ImGui bindings (only NoireLib/Draw3D/Interaction/ may)");
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A device-free frame snapshot: the prepare phase only passes it through, so identity matrices suffice.</summary>
    private static FrameContext MakeFrame()
        => new(Matrix4x4.Identity, Matrix4x4.Identity, Matrix4x4.Identity, Matrix4x4.Identity,
            Vector3.Zero, 0f, new Vector2(1920f, 1080f), Vector2.One, true, 0.1f, true, false, 1);

    private sealed class CountingFeature : ISceneFeature
    {
        public int Calls;

        public void OnPrepareFrame(Scene3D scene, in FrameContext frame) => Calls++;
    }

    private sealed class ThrowingFeature : ISceneFeature
    {
        public void OnPrepareFrame(Scene3D scene, in FrameContext frame)
            => throw new InvalidOperationException("feature failure");
    }

    /// <summary>Registers another feature from inside the prepare loop, to prove the loop runs off a snapshot.</summary>
    private sealed class FeatureAdder : ISceneFeature
    {
        private ISceneFeature? pending;

        public FeatureAdder(ISceneFeature toAdd) => pending = toAdd;

        public void OnPrepareFrame(Scene3D scene, in FrameContext frame)
        {
            if (pending == null)
                return;

            scene.AddFeature(pending);
            pending = null;
        }
    }

    private static string FindDraw3DSourceDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "NoireLib", "Draw3D");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate the NoireLib/Draw3D source directory from the test output path.");
    }
}
