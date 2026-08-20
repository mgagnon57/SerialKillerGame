"""Run the game's own check over the authored map, and report what it found.

WHY THIS IS SEPARATE from serve-viewer.py. The server answers in milliseconds; this takes minutes
and holds the Unity project while it does. Mixing them would mean a page that hangs on Save because
a check happened to be running.

WHAT IT RUNS is Noir.Editor.SmokeTest.Run, which is the only thing that exercises the four survey
passes over the real data and then asks the question a map edit can actually break: is the town
still ONE walkable region. Removing a stretch of road is exactly the edit that can leave a corner
of Rossville unreachable, and nothing else in the project checks for it.

AND THEN IT REFRESHES WHAT THE MAP TOOL LOOKS AT. Noir.Editor.TownExport.ForViewer writes the
per-building preview meshes and the furniture palette the house inspector draws from. Those are
the one half of that tool's data that does NOT ride TownPipeline.Build - game-interiors.json is
rewritten by every build, the mesh cache only by this method - so without a second run here the
two halves drift apart on exactly the event the design named as the thing keeping them together
(the publish button). It is a SEPARATE Unity launch because -executeMethod takes one method, and
its failure is a NOTE, never a red check: the export is a convenience for the browser, while the
smoke test is the answer to "is this map still buildable".

IT WILL NOT TOUCH A UNITY YOU HAVE OPEN. tools/watch-run.ps1 force-kills stale editors, which is
correct for a script the owner typed and completely wrong for something a button in a browser can
set off - the first time it fired while he had the editor open it would take his unsaved scene with
it. If Unity is running this refuses and says so, and the choice of what to close stays his.
"""
import os
import re
import subprocess
import sys
import threading
import time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, ".."))
LOGS = os.path.join(ROOT, "Logs")

UNITY = r"C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Unity.exe"
METHOD = "Noir.Editor.SmokeTest.Run"
EXPORT_METHOD = "Noir.Editor.TownExport.ForViewer"

#: The one running check, if any. Read by the server on /__verify; written only from here.
#: `note` is for things worth saying that are not the check's verdict - the mesh export below.
STATE = {"running": False, "ok": None, "when": None, "lines": [], "log": None, "error": None,
         "note": None}
_lock = threading.Lock()


def unity_is_open():
    """PIDs of any Unity holding the project. Empty when the coast is clear.

    tasklist rather than psutil: this has to work from a bare python with nothing installed.
    """
    try:
        out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq Unity.exe", "/FO", "CSV", "/NH"],
                             capture_output=True, text=True, timeout=20).stdout
    except Exception:
        return []
    pids = []
    for line in out.splitlines():
        m = re.match(r'"Unity\.exe","(\d+)"', line.strip())
        if m:
            pids.append(int(m.group(1)))
    return pids


#: The lines worth reading back. The smoke log is thousands of lines of asset import and licensing
#: noise around about a dozen that say what happened.
KEEP = re.compile(r"^\[(smoke|survey)\]")


def _harvest_with(log, keep):
    lines = []
    try:
        with open(log, encoding="utf-8", errors="replace") as fh:
            for raw in fh:
                s = raw.strip()
                if keep.match(s):
                    lines.append(s)
    except OSError:
        pass
    return lines


def _harvest(log):
    return _harvest_with(log, KEEP)


#: The export's own census line, the one thing worth reading back out of its log.
EXPORT_KEEP = re.compile(r"^\[export\]")


