using System.IO.Pipes;
using System.Text;

namespace TSharpVision.Diagnostics.Keyboard.Protocol;

/// <summary>JSON Lines framing over a duplex stream. One reader and one writer may use it concurrently.</summary>
public sealed class PipeChannel : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly Stream _stream;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;

    public PipeChannel(Stream stream)
    {
        _stream = stream;
        _reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: false, 4096, leaveOpen: true);
        _writer = new StreamWriter(stream, Utf8, 4096, leaveOpen: true) { NewLine = "\n" };
    }

    /// <summary>A current-user-only server that accepts exactly one client.</summary>
    public static NamedPipeServerStream CreateServer(out string name)
    {
        name = "tsv-kbdiag-" + Guid.NewGuid().ToString("N");
        return new NamedPipeServerStream(name, PipeDirection.InOut, maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    }

    public static NamedPipeClientStream Connect(string name, int timeoutMs)
    {
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        client.Connect(timeoutMs);
        return client;
    }

    public void Send(Message message)
    {
        _writer.WriteLine(Wire.Serialize(message));
        _writer.Flush();
    }

    /// <summary>The next message; null when the peer closed the stream or sent something unreadable.</summary>
    public async Task<Message?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        string? line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        return line is null ? null : Wire.Parse(line);
    }

    /// <summary>The next message within <paramref name="timeout"/>; null on timeout, close or garbage.</summary>
    public Message? Receive(TimeSpan timeout)
    {
        using var cancel = new CancellationTokenSource(timeout);
        try { return ReceiveAsync(cancel.Token).GetAwaiter().GetResult(); }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _reader.Dispose();
        try { _writer.Dispose(); } catch (IOException) { } catch (ObjectDisposedException) { }
        _stream.Dispose();
    }
}
