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
        App("Navegadores", "Firefox", "firefox", "Navegador da Mozilla, focado em privacidade.", 0, P("Mozilla.Firefox.pt-BR", "Mozilla Firefox")),
        App("Navegadores", "Brave", "brave", "Navegador que já bloqueia anúncios sozinho.", 0, P("Brave.Brave", "Brave")),
        App("Navegadores", "Opera GX", "operagx", "Navegador para jogadores, limita o uso de RAM e CPU.", 0, P("Opera.OperaGX", "Opera GX")),
        App("Navegadores", "Vivaldi", "vivaldi", "Navegador personalizável, com abas e painéis do seu jeito.", 0, P("Vivaldi.Vivaldi", "Vivaldi")),
        App("Navegadores", "Opera", "opera", "Navegador com VPN grátis e bloqueador de anúncios.", 0, P("Opera.Opera", "Opera Stable")),

        App("Jogos", "Steam", "steam", "Loja e biblioteca de jogos.", Games, P("Valve.Steam", "Steam")),
        App("Jogos", "Epic Games", "epic", "Loja de jogos com um jogo grátis toda semana.", 0, P("EpicGames.EpicGamesLauncher", "Epic Games Launcher")),
        App("Jogos", "EA app", "ea", "Loja e jogos da EA, como EA FC e Battlefield.", 0, P("ElectronicArts.EADesktop", "EA app")),
        App("Jogos", "Ubisoft Connect", "ubisoft", "Loja e jogos da Ubisoft, como Rainbow Six.", 0, P("Ubisoft.Connect", "Ubisoft Connect")),
        App("Jogos", "Battle.net", "battlenet", "Jogos da Blizzard, como Overwatch e Diablo, e Call of Duty.", 0, P("Blizzard.BattleNet", "Battle.net")),
        App("Jogos", "Rockstar Games", "rockstar", "Abre GTA V, Red Dead Redemption e outros da Rockstar.", 0, P("RockstarGames.Launcher", "Rockstar Games Launcher")),

        App("Acesso remoto", "AnyDesk", "anydesk", "Controla outro PC pela internet, bom para dar suporte.", 0, P("AnyDesk.AnyDesk", "AnyDesk")),
        App("Acesso remoto", "TeamViewer", "teamviewer", "Acessa e controla outro computador a distância.", 0, P("TeamViewer.TeamViewer", "TeamViewer")),
        App("Acesso remoto", "Parsec", "parsec", "Joga no seu PC de longe, com pouco atraso.", 0, P("Parsec.Parsec", "Parsec")),

        App("Comunicação", "Discord", "discord", "Chamada de voz e chat para jogar com os amigos.", Games, P("Discord.Discord", "Discord")),
        App("Comunicação", "Telegram", "telegram", "Mensagens no PC com a mesma conta do celular.", 0, P("Telegram.TelegramDesktop", "Telegram Desktop")),
        App("Comunicação", "TeamSpeak", "teamspeak", "Chat de voz leve e com pouco atraso para times.", 0, P("TeamSpeakSystems.TeamSpeakClient", "TeamSpeak")),
        App("Comunicação", "Zoom", "zoom", "Reuniões e aulas por vídeo.", Work, P("Zoom.Zoom", "Zoom")),
        App("Comunicação", "Microsoft Teams", "teams", "Reuniões e chat do trabalho ou da escola.", 0, P("Microsoft.Teams", null)),
        App("Comunicação", "Slack", "slack", "Chat de equipe organizado por canais.", 0, P("SlackTechnologies.Slack", "Slack")),

        App("Gravação e live", "OBS Studio", "obs", "Grava a tela e faz lives na Twitch e no YouTube.", Creator, P("OBSProject.OBSStudio", "OBS Studio")),
        App("Gravação e live", "Streamlabs", "streamlabs", "Faz lives com alertas e temas prontos.", 0, P("Streamlabs.Streamlabs", "Streamlabs")),
        App("Gravação e live", "Lightshot", "lightshot", "Tira print de um pedaço da tela com o Print Screen.", 0, P("Skillbrains.Lightshot", "Lightshot")),

        App("Música e vídeo", "Spotify", "spotify", "Música e podcasts por streaming.", 0, P("Spotify.Spotify", "Spotify")),
        App("Música e vídeo", "Deezer", "deezer", "Música por streaming, com letras e o Flow.", 0, P("Deezer.Deezer", "Deezer")),
        App("Música e vídeo", "VLC", "vlc", "Player que abre praticamente qualquer vídeo ou música.", Post, P("VideoLAN.VLC", "VLC media player")),
        App("Música e vídeo", "K-Lite Codec Pack", "klite", "Codecs para o Windows abrir qualquer formato de vídeo.", 0, P("CodecGuide.K-LiteCodecPack.Standard", "K-Lite Codec Pack")),
        App("Música e vídeo", "iTunes", "itunes", "Música da Apple e backup do iPhone no PC.", 0, P("Apple.iTunes", "iTunes")),

        App("Produtividade", "LibreOffice", "libreoffice", "Textos, planilhas e apresentações, de graça.", Work, P("TheDocumentFoundation.LibreOffice", "LibreOffice")),
        App("Produtividade", "Acrobat Reader", "acrobat", "Abre, preenche e assina PDFs.", Post | Work, P("Adobe.Acrobat.Reader.64-bit", "Adobe Acrobat")),
        App("Produtividade", "Notion", "notion", "Notas, tarefas e documentos num lugar só.", Work, P("Notion.Notion", "Notion")),
        App("Produtividade", "Notepad++", "notepadpp", "Bloco de notas turbinado, bom para código.", Dev, P("Notepad++.Notepad++", "Notepad++")),
        App("Produtividade", "Google Drive", "googledrive", "Seus arquivos do Google Drive direto no Explorer.", Work, P("Google.GoogleDrive", "Google Drive")),
        App("Produtividade", "Dropbox", "dropbox", "Guarda arquivos na nuvem e sincroniza entre aparelhos.", 0, P("Dropbox.Dropbox", "Dropbox")),
        App("Produtividade", "Evernote", "evernote", "Caderno de anotações que sincroniza com o celular.", 0, P("Evernote.Evernote", "Evernote")),
        App("Produtividade", "Google Earth Pro", "googleearth", "Explora o mundo em 3D com imagens de satélite.", 0, P("Google.EarthPro", "Google Earth Pro")),
        App("Produtividade", "OneDrive", "onedrive", "Nuvem da Microsoft; reinstala se ele foi tirado do Windows.", 0, P("Microsoft.OneDrive", "Microsoft OneDrive")),
        App("Produtividade", "ChatGPT", "chatgpt", "Assistente de IA da OpenAI para conversar e tirar dúvidas.", Work, P("OpenAI.ChatGPT", null)),
        App("Produtividade", "Claude", "claude", "Assistente de IA da Anthropic para escrever, estudar e programar.", 0, P("Anthropic.Claude", "Claude")),

        App("Periféricos", "Logitech G HUB", "ghub", "Configura mouses, teclados e headsets da Logitech G.", 0, P("Logitech.GHUB", "Logitech G HUB")),
        App("Periféricos", "Razer Synapse", "razer", "Configura periféricos e luzes RGB da Razer.", 0, P("RazerInc.RazerInstaller.Synapse4", "Razer Synapse")),
        App("Periféricos", "Corsair iCUE", "icue", "Controla luzes RGB, ventoinhas e periféricos da Corsair.", 0, P("Corsair.iCUE.5", "iCUE")),
        App("Periféricos", "SteelSeries GG", "steelseries", "Configura periféricos SteelSeries e o mixer de áudio Sonar.", 0, P("SteelSeries.GG", "SteelSeries GG")),

        App("Criação", "GIMP", "gimp", "Editor de imagens grátis, alternativa ao Photoshop.", Creator, P("GIMP.GIMP.3", "GIMP")),
        App("Criação", "Blender", "blender", "Modelagem, animação e renderização em 3D, de graça.", Creator, P("BlenderFoundation.Blender", "Blender")),
        App("Criação", "CapCut", "capcut", "Edita vídeos curtos para TikTok, Reels e Shorts.", Creator, P("ByteDance.CapCut", "CapCut")),
        App("Criação", "Audacity", "audacity", "Grava e edita áudio, de graça.", Creator, P("Audacity.Audacity", "Audacity")),
        App("Criação", "Figma", "figma", "Desenha telas de apps e sites, em equipe.", 0, P("Figma.Figma", "Figma")),
        App("Criação", "Canva", "canva", "Cria posts, artes e apresentações com modelos prontos.", Creator, P("Canva.Canva", "Canva")),

        App("Utilitários", "7-Zip", "7zip", "Abre e cria arquivos zip, rar e 7z, de graça.", 0, P("7zip.7zip", "7-Zip")),
        App("Utilitários", "WinRAR", "winrar", "Abre e cria arquivos rar e zip; pago depois do teste.", Post | Dev, P("RARLab.WinRAR", "WinRAR")),
        App("Utilitários", "Revo Uninstaller", "revo", "Desinstala programas e apaga o que eles deixam para trás.", 0, P("RevoUninstaller.RevoUninstaller", "Revo Uninstaller")),
        App("Utilitários", "WizTree", "wiztree", "Mostra em segundos o que está ocupando o disco.", 0, P("AntibodySoftware.WizTree", "WizTree")),
        App("Utilitários", "Rufus", "rufus", "Cria um pendrive para instalar o Windows.", 0, P("Rufus.Rufus", "Rufus")),
        App("Utilitários", "qBittorrent", "qbittorrent", "Baixa torrents, sem anúncios.", 0, P("qBittorrent.qBittorrent", "qBittorrent")),

        App("Segurança", "Bitwarden", "bitwarden", "Guarda suas senhas com segurança e preenche sozinho.", 0, P("Bitwarden.Bitwarden", "Bitwarden")),
        App("Segurança", "Proton VPN", "protonvpn", "VPN com plano grátis e sem limite de dados.", 0, P("Proton.ProtonVPN", "Proton VPN")),
        App("Segurança", "Malwarebytes", "malwarebytes", "Procura vírus e ameaças, uma segunda opinião ao antivírus.", 0, P("Malwarebytes.Malwarebytes", "Malwarebytes")),

        App("Desenvolvimento", "VS Code", "vscode", "Editor de código da Microsoft, com extensões para tudo.", Dev, P("Microsoft.VisualStudioCode", "Microsoft Visual Studio Code")),
        App("Desenvolvimento", "Git", "git", "Guarda o histórico de versões dos seus projetos.", Dev, R("Git.Git", @"^Git( version|$)")),
        App("Desenvolvimento", "GitHub Desktop", "githubdesktop", "Usa o Git e o GitHub com janelas, sem terminal.", Dev, R("GitHub.GitHubDesktop", @"^GitHub Desktop$")),
        App("Desenvolvimento", "Python", "python", "Linguagem de programação simples, ótima para começar.", Dev, P("Python.Python.3.14", "Python 3.14")),
        App("Desenvolvimento", "Node.js LTS", "nodejs", "Roda JavaScript fora do navegador.", Dev, P("OpenJS.NodeJS.LTS", "Node.js")),
        App("Desenvolvimento", "Docker Desktop", "docker", "Roda apps em contêineres isolados do sistema.", 0, P("Docker.DockerDesktop", "Docker Desktop")),
        App("Desenvolvimento", "Sublime Text", "sublime", "Editor de código rápido e leve.", 0, P("SublimeHQ.SublimeText.4", "Sublime Text")),

        App("Componentes", "Visual C++ 2015+", "vcredist", "Bibliotecas que a maioria dos jogos e programas precisa.", Post | Games,
            R("Microsoft.VCRedist.2015+.x64", @"^Microsoft Visual C\+\+ (2015-20\d\d|v14) Redistributable \(x64\)"),
            R("Microsoft.VCRedist.2015+.x86", @"^Microsoft Visual C\+\+ (2015-20\d\d|v14) Redistributable \(x86\)")),
        App("Componentes", ".NET Desktop", "dotnet", "Base que muitos apps do Windows precisam para abrir.", Post | Games,
            R("Microsoft.DotNet.DesktopRuntime.8", @"^Microsoft Windows Desktop Runtime - 8\.\d+\.\d+ \(x64\)"),
            R("Microsoft.DotNet.DesktopRuntime.9", @"^Microsoft Windows Desktop Runtime - 9\.\d+\.\d+ \(x64\)"),
            R("Microsoft.DotNet.DesktopRuntime.10", @"^Microsoft Windows Desktop Runtime -? ?10\.\d+\.\d+ \(x64\)")),
        App("Componentes", ".NET Framework 4.8.1", "dotnetfx", "Base de apps e jogos mais antigos do Windows.", Games,
            new AppPackage("Microsoft.DotNet.Framework.DeveloperPack_4", null) { Probe = HasNetFramework481 }),
        App("Componentes", "DirectX 9", "directx", "Arquivos do DirectX que jogos antigos pedem.", Games,
            new AppPackage("Microsoft.DirectX", null) { Probe = HasDirectX9 }),
        App("Componentes", "Java", "java", "Necessário para o Minecraft Java e outros apps em Java.", Post, P("EclipseAdoptium.Temurin.21.JRE", "Eclipse Temurin JRE")),
        App("Componentes", "Edge WebView2", "webview2", "Motor do Edge que muitos apps modernos usam por dentro.", Post, P("Microsoft.EdgeWebView2Runtime", "Microsoft Edge WebView2 Runtime")),
        App("Componentes", "Visual C++ antigos", "vcredist", "Versões 2005 a 2013, para jogos e programas antigos.", Games,
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
