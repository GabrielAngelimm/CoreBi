from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import json, html

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT/'artifacts'/'brand'
BRAND = ROOT/'brand'
BRAND.mkdir(parents=True, exist_ok=True)
OUT.mkdir(parents=True, exist_ok=True)
palette = {'graphite':'#111B24','signal':'#27C9BC','paper':'#EDF4F4','steel':'#8295A3','ink':'#172E3A','copper':'#BF815C'}
# A shared geometric source drives SVG, PNG and the native Windows icon.
shapes = [
    {'role':'foreground','points':[[141,35],[81,35],[34,82],[34,174],[81,221],[141,221],[141,195],[93,195],[60,162],[60,94],[93,61],[141,61]]},
    {'role':'foreground','points':[[155,35],[184,35],[218,69],[218,108],[202,124],[222,144],[222,185],[186,221],[155,221],[155,195],[175,195],[196,174],[196,156],[178,138],[155,138],[155,112],[177,112],[192,97],[192,81],[173,61],[155,61]]},
    {'role':'signal','points':[[95,84],[117,84],[117,106],[95,106]]},
    {'role':'signal','points':[[95,121],[117,121],[117,172],[95,172]]}
]
(BRAND/'geometry.json').write_text(json.dumps({'viewBox':[0,0,256,256],'palette':palette,'shapes':shapes},indent=2),encoding='utf-8')

def svg(fg,accent,wordmark=False):
    width=1020 if wordmark else 256
    polygons=''.join('<polygon points="'+ ' '.join(f'{x},{y}' for x,y in s['points']) +'" fill="'+(accent if s['role']=='signal' else fg)+'"/>' for s in shapes)
    text=f'<text x="296" y="184" fill="{fg}" font-family="Bahnschrift,Segoe UI,sans-serif" font-size="176" font-weight="600" letter-spacing="-5">CoreBi</text>' if wordmark else ''
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} 256" role="img" aria-label="CoreBi"><title>CoreBi — núcleo e dois canais de leitura</title>{polygons}{text}</svg>'

def mark(size,fg,accent,bg=None):
    scale=4
    im=Image.new('RGBA',(size*scale,size*scale),bg or (0,0,0,0)); d=ImageDraw.Draw(im)
    for s in shapes:
        d.polygon([(round(x/256*size*scale),round(y/256*size*scale)) for x,y in s['points']],fill=accent if s['role']=='signal' else fg)
    return im.resize((size,size),Image.Resampling.LANCZOS)

for name,fg,ac in [('dark',palette['paper'],palette['signal']),('light',palette['ink'],'#087E78'),('mono-dark',palette['paper'],palette['paper']),('mono-light',palette['ink'],palette['ink'])]:
    (BRAND/f'corebi-symbol-{name}.svg').write_text(svg(fg,ac),encoding='utf-8')
    (BRAND/f'corebi-wordmark-{name}.svg').write_text(svg(fg,ac,True),encoding='utf-8')
    mark(512,fg,ac).save(BRAND/f'corebi-symbol-{name}.png')

icon=mark(256,palette['paper'],palette['signal'],palette['graphite'])
icon.save(BRAND/'corebi.ico',sizes=[(16,16),(20,20),(24,24),(32,32),(40,40),(48,48),(64,64),(128,128),(256,256)])
def font(size,bold=False,mono=False):
    p=Path(r'C:\Windows\Fonts')/('consola.ttf' if mono else 'bahnschrift.ttf' if bold else 'segoeui.ttf')
    return ImageFont.truetype(str(p),size)

board=Image.new('RGB',(1600,1120),palette['graphite']); d=ImageDraw.Draw(board)
d.line((64,82,1536,82), fill='#334550',width=1)
d.text((64,39),'IDENTIDADE / COREBI',font=font(19,mono=True),fill=palette['steel'])
d.text((1208,39),'01 · NÚCLEO / SINAL',font=font(19,mono=True),fill=palette['steel'])
board.paste(mark(232,palette['paper'],palette['signal']),(72,143),mark(232,palette['paper'],palette['signal']))
d.text((356,149),'CoreBi',font=font(150,bold=True),fill=palette['paper'])
d.text((365,326),'O hardware, em leitura clara.',font=font(29),fill=palette['steel'])
d.text((72,433),'NÚCLEO / LEITURA / IDENTIDADE',font=font(19,mono=True),fill=palette['signal'])
d.text((72,475),'C e B têm a mesma altura, com os centros alinhados.',font=font(23),fill=palette['paper'])
d.text((72,513),'O ponto e a haste desenham o i no espaço interno:',font=font(24),fill=palette['steel'])
d.text((72,549),'a assinatura Bi dentro de um instrumento de diagnóstico.',font=font(24),fill=palette['steel'])
d.rounded_rectangle((1040,145,1535,596),radius=20,fill=palette['paper'])
small=mark(190,palette['ink'],'#087E78'); board.paste(small,(1192,212),small)
d.text((1165,440),'CoreBi',font=font(66,bold=True),fill=palette['ink'])
d.text((1131,541),'VERSÃO PARA FUNDO CLARO',font=font(15,mono=True),fill=palette['ink'])
d.line((64,638,1536,638),fill='#334550')
for i,key in enumerate(['graphite','signal','paper','steel']):
    x=72+i*244
    d.rounded_rectangle((x,694,x+194,775),radius=8,fill=palette[key],outline='#334550')
    d.text((x,794),key.upper(),font=font(16,mono=True),fill=palette['steel'])
    d.text((x,826),palette[key],font=font(22,mono=True),fill=palette['paper'])
d.text((1101,690),'ÍCONE / ESCALA REAL',font=font(16,mono=True),fill=palette['steel'])
for size,x,y in [(96,1098,746),(48,1230,770),(32,1326,778),(16,1398,786)]:
    m=mark(size,palette['paper'],palette['signal']); board.paste(m,(x,y),m)
    d.text((x,y+size+18),str(size),font=font(15,mono=True),fill=palette['steel'])
d.line((64,922,1536,922),fill='#334550')
d.text((72,965),'DESENHO VETORIAL PRÓPRIO · SEM SOMBRAS OU EFEITOS',font=font(17,mono=True),fill=palette['steel'])
d.text((72,1002),'Geometria única para SVG, PNG e ícone Windows. Interface preservada nesta etapa.',font=font(22),fill=palette['paper'])
board.save(BRAND/'CoreBi-identidade.png')
board.save(OUT/'CoreBi-identidade.png')
for name in ['corebi-symbol-dark.svg','corebi-symbol-light.svg','corebi-wordmark-dark.svg','corebi.ico']:
    (OUT/name).write_bytes((BRAND/name).read_bytes())
print(json.dumps({'brand':str(BRAND),'preview':str(OUT/'CoreBi-identidade.png'),'assets':len(list(BRAND.iterdir()))}))

assets=ROOT/'src'/'CoreBi.Desktop'/'Assets'
for source,target in [('corebi-symbol-dark.png','logo.png'),('corebi-symbol-light.png','logo-dark.png'),('corebi-symbol-dark.png','logo-badge.png'),('corebi.ico','corebi.ico')]:
    (assets/target).write_bytes((BRAND/source).read_bytes())
