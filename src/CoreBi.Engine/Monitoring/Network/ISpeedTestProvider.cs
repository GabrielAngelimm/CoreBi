namespace CoreBi.Engine.Monitoring.Network;

internal interface ISpeedTestProvider
{
    string DisplayName { get; }
    int DownloadSeconds { get; }
    int UploadSeconds { get; }
    DateTimeOffset? GetActiveRateLimit(DateTimeOffset now);
    Task<string?> PrepareAsync(HttpClient client, Action<string> status, CancellationToken ct);
    Task<double?> DownloadAsync(NetworkTransfer transfer, HttpClient client, NetworkRateLimitSession limit, CancellationToken ct);
    bool CanUpload(NetworkRateLimitSession limit);
    Task<double?> UploadAsync(NetworkTransfer transfer, HttpClient client, NetworkRateLimitSession limit, CancellationToken ct);
}

internal sealed class CloudflareSpeedProvider(NetworkRateLimitStore rateLimit) : ISpeedTestProvider
{
    public const int DefaultDownloadSeconds = 8;
    public const int DefaultUploadSeconds = 6;
    private const int DownloadStreams = 4, UploadStreams = 3, DownloadWarmupMs = 1000, UploadGraceMs = 3000;

    public string DisplayName => "Cloudflare";
    public int DownloadSeconds => DefaultDownloadSeconds;
    public int UploadSeconds => DefaultUploadSeconds;

    public DateTimeOffset? GetActiveRateLimit(DateTimeOffset now) => rateLimit.Active(now);

    public Task<string?> PrepareAsync(HttpClient client, Action<string> status, CancellationToken ct) =>
        Task.FromResult<string?>(null);

    public Task<double?> DownloadAsync(NetworkTransfer transfer, HttpClient client, NetworkRateLimitSession limit, CancellationToken ct) =>
        NetworkTest.Download(transfer, client, limit, TimeSpan.FromSeconds(DownloadSeconds),
            TimeSpan.FromMilliseconds(DownloadWarmupMs), DownloadStreams, ct);

    public bool CanUpload(NetworkRateLimitSession limit) => !limit.Limited;

    public Task<double?> UploadAsync(NetworkTransfer transfer, HttpClient client, NetworkRateLimitSession limit, CancellationToken ct) =>
        NetworkTest.Upload(transfer, client, limit, TimeSpan.FromSeconds(UploadSeconds),
            TimeSpan.FromMilliseconds(UploadGraceMs), UploadStreams, ct);
}

internal sealed class MlabSpeedProvider : ISpeedTestProvider
{
    private MlabSpeedTest.Target? _target;

    public string DisplayName => _target?.Location ?? "M-Lab";
    public int DownloadSeconds => 10;
    public int UploadSeconds => 10;

    public DateTimeOffset? GetActiveRateLimit(DateTimeOffset now) => null;

    public async Task<string?> PrepareAsync(HttpClient client, Action<string> status, CancellationToken ct)
    {
        status("Procurando servidor M-Lab");
        try
        {
            _target = await MlabSpeedTest.Locate(client, ct);
            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (InvalidOperationException ex) { return ex.Message; }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return "Não foi possível encontrar um servidor M-Lab disponível.";
        }
    }

    public Task<double?> DownloadAsync(NetworkTransfer transfer, HttpClient client, NetworkRateLimitSession limit, CancellationToken ct) =>
        _target != null ? MlabSpeedTest.Download(transfer, _target.Download, ct) : Task.FromResult<double?>(null);

    public bool CanUpload(NetworkRateLimitSession limit) => !limit.Limited && _target != null;

    public Task<double?> UploadAsync(NetworkTransfer transfer, HttpClient client, NetworkRateLimitSession limit, CancellationToken ct) =>
        MlabSpeedTest.Upload(transfer, _target!.Upload, ct);
}
