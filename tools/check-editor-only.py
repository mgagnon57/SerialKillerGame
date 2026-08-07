"""Find references to editor-only symbols from code that ships in a player build.

A member declared inside `#if UNITY_EDITOR` does not exist when Unity compiles Noir.Unity for a
standalone player. Calling it from unguarded code is a compile error that NEVER shows up in the
editor, which is why it can sit there for months.
"""
import os, re, sys
from collections import defaultdict

ROOT = r"C:\SerialKillerGame\Assets\Noir"
AREAS = ["Unity"]                       # Editor/ is editor-only by definition

DIRECTIVE = re.compile(r"^\s*#\s*(if|elif|else|endif)\b(.*)$")
MEMBER = re.compile(
    r"^\s*public\s+(?:static\s+)?(?:readonly\s+)?[\w<>\[\],\.\?]+\s+(\w+)\s*[\(\{=;]")
CLASSDECL = re.compile(r"^\s*(?:public|internal)?\s*(?:static\s+|sealed\s+|partial\s+)*class\s+(\w+)")


def guard_map(lines):
    """For each line, whether it sits inside a UNITY_EDITOR-only region."""
    out, stack = [], []
    for raw in lines:
        m = DIRECTIVE.match(raw)
        if m:
            kind, rest = m.group(1), m.group(2)
            if kind == "if":
                stack.append("UNITY_EDITOR" in rest and "!" not in rest)
            elif kind == "elif":
                if stack: stack[-1] = False
            elif kind == "else":
                if stack: stack[-1] = not stack[-1]
            elif kind == "endif":
                if stack: stack.pop()
            out.append(False)           # the directive line itself
            continue
        out.append(any(stack))
    return out


editor_only = defaultdict(set)          # class -> {members declared editor-only}
files = []
for area in AREAS:
    base = os.path.join(ROOT, area)
    for dirpath, _, names in os.walk(base):
        for n in names:
            if n.endswith(".cs"):
                files.append(os.path.join(dirpath, n))

for path in files:
    lines = open(path, encoding="utf-8", errors="replace").read().split("\n")
    guarded = guard_map(lines)
    current = None
    for i, line in enumerate(lines):
        c = CLASSDECL.match(line)
        if c:
            current = c.group(1)
        if not guarded[i] or current is None:
            continue
        m = MEMBER.match(line)
        if m:
            editor_only[current].add(m.group(1))

print("=== editor-only public members, by class ===")
for cls in sorted(editor_only):
    if editor_only[cls]:
        print(f"  {cls}: {', '.join(sorted(editor_only[cls]))}")
print()

# Now: references to those from UNGUARDED lines anywhere in Unity/
offenders = []
for path in files:
    lines = open(path, encoding="utf-8", errors="replace").read().split("\n")
    guarded = guard_map(lines)
    for i, line in enumerate(lines):
        if guarded[i]:
            continue
        stripped = line.strip()
        if stripped.startswith("//") or stripped.startswith("///") or stripped.startswith("*"):
            continue
        for cls, members in editor_only.items():
            for mem in members:
                if re.search(r"\b%s\s*\.\s*%s\b" % (re.escape(cls), re.escape(mem)), line):
                    # a class referring to its own editor-only member from inside its own
                    # editor-only region is already excluded by the guard check above
                    offenders.append((os.path.relpath(path, ROOT), i + 1, f"{cls}.{mem}", stripped[:90]))

print("=== unguarded references to editor-only members ===")
if not offenders:
    print("  NONE — nothing outside a guard names an editor-only member")
else:
    for f, ln, sym, text in offenders:
        print(f"  {f}:{ln}  {sym}")
        print(f"        {text}")
sys.exit(1 if offenders else 0)