def _export(stamp):
    """Refresh the house inspector's mesh cache and furniture palette. Returns a short note for
    the browser, or None when it went through cleanly and has nothing to say.

    NEVER RAISES AND NEVER FAILS THE CHECK. The smoke test has already passed by the time this
    runs; the map is good whatever happens here, and the worst case is a tool drawing last
    build's houses - which its own timestamp line now makes visible.
    """
    log = os.path.join(LOGS, "export-%s.log" % stamp)
    open_pids = unity_is_open()
    if open_pids:
        # The editor can be reopened during the twenty minutes the smoke test takes, and a
        # second launch into a locked project fails in a way worth naming rather than guessing at.
        return ("the 3D preview cache was not refreshed: Unity was opened (pid %s) while the "
                "check was running." % ", ".join(str(p) for p in open_pids))
    try:
        p = subprocess.run(
            [UNITY, "-batchmode", "-quit", "-projectPath", ROOT,
             "-executeMethod", EXPORT_METHOD, "-logFile", log],
            capture_output=True, text=True, timeout=30 * 60)
    except subprocess.TimeoutExpired:
        return "the 3D preview cache was not refreshed: the export ran over 30 minutes."
    except Exception as e:
        return "the 3D preview cache was not refreshed: %s" % e

    said = [l.strip() for l in _harvest_with(log, EXPORT_KEEP)]
    if p.returncode != 0:
        return ("the 3D preview cache was not refreshed - Unity exited %d, see %s%s"
                % (p.returncode, os.path.basename(log),
                   (". " + said[-1]) if said else ""))
    return said[-1] if said else None


def _run(on_pass):
    stamp = time.strftime("%m%d-%H%M%S")
    log = os.path.join(LOGS, "verify-%s.log" % stamp)
    os.makedirs(LOGS, exist_ok=True)

    with _lock:
        STATE.update(running=True, ok=None, lines=[], log=log, error=None, note=None,
                     when=time.strftime("%H:%M:%S"))

    try:
        # NOT -quit here by accident: -quit is what makes an -executeMethod run exit instead of
        # sitting in the editor loop forever, which from a browser button looks like a hang.
        p = subprocess.run(
            [UNITY, "-batchmode", "-quit", "-projectPath", ROOT,
             "-executeMethod", METHOD, "-logFile", log],
            capture_output=True, text=True, timeout=30 * 60)
        lines = _harvest(log)
        passed = any("SMOKE TEST PASSED" in l for l in lines) and p.returncode == 0

        note = _export(stamp) if passed else None

        with _lock:
            STATE.update(running=False, ok=passed, lines=lines, note=note)
            if not passed and not lines:
                STATE["error"] = ("Unity exited %d and wrote nothing readable - see %s"
                                  % (p.returncode, os.path.basename(log)))
        if passed:
            on_pass()
    except subprocess.TimeoutExpired:
        with _lock:
            STATE.update(running=False, ok=False, error="the check ran over 30 minutes and was given up on")
    except Exception as e:
        with _lock:
            STATE.update(running=False, ok=False, error=str(e))


def start(on_pass):
    """Kick off a check in the background. Returns (started, why_not)."""
    with _lock:
        if STATE["running"]:
            return False, "a check is already running"
    if not os.path.exists(UNITY):
        return False, "Unity not found at %s" % UNITY
    open_pids = unity_is_open()
    if open_pids:
        return False, ("Unity is open (pid %s) and holds the project. It will not be closed for "
                       "you - close it yourself and press this again, or just press Play: the "
                       "survey passes run there too, they simply do not check the town is still "
                       "in one piece." % ", ".join(str(p) for p in open_pids))

    threading.Thread(target=_run, args=(on_pass,), daemon=True).start()
    return True, ""


def snapshot():
    with _lock:
        s = dict(STATE)
    s["log"] = os.path.basename(s["log"]) if s["log"] else None
    return s


if __name__ == "__main__":
    ok = start(lambda: None)
    if not ok[0]:
        print(ok[1])
        raise SystemExit(1)
    while snapshot()["running"]:
        time.sleep(2)
    s = snapshot()
    for l in s["lines"]:
        print("  " + l)
    if s.get("note"):
        print("  " + s["note"])
    print("PASSED" if s["ok"] else "FAILED " + (s["error"] or ""))
    raise SystemExit(0 if s["ok"] else 1)
