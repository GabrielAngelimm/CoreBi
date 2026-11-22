# Ícones dos aplicativos

Os ícones em `src/CoreBi.Desktop/Assets/Apps/` são os ícones oficiais de cada programa do catálogo de Instalar apps, extraídos dos instaladores oficiais (os mesmos que o winget baixa) ou do programa já instalado, com `tools/AppIcons.ps1`. Eles não foram redesenhados.

Esses ícones e os nomes dos programas são marcas dos respectivos donos. Eles não fazem parte do código do CoreBi, não estão sob a licença MIT do aplicativo e aparecem só para identificar o programa que o usuário escolhe instalar. O CoreBi não tem vínculo com esses fabricantes nem é endossado por eles.

Um app sem ícone mostra a inicial do nome no lugar.

Ajustes feitos à mão depois do script, só de recorte e fundo (o desenho é o original): Spotify, Python e GIMP recortados para o logotipo, sem a caixa ou a janela do instalador; ChatGPT e SteelSeries, que são brancos, sobre um quadro escuro, como no ícone do app. LibreOffice fica com a inicial, porque o ícone do programa é uma folha genérica. Rodar o script com `-Force` nesses apps desfaz os ajustes.
