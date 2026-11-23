using Microsoft.Win32;

namespace CoreBi.Engine.Tools.Startup;

/// <summary>
/// Programa que abre junto com o Windows: chave Run do registro, atalho na pasta Inicializar ou tarefa de inicialização de um app
/// da Microsoft Store (as mesmas três origens que o Gerenciador de Tarefas mostra).
/// </summary>
public sealed class StartupEntry
{
    /// <summary>Nome da entrada no Windows (valor da chave Run, nome do atalho ou da tarefa do app).</summary>
    public string Name { get; init; } = "";
    /// <summary>Nome para mostrar: o da entrada ou, quando ele é um identificador ("…AutoLaunch_579CF1…"), o nome do produto.</summary>
    public string Title { get; init; } = "";
    /// <summary>Fabricante do programa (CompanyName do executável, ou da DLL quando a entrada abre pelo rundll32).</summary>
    public string Publisher { get; init; } = "";
    public string Command { get; init; } = "";
    public string Source { get; init; } = "";
    public bool Machine { get; init; } // HKLM / pasta comum (todos os usuários): alterar precisa de administrador
    public bool Enabled { get; set; }
    /// <summary>Descrição do arquivo (FileDescription), mostrada na dica do nome.</summary>
    public string Detail { get; init; } = "";
    public bool FileMissing { get; init; }
    /// <summary>Arquivo de onde sai o ícone: o executável, a DLL ou a imagem do app da Store.</summary>
    public string? IconPath { get; init; }
    /// <summary>Pasta do programa, para "Abrir local do arquivo"; null quando não se sabe onde está.</summary>
    public string? Location { get; init; }
    /// <summary>App da Store: dá para ligar e desligar, mas não remover (a tarefa pertence ao pacote do app).</summary>
    public bool Store => Kind == "Store";
    public bool CanRemove => !Store;
    /// <summary>De onde o Windows abre o programa, em palavras: "Registro, só você", "Pasta Inicializar, todos", "Microsoft Store".</summary>
    public string Origin => OriginOf(Kind, Machine);

    public static string OriginOf(string kind, bool machine) => kind switch
    {
        "Store" => "Microsoft Store",
        "Folder" => machine ? "Pasta Inicializar, todos" : "Pasta Inicializar, só você",
        _ => machine ? "Registro, todos" : "Registro, só você",
    };

    internal string Kind { get; init; } = "";     // Run | Run32 | Folder | Store
    internal string? FilePath { get; init; }      // Folder: o atalho; Store: a chave do estado da tarefa
}

/// <summary>Item removido, guardado no espaço protegido para "Removidos".</summary>
public sealed class StartupBackup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int ValueKind { get; set; } = (int)RegistryValueKind.String;
    public string? FileHash { get; set; }
    public byte[]? ApprovedState { get; set; }
    public bool ApprovedStateCaptured { get; set; }
    public string Kind { get; set; } = "";
    public bool Machine { get; set; }
    public string Name { get; set; } = "";
    public string? Command { get; set; }
    public string? Backup { get; set; }    // cópia do atalho na pasta protegida de Startup
    public string? Original { get; set; }
    /// <summary>Quando saiu da inicialização (backups antigos não têm: vale a data do arquivo).</summary>
    public DateTime? RemovedAt { get; set; }
    /// <summary>Nome e fabricante como apareciam na lista, para a linha de "Removidos".</summary>
    public string? Title { get; set; }
    public string? Publisher { get; set; }
    public string? IconPath { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Title) ? Name : Title!;
}
