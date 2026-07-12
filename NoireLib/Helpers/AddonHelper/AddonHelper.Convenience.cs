using Dalamud.Game.Addon.Lifecycle;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Helpers;

using AddonLifecycleEvent = AddonEvent;

/// <summary>
/// Convenience layer of <see cref="AddonHelper"/>: fluent wrappers, typed addon access, one-shot lifecycle listeners, and ready-waiting helpers.
/// </summary>
public static partial class AddonHelper
{
    /// <summary>
    /// Gets a fluent wrapper for the addon with the given name, whether it is ready or not.<br/>
    /// All wrapper members are safe to call even when the addon does not exist or is not ready.
    /// </summary>
    /// <param name="addonName">The name of the addon to get.</param>
    /// <returns>The addon wrapper. Invalid if the addon does not exist.</returns>
    public static NoireAddon GetAddon(string addonName)
        => NoireAddon.Get(addonName);

    /// <summary>
    /// Gets a fluent wrapper for the addon with the given name, only if it is loaded and ready to be interacted with.
    /// </summary>
    /// <param name="addonName">The name of the addon to get.</param>
    /// <returns>The addon wrapper. Invalid if the addon does not exist or is not ready.</returns>
    public static NoireAddon GetReadyAddon(string addonName)
    {
        var addon = NoireAddon.Get(addonName);
        return addon.IsReady ? addon : default;
    }

    /// <summary>
    /// Determines if the addon with the given name is loaded and ready to be interacted with.
    /// </summary>
    /// <param name="addonName">The name of the addon to check.</param>
    /// <returns>True if the addon exists and is ready; otherwise, false.</returns>
    public static bool IsAddonReady(string addonName)
        => NoireAddon.Get(addonName).IsReady;

    /// <summary>
    /// Tries to get an addon by name as a typed pointer (e.g. AddonTalk, AddonSelectYesno), without checking whether it is ready.
    /// </summary>
    /// <typeparam name="T">The concrete addon struct type to cast to.</typeparam>
    /// <param name="addonName">The name of the addon to get.</param>
    /// <param name="addonPtr">A typed pointer to the addon, if found.</param>
    /// <returns>True if the addon was found; otherwise, false.</returns>
    public static unsafe bool TryGetAddon<T>(string addonName, out T* addonPtr) where T : unmanaged
    {
        addonPtr = null;

        if (!TryGetAddon(addonName, out AtkUnitBase* basePtr))
            return false;

        addonPtr = (T*)basePtr;
        return true;
    }

    /// <summary>
    /// Tries to get an addon by name as a typed pointer (e.g. AddonTalk, AddonSelectYesno), and checks if it's loaded and ready to be interacted with.
    /// </summary>
    /// <typeparam name="T">The concrete addon struct type to cast to.</typeparam>
    /// <param name="addonName">The name of the addon to get.</param>
    /// <param name="addonPtr">
    /// A typed pointer to the addon, if found.<br/>
    /// Will also be populated even if the addon is not ready.
    /// </param>
    /// <returns>True if the addon is found and ready to be interacted with; otherwise, false.</returns>
    public static unsafe bool TryGetReadyAddon<T>(string addonName, out T* addonPtr) where T : unmanaged
    {
        addonPtr = null;

        if (!TryGetReadyAddon(addonName, out AtkUnitBase* basePtr))
        {
            addonPtr = (T*)basePtr;
            return false;
        }

        addonPtr = (T*)basePtr;
        return true;
    }

    /// <summary>
    /// Registers a handler invoked whenever the addon with the given name has finished setting up.
    /// </summary>
    /// <param name="addonName">The addon name to listen for.</param>
    /// <param name="handler">The handler to invoke, receiving a fluent wrapper for the addon.</param>
    /// <param name="once">Whether the handler should only be invoked once, then automatically unregistered.</param>
    /// <returns>A disposable registration that unregisters the handler when disposed.</returns>
    public static IDisposable OnAddonSetup(string addonName, Action<NoireAddon> handler, bool once = false)
        => RegisterOneShotCapableListener(AddonLifecycleEvent.PostSetup, addonName, handler, once);

    /// <summary>
    /// Registers a handler invoked whenever the addon with the given name has finished refreshing.
    /// </summary>
    /// <param name="addonName">The addon name to listen for.</param>
    /// <param name="handler">The handler to invoke, receiving a fluent wrapper for the addon.</param>
    /// <param name="once">Whether the handler should only be invoked once, then automatically unregistered.</param>
    /// <returns>A disposable registration that unregisters the handler when disposed.</returns>
    public static IDisposable OnAddonRefresh(string addonName, Action<NoireAddon> handler, bool once = false)
        => RegisterOneShotCapableListener(AddonLifecycleEvent.PostRefresh, addonName, handler, once);

