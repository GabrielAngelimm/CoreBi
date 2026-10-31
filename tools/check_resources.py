"""Confere as chaves de recurso do CoreBi.Desktop antes de rodar o app.

O compilador de XAML não valida {StaticResource X} nem {DynamicResource X}: uma chave errada só aparece como
exceção (ou pincel vazio) quando a tela abre. Este script cruza cada referência com as chaves definidas:

- Theme.xaml: StaticResource precisa estar definido antes, no próprio arquivo (é carregado em ordem).
- FeatureTemplates.xaml: pode usar o que vem antes nele mesmo ou qualquer chave do Theme.xaml.
- Demais XAML: chaves do Theme.xaml, do FeatureTemplates.xaml ou definidas antes no próprio arquivo.
- C#: FindResource/TryFindResource/Palette/SetResourceReference/ThemeManager com literais de texto.
- A paleta repetida no Theme.xaml confere com a do ThemeManager.cs (tools/sync_theme.py).

Uso: python tools/check_resources.py   (sai com código 1 se encontrar chave inexistente)
"""
from __future__ import annotations

import pathlib
import re
import sys

import sync_theme

ROOT = pathlib.Path(__file__).resolve().parent.parent / "src" / "CoreBi.Desktop"
THEME = ROOT / "Shell" / "Theme.xaml"
TEMPLATES = ROOT / "Shell" / "FeatureTemplates.xaml"
# Paleta fixa do overlay e do aviso: entra no Theme.xaml como dicionário mesclado (carregado antes das chaves dele)
HUD = ROOT / "Shell" / "HudPalette.xaml"

KEY = re.compile(r'x:Key="([^"{]+)"')
REF = re.compile(r"\{(StaticResource|DynamicResource)\s+([^}\s]+)\s*\}")
CS_CALL = re.compile(r"\b(FindResource|TryFindResource|Palette|SetResourceReference|WithAlpha|Brush|Channel|AreaFill)\(([^;]*)")
LITERAL = re.compile(r'"([A-Za-z][A-Za-z0-9]*)"')


def keys_with_positions(text: str) -> list[tuple[int, str]]:
    return [(m.start(), m.group(1)) for m in KEY.finditer(text)]


def check_xaml(path: pathlib.Path, shared: set[str], ordered: bool) -> list[str]:
    text = path.read_text(encoding="utf-8")
    local = keys_with_positions(text)
    problems = []
    for m in REF.finditer(text):
        kind, key = m.group(1), m.group(2)
        if key.startswith("{"):
            continue
        defined_before = any(pos < m.start() and k == key for pos, k in local)
        defined_anywhere = any(k == key for _, k in local)
        ok = key in shared or (defined_before if (kind == "StaticResource" and ordered) else defined_anywhere)
        if not ok:
            line = text.count("\n", 0, m.start()) + 1
            problems.append(f"{path.relative_to(ROOT)}:{line}: {kind} '{key}' não definido")
    return problems


def main() -> int:
    hud_keys = {k for _, k in keys_with_positions(HUD.read_text(encoding="utf-8"))}
    theme_keys = {k for _, k in keys_with_positions(THEME.read_text(encoding="utf-8"))} | hud_keys
    template_keys = {k for _, k in keys_with_positions(TEMPLATES.read_text(encoding="utf-8"))}
    app_keys = theme_keys | template_keys

    problems = check_xaml(HUD, set(), ordered=True)
    problems += check_xaml(THEME, hud_keys, ordered=True)
    problems += check_xaml(TEMPLATES, theme_keys, ordered=True)
    for path in sorted(ROOT.rglob("*.xaml")):
        if path in (THEME, TEMPLATES, HUD) or "obj" in path.parts or "bin" in path.parts:
            continue
        problems += check_xaml(path, app_keys, ordered=True)

    # Estilos e chaves locais de UserControl.Resources não são visíveis no código do MainWindow: o código só usa as globais.
    for path in sorted(ROOT.rglob("*.cs")):
        # O ThemeManager define as chaves (e monta nomes como key + "Deep"): não é consumidor
        if "obj" in path.parts or "bin" in path.parts or path.name == "ThemeManager.cs":
            continue
        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            literals: list[str] = []
            for m in CS_CALL.finditer(line):
                literals += LITERAL.findall(m.group(2))
            if "SetRunning(" in line:
                continue
            for key in literals:
                if key not in app_keys:
                    problems.append(f"{path.relative_to(ROOT)}:{number}: chave '{key}' não definida")

    theme_text = THEME.read_text(encoding="utf-8")
    if sync_theme.render() not in theme_text:
        problems.append("Shell/Theme.xaml: a paleta não confere com o ThemeManager.cs; rode python tools/sync_theme.py")

    for p in problems:
        print(p)
    print(f"{len(problems)} problema(s) em {len(app_keys)} chaves conhecidas.")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
