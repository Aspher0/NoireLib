using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using FluentAssertions;
using NoireLib.Core.Subscriptions;
using NoireLib.GameWatcher;
using NoireLib.TaskQueue;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Game-free tests for the NoireGameWatcher module: scope matching, the character diff engine, the
/// declarative condition table, condition combinators and latches, the custom-event seam
/// (<see cref="NoireGameWatcher.Publish{TEvent}"/>), subscription semantics and the TaskQueue bridge.
/// </summary>
public class NoireGameWatcherTests
{
    #region Helpers

    private static CharacterSnapshot MakeSnapshot(
        uint entityId = 1,
        ulong contentId = 0,
        string name = "Some Character",
        uint homeWorldId = 0,
        SubjectFlags flags = SubjectFlags.None,
        Dalamud.Game.ClientState.Objects.Enums.ObjectKind kind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc,
        uint currentHp = 100,
        uint maxHp = 100,
        uint currentMp = 100,
        bool isCasting = false,
        uint castActionId = 0,
        bool isInCombat = false,
        bool isDead = false,
        byte mode = 1,
        byte modeParam = 0,
        uint level = 90,
        uint classJobId = 1,
        uint onlineStatusId = 0)
        => new()
        {
            EntityId = entityId,
            GameObjectId = entityId,
            ContentId = contentId,
            Name = name,
            HomeWorldId = homeWorldId,
            CurrentWorldId = homeWorldId,
            ObjectKind = kind,
            Flags = flags,
            ClassJobId = classJobId,
            Level = level,
            CurrentHp = currentHp,
            MaxHp = maxHp,
            CurrentMp = currentMp,
            MaxMp = 100,
            CurrentGp = 0,
            MaxGp = 0,
            CurrentCp = 0,
            MaxCp = 0,
            ShieldPercentage = 0,
            IsCasting = isCasting,
            IsCastInterruptible = false,
            CastActionId = castActionId,
            CastTargetEntityId = 0,
            TotalCastTime = 0,
            CurrentCastTime = 0,
            IsInCombat = isInCombat,
            IsTargetable = true,
            TargetEntityId = null,
            IsDead = isDead,
            Mode = mode,
            ModeParam = modeParam,
            OnlineStatusId = onlineStatusId,
            Position = Vector3.Zero,
            Rotation = 0,
            CapturedAt = DateTimeOffset.UtcNow,
        };

    private static NoireGameWatcher MakeWatcher()
        => new(options: null, active: false, enableLogging: false);

    private sealed record TestEvent(int Value);

    #endregion

    #region Scope matching

    [Fact]
    public void Scope_LocalPlayer_MatchesOnlyLocalFlag()
    {
        var local = MakeSnapshot(flags: SubjectFlags.IsLocalPlayer);
        var other = MakeSnapshot(entityId: 2);

        Scope.LocalPlayer.Matches(local).Should().BeTrue();
        Scope.LocalPlayer.Matches(other).Should().BeFalse();
    }

    [Fact]
    public void Scope_Party_IncludesLocalPlayerAndPartyMembers()
    {
        Scope.Party.Matches(MakeSnapshot(flags: SubjectFlags.IsLocalPlayer)).Should().BeTrue();
        Scope.Party.Matches(MakeSnapshot(flags: SubjectFlags.IsPartyMember)).Should().BeTrue();
        Scope.Party.Matches(MakeSnapshot(flags: SubjectFlags.IsAllianceMember)).Should().BeFalse();
        Scope.Party.Matches(MakeSnapshot()).Should().BeFalse();
    }

    [Fact]
    public void Scope_Friends_MatchesFriendFlag()
    {
        Scope.Friends.Matches(MakeSnapshot(flags: SubjectFlags.IsFriend)).Should().BeTrue();
        Scope.Friends.Matches(MakeSnapshot()).Should().BeFalse();
    }

