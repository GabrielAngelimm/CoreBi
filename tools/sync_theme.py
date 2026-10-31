"""Copia a paleta do ThemeManager.cs para o Theme.xaml.

As cores do app têm uma fonte só: as tabelas Palette, Gradients e Glows de Shell/ThemeManager.cs, que trocam os pincéis
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
GRADIENT = re.compile(r'^\("(\w+)",\s*Direction\.(Vertical|Horizontal),\s*\[([^\]]*)\],\s*\[([^\]]*)\]\),?$')
GLOW = re.compile(r'^\("(\w+)",\s*' + HEX + r",\s*" + HEX + r"\),?$")


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

    out.append("<!-- Gradientes (a luz dos painéis, a tecla acesa e o preenchimento dos canais) -->")
    for line in table(source, "Gradients"):
        m = GRADIENT.match(line)
        if not m:
            raise ValueError(f"linha dos Gradients não reconhecida: {line}")
        key, direction, dark = m.group(1), m.group(2), re.findall(HEX, m.group(3))
        end = "0,1" if direction == "Vertical" else "1,0"
        out.append(f'<LinearGradientBrush x:Key="{key}" StartPoint="0,0" EndPoint="{end}">')
        for i, color in enumerate(dark):
            offset = 0 if len(dark) == 1 else i / (len(dark) - 1)
            out.append(f'{INDENT}<GradientStop Color="{color}" Offset="{offset:g}"/>')
        out.append("</LinearGradientBrush>")

    out.append("<!-- Brilhos (luz radial do canal atrás de uma leitura) -->")
    for line in table(source, "Glows"):
        m = GLOW.match(line)
        if not m:
            raise ValueError(f"linha dos Glows não reconhecida: {line}")
        key, dark = m.group(1), m.group(2)
        alpha, rgb = int(dark[1:3], 16), dark[3:]
        out.append(f'<RadialGradientBrush x:Key="{key}">')
        for a, offset in ((alpha, "0"), (alpha // 3, "0.55"), (0, "1")):
            out.append(f'{INDENT}<GradientStop Color="#{a:02X}{rgb}" Offset="{offset}"/>')
        out.append("</RadialGradientBrush>")
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
