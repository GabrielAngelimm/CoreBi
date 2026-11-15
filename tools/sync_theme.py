"""Copia a paleta do ThemeManager.cs para o Theme.xaml.

As cores do app têm uma fonte só: a tabela Palette de Shell/ThemeManager.cs, que troca os pincéis
em tempo de execução. O Theme.xaml repete os valores do tema escuro (para o designer do XAML e para quem abre uma tela
antes do ThemeManager) num bloco gerado, entre os marcadores "paleta:início" e "paleta:fim". Depois de mexer numa cor,
rode este script em vez de editar o bloco à mão.

Uso: python tools/sync_theme.py           (reescreve o bloco)
     python tools/sync_theme.py --check   (sai com código 1 se o bloco estiver desatualizado)
"""
from __future__ import annotations

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent / "src" / "CoreBi.Desktop" / "Shell"
MANAGER = ROOT / "ThemeManager.cs"
THEME = ROOT / "Theme.xaml"
START = "<!-- paleta:início (gerado por tools/sync_theme.py a partir do ThemeManager.cs; não editar à mão) -->"
END = "<!-- paleta:fim -->"
INDENT = "    "

HEX = r'"(#[0-9A-Fa-f]{6,8})"'
SOLID = re.compile(r'^\(("?)([\w.]+)\1,\s*' + HEX + r",\s*" + HEX + r"\),?$")
def table(source: str, name: str) -> list[str]:
    """Linhas do inicializador de uma tabela (entre a chave que abre e a que fecha)."""
    start = source.index(f" {name} =")
    body = source[source.index("{", start) + 1 : source.index("};", start)]
    return [line.strip() for line in body.splitlines() if line.strip()]


def render() -> str:
    source = MANAGER.read_text(encoding="utf-8")
    out = [START]
    for line in table(source, "Palette"):
        if line.startswith("//"):
            out.append(f"<!-- {line[2:].strip()} -->")
            continue
        m = SOLID.match(line)
        if not m:
            raise ValueError(f"linha da Palette não reconhecida: {line}")
        quoted, key, dark = m.group(1), m.group(2), m.group(3)
        xkey = key if quoted else "{x:Static " + key + "}"
        out.append(f'<SolidColorBrush x:Key="{xkey}" Color="{dark}"/>')

    out.append(END)
    return "\n".join(INDENT + line for line in out)


def main() -> int:
    theme = THEME.read_text(encoding="utf-8")
    block = render()
    pattern = re.compile(re.escape(INDENT + START) + r".*?" + re.escape(END), re.S)
    if not pattern.search(theme):
        print(f"{THEME.name}: marcadores da paleta não encontrados")
        return 1
    updated = pattern.sub(lambda _: block, theme, count=1)
    if "--check" in sys.argv[1:]:
        if updated != theme:
            print(f"{THEME.name}: a paleta não confere com o ThemeManager.cs; rode python tools/sync_theme.py")
            return 1
        print("Paleta do Theme.xaml em dia com o ThemeManager.cs.")
        return 0
    if updated != theme:
        with open(THEME, "w", encoding="utf-8", newline="") as f:
            f.write(updated)
        print(f"{THEME.name}: paleta atualizada.")
    else:
        print(f"{THEME.name}: nada a mudar.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
