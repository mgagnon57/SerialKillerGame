using System.Collections.Generic;

namespace Noir.Unity
{
    /// <summary>
    /// A real, confirmed address for a parcel - from Vermilion County's own tax assessment
    /// records, not a guess. See Content/parcel-addresses.txt for how these were matched and
    /// tools/rossville-property-records.json for the raw county data behind them.
    ///
    /// Read-only, unlike ParcelNotes: this is county data, not something the game or the player
    /// authors. Keyed the same way - the parcel's line number in Content/parcels.txt - because
    /// most of the county's own addressed lots never got a generated Place either.
    /// </summary>
    public static class ParcelAddresses
    {
        public readonly struct Entry
        {
            /// <summary>The parcel identification number, always present when there is any
            /// county record at all - even the 199 unaddressed lots have one.</summary>
            public readonly string Pin;

            /// <summary>The real situs address, or null if the county has never addressed this
            /// lot (vacant land, farmland, right-of-way). Null is the confirmed answer, not a
            /// missing one - see StreetAddressing.Estimate for the fallback guess used only
            /// where even this file has nothing on file.</summary>
            public readonly string Address;

            public Entry(string pin, string address) { Pin = pin; Address = address; }
        }

        private static Dictionary<int, Entry> _byId;

        public static Entry? For(int parcelId)
        {
            Load();
            return _byId.TryGetValue(parcelId, out var e) ? e : (Entry?)null;
        }

        private static void Load()
        {
            if (_byId != null) return;
            _byId = new Dictionary<int, Entry>();

            string text;
            try { text = ContentLoader.Read("parcel-addresses.txt"); }
            catch { return; }

            var pins = new Dictionary<int, string>();
            var addresses = new Dictionary<int, string>();

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                var parts = line.Split(new[] { ' ' }, 3);
                if (parts.Length < 3 || parts[0] != "parcel") continue;
                if (!int.TryParse(parts[1], out int id)) continue;

                var rest = parts[2];
                if (rest.StartsWith("pin "))
                    pins[id] = Unquote(rest.Substring(4));
                else if (rest.StartsWith("address "))
                    addresses[id] = Unquote(rest.Substring(8));
            }

            foreach (var kv in pins)
            {
                addresses.TryGetValue(kv.Key, out string address);
                _byId[kv.Key] = new Entry(kv.Value, address);
            }
        }

        private static string Unquote(string s)
        {
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') s = s.Substring(1, s.Length - 2);
            return s.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
    }
}