    [Fact]
    public void Scope_AllPlayers_MatchesOnlyPlayers()
    {
        Scope.AllPlayers.Matches(MakeSnapshot()).Should().BeTrue();
        Scope.AllPlayers.Matches(MakeSnapshot(kind: Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc)).Should().BeFalse();
        Scope.AllCharacters.Matches(MakeSnapshot(kind: Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc)).Should().BeTrue();
    }

    [Fact]
    public void Scope_Entity_And_ContentId_MatchIdentity()
    {
        Scope.Entity(42).Matches(MakeSnapshot(entityId: 42)).Should().BeTrue();
        Scope.Entity(42).Matches(MakeSnapshot(entityId: 43)).Should().BeFalse();

        Scope.ContentId(0xBEEF).Matches(MakeSnapshot(contentId: 0xBEEF)).Should().BeTrue();
        Scope.ContentId(0xBEEF).Matches(MakeSnapshot(contentId: 0xDEAD)).Should().BeFalse();
        Scope.ContentId(0).Matches(MakeSnapshot(contentId: 0)).Should().BeFalse("content id 0 means unavailable and must never match");
    }

    [Fact]
    public void Scope_Name_MatchesCaseInsensitive_AndWorldRestricts()
    {
        Scope.Name("some character").Matches(MakeSnapshot(name: "Some Character")).Should().BeTrue();
        Scope.Name("Some Character", worldId: 5).Matches(MakeSnapshot(name: "Some Character", homeWorldId: 5)).Should().BeTrue();
        Scope.Name("Some Character", worldId: 5).Matches(MakeSnapshot(name: "Some Character", homeWorldId: 6)).Should().BeFalse();
    }

    [Fact]
    public void Scope_Where_NarrowsButNeverWidens()
    {
        var scope = Scope.Party.Where(s => s.Level >= 90);

        scope.Matches(MakeSnapshot(flags: SubjectFlags.IsPartyMember, level: 90)).Should().BeTrue();
        scope.Matches(MakeSnapshot(flags: SubjectFlags.IsPartyMember, level: 80)).Should().BeFalse();
        scope.Matches(MakeSnapshot(level: 90)).Should().BeFalse("the root still bounds the scope");
    }

    [Fact]
    public void Scope_Union_MatchesEitherSide()
    {
        var scope = Scope.Entity(1).Union(Scope.Entity(2));

        scope.Matches(MakeSnapshot(entityId: 1)).Should().BeTrue();
        scope.Matches(MakeSnapshot(entityId: 2)).Should().BeTrue();
        scope.Matches(MakeSnapshot(entityId: 3)).Should().BeFalse();
    }

    [Fact]
    public void Scope_IterationClass_ReflectsRootBreadth()
    {
        Scope.LocalPlayer.GetIterationClass().Should().Be(Scope.IterationClass.LocalOnly);
        Scope.Party.GetIterationClass().Should().Be(Scope.IterationClass.Players);
        Scope.AllPlayers.GetIterationClass().Should().Be(Scope.IterationClass.Players);
        Scope.AllCharacters.GetIterationClass().Should().Be(Scope.IterationClass.AllCharacters);
        Scope.LocalPlayer.Union(Scope.AllCharacters).GetIterationClass().Should().Be(Scope.IterationClass.AllCharacters);
        Scope.LocalPlayer.Where(_ => true).GetIterationClass().Should().Be(Scope.IterationClass.LocalOnly, "predicates never widen the iteration cost");
    }

    #endregion

    #region Character diff engine

    [Fact]
    public void DiffEngine_IdenticalFieldSets_ReportNoChange()
    {
        var snapshot = MakeSnapshot();
        var a = CharacterFieldSet.FromSnapshot(snapshot);
        var b = CharacterFieldSet.FromSnapshot(snapshot);

        CharacterDiffEngine.ComputeChangedAspects(in a, in b).Should().Be(CharacterAspect.None);
    }

