"""Collect notices from restored NuGet packages. Run after a win-x64 restore.
No package source or binary is modified. SPDX texts are fetched only when absent.
--check compares licenses/packages.json with src/CoreBi.Desktop/packages.lock.json (no restore or NuGet cache needed).
"""
from pathlib import Path
import argparse
import json
import os
import re
import shutil
import urllib.request
import xml.etree.ElementTree as ET

NON_WINDOWS_RUNTIME = re.compile(r'^runtime\.(android|linux|osx|maccatalyst|ios|tvos|freebsd|illumos|solaris|browser|wasi)', re.I)

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--packages', type=Path, default=Path(os.environ.get('NUGET_PACKAGES', str(Path.home()/'.nuget'/'packages'))))
parser.add_argument('--check', action='store_true', help='compare licenses/packages.json with src/CoreBi.Desktop/packages.lock.json; no NuGet cache needed')
args = parser.parse_args()

# Runtime packs come from the pinned SDK (downloadDependencies), not from the lock file.
SDK_RUNTIME_PACK = re.compile(r'^Microsoft\.(NETCore|WindowsDesktop)\.App\.Runtime\.', re.I)

def check():
    lock = json.loads((ROOT/'src/CoreBi.Desktop/packages.lock.json').read_text(encoding='utf-8'))
    locked = {}
    for target in lock['dependencies'].values():
        for package_id, entry in target.items():
            if entry.get('type') == 'Project' or NON_WINDOWS_RUNTIME.match(package_id):
                continue
            locked.setdefault(package_id.lower(), set()).add((package_id, entry['resolved']))
    listed = {}
    for row in json.loads((ROOT/'licenses/packages.json').read_text(encoding='utf-8')):
        if not SDK_RUNTIME_PACK.match(row['package']):
            listed[row['package'].lower()] = (row['package'], row['version'])
    problems = []
    for key in sorted(locked.keys() - listed.keys()):
        for package_id, version in sorted(locked[key]):
            problems.append(f'faltando em licenses/packages.json: {package_id} {version}')
    for key in sorted(listed.keys() - locked.keys()):
        problems.append(f'sobrando em licenses/packages.json: {listed[key][0]} {listed[key][1]}')
    for key in sorted(locked.keys() & listed.keys()):
        versions = sorted(version for _, version in locked[key])
        if versions != [listed[key][1]]:
            problems.append(f'versão divergente: {listed[key][0]} {listed[key][1]} em licenses/packages.json, {", ".join(versions)} no lock')
    if problems:
        print('licenses/ desatualizado em relação a src/CoreBi.Desktop/packages.lock.json:')
        for problem in problems:
            print('  - ' + problem)
        print('Rode uma restauração win-x64 e depois: python tools/collect_licenses.py')
        raise SystemExit(1)
    print(f'licenses/packages.json confere com o lock ({len(locked)} pacotes; runtime packs do SDK não verificados).')

if args.check:
    check()
    raise SystemExit(0)
assets = json.loads((ROOT/'src/CoreBi.Desktop/obj/project.assets.json').read_text(encoding='utf-8'))
packages = {
    key for key, value in assets['libraries'].items()
    if value['type'] == 'package' and not NON_WINDOWS_RUNTIME.match(key)
}
for framework in assets['project']['frameworks'].values():
    for item in framework.get('downloadDependencies', []):
        if 'AspNetCore' not in item['name']:
            packages.add(item['name']+'/'+item['version'].strip('[]').split(',')[0].strip())

destination = ROOT/'licenses'
destination.mkdir(exist_ok=True)
for child in destination.iterdir():
    if child.is_dir() and child.name != 'SPDX':
        shutil.rmtree(child)
rows = []
expressions = set()
for package in sorted(packages, key=str.lower):
    package_id, version = package.split('/')
    folder = args.packages/package_id.lower()/version.lower()
    nuspecs = list(folder.glob('*.nuspec'))
    if not nuspecs:
        raise FileNotFoundError(f'Missing .nuspec for {package} in {folder}')
    nuspec = nuspecs[0]
    tree = ET.parse(nuspec).getroot()
    ns = tree.tag.partition('}')[0]+'}' if '}' in tree.tag else ''
    meta = tree.find(ns+'metadata')
    def value(name):
        return meta.findtext(ns+name) or ''
    license_node = meta.find(ns+'license')
    license_name = license_node.text if license_node is not None else value('licenseUrl')
    if license_node is not None and license_node.get('type') == 'expression':
        expressions.update(token for token in re.findall(r'[A-Za-z0-9.+-]+',license_name) if token not in {'AND','OR','WITH'})
    notices = []
    for file in folder.rglob('*'):
        if file.is_file() and re.search(r'license|copying|copyright|third.party|notice',file.name,re.I) and file.suffix.lower() not in {'.dll','.pdb','.nupkg'}:
            target = destination/package_id/version/file.relative_to(folder)
            target.parent.mkdir(parents=True,exist_ok=True)
            shutil.copyfile(file,target)
            notices.append(target.relative_to(ROOT).as_posix())
    repository = meta.find(ns+'repository')
    repo = repository.get('url','') if repository is not None else value('projectUrl')
    if repo.startswith('git://github.com/'):
        repo = 'https://github.com/' + repo[len('git://github.com/'):]
    commit = repository.get('commit','') if repository is not None else ''
    source = repo+'/tree/'+commit if repo.startswith('https://github.com/') and commit else repo
    rows.append(dict(package=package_id,version=version,license=license_name,authors=value('authors'),copyright=value('copyright'),source=source,notices=notices))

for expression in sorted(expressions):
    target = destination/'SPDX'/f'{expression}.txt'
    if not target.exists():
        target.parent.mkdir(exist_ok=True)
        url = f'https://raw.githubusercontent.com/spdx/license-list-data/main/text/{expression}.txt'
        with urllib.request.urlopen(url,timeout=30) as response:
            target.write_bytes(response.read())

(destination/'packages.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf-8')
text = '''# Componentes de terceiros

Inventário gerado a partir de `project.assets.json` e metadados NuGet restaurados. Inclui dependências transitivas; nem todos os ativos de plataformas listados são embarcados no executável Windows. Avisos originais e textos SPDX ficam nesta pasta; autores, copyright e links de origem completos em `packages.json`.

LibreHardwareMonitorLib é distribuído sem modificações na biblioteca, sob MPL-2.0. Seu código-fonte correspondente está no commit indicado na tabela. CoreBi não incorpora uma cópia modificada dessas fontes. A licença MIT do aplicativo não altera a licença dessas bibliotecas.

`System.IO.Ports` é uma dependência transitiva do monitoramento de hardware.

O pacote publicado acompanha estes avisos, as licenças originais disponíveis e as licenças dos runtimes .NET/WPF. As fontes de terceiros continuam disponíveis nos endereços abaixo.

| Pacote | Versão | Licença declarada | Fonte |
| --- | --- | --- | --- |
'''
for row in rows:
    source = f"[origem]({row['source']})" if row['source'] else 'Ver metadados NuGet'
    text += f"| {row['package']} | {row['version']} | {row['license']} | {source} |\n"
(destination/'THIRD-PARTY.md').write_text(text,encoding='utf-8')
print(json.dumps({'packages':len(rows),'licenseExpressions':sorted(expressions),'noticeFiles':sum(len(r['notices']) for r in rows)}))
