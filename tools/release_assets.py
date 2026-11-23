"""Arquivos de uma release a partir do que o Publish.ps1 e o Installer.ps1 geraram.

Monta em artifacts/release: o instalador, o zip portátil (a pasta publicada inteira, com caminhos em "/"), o
SHA256SUMS.txt dos dois e as notas da release, tiradas da seção da versão no docs/CHANGELOG.md.
O código-fonte de cada release o GitHub já gera sozinho a partir da tag.

Uso: python -B tools/release_assets.py 1.0.0
"""
from pathlib import Path
import hashlib
import re
import shutil
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[1]
PUBLISH = ROOT / 'artifacts' / 'publish' / 'win-x64'
INSTALLER = ROOT / 'artifacts' / 'installer'
OUT = ROOT / 'artifacts' / 'release'


def project_version():
    props = (ROOT / 'Directory.Build.props').read_text(encoding='utf-8')
    match = re.search(r'<Version>([^<]+)</Version>', props)
    if not match:
        sys.exit('Versão não encontrada em Directory.Build.props')
    return match.group(1).strip()


def changelog_section(version):
    text = (ROOT / 'docs' / 'CHANGELOG.md').read_text(encoding='utf-8')
    match = re.search(rf'^## {re.escape(version)} — ([^\n]+)\n(.*?)(?=^## |\Z)', text, re.M | re.S)
    if not match:
        sys.exit(f'Seção "## {version} — <data>" não encontrada em docs/CHANGELOG.md')
    if not re.fullmatch(r'\d{4}-\d{2}-\d{2}', match.group(1).strip()):
        sys.exit(f'A seção {version} do CHANGELOG ainda não tem data: feche-a com a data da publicação')
    return match.group(2).strip()


def sha256(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1 << 20), b''):
            digest.update(block)
    return digest.hexdigest()


def main():
    if len(sys.argv) != 2:
        sys.exit('Uso: python -B tools/release_assets.py <versão>')
    version = sys.argv[1].removeprefix('v')
    if version != project_version():
        sys.exit(f'A versão pedida ({version}) não é a do Directory.Build.props ({project_version()})')
    notes_body = changelog_section(version)
    setup = INSTALLER / f'CoreBi-{version}-win-x64-setup.exe'
    if not (PUBLISH / 'CoreBi.exe').is_file() or not setup.is_file():
        sys.exit('Rode tools/Publish.ps1 e tools/Installer.ps1 antes')
    if not (PUBLISH / 'licenses' / 'LICENSE').is_file():
        sys.exit('A pasta publicada está sem licenses/LICENSE')

    if OUT.exists():
        shutil.rmtree(OUT)
    OUT.mkdir(parents=True)
    shutil.copy2(setup, OUT / setup.name)

    # O CoreBi.exe não roda sozinho: as DLLs nativas, a pasta amd64 e licenses/ vão juntas, dentro de uma pasta CoreBi
    portable = OUT / f'CoreBi-{version}-win-x64-portable.zip'
    with zipfile.ZipFile(portable, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in sorted(p for p in PUBLISH.rglob('*') if p.is_file()):
            archive.write(path, 'CoreBi/' + path.relative_to(PUBLISH).as_posix())

    files = [setup.name, portable.name]
    sums = ''.join(f'{sha256(OUT / name)}  {name}\n' for name in files)
    (OUT / 'SHA256SUMS.txt').write_text(sums, encoding='utf-8', newline='\n')

    notes = f"""## Baixar

| Arquivo | Para quê |
| --- | --- |
| `{setup.name}` | Instalador, o caminho recomendado: instala em Arquivos de Programas, cria os atalhos e oferece o PawnIO e o RTSS |
| `{portable.name}` | Sem instalar: extraia a pasta inteira e abra o `CoreBi.exe` |
| `SHA256SUMS.txt` | O SHA256 dos dois arquivos |

Requer Windows 10 ou 11 de 64 bits e permissão de administrador. O instalador não tem assinatura digital, então o Windows pode mostrar "O Windows protegeu o computador": confira o SHA256 com `Get-FileHash .\\{setup.name} -Algorithm SHA256` e, se bater, use **Mais informações** e **Executar assim mesmo**.

```text
{sums.rstrip()}
```

## O que tem nesta versão

{notes_body}
"""
    (OUT / 'release-notes.md').write_text(notes, encoding='utf-8', newline='\n')
    for name in files + ['SHA256SUMS.txt', 'release-notes.md']:
        print(f'{OUT / name}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
