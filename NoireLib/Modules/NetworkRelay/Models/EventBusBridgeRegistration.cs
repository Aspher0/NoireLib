using NoireLib.EventBus;

namespace NoireLib.NetworkRelay;

internal sealed class EventBusBridgeRegistration(string? key, EventSubscriptionToken token)
{
    public string? Key { get; } = key;
    public EventSubscriptionToken Token { get; } = token;
}
