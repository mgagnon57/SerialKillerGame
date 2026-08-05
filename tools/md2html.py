"""Markdown -> one self-contained HTML file, for reading these docs away from a terminal.

Covers exactly what the two documents use: headings, tables, blockquotes, task lists,
nested bullets, horizontal rules, and inline code / bold / italic / links. Not a general
markdown implementation and does not pretend to be one.
"""
import io, re, sys, html


def inline(t):
    # Escape first, then re-introduce the markup. Doing it the other way round lets a stray
    # angle bracket in the prose eat the rest of the line.
    t = html.escape(t, quote=False)

    # Code spans are taken out before anything else, so a ** inside one survives verbatim.
    spans = []
    def stash(m):
        spans.append(m.group(1))
        return "\x00%d\x00" % (len(spans) - 1)
    t = re.sub(r"`([^`]+)`", stash, t)

    t = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", r'<a href="\2">\1</a>', t)
    t = re.sub(r"\*\*([^*]+)\*\*", r"<strong>\1</strong>", t)      # bold before italic
    t = re.sub(r"(?<![*\w])\*([^*\n]+)\*(?![*\w])", r"<em>\1</em>", t)
    t = re.sub(r"(?<![_\w])_([^_\n]+)_(?![_\w])", r"<em>\1</em>", t)

    for i, s in enumerate(spans):
        t = t.replace("\x00%d\x00" % i, "<code>%s</code>" % s)
    return t


def convert(md):
    out, lines, i = [], md.split("\n"), 0
    list_depth = 0

    def close_lists(to=0):
        nonlocal list_depth
        while list_depth > to:
            out.append("</ul>")
            list_depth -= 1

    while i < len(lines):
        raw = lines[i].rstrip()
        line = raw.strip()

        if not line:
            close_lists()
            i += 1
            continue

        if line.startswith("---") and set(line) == {"-"}:
            close_lists(); out.append("<hr>"); i += 1; continue

        m = re.match(r"^(#{1,6})\s+(.*)$", line)
        if m:
            close_lists()
            n = len(m.group(1))
            out.append("<h%d>%s</h%d>" % (n, inline(m.group(2)), n))
            i += 1
            continue

        # A table: a header row, a separator of dashes, then body rows.
        if line.startswith("|") and i + 1 < len(lines) and re.match(r"^\s*\|[\s:|-]+\|\s*$", lines[i + 1]):
            close_lists()
            def cells(s):
                return [c.strip() for c in s.strip().strip("|").split("|")]
            out.append("<table><thead><tr>")
            for c in cells(line):
                out.append("<th>%s</th>" % inline(c))
            out.append("</tr></thead><tbody>")
            i += 2
            while i < len(lines) and lines[i].strip().startswith("|"):
                out.append("<tr>")
                for c in cells(lines[i]):
                    out.append("<td>%s</td>" % inline(c))
                out.append("</tr>")
                i += 1
            out.append("</tbody></table>")
            continue

        if line.startswith(">"):
            close_lists()
            block = []
            while i < len(lines) and lines[i].strip().startswith(">"):
                block.append(lines[i].strip().lstrip(">").strip())
                i += 1
            out.append("<blockquote>%s</blockquote>" % inline(" ".join(block)))
            continue

        m = re.match(r"^(\s*)[-*]\s+(.*)$", raw)
        if m:
            depth = len(m.group(1)) // 2 + 1
            body = m.group(2)

            while list_depth < depth:
                out.append("<ul>"); list_depth += 1
            close_lists(depth)

            # GATHER THE WRAPPED CONTINUATION LINES FIRST, then convert the whole bullet in
            # one go. Converting each line as it arrived broke every span that opened on one
            # line and closed on the next - which in these documents is most of the bold,
            # because the prose is wrapped at ninety columns.
            i += 1
            while i < len(lines):
                nxt = lines[i]
                if not nxt.strip() or re.match(r"^\s*[-*]\s+", nxt) or nxt.strip().startswith("#"):
                    break
                if len(nxt) - len(nxt.lstrip()) < 2:
                    break
                body += " " + nxt.strip()
                i += 1

            done = re.match(r"^\[([ xX])\]\s+(.*)$", body)
            if done:
                checked = done.group(1).lower() == "x"
                out.append('<li class="task %s"><span class="box">%s</span>%s</li>'
                           % ("done" if checked else "open", "x" if checked else "",
                              inline(done.group(2))))
            else:
                out.append("<li>%s</li>" % inline(body))
            continue

        close_lists()
        para = [line]
        i += 1
        while i < len(lines) and lines[i].strip() and not re.match(
                r"^\s*([-*#>|]|---)", lines[i]):
            para.append(lines[i].strip()); i += 1
        out.append("<p>%s</p>" % inline(" ".join(para)))

    close_lists()
    return "\n".join(out)


