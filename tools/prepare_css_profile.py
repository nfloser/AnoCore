"""Prepare private backup-matched role display and scoped CSS flags offline."""

import argparse
import json
import re
import shutil
from pathlib import Path

PRIORITIES = {'#css/admin': 400, '#css/dev': 300, '#css/host': 200, '#css/og': 100, '#css/normal': 0}
COLORS = {name.upper(): name for name in (
    'Default', 'White', 'DarkRed', 'LightPurple', 'Green', 'Olive', 'Lime', 'Red',
    'Grey', 'Yellow', 'Silver', 'LightBlue', 'DarkBlue', 'Purple', 'LightRed', 'Orange')}
COLORS['MAGENTA'] = 'Purple'
PASSTHROUGH = ['.map', '.prac', '.ready', '.unready', '.pause', '.unpause', '.stay', '.switch']


def uncomment(text):
    """Strip JSONC comments while retaining strings, escaped quotes and URLs."""
    result = []
    index = 0
    in_string = False
    while index < len(text):
        char = text[index]
        if in_string:
            result.append(char)
            if char == '\\':
                index += 1
                if index < len(text):
                    result.append(text[index])
            elif char == '"':
                in_string = False
        elif char == '"':
            in_string = True
            result.append(char)
        elif text.startswith('//', index):
            end = text.find('\n', index)
            index = len(text) if end < 0 else end
            continue
        elif text.startswith('/*', index):
            end = text.find('*/', index + 2)
            if end < 0:
                raise ValueError('Unterminated JSON comment')
            result.append(' ')
            index = end + 2
            continue
        else:
            result.append(char)
        index += 1
    return ''.join(result)


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('Duplicate JSON property')
        result[key] = value
    return result


def read(path):
    if path.stat().st_size > 1024 * 1024:
        raise ValueError('Input exceeds 1 MiB')
    value = json.loads(uncomment(path.read_text(encoding='utf-8-sig')), object_pairs_hook=unique_object)
    if not isinstance(value, dict):
        raise ValueError('JSON object required')
    return value


def appearance(value):
    if not isinstance(value, dict) or set(value) - {'NameTag', 'NameColor', 'ChatColor', 'ClanTag', 'AvailableConfigs'}:
        raise ValueError('Unsupported CustomTags fields')
    for field in ('NameColor', 'ChatColor', 'ClanTag'):
        if value.get(field, '') != '':
            raise ValueError('Nonempty name/message colors or clan tags require explicit manual migration')
    if value.get('AvailableConfigs', []) not in ([], ['']):
        raise ValueError('Selectable CustomTags configurations require explicit manual migration')
    raw = value.get('NameTag')
    if not isinstance(raw, str) or len(raw) > 80:
        raise ValueError('A bounded NameTag string is required')
    match = re.fullmatch(r'(?:\{([A-Z]+)\})?([^{}]+)', raw)
    if match is None:
        raise ValueError('NameTag must have at most one leading supported color')
    color = 'None' if match[1] is None else COLORS.get(match[1])
    text = match[2].strip()
    if color is None or not 1 <= len(text) <= 24 or any(ord(char) < 32 or 127 <= ord(char) <= 159 for char in raw):
        raise ValueError('Unsupported tag text or color')
    return {'Text': text, 'Color': color}


def prepare(tags_path, groups_path, output):
    if output.exists() or output.is_symlink():
        raise ValueError('Output must be a new directory; existing files are never replaced')
    tags = read(tags_path)
    groups = read(groups_path)
    if len(tags) > 65 or len(groups) > 256:
        raise ValueError('Too many tag or group definitions')
    roles = {'Enabled': True, 'DefaultTag': None, 'Groups': [], 'PlayerOverrides': {}}
    for key, value in tags.items():
        tag = appearance(value)
        if key == 'all':
            roles['DefaultTag'] = tag
        elif key in PRIORITIES:
            roles['Groups'].append({'Group': key, 'Priority': PRIORITIES[key], 'Tag': tag})
        elif key.isascii() and key.isdigit() and str(int(key)) == key and 0 < int(key) <= 18446744073709551615:
            roles['PlayerOverrides'][key] = tag
        else:
            raise ValueError('Unsupported tag selector; only known role groups, all and SteamID64 are supported')
    if len(roles['PlayerOverrides']) > 32:
        raise ValueError('At most 32 private player overrides are supported')
    roles['Groups'].sort(key=lambda item: -item['Priority'])
    for name in ('#css/dev', '#css/host'):
        group = groups.get(name)
        if not isinstance(group, dict) or not isinstance(group.get('flags'), list):
            raise ValueError('Existing CSS dev and host groups with flags arrays are required')
        flags = group['flags']
        if len(flags) > 256 or any(not isinstance(flag, str) or not flag.startswith('@')
                                   or len(flag) > 128 or any(char.isspace() for char in flag) for flag in flags):
            raise ValueError('Malformed operator flags')
        for flag in ('@anocore/team', '@anocore/veto'):
            if flag not in flags:
                flags.append(flag)
    chat = {'PublicTemplate': '{player.name}: {message}', 'TeamTemplate': '(TEAM) {player.name}: {message}',
            'RankColor': 'None', 'NameColor': 'None', 'MessageColor': 'None', 'PassthroughCommands': PASSTHROUGH}
    # Validate and serialize every file before creating the destination. Sources
    # remain read-only; private overrides live only in this operator-owned output.
    files = {'role-chat-tags.json': roles, 'chat-format.json': chat, 'admin_groups.json': groups}
    serialized = {name: json.dumps(data, ensure_ascii=False, indent=2, allow_nan=False) + '\n'
                  for name, data in files.items()}
    output.mkdir(mode=0o700, parents=True, exist_ok=False)
    try:
        for name, data in serialized.items():
            target = output / name
            with target.open('x', encoding='utf-8') as stream:
                stream.write(data)
            target.chmod(0o600)
    except Exception:
        shutil.rmtree(output)
        raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--tags', required=True, type=Path)
    parser.add_argument('--groups', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    try:
        prepare(args.tags, args.groups, args.output)
    except (OSError, ValueError):
        # Do not echo parsed private selectors or JSON diagnostics containing IDs.
        parser.exit(1, 'Preparation failed: verify supported backup tags, operator groups and a new output directory.\n')
    print('Prepared role-chat-tags.json, chat-format.json and admin_groups.json. Review before installation.')


if __name__ == '__main__':
    main()
