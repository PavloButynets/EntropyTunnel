namespace EntropyTunnel.Client.Services;

/// <summary>
/// Lightweight service that holds the current tunnel connection state.
/// Written by TunnelService; read by the dashboard API (/api/status).
/// All fields use volatile to guarantee visibility across threads without locking.
/// </summary>
public sealed class TunnelStatusService
{
    private volatile bool _isConnected;
    private volatile string _publicUrl = string.Empty;
    private DateTimeOffset _connectedSince;

    public bool IsConnected => _isConnected;
    public string PublicUrl => _publicUrl;

    public void SetConnected(string publicUrl)
    {
        _publicUrl = publicUrl;
        _connectedSince = DateTimeOffset.UtcNow;
        _isConnected = true;
    }

    public void SetDisconnected()
    {
        _isConnected = false;
    }

    public object GetStatus() => new
    {
        IsConnected = _isConnected,
        PublicUrl = _publicUrl,
        UptimeSeconds = _isConnected
            ? (long)(DateTimeOffset.UtcNow - _connectedSince).TotalSeconds
            : 0L
    };
}
