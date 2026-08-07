"""Run the game's own check over the authored map, and report what it found.

WHY THIS IS SEPARATE from serve-viewer.py. The server answers in milliseconds; this takes minutes
and holds the Unity project while it does. Mixing them would mean a page that hangs on Save because
a check happened to be running.

WHAT IT RUNS is Noir.Editor.SmokeTest.Run, which is the only thing that exercises the four survey
passes over the real data and then asks the question a map edit can actually break: is the town
still ONE walkable region. Removing a stretch of road is exactly the edit that can leave a corner
of Rossville unreachable, and nothing else in the project checks for it.

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

#: The one running check, if any. Read by the server on /__verify; written only from here.
STATE = {"running": False, "ok": None, "when": None, "lines": [], "log": None, "error": None}
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


def _harvest(log):
    lines = []
    try:
        with open(log, encoding="utf-8", errors="replace") as fh:
            for raw in fh:
                s = raw.strip()
                if KEEP.match(s):
                    lines.append(s)
    except OSError:
        pass
    return lines


def _run(on_pass):
    stamp = time.strftime("%m%d-%H%M%S")
    log = os.path.join(LOGS, "verify-%s.log" % stamp)
    os.makedirs(LOGS, exist_ok=True)

    with _lock:
        STATE.update(running=True, ok=None, lines=[], log=log, error=None,
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

        with _lock:
            STATE.update(running=False, ok=passed, lines=lines)
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
    print("PASSED" if s["ok"] else "FAILED " + (s["error"] or ""))
    raise SystemExit(0 if s["ok"] else 1)
