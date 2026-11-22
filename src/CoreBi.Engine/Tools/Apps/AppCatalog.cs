using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CoreBi.Engine.Tools.Apps;

/// <summary>
/// Catálogo de "Instalar apps", escolhido por Gabriel em 22/11/2026 (73 cartões). Os IDs foram conferidos no repositório oficial
/// do winget (microsoft/winget-pkgs) e os nomes de registro nos manifestos (AppsAndFeaturesEntries) ou no próprio instalador.
/// A ordem das categorias é a da página: as colunas são preenchidas nessa ordem, cada uma com as categorias inteiras.
/// </summary>
public static class AppCatalog
{
    public static readonly IReadOnlyList<string> Categories = new[]
    {
        "Navegadores", "Jogos", "Acesso remoto", "Comunicação", "Gravação e live", "Música e vídeo", "Produtividade",
        "Periféricos", "Criação", "Utilitários", "Segurança", "Desenvolvimento", "Componentes",
    };

    private const AppKit Post = AppKit.PostFormat, Games = AppKit.Games, Work = AppKit.Work, Creator = AppKit.Creator, Dev = AppKit.Dev;

    /// <summary>Pacote cujo nome no registro começa com <paramref name="displayPrefix"/>.</summary>
    private static AppPackage P(string id, string? displayPrefix) => new(id, displayPrefix == null ? null : "^" + Regex.Escape(displayPrefix));
    /// <summary>Pacote cujo nome no registro segue uma expressão (as variantes x86/x64 do Visual C++ só se distinguem assim).</summary>
    private static AppPackage R(string id, string pattern) => new(id, pattern);

    private static InstallableApp App(string category, string name, string icon, string description, AppKit kits, params AppPackage[] packages) =>
        new() { Category = category, Name = name, Icon = icon, Description = description, Kits = kits, Packages = packages };