    [Theory]
    [InlineData(nameof(CharacterAspect.Vitals))]
    [InlineData(nameof(CharacterAspect.Cast))]
    [InlineData(nameof(CharacterAspect.Combat))]
    [InlineData(nameof(CharacterAspect.Life))]
    [InlineData(nameof(CharacterAspect.Mode))]
    [InlineData(nameof(CharacterAspect.JobLevel))]
    [InlineData(nameof(CharacterAspect.Identity))]
    [InlineData(nameof(CharacterAspect.OnlineStatus))]
    public void DiffEngine_DetectsEachAspect(string aspectName)
    {
        var aspect = Enum.Parse<CharacterAspect>(aspectName);
        var before = CharacterFieldSet.FromSnapshot(MakeSnapshot());

        var after = aspect switch
        {
            CharacterAspect.Vitals => CharacterFieldSet.FromSnapshot(MakeSnapshot(currentHp: 50)),
            CharacterAspect.Cast => CharacterFieldSet.FromSnapshot(MakeSnapshot(isCasting: true, castActionId: 7)),
            CharacterAspect.Combat => CharacterFieldSet.FromSnapshot(MakeSnapshot(isInCombat: true)),
            CharacterAspect.Life => CharacterFieldSet.FromSnapshot(MakeSnapshot(isDead: true)),
            CharacterAspect.Mode => CharacterFieldSet.FromSnapshot(MakeSnapshot(mode: 3, modeParam: 12)),
            CharacterAspect.JobLevel => CharacterFieldSet.FromSnapshot(MakeSnapshot(level: 91)),
            CharacterAspect.Identity => CharacterFieldSet.FromSnapshot(MakeSnapshot(contentId: 0x1234)),
            CharacterAspect.OnlineStatus => CharacterFieldSet.FromSnapshot(MakeSnapshot(onlineStatusId: 17)),
            _ => throw new InvalidOperationException(),
        };

        var changed = CharacterDiffEngine.ComputeChangedAspects(in before, in after);
        changed.HasFlag(aspect).Should().BeTrue();
    }

    #endregion

    #region Condition pair table

    [Fact]
    public void ConditionPairTable_EveryRow_HasDistinctEventTypesAndWorkingFactories()
    {
        var seen = new HashSet<Type>();

        foreach (var row in ConditionPairTable.Rows)
        {
            row.Flags.Should().NotBeEmpty(row.Name);
            seen.Add(row.EnterEventType).Should().BeTrue($"{row.Name} enter type must be unique");
            seen.Add(row.LeaveEventType).Should().BeTrue($"{row.Name} leave type must be unique");
            row.CreateEnterEvent().Should().BeOfType(row.EnterEventType);
            row.CreateLeaveEvent().Should().BeOfType(row.LeaveEventType);
        }
    }

    [Fact]
    public void ConditionPairTable_ComputeState_IsAnyOf()
    {
        var mounted = ConditionPairTable.Rows.Should()
            .ContainSingle(row => row.Name == "mounted").Subject;

        ConditionPairTable.ComputeState(mounted, flag => flag == ConditionFlag.RidingPillion).Should().BeTrue();
        ConditionPairTable.ComputeState(mounted, _ => false).Should().BeFalse();
    }

    #endregion

    #region GameCondition combinators & latch

    [Fact]
    public void GameCondition_Combinators_ComposeCorrectly()
    {
        var yes = GameConditions.FromPredicate(() => true);
        var no = GameConditions.FromPredicate(() => false);

        yes.And(yes).IsMet().Should().BeTrue();
        yes.And(no).IsMet().Should().BeFalse();
        no.Or(yes).IsMet().Should().BeTrue();
        no.Or(no).IsMet().Should().BeFalse();
        no.Not().IsMet().Should().BeTrue();
        yes.Not().IsMet().Should().BeFalse();
    }

