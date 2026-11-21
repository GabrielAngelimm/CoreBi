using Microsoft.Win32;

namespace CoreBi.Engine.Tools.Startup;

/// <summary>Programa que abre junto com o Windows (chave Run do registro ou atalho na pasta Inicializar).</summary>
public sealed class StartupEntry
{
    public string Name { get; init; } = "";
    public string Command { get; init; } = "";
    public string Source { get; init; } = "";
    public bool Machine { get; init; } // HKLM / pasta comum (todos os usuários): alterar precisa de administrador
    public bool Enabled { get; set; }
    public string Detail { get; init; } = "";
    public bool FileMissing { get; init; }

    internal string Kind { get; init; } = "";     // Run | Run32 | Folder
    internal string? FilePath { get; init; }      // só para Folder
}

/// <summary>Item removido, guardado no espaço protegido para "Restaurar removidos".</summary>
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
}
