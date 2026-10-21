#!/usr/bin/env python3
"""
Приводит CHANGELOG.md в порядок к выпуску версии (вызывается из release.yml):
  1. Нет раздела «## [X.Y.Z]», а в «## [Unreleased]» есть изменения — раздел переименовывается
     в «## [X.Y.Z] — дата», сверху добавляется новый пустой «## [Unreleased]».
  2. Раздел «## [Unreleased]» есть всегда (если пропал — добавляется пустой).
  3. Ссылки внизу файла: у каждой версии — сравнение с предыдущей (без них заголовки на GitHub не кликабельные),
     [Unreleased] — сравнение последней версии с HEAD. Уже существующие ссылки на версии не трогаются.

Использование: changelog_release.py <версия> <owner/repo> [CHANGELOG.md] [дата ГГГГ-ММ-ДД]
Печатает, что изменено; файл перезаписывается, только если есть изменения.
"""
import datetime
import re
import sys

HEADING = re.compile(r'^## \[(Unreleased|\d+\.\d+\.\d+)\]')
LINK = re.compile(r'^\[([^\]]+)\]: \S+')


def tidy(text, version, repo, date):
    lines = text.splitlines()
    notes = []

    # ---- 1. Unreleased -> версия ----
    has_version = any(l.startswith(f'## [{version}]') for l in lines)
    if not has_version:
        idx = next((i for i, l in enumerate(lines) if l.startswith('## [Unreleased]')), None)
        if idx is not None:
            end = next((j for j in range(idx + 1, len(lines))
                        if lines[j].startswith('## [') or LINK.match(lines[j])), len(lines))
            if any(l.strip() for l in lines[idx + 1:end]):
                lines[idx:idx + 1] = ['## [Unreleased]', '', f'## [{version}] — {date}']
                notes.append(f'раздел [Unreleased] переименован в [{version}]')
            else:
                notes.append(f'нет раздела [{version}], а [Unreleased] пуст — оставлено как есть')

    # ---- 2. раздел [Unreleased] есть всегда — сюда записываются следующие изменения ----
    if not any(l.startswith('## [Unreleased]') for l in lines):
        first = next((i for i, l in enumerate(lines) if HEADING.match(l)), None)
        if first is not None:
            lines[first:first] = ['## [Unreleased]', '']
            notes.append('добавлен пустой раздел [Unreleased]')

    # ---- 3. ссылки ----
    order =[m.group(1) for m in map(HEADING.match, lines) if m]       # сверху вниз
    versions = [h for h in order if h != 'Unreleased']
    links = {}
    for l in lines:
        m = LINK.match(l)
        if m:
            links[m.group(1)] = l
    base = f'https://github.com/{repo}'

    wanted = []
    if 'Unreleased' in order:
        target = f'[Unreleased]: {base}/compare/v{versions[0]}...HEAD' if versions else f'[Unreleased]: {base}/commits/HEAD'
        if links.get('Unreleased') != target:
            notes.append('обновлена ссылка [Unreleased]')
        wanted.append(target)
    for i, v in enumerate(versions):
        if v in links:
            wanted.append(links[v])
            continue
        prev = versions[i + 1] if i + 1 < len(versions) else None
        wanted.append(f'[{v}]: {base}/compare/v{prev}...v{v}' if prev else f'[{v}]: {base}/releases/tag/v{v}')
        notes.append(f'добавлена ссылка [{v}]')
    # прочие ссылки (не на версии) — сохраняем как были
    known = set(order)
    wanted += [l for k, l in links.items() if k not in known]

    body = [l for l in lines if not LINK.match(l)]
    while body and not body[-1].strip():
        body.pop()
    result = '\n'.join(body + [''] + wanted) + '\n'
    return result, notes


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(2)
    version = sys.argv[1].lstrip('v')
    repo = sys.argv[2]
    path = sys.argv[3] if len(sys.argv) > 3 else 'CHANGELOG.md'
    date = sys.argv[4] if len(sys.argv) > 4 else datetime.date.today().isoformat()
    with open(path, encoding='utf-8') as f:
        text = f.read()
    result, notes = tidy(text, version, repo, date)
    if result != text:
        with open(path, 'w', encoding='utf-8', newline='\n') as f:
            f.write(result)
    print('; '.join(notes) if notes else 'CHANGELOG.md в порядке')


if __name__ == '__main__':
    main()