    [Fact]
    public void EventLatch_ArmsOnFirstEvaluation_LatchesOnce_AndResets()
    {
        using var watcher = MakeWatcherDisposable(out var module);

        var latch = GameConditions.FromEvent<TestEvent>(module, e => e.Value == 42);

        // Not armed yet: an event published before the first evaluation is not captured.
        module.Publish(new TestEvent(42));
        latch.IsMet().Should().BeFalse("the latch arms on the first IsMet evaluation");

        module.Publish(new TestEvent(1));
        latch.IsMet().Should().BeFalse("the filter must match");

        module.Publish(new TestEvent(42));
        latch.IsMet().Should().BeTrue();
        latch.MatchedEvent.Should().Be(new TestEvent(42));
        latch.IsMet().Should().BeTrue("latches are one-shot and stay true");

        latch.Reset();
        latch.IsMet().Should().BeFalse("Reset re-arms the latch");

        module.Publish(new TestEvent(42));
        latch.IsMet().Should().BeTrue("a reset latch captures again");
    }

    [Fact]
    public void EventLatch_ArmImmediately_CapturesBeforeFirstEvaluation()
    {
        using var watcher = MakeWatcherDisposable(out var module);

        var latch = GameConditions.FromEvent<TestEvent>(module, armImmediately: true);

        module.Publish(new TestEvent(7));
        latch.IsMet().Should().BeTrue();
    }

    private static IDisposable MakeWatcherDisposable(out NoireGameWatcher module)
    {
        var watcher = MakeWatcher();
        module = watcher;
        return new ActionDisposable(watcher.Dispose);
    }

    private sealed class ActionDisposable : IDisposable
    {
        private readonly Action action;
        public ActionDisposable(Action action) => this.action = action;
        public void Dispose() => action();
    }

    #endregion

    #region Publish & subscriptions (the custom-event seam)

    [Fact]
    public void Publish_ReachesSubscribers_WithFilterAndPriority()
    {
        var watcher = MakeWatcher();
        var received = new List<string>();

        watcher.Subscribe<TestEvent>(_ => received.Add("low"), new NoireSubscriptionOptions<TestEvent> { Priority = 0 });
        watcher.Subscribe<TestEvent>(_ => received.Add("high"), new NoireSubscriptionOptions<TestEvent> { Priority = 10 });
        watcher.Subscribe<TestEvent>(_ => received.Add("filtered"), new NoireSubscriptionOptions<TestEvent> { Filter = e => e.Value > 100 });

        watcher.Publish(new TestEvent(1));

        received.Should().Equal("high", "low");
        watcher.Dispose();
    }

    [Fact]
    public void Publish_OnceSubscription_FiresOnceAndCleansUp()
    {
        var watcher = MakeWatcher();
        var count = 0;

        var token = watcher.Subscribe<TestEvent>(_ => count++, new NoireSubscriptionOptions<TestEvent> { Once = true });

        watcher.Publish(new TestEvent(1));
        watcher.Publish(new TestEvent(2));

        count.Should().Be(1);
        token.IsActive.Should().BeFalse();
        watcher.SubscriptionCount.Should().Be(0);
        watcher.Dispose();
    }

    [Fact]
    public void KeyedSubscription_ReplacesPrevious()
    {
        var watcher = MakeWatcher();
        var received = new List<string>();

        watcher.Subscribe<TestEvent>(_ => received.Add("first"), new NoireSubscriptionOptions<TestEvent> { Key = "the-key" });
        watcher.Subscribe<TestEvent>(_ => received.Add("second"), new NoireSubscriptionOptions<TestEvent> { Key = "the-key" });

        watcher.Publish(new TestEvent(1));

        received.Should().Equal("second");
        watcher.SubscriptionCount.Should().Be(1);

        watcher.Unsubscribe("the-key").Should().BeTrue();
        watcher.Publish(new TestEvent(2));
        received.Should().Equal("second");
        watcher.Dispose();
    }

    [Fact]
    public void UnsubscribeOwner_RemovesEverythingTaggedWithOwner()
    {
        var watcher = MakeWatcher();
        var owner = new object();
        var count = 0;

        watcher.Subscribe<TestEvent>(_ => count++, new NoireSubscriptionOptions<TestEvent> { Owner = owner });
        watcher.Subscribe<TestEvent>(_ => count++, new NoireSubscriptionOptions<TestEvent> { Owner = owner });
        watcher.Subscribe<TestEvent>(_ => count++);

        watcher.UnsubscribeOwner(owner).Should().Be(2);

        watcher.Publish(new TestEvent(1));
        count.Should().Be(1, "only the unowned subscription remains");
        watcher.Dispose();
    }

