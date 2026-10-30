"""Check public artifacts, Markdown links, source provenance and private-data exclusions."""
import argparse
import hashlib
import json
from pathlib import Path
import re


def check(root):
    problems=[]
    excluded_dirs={'.git','node_modules','__pycache__','build','reports'}
    forbidden_suffixes={'.efi','.bin','.exe','.dll','.rmp','.pfx','.pem','.key','.iso','.img','.zip'}
    files=[]
    for path in root.rglob('*'):
        rel=path.relative_to(root)
        if any(part in excluded_dirs for part in rel.parts):
            continue
        if path.is_symlink():
            problems.append(f'Symlink is not allowed in this public package: {rel}')
        if path.is_file():
            files.append(path)
            if path.suffix.lower() in forbidden_suffixes:
                problems.append(f'Forbidden executable/payload/private file: {rel}')
            if path.name.lower() in {'.env','prom.bin','profile.bin'} or 'private-recovery' in str(rel).lower():
                problems.append(f'Private input: {rel}')
            if path.stat().st_size>10*1024*1024:
                problems.append(f'Unexpected large public file: {rel}')
    own_markdown=[root/'README.md',root/'README.ar.md',root/'RESULTS.md',root/'THIRD_PARTY_NOTICES.md',root/'UPSTREAM-NOTICES.md',root/'results/README.md']
    own_markdown+=list((root/'docs').glob('*.md'))
    for path in own_markdown:
        text=path.read_text(encoding='utf-8-sig')
        refs=re.findall(r'!?\[[^\]]*\]\(([^\s)]+)(?:\s+[^)]*)?\)',text)
        refs+=re.findall(r'<img\b[^>]*\bsrc="([^"]+)"',text)
        for ref in refs:
            if re.match(r'^[a-zA-Z][a-zA-Z0-9+.-]*:',ref) or ref.startswith('#'):
                continue
            ref=ref.split('#',1)[0]
            if ref and not (path.parent/ref).exists():
                problems.append(f'Broken local link in {path.relative_to(root)}: {ref}')
    # Recovery-password shape; do not print the matching value.
    for path in files:
        if path.suffix.lower() not in {'.md','.json','.ps1','.py','.cs','.c','.h','.yml','.txt'}:
            continue
        text=path.read_text(encoding='utf-8',errors='replace')
        if re.search(r'(?<!\d)\d{6}(?:-\d{6}){7}(?!\d)',text):
            problems.append(f'Possible recovery password in {path.relative_to(root)}; value suppressed')
        if re.search(r'(?i)C:[\\/]+Users[\\/]+User[\\/]+(?:Desktop|Documents)[\\/]+(?:gpu-unlock|Codex)',text):
            problems.append(f'Private workspace path in {path.relative_to(root)}')
    provenance=json.loads((root/'results/source-provenance.json').read_text(encoding='utf-8'))
    for row in provenance['public_source_files_copied']:
        path=root/'firmware'/row['path']
        if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest()!=row['sha256']:
            problems.append(f'Copied source hash mismatch: {row["path"]}')
    template=(root/'firmware/src/gop/palit_internal_config.h').read_text()
    if '#error' not in template or 'PalitUnlockPartitionGuid =' in template:
        problems.append('Internal public config must remain an unconfigured refusing template')
    for name in ('LICENSE','UPSTREAM-NOTICES.md','THIRD_PARTY_NOTICES.md','README.md','README.ar.md','RESULTS.md'):
        if not (root/name).is_file():problems.append(f'Missing required document: {name}')
    return problems,len(files)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root',type=Path,default=Path(__file__).resolve().parent.parent)
    args=parser.parse_args()
    try:
        problems,count=check(args.root)
        if problems:
            for problem in problems:print('FAIL:',problem)
            raise SystemExit(1)
        print(f'PASS: {count} public files; local document links, copied-source hashes and exclusion scan.')
        print('Generated build/reports/cache and Git metadata are excluded; review git status before publication.')
    except (OSError,KeyError,ValueError) as error:
        parser.exit(1,f'Package check failed: {error}\n')
