"""Fail before a Workshop build can publish an incomplete Biggest Boy slot."""
from pathlib import Path
import sys

NAMES = ('book', 'kebap', 'beer', 'cigarette', 'butter', 'shot', 'funnel', 'g', 'o', 'a', 't', 'scatter')
ROOT = Path(__file__).resolve().parents[1]


def missing_assets(root=ROOT):
    directory = root / 'ui/AnoCore/styles/custom_game/anocore/casino'
    missing = []
    for name in NAMES:
        png = directory / f'biggest_{name}.png'
        descriptor = directory / f'biggest_{name}.vtex'
        if not png.is_file() or not png.read_bytes().startswith(b'\x89PNG\r\n\x1a\n'): missing.append(str(png.relative_to(root)))
        if not descriptor.is_file() or f'biggest_{name}.png' not in descriptor.read_text(): missing.append(str(descriptor.relative_to(root)))
    return missing


if __name__ == '__main__':
    missing = missing_assets()
    if missing:
        print('Biggest Boy Workshop build blocked: missing original symbol assets:', file=sys.stderr)
        print('\n'.join(missing), file=sys.stderr)
        sys.exit(1)
    print('All twelve original PNG/VTEX symbol pairs are present.')
