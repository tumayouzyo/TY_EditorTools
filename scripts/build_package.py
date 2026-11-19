"""Build the VPM release archive."""
import hashlib
import json
from pathlib import Path
import re
import zipfile

ROOT = Path(__file__).resolve().parents[1]

def main():
    manifest = json.loads((ROOT / 'package.json').read_text(encoding='utf-8'))
    version = manifest['version']
    if not re.fullmatch(r'\d+\.\d+\.\d+', version):
        raise ValueError('Invalid version')
    filename = f"{manifest['name']}-{version}.zip"
    expected = f'https://github.com/tumayouzyo/TY_EditorTools/releases/download/v{version}/{filename}'
    if manifest['url'] != expected:
        raise ValueError('Release URL mismatch')
    files = sorted(p for p in (ROOT / 'Editor').rglob('*') if p.is_file())
    files += [ROOT / 'Editor.meta']
    for name in ('package.json', 'LICENSE', 'README.md', 'CHANGELOG.md'):
        files += [ROOT / name, ROOT / (name + '.meta')]
    guids = set()
    for path in files:
        if path.suffix != '.meta' and not path.with_name(path.name + '.meta').exists():
            raise ValueError(f'Missing meta: {path}')
        if path.suffix == '.meta':
            match = re.search(r'^guid: ([0-9a-f]{32})$', path.read_text(), re.M)
            if not match or match[1] in guids:
                raise ValueError(f'Invalid/duplicate GUID: {path}')
            guids.add(match[1])
    output = ROOT / 'dist'
    output.mkdir(exist_ok=True)
    archive_path = output / filename
    with zipfile.ZipFile(archive_path, 'w', zipfile.ZIP_DEFLATED) as archive:
        for path in files:
            archive.write(path, path.relative_to(ROOT).as_posix())
    with zipfile.ZipFile(archive_path) as archive:
        assert json.loads(archive.read('package.json')) == manifest
        assert archive.testzip() is None
    (output / 'package.json').write_bytes((ROOT / 'package.json').read_bytes())
    print(filename, hashlib.sha256(archive_path.read_bytes()).hexdigest())

if __name__ == '__main__':
    main()
