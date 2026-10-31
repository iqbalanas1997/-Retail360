namespace Retail360.Application.Audit;

public static class AuditActor
{
    private static readonly AsyncLocal<string?> Current = new();

    public static string? UserId => string.IsNullOrWhiteSpace(Current.Value) ? null : Current.Value;

    public static IDisposable Begin(string? userId)
    {
        var previous = Current.Value;
        Current.Value = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();
        return new Restore(previous);
    }

    private sealed class Restore : IDisposable
    {
        private readonly string? _previous;
        private bool _disposed;

        public Restore(string? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Current.Value = _previous;
        }
    }
}
