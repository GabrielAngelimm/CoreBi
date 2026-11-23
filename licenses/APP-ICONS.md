# Ícones dos aplicativos

Os ícones em `src/CoreBi.Desktop/Assets/Apps/` são os ícones oficiais de cada programa do catálogo de Instalar apps, extraídos dos instaladores oficiais (os mesmos que o winget baixa) ou do programa já instalado, com `tools/AppIcons.ps1`. Eles não foram redesenhados.

Esses ícones e os nomes dos programas são marcas dos respectivos donos. Eles não fazem parte do código do CoreBi, não estão sob a licença MIT do aplicativo e aparecem só para identificar o programa que o usuário escolhe instalar. O CoreBi não tem vínculo com esses fabricantes nem é endossado por eles.

Um app sem ícone mostra a inicial do nome no lugar.

Ajustes feitos à mão depois do script, só de recorte e fundo (o desenho é o original): Spotify, Python e GIMP recortados para o logotipo, sem a caixa ou a janela do instalador; ChatGPT e SteelSeries, que são brancos, sobre um quadro escuro, como no ícone do app. Rodar o script com `-Force` nesses apps desfaz os ajustes.

Ícones que o script não acha (o instalador é só um baixador, com ícone genérico), tirados do material oficial de cada fabricante e reduzidos a 64 × 64:

- Brave, Lightshot, WizTree, Sublime Text e Malwarebytes: o ícone do app publicado no site oficial (brave.com, app.prntscr.com, diskanalyzer.com, sublimetext.com, malwarebytes.com).
- Dropbox: o símbolo recortado da imagem oficial do site (fundo azul do original).
- Corsair iCUE: o emblema hexagonal do logotipo oficial do iCUE, recortado do nome (a metade direita completada pelo contorno da esquerda).
- Revo Uninstaller: o ícone do instalador oficial (revosetup.exe).
- .NET Desktop e .NET Framework: o logotipo .NET do site oficial (dotnet.microsoft.com).
- LibreOffice: o ícone do programa no código-fonte oficial (LibreOffice/core, `sysui/desktop/icons/hicolor/256x256/apps/main.png`).

Visual C++ e DirectX continuam com a inicial: não têm um ícone próprio de boa qualidade (o instalador do Visual C++ usa um ícone genérico, e o do DirectX tem só 50 px com fundo branco).
