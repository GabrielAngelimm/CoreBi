using CoreBi.Engine.Persistence;


using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using CoreBi.Engine.Infrastructure;

namespace CoreBi.Engine.Games;

/// <summary>
/// Cliente opcional HowManyFPS. O contrato externo precisa de validação com credenciais reais.
/// Respostas válidas ficam em cache por sete dias, separadas por credencial e parâmetros.
/// </summary>
public sealed class HowManyFpsClient
{
    private const string Endpoint = "https://howmanyfps.com/api";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(7);
    private readonly string _cachePath;

    private static readonly HttpClient DefaultHttp = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HttpClient _http;
    private readonly object _sync = new();
    private readonly Dictionary<string, CacheEntry> _cache;

    public string? ApiKey { get; set; }
    public bool Configured => !string.IsNullOrWhiteSpace(ApiKey);

    public sealed record Result(double AvgFps, double Min1Fps);
    private sealed record CacheEntry(double AvgFps, double Min1Fps, DateTime Fetched);

    private sealed class Response
    {
        [JsonPropertyName("avgFps")] public double? AvgFps { get; set; }
        [JsonPropertyName("min1Fps")] public double? Min1Fps { get; set; }
    }

    public HowManyFpsClient() : this(DefaultHttp, Path.Combine(AppSettings.Folder, "fps-cache.json")) { }

    internal HowManyFpsClient(HttpClient http, string cachePath)
    {
        _http = http;
        _cachePath = cachePath;
        try
        {
            _cache = File.Exists(cachePath) && new FileInfo(cachePath).Length <= 2_097_152
                ? JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(File.ReadAllText(cachePath)) ?? new()
                : new();
            foreach (string key in _cache.Where(x => x.Value == null || !Valid(x.Value.AvgFps, x.Value.Min1Fps)).Select(x => x.Key).ToArray()) _cache.Remove(key);
        }
        catch (Exception ex) { DiagnosticLog.Write("fps.cache.load", ex); _cache = new(); }
    }

    /// <summary>Consulta (ou devolve do cache). Lança exceção com mensagem legível em caso de erro.</summary>
    public async Task<Result> GetAsync(string cpu, string gpu, string game, string resolution, string upscaling,
                                       CancellationToken ct = default)
    {
        if (!Configured) throw new InvalidOperationException("Chave da API não configurada.");
        ct.ThrowIfCancellationRequested();
        string apiKey = ApiKey!;
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { apiKey, cpu, gpu, game, resolution, upscaling }))));
        lock (_sync)
            if (_cache.TryGetValue(key, out var c) && DateTime.UtcNow - c.Fetched is var age && age >= TimeSpan.Zero && age < CacheTtl
                && Valid(c.AvgFps, c.Min1Fps)) return new Result(c.AvgFps, c.Min1Fps);

        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new { cpu, gpu, game, resolution, upscaling })
        };
        // O formato exato de autenticação vem com as credenciais; enviamos os dois cabeçalhos mais comuns.
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        req.Headers.TryAddWithoutValidation("x-api-key", apiKey);

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            string msg = (int)resp.StatusCode switch
            {
                401 or 403 => "chave recusada pela API",
                404 => "jogo ou hardware não encontrado na base",
                429 => "cota de requisições esgotada",
                _ => $"HTTP {(int)resp.StatusCode}"
            };
            throw new HttpRequestException(msg);
        }

        await resp.Content.LoadIntoBufferAsync(65_536, ct).ConfigureAwait(false);
        var data = await resp.Content.ReadFromJsonAsync<Response>(cancellationToken: ct).ConfigureAwait(false)
                   ?? throw new HttpRequestException("resposta vazia");
        if (data.AvgFps is not double avg) throw new HttpRequestException("resposta sem avgFps");
        var result = new Result(avg, data.Min1Fps ?? 0);
        if (!Valid(result.AvgFps, result.Min1Fps)) throw new HttpRequestException("resposta com FPS inválido");

        lock (_sync)
        {
            _cache[key] = new CacheEntry(result.AvgFps, result.Min1Fps, DateTime.UtcNow);
            SaveCache();
        }
        return result;
    }

    private void SaveCache()
    {
        try
        {
            foreach (var key in _cache.Where(x => DateTime.UtcNow - x.Value.Fetched > CacheTtl).Select(x => x.Key).ToArray()) _cache.Remove(key);
            while (_cache.Count > 500) _cache.Remove(_cache.MinBy(x => x.Value.Fetched).Key);
            AtomicJsonFile.Write(_cachePath, _cache);
        }
        catch (Exception ex) { DiagnosticLog.Write("fps.cache.save", ex); }
    }

    private static bool Valid(double average, double low) => double.IsFinite(average) && average > 0
        && double.IsFinite(low) && low >= 0 && low <= average;
}
