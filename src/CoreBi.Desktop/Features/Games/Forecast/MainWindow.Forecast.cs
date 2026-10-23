using LibreHardwareMonitor.Hardware;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CoreBi.Engine.Games;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace CoreBi.Desktop;

public partial class MainWindow
{
    internal readonly HowManyFpsClient _fpsApi = new();
    internal List<GameRow> _gameRows = new();
    internal ListCollectionView? _gameView;
    internal CancellationTokenSource? _gameFetch;
    internal string _cpuName = "", _gpuName = "";
    internal double _cpuIdx, _gpuIdx;

    // ---------------- Jogos ----------------

    internal void InitGames()
    {
        try
        {
            using var s = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            foreach (System.Management.ManagementObject mo in s.Get()) { _cpuName = mo["Name"]?.ToString()?.Trim() ?? ""; break; }
        }
        catch { }
        _gpuName = _gpus.Count > 0 ? _gpus[0].Name : "";
        _gpuIdx = GameCatalog.GpuPerf(_gpuName) ?? 0;
        _cpuIdx = GameCatalog.CpuPerf(_cpuName) ?? 0;
        GamesSub.Text = $"FPS esperado no seu PC com {GameCatalog.ShortGpuName(_gpuName)} e {GameCatalog.ShortCpuName(_cpuName)}.";

        _fpsApi.ApiKey = _settings.HowManyFpsKey;
        if (_fpsApi.Configured) ApiKeyBox.Password = _settings.HowManyFpsKey;

        _gameRows = GameCatalog.Games.Select(g => new GameRow(g)).ToList();
        _gameView = new ListCollectionView(_gameRows);
        GameList.ItemsSource = _gameView;
        RefreshGames();
    }

    internal void GameOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_gameView != null) RefreshGames(); // também dispara durante InitializeComponent
    }

    internal void GameSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (_gameView == null) return;
        string q = GameSearch.Text.Trim();
        _gameView.Filter = q.Length == 0 ? null : o => ((GameRow)o).Name.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    internal void ApiKeyBox_Changed(object sender, RoutedEventArgs e)
        => ApiKeyHint.Visibility = ApiKeyBox.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    internal void BtnApiKey_Click(object sender, RoutedEventArgs e)
    {
        string? previous = _settings.HowManyFpsKey;
        _settings.HowManyFpsKey = ApiKeyBox.Password;
        if (!SaveSettings()) { _settings.HowManyFpsKey = previous; return; }
        _fpsApi.ApiKey = _settings.HowManyFpsKey;
        RefreshGames();
    }

    /// <summary>Recalcula a estimativa local de todos os jogos e, com chave configurada, consulta a API externa.</summary>
    internal void RefreshGames()
    {
        string res = CheckedTag(GameResPanel) ?? "1920x1080";
        string preset = CheckedTag(GamePresetPanel) ?? "Alto";
        string up = CheckedTag(GameUpscalePanel) ?? "Native";

        foreach (var row in _gameRows)
        {
            var est = GameCatalog.Estimate(row.Game, _gpuIdx, _cpuIdx, res, preset, up);
            string limiter = est.Limiter switch
            {
                Bottleneck.Cpu => "gargalo: CPU",
                Bottleneck.Gpu => "gargalo: GPU",
                Bottleneck.Engine => $"trava do jogo em {row.Game.EngineCap:0} FPS",
                _ => ""
            };
            row.Set(est.Avg, 0, est.Limiter == Bottleneck.Unknown ? "Sem referência" : "Estimativa local",
                est.Limiter == Bottleneck.Unknown ? "CPU/GPU fora da base; nenhum FPS local foi presumido." : limiter);
        }

        _gameFetch?.Cancel();
        _gameFetch?.Dispose();
        _gameFetch = null;
        if (_fpsApi.Configured)
        {
            GameSourceTitle.Text = "Dados da HowManyFPS (API externa)";
            GameSourceText.Text = "Valores devolvidos pela API externa; compatibilidade requer validação com sua chave. Cache de 7 dias. " +
                                  "A API usa as configurações gráficas dela; \"Gráficos\" vale só para a estimativa.";
            BtnApiKey.Content = "Atualizar chave";
            _gameFetch = CancellationTokenSource.CreateLinkedTokenSource(_features.Lifetime);
            _ = FetchGamesAsync(res, up, _gameFetch.Token);
        }
        else
        {
            GameSourceTitle.Text = "Estimativa local, aproximada";
            GameSourceText.Text = "Aproximação por referências de CPU/GPU. Modelos fora da base ficam sem estimativa. " +
                                  "A integração opcional HowManyFPS requer chave e validação do serviço.";
            BtnApiKey.Content = "Salvar chave";
        }
    }

    internal async Task FetchGamesAsync(string res, string up, CancellationToken ct)
    {
        string cpu = GameCatalog.ShortCpuName(_cpuName), gpu = GameCatalog.ShortGpuName(_gpuName);
        int ok = 0;
        foreach (var row in _gameRows)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                var r = await _fpsApi.GetAsync(cpu, gpu, row.Name, res, up, ct);
                ct.ThrowIfCancellationRequested();
                row.Set(r.AvgFps, r.Min1Fps, "HowManyFPS", "");
                ok++;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) return;
                row.Set(row.Fps, 0, "Estimativa local", $"API: {ex.Message}");
                if (ex.Message.Contains("chave") || ex.Message.Contains("cota"))
                {
                    GameSourceText.Text = $"A API recusou a consulta ({ex.Message}). Mostrando a estimativa local.";
                    return; // não adianta insistir nos outros jogos
                }
            }
        }
        if (!ct.IsCancellationRequested)
            GameSourceText.Text = $"{ok} de {_gameRows.Count} jogos com dados da HowManyFPS; os demais usam a estimativa local.";
    }

}
