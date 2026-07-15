using FluentAssertions;
using NoireLib.Draw3D.Scene;
using System;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the scene-as-ownership-scope semantics (no GPU needed): <see cref="Scene3D.Own{T}"/> /
/// <see cref="Scene3D.Disown"/>, idempotent <see cref="Scene3D.Dispose"/> that frees owned disposables and destroys
/// nodes, the MainScene (hub-owned) Dispose guard, and rejection of node creation after disposal.
/// </summary>
public class Draw3DSceneOwnershipTests
{
    private sealed class TrackDisposable : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }

    [Fact]
    public void Own_ReturnsSameInstance()
    {
        var scene = new Scene3D("t");
        var d = new TrackDisposable();
        scene.Own(d).Should().BeSameAs(d);
    }

    [Fact]
    public void Dispose_FreesOwnedDisposablesAndDestroysNodes()
    {
        var scene = new Scene3D("t");
        var d = scene.Own(new TrackDisposable());
        var node = scene.CreateNode("n");

        scene.Dispose();

        d.DisposeCount.Should().Be(1);
        node.Destroyed.Should().BeTrue();
        scene.NodeCount.Should().Be(0);
        scene.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var scene = new Scene3D("t");
        var d = scene.Own(new TrackDisposable());

        scene.Dispose();
        scene.Dispose();

        d.DisposeCount.Should().Be(1, "a second Dispose must not free owned disposables again");
    }

    [Fact]
    public void Disown_PreventsDisposal()
    {
        var scene = new Scene3D("t");
        var d = scene.Own(new TrackDisposable());

        scene.Disown(d).Should().BeTrue();
        scene.Dispose();

        d.DisposeCount.Should().Be(0);
    }

    [Fact]
    public void Own_AfterDispose_DisposesImmediately()
    {
        var scene = new Scene3D("t");
        scene.Dispose();

        var d = new TrackDisposable();
        scene.Own(d);
        d.DisposeCount.Should().Be(1, "handing a straggler to an already-disposed scene must not leak it");
    }

    [Fact]
    public void CreateNode_AfterDispose_Throws()
    {
        var scene = new Scene3D("t");
        scene.Dispose();

        var act = () => scene.CreateNode();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void MainScene_IgnoresDispose()
    {
        var scene = new Scene3D("main") { IsHubOwned = true };
        var d = scene.Own(new TrackDisposable());

        scene.Dispose();

        d.DisposeCount.Should().Be(0, "the hub-owned main scene lives for the library lifetime; Dispose is a no-op");
        scene.IsDisposed.Should().BeFalse();
    }
}