    [Fact]
    public void DisposingToken_StopsDelivery()
    {
        var watcher = MakeWatcher();
        var count = 0;

        var token = watcher.Subscribe<TestEvent>(_ => count++);
        watcher.Publish(new TestEvent(1));
        token.Dispose();
        watcher.Publish(new TestEvent(2));

        count.Should().Be(1);
        watcher.Dispose();
    }

    [Fact]
    public async Task WaitFor_CompletesOnMatchingPublish()
    {
        var watcher = MakeWatcher();

        var wait = watcher.WaitFor<TestEvent>(e => e.Value == 5);

        wait.IsCompleted.Should().BeFalse();
        watcher.Publish(new TestEvent(4));
        wait.IsCompleted.Should().BeFalse();
        watcher.Publish(new TestEvent(5));

        (await wait).Should().Be(new TestEvent(5));
        watcher.Dispose();
    }

    [Fact]
    public async Task WaitFor_Cancellation_ThrowsAndCleansUp()
    {
        var watcher = MakeWatcher();
        using var cts = new System.Threading.CancellationTokenSource();

        var wait = watcher.WaitFor<TestEvent>(ct: cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        watcher.Dispose();
    }

    #endregion

    #region TaskQueue bridge

    [Fact]
    public void CompleteWhen_GatesTaskCompletionOnCondition()
    {
        var flag = false;
        var task = TaskBuilder.Create("test")
            .WithAction(() => { })
            .CompleteWhen(GameConditions.FromPredicate(() => flag))
            .Build();

        task.CompletionCondition.Should().NotBeNull();
        task.CompletionCondition!.IsMet().Should().BeFalse();

        flag = true;
        task.CompletionCondition.IsMet().Should().BeTrue();
    }

    [Fact]
    public void CompleteOnGameEvent_UsesAFreshLatchPerCall()
    {
        var watcher = MakeWatcher();

        var taskA = TaskBuilder.Create("a").WithAction(() => { })
            .CompleteOnGameEvent<TestEvent>(watcher, e => e.Value == 1, armImmediately: true)
            .Build();

        var taskB = TaskBuilder.Create("b").WithAction(() => { })
            .CompleteOnGameEvent<TestEvent>(watcher, e => e.Value == 1, armImmediately: true)
            .Build();

        watcher.Publish(new TestEvent(1));

        taskA.CompletionCondition!.IsMet().Should().BeTrue();
        taskB.CompletionCondition!.IsMet().Should().BeTrue("each call owns its own latch");

        // A latch matched for one task never leaks into a task built later.
        var taskC = TaskBuilder.Create("c").WithAction(() => { })
            .CompleteOnGameEvent<TestEvent>(watcher, e => e.Value == 1, armImmediately: true)
            .Build();

        taskC.CompletionCondition!.IsMet().Should().BeFalse("the fresh latch has not seen a match yet");
        watcher.Dispose();
    }

    #endregion

    #region Chat rules

    private static ChatMessageEvent MakeMessage(string text, XivChatType type = XivChatType.Say, string sender = "Some Player")
        => new()
        {
            Type = type,
            Timestamp = 0,
            Sender = new SeString(),
            Message = new SeString(),
            PlainText = text,
            SenderName = sender,
            SenderWorldId = null,
            SenderWorldName = null,
            RepeatCount = 1,
        };

    [Fact]
    public void ChatRules_MatchAsDocumented()
    {
        ChatRule.Contains("hello").Matches(MakeMessage("Well HELLO there")).Should().BeTrue();
        ChatRule.Contains("hello", XivChatType.Party).Matches(MakeMessage("hello")).Should().BeFalse("channel restriction applies");

        ChatRule.MatchesWildcard("buy * now").Matches(MakeMessage("buy this now")).Should().BeTrue();
        ChatRule.MatchesWildcard("buy * now").Matches(MakeMessage("buy this later")).Should().BeFalse();

        ChatRule.MatchesRegex("^\\d+$").Matches(MakeMessage("12345")).Should().BeTrue();

        ChatRule.Command("!roll").Matches(MakeMessage("!roll 100")).Should().BeTrue();
        ChatRule.Command("!roll").Matches(MakeMessage("!rollx")).Should().BeFalse();

        ChatRule.FromSender("Some Player").Matches(MakeMessage("hi")).Should().BeTrue();
        ChatRule.FromSender("Nobody").Matches(MakeMessage("hi")).Should().BeFalse();

        ChatRule.Contains("a").And(ChatRule.Contains("b")).Matches(MakeMessage("ab")).Should().BeTrue();
        ChatRule.Contains("a").And(ChatRule.Contains("b")).Matches(MakeMessage("a")).Should().BeFalse();
        ChatRule.Contains("x").Or(ChatRule.Contains("a")).Matches(MakeMessage("a")).Should().BeTrue();
        ChatRule.Contains("a").Not().Matches(MakeMessage("zzz")).Should().BeTrue();
    }

    #endregion

    #region Eorzea time & region shapes

    [Fact]
    public void EorzeaHour_IsDeterministicAndInRange()
    {
        var time = DateTimeOffset.FromUnixTimeSeconds(0);
        EorzeaTimeSource.ComputeEorzeaHour(time).Should().Be(0);

        // One Eorzea hour = 175 real seconds.
        EorzeaTimeSource.ComputeEorzeaHour(DateTimeOffset.FromUnixTimeSeconds(175)).Should().Be(1);
        EorzeaTimeSource.ComputeEorzeaHour(DateTimeOffset.FromUnixTimeSeconds(175 * 24)).Should().Be(0);

        EorzeaTimeSource.IsNight(5).Should().BeTrue();
        EorzeaTimeSource.IsNight(6).Should().BeFalse();
        EorzeaTimeSource.IsNight(17).Should().BeFalse();
        EorzeaTimeSource.IsNight(18).Should().BeTrue();
    }

    [Fact]
    public void RegionShapes_ContainAndApplyMargins()
    {
        var circle = RegionShape.Circle(new Vector3(0, 0, 0), 10);
        circle.Contains(new Vector3(5, 99, 5)).Should().BeTrue("circles ignore height");
        circle.Contains(new Vector3(11, 0, 0)).Should().BeFalse();
        circle.ContainsWithMargin(new Vector3(10.4f, 0, 0), 0.5f).Should().BeTrue();

        var box = RegionShape.Box(new Vector3(0, 0, 0), new Vector3(10, 10, 10));
        box.Contains(new Vector3(5, 5, 5)).Should().BeTrue();
        box.Contains(new Vector3(5, 11, 5)).Should().BeFalse();
        box.ContainsWithMargin(new Vector3(10.3f, 5, 5), 0.5f).Should().BeTrue();

        var predicate = RegionShape.Predicate(p => p.X > 100);
        predicate.Contains(new Vector3(101, 0, 0)).Should().BeTrue();
        predicate.Contains(new Vector3(99, 0, 0)).Should().BeFalse();
    }

    #endregion

    #region Module lifecycle without the game

    [Fact]
    public void Dispose_InvalidatesEveryToken()
    {
        var watcher = MakeWatcher();

        var token = watcher.Subscribe<TestEvent>(_ => { });
        var watchToken = watcher.WatchValue(() => 1, (_, _) => { });

        watcher.Dispose();

        token.IsActive.Should().BeFalse();
        watcher.SubscriptionCount.Should().Be(0);
        watcher.ValueWatcherCount.Should().Be(0);
        _ = watchToken;
    }

    [Fact]
    public void PublishToEventBus_WithoutBus_IsInertAndLogsOnly()
    {
        var watcher = MakeWatcher();

        var token = watcher.PublishToEventBus<TestEvent>();
        token.Should().NotBeNull();

        // Dispatch still works and nothing throws without a bus.
        watcher.Publish(new TestEvent(1));
        watcher.Dispose();
    }

    #endregion
}
