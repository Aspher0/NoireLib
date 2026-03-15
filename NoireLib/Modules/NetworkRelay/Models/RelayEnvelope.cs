using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace NoireLib.NetworkRelay;

internal sealed class RelayEnvelope
{
    public string Kind { get; set; } = "message";
    public string Channel { get; set; } = "default";
    public string MessageId { get; set; } = Guid.NewGuid().ToString("N");
    public string? CorrelationMessageId { get; set; }
    public string SenderId { get; set; } = string.Empty;
    public string? SenderDisplayName { get; set; }
    public int? SenderReliablePort { get; set; }
    public bool RequiresAcknowledgement { get; set; }
    public string? MessageType { get; set; }
    public JToken? Payload { get; set; }
    public DateTimeOffset SentAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? TargetPeerId { get; set; }
    public List<string>? TargetPeerIds { get; set; }
}