    public static List<InstallableApp> Create() => new()
    {
        App("Navegadores", "Google Chrome", "chrome", "Navegador do Google, sincroniza com a conta.", Post, P("Google.Chrome", "Google Chrome")),
        App("Navegadores", "Firefox", "firefox", "Navegador aberto da Mozilla, em português.", 0, P("Mozilla.Firefox.pt-BR", "Mozilla Firefox")),
        App("Navegadores", "Brave", "brave", "Navegador que já bloqueia anúncios.", 0, P("Brave.Brave", "Brave")),
        App("Navegadores", "Opera GX", "operagx", "Navegador para jogos, limita RAM e CPU.", 0, P("Opera.OperaGX", "Opera GX")),
        App("Navegadores", "Vivaldi", "vivaldi", "Navegador com abas e painéis sob medida.", 0, P("Vivaldi.Vivaldi", "Vivaldi")),
        App("Navegadores", "Opera", "opera", "Navegador com VPN e bloqueador grátis.", 0, P("Opera.Opera", "Opera Stable")),

        App("Jogos", "Steam", "steam", "Loja e biblioteca de jogos da Valve.", Games, P("Valve.Steam", "Steam")),
        App("Jogos", "Epic Games", "epic", "Loja da Epic, com jogos grátis toda semana.", Games, P("EpicGames.EpicGamesLauncher", "Epic Games Launcher")),
        App("Jogos", "EA app", "ea", "Jogos da EA e do EA Play.", 0, P("ElectronicArts.EADesktop", "EA app")),
        App("Jogos", "Ubisoft Connect", "ubisoft", "Jogos da Ubisoft, como Rainbow Six.", Games, P("Ubisoft.Connect", "Ubisoft Connect")),
        App("Jogos", "Battle.net", "battlenet", "Jogos da Blizzard e Call of Duty.", 0, P("Blizzard.BattleNet", "Battle.net")),
        App("Jogos", "Rockstar Games", "rockstar", "GTA V, Red Dead e outros da Rockstar.", 0, P("RockstarGames.Launcher", "Rockstar Games Launcher")),

        App("Acesso remoto", "AnyDesk", "anydesk", "Acesso remoto para dar suporte.", 0, P("AnyDesk.AnyDesk", "AnyDesk")),
        App("Acesso remoto", "TeamViewer", "teamviewer", "Acesso remoto clássico.", 0, P("TeamViewer.TeamViewer", "TeamViewer")),
        App("Acesso remoto", "Parsec", "parsec", "Jogue no PC de longe, com baixa latência.", 0, P("Parsec.Parsec", "Parsec")),

        App("Comunicação", "Discord", "discord", "Voz e texto para jogar com os amigos.", Post | Games, P("Discord.Discord", "Discord")),
        App("Comunicação", "Telegram", "telegram", "Mensagens com a conta do celular.", 0, P("Telegram.TelegramDesktop", "Telegram Desktop")),
        App("Comunicação", "TeamSpeak", "teamspeak", "Voz com pouca latência para times.", 0, P("TeamSpeakSystems.TeamSpeakClient", "TeamSpeak")),
        App("Comunicação", "Zoom", "zoom", "Reuniões e aulas por vídeo.", Work, P("Zoom.Zoom", "Zoom")),
        App("Comunicação", "Microsoft Teams", "teams", "Reuniões e chat do trabalho ou escola.", Work, P("Microsoft.Teams", null)),
        App("Comunicação", "Slack", "slack", "Chat de equipes por canais.", 0, P("SlackTechnologies.Slack", "Slack")),

        App("Gravação e live", "OBS Studio", "obs", "Grava e transmite a tela e os jogos.", Games | Creator, P("OBSProject.OBSStudio", "OBS Studio")),
        App("Gravação e live", "Streamlabs", "streamlabs", "Transmissão com alertas e temas prontos.", 0, P("Streamlabs.Streamlabs", "Streamlabs")),
        App("Gravação e live", "Lightshot", "lightshot", "Captura de tela rápida no Print Screen.", 0, P("Skillbrains.Lightshot", "Lightshot")),

        App("Música e vídeo", "Spotify", "spotify", "Música e podcasts.", Post, P("Spotify.Spotify", "Spotify")),
        App("Música e vídeo", "Deezer", "deezer", "Música com letras e Flow.", 0, P("Deezer.Deezer", "Deezer")),
        App("Música e vídeo", "VLC", "vlc", "Player que abre qualquer vídeo.", Post, P("VideoLAN.VLC", "VLC media player")),
        App("Música e vídeo", "K-Lite Codec Pack", "klite", "Codecs para o Windows abrir tudo.", 0, P("CodecGuide.K-LiteCodecPack.Standard", "K-Lite Codec Pack")),
        App("Música e vídeo", "iTunes", "itunes", "Música da Apple e backup de iPhone.", 0, P("Apple.iTunes", "iTunes")),

        App("Produtividade", "LibreOffice", "libreoffice", "Textos, planilhas e slides, grátis.", Work, P("TheDocumentFoundation.LibreOffice", "LibreOffice")),
        App("Produtividade", "Acrobat Reader", "acrobat", "Lê, assina e preenche PDFs.", Work, P("Adobe.Acrobat.Reader.64-bit", "Adobe Acrobat")),
        App("Produtividade", "Notion", "notion", "Notas, tarefas e wikis.", 0, P("Notion.Notion", "Notion")),
        App("Produtividade", "Notepad++", "notepadpp", "Editor de texto e código.", Dev, P("Notepad++.Notepad++", "Notepad++")),
        App("Produtividade", "Google Drive", "googledrive", "Seus arquivos do Drive no Explorer.", Work, P("Google.GoogleDrive", "Google Drive")),
        App("Produtividade", "Dropbox", "dropbox", "Arquivos na nuvem e sincronizados.", 0, P("Dropbox.Dropbox", "Dropbox")),
        App("Produtividade", "Evernote", "evernote", "Notas e anotações na nuvem.", 0, P("Evernote.Evernote", "Evernote")),
        App("Produtividade", "Google Earth Pro", "googleearth", "O globo 3D do Google.", 0, P("Google.EarthPro", "Google Earth Pro")),
        App("Produtividade", "OneDrive", "onedrive", "Já vem no Windows, serve para reinstalar.", 0, P("Microsoft.OneDrive", "Microsoft OneDrive")),
        App("Produtividade", "ChatGPT", "chatgpt", "App da OpenAI.", 0, P("OpenAI.ChatGPT", null)),
        App("Produtividade", "Claude", "claude", "App da Anthropic.", 0, P("Anthropic.Claude", "Claude")),

        App("Periféricos", "Logitech G HUB", "ghub", "Mouses, teclados e headsets Logitech G.", 0, P("Logitech.GHUB", "Logitech G HUB")),
        App("Periféricos", "Razer Synapse", "razer", "Periféricos e luzes da Razer.", 0, P("RazerInc.RazerInstaller.Synapse4", "Razer Synapse")),
        App("Periféricos", "Corsair iCUE", "icue", "Luzes, ventoinhas e periféricos Corsair.", 0, P("Corsair.iCUE.5", "iCUE")),
        App("Periféricos", "SteelSeries GG", "steelseries", "Periféricos SteelSeries e Sonar.", 0, P("SteelSeries.GG", "SteelSeries GG")),

        App("Criação", "GIMP", "gimp", "Edição de imagens gratuita.", Creator, P("GIMP.GIMP.3", "GIMP")),
        App("Criação", "Blender", "blender", "Modelagem, animação e render 3D.", 0, P("BlenderFoundation.Blender", "Blender")),
        App("Criação", "CapCut", "capcut", "Edição de vídeo para redes sociais.", Creator, P("ByteDance.CapCut", "CapCut")),
        App("Criação", "Audacity", "audacity", "Grava e edita áudio.", Creator, P("Audacity.Audacity", "Audacity")),
        App("Criação", "Figma", "figma", "Design de interfaces em equipe.", 0, P("Figma.Figma", "Figma")),
        App("Criação", "Canva", "canva", "Artes e posts com modelos prontos.", Creator, P("Canva.Canva", "Canva")),

        App("Utilitários", "7-Zip", "7zip", "Compactador gratuito para zip, rar e 7z.", Post, P("7zip.7zip", "7-Zip")),
        App("Utilitários", "WinRAR", "winrar", "Compactador clássico, pago após o teste.", 0, P("RARLab.WinRAR", "WinRAR")),
        App("Utilitários", "Revo Uninstaller", "revo", "Desinstala e limpa as sobras.", 0, P("RevoUninstaller.RevoUninstaller", "Revo Uninstaller")),
        App("Utilitários", "WizTree", "wiztree", "Mostra o que ocupa o disco.", 0, P("AntibodySoftware.WizTree", "WizTree")),
        App("Utilitários", "Rufus", "rufus", "Cria pendrive de instalação do Windows.", 0, P("Rufus.Rufus", "Rufus")),
        App("Utilitários", "qBittorrent", "qbittorrent", "Cliente de torrent sem anúncios.", 0, P("qBittorrent.qBittorrent", "qBittorrent")),

        App("Segurança", "Bitwarden", "bitwarden", "Cofre de senhas aberto e grátis.", 0, P("Bitwarden.Bitwarden", "Bitwarden")),
        App("Segurança", "Proton VPN", "protonvpn", "VPN com plano grátis sem limite.", 0, P("Proton.ProtonVPN", "Proton VPN")),
        App("Segurança", "Malwarebytes", "malwarebytes", "Segunda opinião contra vírus.", 0, P("Malwarebytes.Malwarebytes", "Malwarebytes")),

        App("Desenvolvimento", "VS Code", "vscode", "Editor de código da Microsoft.", Dev, P("Microsoft.VisualStudioCode", "Microsoft Visual Studio Code")),
        App("Desenvolvimento", "Git", "git", "Controle de versão.", Dev, R("Git.Git", @"^Git( version|$)")),
        App("Desenvolvimento", "GitHub Desktop", "githubdesktop", "Git com janela, sem terminal.", Dev, R("GitHub.GitHubDesktop", @"^GitHub Desktop$")),
        App("Desenvolvimento", "Python", "python", "Linguagem Python 3.14.", Dev, P("Python.Python.3.14", "Python 3.14")),
        App("Desenvolvimento", "Node.js LTS", "nodejs", "JavaScript fora do navegador.", Dev, P("OpenJS.NodeJS.LTS", "Node.js")),
        App("Desenvolvimento", "Docker Desktop", "docker", "Containers.", 0, P("Docker.DockerDesktop", "Docker Desktop")),
        App("Desenvolvimento", "Sublime Text", "sublime", "Editor de código leve.", 0, P("SublimeHQ.SublimeText.4", "Sublime Text")),

        App("Componentes", "Visual C++ 2015+", "vcredist", "Bibliotecas que quase todo jogo pede (x64 e x86).", Post | Games,
            R("Microsoft.VCRedist.2015+.x64", @"^Microsoft Visual C\+\+ (2015-20\d\d|v14) Redistributable \(x64\)"),
            R("Microsoft.VCRedist.2015+.x86", @"^Microsoft Visual C\+\+ (2015-20\d\d|v14) Redistributable \(x86\)")),
        App("Componentes", ".NET Desktop", "dotnet", "Base de muitos apps do Windows (versões 8, 9 e 10).", Post,
            R("Microsoft.DotNet.DesktopRuntime.8", @"^Microsoft Windows Desktop Runtime - 8\.\d+\.\d+ \(x64\)"),
            R("Microsoft.DotNet.DesktopRuntime.9", @"^Microsoft Windows Desktop Runtime - 9\.\d+\.\d+ \(x64\)"),
            R("Microsoft.DotNet.DesktopRuntime.10", @"^Microsoft Windows Desktop Runtime -? ?10\.\d+\.\d+ \(x64\)")),
        App("Componentes", ".NET Framework 4.8.1", "dotnetfx", "Para apps e jogos mais antigos.", 0,
            new AppPackage("Microsoft.DotNet.Framework.DeveloperPack_4", null) { Probe = HasNetFramework481 }),
        App("Componentes", "DirectX 9", "directx", "Peças do DirectX que jogos antigos pedem.", Games,
            new AppPackage("Microsoft.DirectX", null) { Probe = HasDirectX9 }),
        App("Componentes", "Java", "java", "Para Minecraft Java e apps em Java.", 0, P("EclipseAdoptium.Temurin.21.JRE", "Eclipse Temurin JRE")),
        App("Componentes", "Edge WebView2", "webview2", "Muitos apps modernos precisam dele.", 0, P("Microsoft.EdgeWebView2Runtime", "Microsoft Edge WebView2 Runtime")),
        App("Componentes", "Visual C++ antigos", "vcredist", "Versões 2005 a 2013, que jogos antigos pedem.", Games,
            R("Microsoft.VCRedist.2005.x64", @"^Microsoft Visual C\+\+ 2005 Redistributable \(x64\)"),
            R("Microsoft.VCRedist.2005.x86", @"^Microsoft Visual C\+\+ 2005 Redistributable($| - )"),
            R("Microsoft.VCRedist.2008.x64", @"^Microsoft Visual C\+\+ 2008 Redistributable - x64"),
            R("Microsoft.VCRedist.2008.x86", @"^Microsoft Visual C\+\+ 2008 Redistributable - x86"),
            R("Microsoft.VCRedist.2010.x64", @"^Microsoft Visual C\+\+ 2010\s+x64 Redistributable"),
            R("Microsoft.VCRedist.2010.x86", @"^Microsoft Visual C\+\+ 2010\s+x86 Redistributable"),
            R("Microsoft.VCRedist.2012.x64", @"^Microsoft Visual C\+\+ 2012 Redistributable \(x64\)"),
            R("Microsoft.VCRedist.2012.x86", @"^Microsoft Visual C\+\+ 2012 Redistributable \(x86\)"),
            R("Microsoft.VCRedist.2013.x64", @"^Microsoft Visual C\+\+ 2013 Redistributable \(x64\)"),
            R("Microsoft.VCRedist.2013.x86", @"^Microsoft Visual C\+\+ 2013 Redistributable \(x86\)")),
    };

    /// <summary>.NET Framework 4.8.1 (Release 533320 ou maior), que o Windows 11 recente já traz e não aparece em "Aplicativos instalados".</summary>
    private static bool HasNetFramework481()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
            return key?.GetValue("Release") is int release && release >= 533320;
        }
        catch { return false; }
    }

    /// <summary>O runtime do DirectX 9 (D3DX9) só deixa as DLLs na pasta do sistema; a última da série é a d3dx9_43.</summary>
    private static bool HasDirectX9()
    {
        try { return File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "d3dx9_43.dll")); }
        catch { return false; }
    }
}
