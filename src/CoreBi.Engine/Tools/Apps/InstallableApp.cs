using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace CoreBi.Engine.Tools.Apps;

/// <summary>Kits da página: marcam de uma vez os apps de um uso comum.</summary>
[Flags]
public enum AppKit
{
    None = 0,
    PostFormat = 1,
    Games = 2,
    Work = 4,
}

/// <summary>
/// Um pacote do winget e como ele aparece em "Aplicativos instalados". O nome serve para a leitura rápida do registro; o ID é o
/// que vale quando o winget responde (pacotes MSIX, como Teams e ChatGPT, nem aparecem no registro).
/// </summary>
public sealed class AppPackage
{
    public AppPackage(string id, string? displayPattern)
    {
        Id = id;
        DisplayPattern = displayPattern == null ? null : new Regex(displayPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public string Id { get; }
    /// <summary>Expressão para o DisplayName no registro; nulo quando o app não aparece lá.</summary>
    public Regex? DisplayPattern { get; }
    /// <summary>Teste próprio de presença (componentes que não entram em "Aplicativos instalados", como o DirectX 9).</summary>
    public Func<bool>? Probe { get; init; }
}

/// <summary>App do catálogo "Instalar apps", instalado pelo winget. Um cartão pode trazer mais de um pacote (Visual C++ antigos).</summary>
public sealed class InstallableApp : INotifyPropertyChanged
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Category { get; init; } = "";
    /// <summary>Nome do ícone em Assets/Apps (sem a extensão).</summary>
    public string Icon { get; init; } = "";
    public IReadOnlyList<AppPackage> Packages { get; init; } = Array.Empty<AppPackage>();
    public AppKit Kits { get; init; }

    /// <summary>O primeiro pacote, que identifica o cartão.</summary>
    public string WingetId => Packages.Count > 0 ? Packages[0].Id : "";

    private bool _selected, _installed;
    private string _status = "";

    public bool Selected { get => _selected; set => Set(ref _selected, value); }
    public bool Installed
    {
        get => _installed;
        set { Set(ref _installed, value); OnChanged(nameof(CanSelect)); }
    }
    public bool CanSelect => !Installed;
    public string Status { get => _status; set => Set(ref _status, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    private void Set<T>(ref T field, T value, [CallerMemberName] string? p = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnChanged(p);
    }
}
