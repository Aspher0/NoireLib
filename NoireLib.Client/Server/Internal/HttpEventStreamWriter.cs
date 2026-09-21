using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote.Internal;

internal static class HttpEventStreamWriter
{
    // The SSE keep-alive comment. It also notices a peer that vanished without closing.
    internal static readonly byte[] KeepAlive = Encoding.UTF8.GetBytes(": keep-alive\n\n");

    // Type = topic, id = the sequence to resume from, data = the whole event document.
    internal static async Task WriteAsync(Stream stream, string topic, long seq, object payload, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();

        text.Append("event: ").Append(topic).Append('\n');

        if (seq > 0)
            text.Append("id: ").Append(seq.ToString(CultureInfo.InvariantCulture)).Append('\n');

        text.Append("data: ").Append(NoireRemoteJson.Write(payload)).Append('\n').Append('\n');

        var bytes = Encoding.UTF8.GetBytes(text.ToString());

        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
