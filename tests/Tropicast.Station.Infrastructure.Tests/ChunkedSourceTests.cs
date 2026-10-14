using System.Net;
using System.Net.Sockets;
using System.Text;
using Tropicast.Station.Core.Broadcasting;
using Tropicast.Station.Tests;

namespace Tropicast.Station.Infrastructure.Tests;

public sealed class ChunkedSourceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_write_is_one_chunk_and_complete_sends_the_last_chunk()
    {
        using var inner = new MemoryStream();
        var chunked = new ChunkedWriteStream(inner);
        await chunked.WriteAsync("abc"u8.ToArray(), Token);
        await chunked.WriteAsync(ReadOnlyMemory<byte>.Empty, Token);
        await chunked.WriteAsync(new byte[26], Token);
        await chunked.CompleteAsync(Token);
        await chunked.CompleteAsync(Token);

        Assert.Equal("3\r\nabc\r\n1A\r\n" + new string('\0', 26) + "\r\n0\r\n\r\n", Encoding.ASCII.GetString(inner.ToArray()));
    }

    [Fact]
    public async Task After_a_failed_write_nothing_more_is_written()
    {
        using var inner = new FailingStream();
        var chunked = new ChunkedWriteStream(inner);
        await Assert.ThrowsAsync<IOException>(() => chunked.WriteAsync("abc"u8.ToArray(), Token).AsTask());

        inner.Fail = false;
        await Assert.ThrowsAsync<IOException>(() => chunked.WriteAsync("abc"u8.ToArray(), Token).AsTask());
        await chunked.CompleteAsync(Token);
        Assert.Equal(0, inner.Length);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Source_body_is_chunked_only_when_the_profile_asks(bool chunked)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var serve = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(deadline.Token);
            await using var stream = client.GetStream();
            var header = new StringBuilder();
            var one = new byte[1];
            while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                Assert.NotEqual(0, await stream.ReadAsync(one, deadline.Token));
                header.Append((char)one[0]);
            }
            // Icecast 2.5 answers a PUT with Expect: 100-continue like this.
            await stream.WriteAsync("HTTP/1.1 100 Continue\r\nContent-Length: 0\r\n\r\n"u8.ToArray(), deadline.Token);
            using var body = new MemoryStream();
            await stream.CopyToAsync(body, deadline.Token);
            return (header.ToString(), Encoding.ASCII.GetString(body.ToArray()));
        }, deadline.Token);

        var profile = TestProfiles.Valid() with { Host = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port, ChunkedBody = chunked };
        await using (var connection = await TropicastSourceConnection.ConnectAsync(new BroadcastTarget(profile, "test-only"), deadline.Token))
        {
            await connection.AudioStream.WriteAsync("audio"u8.ToArray(), deadline.Token);
        }

        var (requestHeader, received) = await serve;
        if (chunked)
        {
            Assert.Contains("Transfer-Encoding: chunked\r\n", requestHeader, StringComparison.Ordinal);
            Assert.Equal("5\r\naudio\r\n0\r\n\r\n", received);
        }
        else
        {
            Assert.DoesNotContain("Transfer-Encoding", requestHeader, StringComparison.Ordinal);
            Assert.Equal("audio", received);
        }
    }

    private sealed class FailingStream : MemoryStream
    {
        internal bool Fail { get; set; } = true;

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => Fail ? ValueTask.FromException(new IOException("Connection reset.")) : base.WriteAsync(buffer, cancellationToken);
    }
}