PAGE = """<!doctype html>
<html lang="en"><head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>%(title)s</title>
<style>
  :root {
    --ink:#e8e4d9; --dim:#9a958a; --bg:#14161a; --panel:#1b1e23;
    --line:#2c3038; --accent:#d8a657; --link:#7fb3d5; --code:#c9d4a8;
  }
  @media (prefers-color-scheme: light) {
    :root { --ink:#23262b; --dim:#5f6570; --bg:#f6f4ef; --panel:#fffdf8;
            --line:#ddd8cd; --accent:#8a6316; --link:#255f8a; --code:#4a5a28; }
  }
  * { box-sizing:border-box; }
  body { margin:0; padding:2.5rem 1.25rem 6rem; background:var(--bg); color:var(--ink);
         font:16px/1.65 "Iowan Old Style","Palatino Linotype",Palatino,Georgia,serif; }
  main { max-width:52rem; margin:0 auto; }
  h1,h2,h3,h4 { font-family:"Inter","Segoe UI",system-ui,sans-serif; line-height:1.25;
                text-wrap:balance; }
  h1 { font-size:1.9rem; margin:0 0 .3rem; letter-spacing:-.01em; }
  h2 { font-size:1.3rem; margin:2.6rem 0 .7rem; padding-bottom:.35rem;
       border-bottom:1px solid var(--line); }
  h3 { font-size:1.05rem; margin:1.9rem 0 .5rem; color:var(--accent); }
  h4 { font-size:.95rem; margin:1.4rem 0 .4rem; color:var(--dim); }
  p { margin:.8rem 0; }
  a { color:var(--link); }
  hr { border:0; border-top:1px solid var(--line); margin:2.4rem 0; }
  code { font:.88em/1.5 "Cascadia Mono",Consolas,"DejaVu Sans Mono",monospace;
         color:var(--code); background:var(--panel); padding:.1em .35em;
         border-radius:3px; border:1px solid var(--line); }
  blockquote { margin:1.1rem 0; padding:.7rem 1.1rem; background:var(--panel);
               border-left:3px solid var(--accent); color:var(--ink); }
  ul { margin:.6rem 0; padding-left:1.3rem; }
  li { margin:.42rem 0; }
  li.task { list-style:none; margin-left:-1.3rem; display:flex; gap:.6rem;
            align-items:baseline; }
  .box { flex:none; width:1.05em; height:1.05em; border:1px solid var(--line);
         border-radius:3px; background:var(--panel); font:700 .75em/1.05em
         "Cascadia Mono",monospace; text-align:center; color:var(--accent); }
  li.done { color:var(--dim); }
  li.done .box { border-color:var(--accent); }
  .wrap { overflow-x:auto; }
  table { border-collapse:collapse; margin:1.1rem 0; width:100%%;
          font-family:"Inter","Segoe UI",system-ui,sans-serif; font-size:.9rem;
          font-variant-numeric:tabular-nums; }
  th,td { text-align:left; padding:.5rem .7rem; border-bottom:1px solid var(--line);
          vertical-align:top; }
  th { color:var(--dim); font-weight:600; font-size:.8rem; letter-spacing:.04em;
       text-transform:uppercase; }
  tbody tr:hover { background:var(--panel); }
  .meta { color:var(--dim); font-family:"Inter","Segoe UI",system-ui,sans-serif;
          font-size:.82rem; margin:0 0 2rem; }
</style>
</head><body><main>
<p class="meta">%(subtitle)s</p>
%(body)s
</main></body></html>
"""

if __name__ == "__main__":
    src, dst, title, subtitle = sys.argv[1:5]
    md = io.open(src, encoding="utf-8").read()
    body = convert(md)
    # Tables get their own scroll container so the page body never scrolls sideways.
    body = body.replace("<table>", '<div class="wrap"><table>').replace("</table>", "</table></div>")
    io.open(dst, "w", encoding="utf-8", newline="").write(
        PAGE % dict(title=title, subtitle=subtitle, body=body))
    print("wrote %s" % dst)
