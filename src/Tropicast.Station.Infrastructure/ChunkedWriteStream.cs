using System.Globalization;
using System.Text;

namespace Tropicast.Station.Infrastructure;

/// <summary>
/// Write-only HTTP/1.1 chunked body (RFC 9112 §7.1): each write becomes one chunk, sent with a single write so a
/// chunk never leaves in several small TCP segments. <see cref="CompleteAsync"/> sends the terminating chunk.
/// </summary>
internal sealed class ChunkedWriteStream(Stream inner) : Stream
{
    private static readonly byte[] LastChunk = "0\r\n\r\n"u8.ToArray();
    private bool _failed;
    private bool _completed;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty)
        {
            // An empty chunk would end the body.
            return;
        }
        ObjectDisposedException.ThrowIf(_completed, this);
        if (_failed)
        {
            throw new IOException("The source stream failed earlier and can no longer be written.");
        }
        var size = Encoding.ASCII.GetBytes(buffer.Length.ToString("X", CultureInfo.InvariantCulture) + "\r\n");
        var chunk = new byte[size.Length + buffer.Length + 2];
        size.CopyTo(chunk, 0);
        buffer.Span.CopyTo(chunk.AsSpan(size.Length));
        chunk[^2] = (byte)'\r';
        chunk[^1] = (byte)'\n';
        try
        {
            await inner.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // A partial chunk may be on the wire: the framing can no longer be trusted.
            _failed = true;
            throw;
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Write(byte[] buffer, int offset, int count)
        => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    /// <summary>Ends the body cleanly. Does nothing after a failed write: the connection is closed instead.</summary>
    public async Task CompleteAsync(CancellationToken cancellationToken)
    {
        if (_completed || _failed)
        {
            return;
        }
        _completed = true;
        await inner.WriteAsync(LastChunk, cancellationToken).ConfigureAwait(false);
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override void Flush() => inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
