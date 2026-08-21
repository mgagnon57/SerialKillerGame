#!/usr/bin/env python3
"""Fetch real-world reference imagery: Street View + satellite, straight from the APIs.

The design-research half of the owner-model pipeline: when a ruling needs ground truth
("what actually stands at Perry and Chicago"), this pulls it without the browser dance.
Requires GOOGLE_MAPS_API_KEY in the environment, with these enabled on the key's project:
Street View Static API, Maps Static API, and (for address input) Geocoding API.

    python tools/streetview.py "101 Perry St, Rossville, IL" out_prefix [heading] [pitch]
    python tools/streetview.py 40.3768,-87.6678 out_prefix 160 5

Writes <out_prefix>-sv.jpg (street level, 640x640 - the standard tier's max) and
<out_prefix>-sat.png (satellite, zoom 19, scale 2). Heading is degrees clockwise from
north (default 0), pitch degrees up from level (default 0).

GOOGLE'S IMAGERY NEVER GOES IN THE REPO. Write the files to the scratchpad or another
untracked spot; they are reference, not content.
"""
import os
import sys
import urllib.parse
import urllib.request
import json


def fetch(url, path):
    with urllib.request.urlopen(url) as r:
        data = r.read()
    if len(data) < 1000:
        raise SystemExit(f"suspiciously small response ({len(data)} bytes) - "
                         f"key missing an API enablement? {data[:200]!r}")
    with open(path, 'wb') as f:
        f.write(data)
    print(f"{path}  {len(data):,} bytes")


def main():
    key = os.environ.get("GOOGLE_MAPS_API_KEY")
    if not key:
        raise SystemExit("GOOGLE_MAPS_API_KEY is not set")
    if len(sys.argv) < 3:
        raise SystemExit(__doc__)

    where, prefix = sys.argv[1], sys.argv[2]
    heading = sys.argv[3] if len(sys.argv) > 3 else "0"
    pitch = sys.argv[4] if len(sys.argv) > 4 else "0"

    # An address geocodes; a bare "lat,lng" passes through.
    parts = where.split(',')
    is_coords = len(parts) == 2 and all(
        p.strip().replace('-', '').replace('.', '').isdigit() for p in parts)
    if is_coords:
        location = where
    else:
        q = urllib.parse.quote(where)
        with urllib.request.urlopen(
                f"https://maps.googleapis.com/maps/api/geocode/json?address={q}&key={key}") as r:
            geo = json.load(r)
        if geo.get("status") != "OK":
            raise SystemExit(f"geocode failed: {geo.get('status')} {geo.get('error_message','')}")
        loc = geo["results"][0]["geometry"]["location"]
        location = f"{loc['lat']},{loc['lng']}"
        print(f"geocoded to {location}: {geo['results'][0].get('formatted_address','')}")

    loc_q = urllib.parse.quote(location)
    fetch(f"https://maps.googleapis.com/maps/api/streetview?size=640x640"
          f"&location={loc_q}&heading={heading}&pitch={pitch}&fov=90&key={key}",
          prefix + "-sv.jpg")
    fetch(f"https://maps.googleapis.com/maps/api/staticmap?center={loc_q}"
          f"&zoom=19&size=640x640&scale=2&maptype=satellite&key={key}",
          prefix + "-sat.png")


if __name__ == "__main__":
    main()
