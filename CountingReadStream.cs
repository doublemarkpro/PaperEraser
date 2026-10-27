using System.IO;

namespace PaperEraser;

internal sealed class CountingReadStream : Stream
{
    private readonly Stream inner;
    private readonly Action<long> report;
    private long read;
    internal CountingReadStream(Stream inner, Action<long> report) { this.inner = inner; this.report = report; }
    private void Count(int count) { if (count > 0) { read += count; report(read); } }
    public override int Read(byte[] buffer, int offset, int count) { var n = inner.Read(buffer, offset, count); Count(n); return n; }
    public override int Read(Span<byte> buffer) { var n = inner.Read(buffer); Count(n); return n; }
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) { var n = await inner.ReadAsync(buffer, offset, count, cancellationToken); Count(n); return n; }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) { var n = await inner.ReadAsync(buffer, cancellationToken); Count(n); return n; }
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
