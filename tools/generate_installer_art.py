"""Imagens do instalador (Inno Setup 6.7), a partir da geometria da marca em brand/geometry.json.

O painel da esquerda (boas-vindas e conclusão) é a barra lateral do app: o fundo dela, o símbolo e uma fileira com a cor
de cada peça. O símbolo pequeno do canto vai em duas versões, para o tema claro e o escuro do Windows. Cada imagem sai
desenhada no tamanho de cada escala de tela (100% a 250%), nunca reduzida de uma maior, para o traço ficar nítido.

Uso: python -B tools/generate_installer_art.py   (gera installer/images; --check só confere se estão em dia)
"""
from io import BytesIO
from pathlib import Path
import json
import sys

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'installer' / 'images'
GEOMETRY = json.loads((ROOT / 'brand' / 'geometry.json').read_text(encoding='utf-8'))
PALETTE = GEOMETRY['palette']

SIDEBAR = '#0A0D0F'   # Sidebar do tema escuro (ThemeManager.Palette)
LINE = '#24282B'      # Line do tema escuro
# Cores das peças no tema escuro, na ordem da Visão geral: processador, placa de vídeo, memória, armazenamento, placa-mãe, rede
PARTS = ['#1F8A48', '#E8894A', '#B296FF', '#4A82CC', '#C8A370', '#E3D45B']

# Área de cada imagem nas escalas de 100% a 250% (documentação do Inno Setup 6.7: WizardImageFile e WizardSmallImageFile)
PANEL_SIZES = [(202, 386), (269, 515), (336, 643), (403, 772), (430, 824), (498, 953), (534, 1022)]
SMALL_SIZES = [58, 77, 97, 116, 124, 143, 159]
SUPER = 4  # desenha 4x maior e reduz uma vez: bordas suaves sem borrar o desenho


def mark(draw, left, top, size, fg, accent):
    for shape in GEOMETRY['shapes']:
        points = [(left + x / 256 * size, top + y / 256 * size) for x, y in shape['points']]
        draw.polygon(points, fill=accent if shape['role'] == 'signal' else fg)


def panel(width, height):
    w, h = width * SUPER, height * SUPER
    image = Image.new('RGB', (w, h), SIDEBAR)
    draw = ImageDraw.Draw(image)
    # Símbolo, fio e fileira formam um bloco só, um pouco acima do centro (o centro óptico do painel)
    size = w * 0.50
    top = h * 0.38 - size / 2
    mark(draw, (w - size) / 2, top, size, PALETTE['paper'], PALETTE['signal'])
    # Fio da bancada e a fileira de peças: um LED quadrado por peça, com o raio pequeno do app
    rule_y = h * 0.585
    draw.line([(w * 0.16, rule_y), (w * 0.84, rule_y)], fill=LINE, width=max(SUPER, round(w / 200)))
    led = w * 0.052
    gap = led * 0.9
    row = len(PARTS) * led + (len(PARTS) - 1) * gap
    x = (w - row) / 2
    y = rule_y + h * 0.04
    for color in PARTS:
        draw.rounded_rectangle([x, y, x + led, y + led], radius=led * 0.22, fill=color)
        x += led + gap
    return image.resize((width, height), Image.Resampling.LANCZOS)


def small(size, fg, accent):
    s = size * SUPER
    image = Image.new('RGBA', (s, s), (0, 0, 0, 0))
    inset = s * 0.06
    mark(ImageDraw.Draw(image), inset, inset, s - 2 * inset, fg, accent)
    return image.resize((size, size), Image.Resampling.LANCZOS)


def files():
    for width, height in PANEL_SIZES:
        yield f'painel-{width}x{height}.png', panel(width, height)
    for size in SMALL_SIZES:
        yield f'simbolo-claro-{size}.png', small(size, PALETTE['ink'], '#087E78')
        yield f'simbolo-escuro-{size}.png', small(size, PALETTE['paper'], PALETTE['signal'])


def png(image):
    buffer = BytesIO()
    image.save(buffer, format='PNG', optimize=True)
    return buffer.getvalue()


def main():
    check = '--check' in sys.argv[1:]
    stale = []
    expected = set()
    OUT.mkdir(parents=True, exist_ok=True)
    for name, image in files():
        expected.add(name)
        data = png(image)
        target = OUT / name
        if check:
            if not target.exists() or target.read_bytes() != data:
                stale.append(name)
        else:
            target.write_bytes(data)
    extra = sorted(p.name for p in OUT.glob('*.png') if p.name not in expected)
    if check and (stale or extra):
        print('Imagens do instalador desatualizadas: ' + ', '.join(stale + extra) + '. Rode python -B tools/generate_installer_art.py')
        return 1
    for name in extra:
        (OUT / name).unlink()
    print(f'{len(expected)} imagens em {OUT}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
