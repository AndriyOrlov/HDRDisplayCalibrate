"""Collects every S.T("English", "Українська") / S.F(...) string from the sources into tools/strings.json.

Each entry is keyed by the same short hash the app uses (first 8 hex digits of SHA-1 of the English text), so a
translation file i18n/<lang>.json only has to map those keys to translated text. Run from the repository root:

    python tools/extract_strings.py            # writes tools/strings.json
    python tools/extract_strings.py --check    # also reports missing / stale keys and placeholder mismatches
"""
import hashlib, json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "src")


def unescape(body, verbatim):
    if verbatim:
        return body.replace('""', '"')
    out, i = [], 0
    while i < len(body):
        c = body[i]
        if c == '\\' and i + 1 < len(body):
            n = body[i + 1]
            if n == 'u':
                out.append(chr(int(body[i + 2:i + 6], 16))); i += 6; continue
            out.append({'n': '\n', 't': '\t', 'r': '\r', '0': '\0', '"': '"', '\\': '\\', "'": "'"}.get(n, n)); i += 2; continue
        out.append(c); i += 1
    return ''.join(out)


def read_literal(s, i):
    """s[i] is '"' or '@'; returns (text, index after the literal)."""
    verbatim = s[i] == '@'
    if verbatim: i += 1
    assert s[i] == '"'
    j = i + 1
    while True:
        if verbatim:
            if s[j] == '"':
                if s[j + 1] == '"': j += 2; continue
                break
        else:
            if s[j] == '\\': j += 2; continue
            if s[j] == '"': break
        j += 1
    return unescape(s[i + 1:j], verbatim), j + 1


def read_concat(s, i):
    """A string argument made of literals joined with '+'; returns (text, index) or (None, index) if not literal."""
    parts = []
    while True:
        while s[i].isspace(): i += 1
        if s[i] == '"' or (s[i] == '@' and s[i + 1] == '"'):
            t, i = read_literal(s, i); parts.append(t)
            j = i
            while s[j].isspace(): j += 1
            if s[j] == '+': i = j + 1; continue
            return ''.join(parts), i
        return None, i


def key(en):
    return hashlib.sha1(en.encode('utf-8')).hexdigest()[:8]


def collect():
    found, odd = {}, []
    for dirpath, _, files in os.walk(SRC):
        if os.sep + 'bin' in dirpath or os.sep + 'obj' in dirpath: continue
        for f in files:
            if not f.endswith('.cs'): continue
            p = os.path.join(dirpath, f)
            s = open(p, encoding='utf-8').read()
            for m in re.finditer(r'\bS\.[TF]\(', s):
                i = m.end()
                en, i = read_concat(s, i)
                if en is None:
                    odd.append(f"{os.path.relpath(p, ROOT)}:{s.count(chr(10), 0, m.start()) + 1}"); continue
                while s[i].isspace() or s[i] == ',': i += 1
                uk, _ = read_concat(s, i)
                found[key(en)] = {"en": en, "uk": uk or ""}
    return found, odd


def placeholders(t):
    return sorted(re.findall(r'\{\d+(?::[^}]*)?\}', t))


if __name__ == '__main__':
    found, odd = collect()
    out = os.path.join(ROOT, 'tools', 'strings.json')
    with open(out, 'w', encoding='utf-8') as fh:
        json.dump(dict(sorted(found.items(), key=lambda kv: kv[1]['en'].lower())), fh, ensure_ascii=False, indent=1)
    print(f"{len(found)} strings -> {os.path.relpath(out, ROOT)}")
    for o in odd: print("  not a plain literal (left in English):", o)
    if '--check' in sys.argv:
        for fn in sorted(os.listdir(os.path.join(SRC, 'i18n'))):
            tr = json.load(open(os.path.join(SRC, 'i18n', fn), encoding='utf-8'))
            missing = [k for k in found if k not in tr]
            stale = [k for k in tr if k not in found]
            bad = [k for k in tr if k in found and placeholders(tr[k]) != placeholders(found[k]['en'])]
            print(f"{fn}: {len(tr)} translated, {len(missing)} missing, {len(stale)} stale, {len(bad)} placeholder mismatches")
            for k in bad: print("   placeholder mismatch", k, found[k]['en'][:60])
