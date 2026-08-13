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
# `const` as well as `static`/`readonly`. A `public const string Folk` inside a guard was invisible
# to this tool, which is a member other assemblies genuinely reference - AgentBody.Folk is exactly
# that, and it is the path the whole cast is loaded from.
MEMBER = re.compile(
    r"^\s*public\s+(?:static\s+|const\s+|readonly\s+)*[\w<>\[\],\.\?]+\s+(\w+)\s*[\(\{=;]")
CLASSDECL = re.compile(r"^\s*(?:public|internal)?\s*(?:static\s+|sealed\s+|partial\s+)*class\s+(\w+)")


def editor_only_cond(rest):
    """Does this condition hold ONLY in the editor?

    Conservative on purpose: saying "not editor-only" costs a missed warning, saying "editor-only"
    wrongly costs a false failure on a correct file, and this gate is meant to be trusted.

    `A && UNITY_EDITOR` is editor-only - both must hold. `A || UNITY_EDITOR` is NOT, because A
    alone compiles it into a player. `!UNITY_EDITOR` is the player branch.
    """
    cond = rest.split("//")[0].strip()
    if "UNITY_EDITOR" not in cond:
        return False
    if "||" in cond:                    # some other define can satisfy it without the editor
        return False
    for term in cond.split("&&"):
        term = term.strip().strip("()").strip()
        if term == "UNITY_EDITOR":
            return True
    return False


def negated_editor_cond(rest):
    """Is this condition `!UNITY_EDITOR` - the branch that compiles only in a PLAYER?"""
    cond = rest.split("//")[0].strip().replace(" ", "")
    return cond in ("!UNITY_EDITOR", "(!UNITY_EDITOR)")


def guard_map(lines):
    """For each line, whether it sits inside a UNITY_EDITOR-only region.

    THE `#else` CASE NEEDS MORE THAN ONE BOOLEAN PER LEVEL, and getting it wrong inverts the
    answer on the branch that actually ships. This used to be `stack[-1] = not stack[-1]`, so

        #if UNITY_EDITOR / #elif FOO / #else

    marked the `#else` body - the branch that compiles INTO A PLAYER - as editor-only. Each level
    therefore carries three facts: whether the current branch is editor-only, whether the editor
    branch has already been taken at this level, and whether the level opened with `!UNITY_EDITOR`.

    An `#else` is editor-only in exactly one situation: the level opened with `!UNITY_EDITOR` and
    no branch since has been the editor branch. After a plain `#if UNITY_EDITOR`, the `#else` is
    the player's. After `#if FOO`, neither branch has anything to do with the editor.
    """
    out, stack = [], []
    for raw in lines:
        m = DIRECTIVE.match(raw)
        if m:
            kind, rest = m.group(1), m.group(2)
            if kind == "if":
                editor = editor_only_cond(rest)
                stack.append([editor, editor, negated_editor_cond(rest)])
            elif kind == "elif":
                if stack:
                    editor = editor_only_cond(rest)
                    stack[-1][0] = editor
                    stack[-1][1] = stack[-1][1] or editor
            elif kind == "else":
                if stack:
                    stack[-1][0] = stack[-1][2] and not stack[-1][1]
            elif kind == "endif":
                if stack: stack.pop()
            out.append(False)           # the directive line itself
            continue
        out.append(any(entry[0] for entry in stack))
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