    /// <summary>
    /// Registers a handler invoked whenever the addon with the given name is about to be destroyed.
    /// </summary>
    /// <param name="addonName">The addon name to listen for.</param>
    /// <param name="handler">The handler to invoke, receiving the addon name.</param>
    /// <param name="once">Whether the handler should only be invoked once, then automatically unregistered.</param>
    /// <returns>A disposable registration that unregisters the handler when disposed.</returns>
    public static IDisposable OnAddonFinalize(string addonName, Action<string> handler, bool once = false)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return RegisterOneShotCapableListener(AddonLifecycleEvent.PreFinalize, addonName, addon => handler(addonName), once);
    }

    /// <summary>
    /// Runs an action as soon as the addon with the given name is loaded and ready to be interacted with.<br/>
    /// The check runs once per framework tick and the action is invoked on the framework thread.
    /// </summary>
    /// <param name="addonName">The addon name to wait for.</param>
    /// <param name="action">The action to run, receiving a fluent wrapper for the ready addon.</param>
    /// <param name="timeout">The optional maximum time to wait. When exceeded, the action is never invoked.</param>
    /// <returns>A disposable registration that cancels the wait when disposed.</returns>
    public static IDisposable RunWhenReady(string addonName, Action<NoireAddon> action, TimeSpan? timeout = null)
    {
        ValidateAddonName(addonName, nameof(addonName));
        ArgumentNullException.ThrowIfNull(action);

        return new ReadyPoller(addonName, action, null, timeout);
    }

    /// <summary>
    /// Asynchronously waits until the addon with the given name is loaded and ready to be interacted with.<br/>
    /// The check runs once per framework tick.
    /// </summary>
    /// <param name="addonName">The addon name to wait for.</param>
    /// <param name="timeout">The optional maximum time to wait.</param>
    /// <param name="cancellationToken">The optional cancellation token to cancel the wait.</param>
    /// <returns>A task that completes with true once the addon is ready, or false when the timeout was exceeded.</returns>
    public static Task<bool> WaitUntilReadyAsync(string addonName, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ValidateAddonName(addonName, nameof(addonName));

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<bool>(cancellationToken);

        TaskCompletionSource<bool> completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ReadyPoller poller = new(addonName, _ => completionSource.TrySetResult(true), () => completionSource.TrySetResult(false), timeout);

        if (cancellationToken.CanBeCanceled)
        {
            CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(() =>
            {
                poller.Dispose();
                completionSource.TrySetCanceled(cancellationToken);
            });

            completionSource.Task.ContinueWith(_ => cancellationRegistration.Dispose(), TaskScheduler.Default);
        }

        return completionSource.Task;
    }

    private static IDisposable RegisterOneShotCapableListener(AddonLifecycleEvent eventType, string addonName, Action<NoireAddon> handler, bool once)
    {
        ValidateAddonName(addonName, nameof(addonName));
        ArgumentNullException.ThrowIfNull(handler);

        if (!once)
            return RegisterLifecycleListener(eventType, addonName, (_, args) => handler(NoireAddon.From(args)));

        OneShotRegistration registration = new();

        IDisposable innerRegistration = RegisterLifecycleListener(eventType, addonName, (_, args) =>
        {
            if (!registration.TryClaim())
                return;

            try
            {
                handler(NoireAddon.From(args));
            }
            finally
            {
                // Unregistering from inside the lifecycle dispatch is deferred to the next tick to avoid mutating the listener list while it is being iterated.
                NoireService.Framework.RunOnTick(registration.Dispose);
            }
        });

        registration.SetInner(innerRegistration);
        return registration;
    }

    private sealed class OneShotRegistration : IDisposable
    {
        private readonly object gate = new();
        private IDisposable? inner;
        private bool claimed;
        private bool disposed;

        public bool TryClaim()
        {
            lock (gate)
            {
                if (claimed)
                    return false;

                claimed = true;
                return true;
            }
        }

        public void SetInner(IDisposable registration)
        {
            bool disposeNow;

            lock (gate)
            {
                disposeNow = disposed;

                if (!disposed)
                    inner = registration;
            }

            if (disposeNow)
                registration.Dispose();
        }

        public void Dispose()
        {
            IDisposable? registrationToDispose;

            lock (gate)
            {
                claimed = true;
                disposed = true;
                registrationToDispose = inner;
                inner = null;
            }

            registrationToDispose?.Dispose();
        }
    }

    private sealed class ReadyPoller : IDisposable
    {
        private readonly string addonName;
        private readonly Action<NoireAddon> onReady;
        private readonly Action? onTimeout;
        private readonly long? deadlineTick;
        private int completed;

        public ReadyPoller(string addonName, Action<NoireAddon> onReady, Action? onTimeout, TimeSpan? timeout)
        {
            this.addonName = addonName;
            this.onReady = onReady;
            this.onTimeout = onTimeout;
            deadlineTick = timeout.HasValue ? Environment.TickCount64 + (long)timeout.Value.TotalMilliseconds : null;

            NoireService.Framework.Update += OnFrameworkUpdate;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref completed, 1) == 0)
                NoireService.Framework.Update -= OnFrameworkUpdate;
        }

        private void OnFrameworkUpdate(Dalamud.Plugin.Services.IFramework framework)
        {
            var addon = GetReadyAddon(addonName);

            if (addon.IsReady)
            {
                Complete(addon);
                return;
            }

            if (deadlineTick.HasValue && Environment.TickCount64 >= deadlineTick.Value)
                CompleteTimedOut();
        }

        private void Complete(NoireAddon addon)
        {
            if (Interlocked.Exchange(ref completed, 1) != 0)
                return;

            NoireService.Framework.Update -= OnFrameworkUpdate;

            try
            {
                onReady(addon);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"Error in {nameof(AddonHelper)}.{nameof(RunWhenReady)} action for addon '{addonName}'.");
            }
        }

        private void CompleteTimedOut()
        {
            if (Interlocked.Exchange(ref completed, 1) != 0)
                return;

            NoireService.Framework.Update -= OnFrameworkUpdate;

            try
            {
                onTimeout?.Invoke();
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"Error in {nameof(AddonHelper)}.{nameof(RunWhenReady)} timeout handler for addon '{addonName}'.");
            }
        }
    }
}
