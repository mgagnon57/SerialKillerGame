"""Exercise the rulings read/write path against a throwaway file.

Every case here is a way the real Content/parcel-1991.txt could have been destroyed.
"""
import importlib.util, os, sys, tempfile, traceback

SRC = r"C:\SerialKillerGame\tools\serve-viewer.py"
spec = importlib.util.spec_from_file_location("serve_viewer", SRC)
sv = importlib.util.module_from_spec(spec)
sys.argv = ["serve-viewer.py"]           # so _port_from_argv sees no stray flags
spec.loader.exec_module(sv)

tmpdir = tempfile.mkdtemp()
sv.VERDICTS = os.path.join(tmpdir, "parcel-1991.txt")

results = []
def check(name, fn):
    try:
        fn()
        results.append(("PASS", name, ""))
    except AssertionError as e:
        results.append(("FAIL", name, str(e)))
    except Exception as e:
        results.append(("ERROR", name, f"{type(e).__name__}: {e}"))


def missing_file_is_empty_not_an_error():
    if os.path.exists(sv.VERDICTS):
        os.remove(sv.VERDICTS)
    assert sv.read_verdicts() == {}, "a missing file should read as no rulings"


def unreadable_file_raises_rather_than_looking_empty():
    # A directory where the file should be is the portable way to make open() fail with an
    # OSError that is NOT FileNotFoundError.
    path = os.path.join(tmpdir, "as-a-dir", "parcel-1991.txt")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    os.makedirs(path, exist_ok=True)
    keep, sv.VERDICTS = sv.VERDICTS, path
    try:
        try:
            sv.read_verdicts()
        except OSError:
            return                      # correct: it refused to pretend
        raise AssertionError("an unreadable file returned normally instead of raising - "
                             "this is the bug that could rewrite the file from {}")
    finally:
        sv.VERDICTS = keep


def unknown_verbs_survive_a_round_trip():
    sv.write_verdicts({
        7: {"was": "built", "kind": "dwelling"},
        9: {"was": "built", "someFutureVerb": "keep me", "footprint": "later"},
    })
    back = sv.read_verdicts()
    assert back[9].get("someFutureVerb") == "keep me", \
        f"unknown verb was dropped: {back.get(9)}"
    assert back[9].get("footprint") == "later", "footprint was dropped"
    assert back[7].get("kind") == "dwelling", "known verb was dropped"


def a_normal_single_lot_save_works():
    v = sv.read_verdicts()
    v[11] = {"was": "vacant", "kind": "", "property": "", "note": "nothing here", "footprint": ""}
    sv.write_verdicts(v)
    back = sv.read_verdicts()
    assert back[11]["was"] == "vacant", "ordinary save did not land"
    assert back[9].get("someFutureVerb") == "keep me", "an unrelated lot lost its unknown verb"


def the_cliff_guard_refuses_a_mass_deletion():
    v = {i: {"was": "built"} for i in range(1, 41)}      # 40 ruled parcels
    sv.write_verdicts(v)
    assert sv._count_ruled_parcels() == 40, "setup failed"
    try:
        sv.write_verdicts({1: {"was": "built"}})         # would drop 39 of 40
    except RuntimeError:
        after = sv._count_ruled_parcels()
        assert after == 40, f"guard raised but the file was still damaged: {after} left"
        return
    raise AssertionError("the cliff guard did NOT fire on a 40 -> 1 write")


def the_cliff_guard_allows_an_ordinary_edit():
    v = {i: {"was": "built"} for i in range(1, 41)}
    sv.write_verdicts(v)
    v[41] = {"was": "vacant"}
    sv.write_verdicts(v)                                  # 40 -> 41, must be allowed
    assert sv._count_ruled_parcels() == 41, "an ordinary addition was blocked"
    del v[41]
    sv.write_verdicts(v)                                  # 41 -> 40, one lot cleared, allowed
    assert sv._count_ruled_parcels() == 40, "clearing one lot was blocked"


for name, fn in [
    ("missing file reads as empty, not an error", missing_file_is_empty_not_an_error),
    ("unreadable file RAISES instead of looking empty", unreadable_file_raises_rather_than_looking_empty),
    ("unknown verbs survive a round trip", unknown_verbs_survive_a_round_trip),
    ("an ordinary single-lot save works", a_normal_single_lot_save_works),
    ("the cliff guard refuses a mass deletion", the_cliff_guard_refuses_a_mass_deletion),
    ("the cliff guard allows ordinary edits", the_cliff_guard_allows_an_ordinary_edit),
]:
    check(name, fn)

width = max(len(n) for _, n, _ in results)
bad = 0
for status, name, detail in results:
    print(f"  {status:5}  {name.ljust(width)}  {detail}")
    if status != "PASS":
        bad += 1
print()
print(f"{len(results) - bad}/{len(results)} passed")
sys.exit(1 if bad else 0)
