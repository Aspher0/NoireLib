using FluentAssertions;
using NoireLib.Draw3D.Core;
using NoireLib.Draw3D.Geometry;
using NoireLib.Draw3D.Scene;
using System;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the trap doors of the Draw3D core: constant-buffer packing sizes (a drive-by field addition
/// must fail the build, not the visuals), scene-graph semantics (dirty flags, cycles, destruction),
/// steady-state allocation, and Law 11 — zero ImGui anywhere under NoireLib/Draw3D (executable policy,
/// not convention).
/// </summary>
public class Draw3DCoreContractTests
{
    // ---------------------------------------------------------------- constant-buffer packing

    [Fact]
    public void FrameCB_Is240Bytes() => Unsafe.SizeOf<FrameCBData>().Should().Be(240); // + DepthCal (runtime depth calibration)

    [Fact]
    public void ObjectCB_Is176Bytes() => Unsafe.SizeOf<ObjectCBData>().Should().Be(176);

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

    // ---------------------------------------------------------------- steady-state allocation

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
        // Law 11 protects the Draw3D *rendering core* — the pure D3D11 renderer that works and must not be destabilised
        // by input/UI concerns. That core reads no input and never touches ImGui (not for rendering, not for
        // diagnostics). The one sanctioned exception is the interaction layer under NoireLib/Draw3D/Interaction/
        // (NoireInteract + the gizmo, incl. its ImGuizmo backend), which is allowed to read ImGui IO and drive
        // ImGui/ImGuizmo — it sits above the renderer and never touches the render pipeline. Everything else stays pure.
        var draw3dDir = FindDraw3DSourceDirectory();
        var offenders = new System.Collections.Generic.List<string>();

        foreach (var file in Directory.EnumerateFiles(draw3dDir, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(draw3dDir, file);
            if (relative.StartsWith("Interaction" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                continue; // the sanctioned input layer — allowed to read ImGui IO

            var text = File.ReadAllText(file);
            if (text.Contains("Dalamud.Bindings.ImGui", StringComparison.Ordinal))
                offenders.Add(Path.GetFileName(file));
        }

        offenders.Should().BeEmpty("Law 11: the Draw3D renderer core must never reference the ImGui bindings (only NoireLib/Draw3D/Interaction/ may)");
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
