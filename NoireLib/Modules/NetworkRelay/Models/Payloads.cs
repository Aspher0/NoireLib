using Newtonsoft.Json.Linq;
using System;

namespace NoireLib.NetworkRelay;

internal sealed record RelayHelloPayload(int Port, int? ReliablePort, bool ReliableTransportEnabled);

internal sealed record RelayedEventPayload(string EventType, JToken Payload);

internal sealed record RelayAcknowledgementPayload(string MessageId, bool Success, string? ErrorMessage, DateTimeOffset AcknowledgedAtUtc);
