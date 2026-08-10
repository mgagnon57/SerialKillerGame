using System.Collections.Generic;
using System.Globalization;

namespace Noir.Unity
{
    /// <summary>
    /// What Vermilion County knows about a parcel: its address, its parcel number, what the
    /// assessor classes it as, who occupies it and what it is assessed at. Read from
    /// Content/parcel-county.txt, which holds ONLY what the county literally records.
    ///
    /// Everything interpreted rather than recorded lives here rather than in the file - the
    /// zoning a class code implies, whether a building stands on the lot, roughly what it is
    /// worth - so the interpretation can be corrected without the source data being re-fetched.
    ///
    /// Read-only, unlike ParcelNotes: this is the county's data, not the author's. Where both
    /// have an opinion the author wins; see VillageUI, which shows the county's answer as the
    /// starting value and lets it be overridden.
    ///
    /// NO OWNER NAMES, AND THAT IS DELIBERATE - see the header of Content/parcel-county.txt for
    /// why a real name at a real address is not a thing this project will carry.
    /// </summary>
    public static class CountyRecord
    {
        public sealed class Entry
        {
            /// <summary>The county's parcel identification number. Present whenever there is any
            /// record at all, including for the lots that have never been addressed.</summary>
            public string Pin;

            /// <summary>The real situs address, or null where the county has never addressed
            /// this lot. Null is the confirmed ANSWER - vacant land, farm ground, right-of-way -
            /// not a missing value.</summary>
            public string Address;

            /// <summary>The assessor's own class code, and the assessor's own wording for it.
            /// Kept verbatim so the derivation below can be audited against the source.</summary>
            public string ClassCode, ClassName;

            public Occupancy Occupied;

            /// <summary>The county's over-65 exemption is claimed on this lot. A fact about the
            /// OWNER, and used only as a RATE when generating household ages - never to identify
            /// anybody, which is not possible from this file anyway.</summary>
            public bool Over65;

            /// <summary>Assessed value in dollars, split the way the county splits it. Illinois
            /// assesses at one third of market value.</summary>
            public int HomesiteValue, DwellingValue;

            public float Acres;

            /// <summary>Something is built on this lot: the county assesses a dwelling on it.
            /// The truest "is this an empty lot" test the public record offers.</summary>
            public bool HasBuilding => DwellingValue > 0;

            /// <summary>Roughly what the structure would fetch, in dollars - assessed value is
            /// one third of market by Illinois statute. The best proxy the public record gives
            /// for how big and how good a house is: this county publishes no square footage,
            /// bedroom or bathroom figures anywhere online.</summary>
            public int MarketValue => DwellingValue * 3;

            /// <summary>What the assessor's class code implies about how the lot is used. The
            /// county does not zone in these words - this is a reading of its classes, and the
            /// author can overrule it on any parcel.</summary>
            public ParcelNotes.Zoning Zoning
            {
                get
                {
                    switch (ClassCode)
                    {
                        case "0011":            // Homesite-Dwelling
                        case "0040":            // Improved Residential Lot
                        case "0050":            // Commercial >6 Units - assessed commercial in
                                                // Illinois, but it is where people live
                            return ParcelNotes.Zoning.Residential;
                        case "0021": return ParcelNotes.Zoning.Agricultural;
                        case "0030":            // Vac Lots-Lands/6 units
                        case "0032": return ParcelNotes.Zoning.Vacant;
                        case "0060": return ParcelNotes.Zoning.Commercial;
                        case "0080":            // Industrial
                        case "5060": return ParcelNotes.Zoning.Industrial;   // Railroad - Leased
                        case "0090": return ParcelNotes.Zoning.Civic;        // Tax Exempt
                        default: return ParcelNotes.Zoning.Unset;
                    }
                }
            }

            /// <summary>Six units or more, assessed as commercial - the town's apartment
            /// buildings. The only housing form the public record names outright.</summary>
            public ParcelNotes.HousingType Housing =>
                ClassCode == "0050" ? ParcelNotes.HousingType.ApartmentComplex
                                    : ParcelNotes.HousingType.Unset;
        }

        /// <summary>Whether the owner lives on the lot. `Absentee` means the tax bill goes
        /// somewhere other than the property, which is what a rented house looks like in the
        /// record - the mailing address itself is never stored.</summary>
        public enum Occupancy { Unknown, Owner, Absentee, Other }

        private static Dictionary<int, Entry> _byId;

        public static Entry For(int parcelId)
        {
            Load();
            return _byId.TryGetValue(parcelId, out var e) ? e : null;
        }

        public static IReadOnlyDictionary<int, Entry> All { get { Load(); return _byId; } }

        private static void Load()
        {
            if (_byId != null) return;
            _byId = new Dictionary<int, Entry>();

            // Same class as elevation.txt, one file over: 4,534 lines of the county's own zoning.
            // Unread, every parcel is `Unset`, so commercial, industrial and vacant ground all
            // draw as the fiction's own answer and GroundZoning's whole decision disappears
            // without a word. Survivable - the town builds - and worth a sentence.
            string text;
            try { text = ContentLoader.Read("parcel-county.txt"); }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning("[county] parcel-county.txt did not load, so NO PARCEL HAS A "
                    + "ZONING - every lot draws as whatever the map fiction says. " + ex.Message);
                return;
            }

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                var parts = line.Split(new[] { ' ' }, 3);
                if (parts.Length < 3 || parts[0] != "parcel") continue;
                if (!int.TryParse(parts[1], out int id)) continue;

                if (!_byId.TryGetValue(id, out var entry)) _byId[id] = entry = new Entry();
                var rest = parts[2];

                if (rest.StartsWith("pin "))
                    entry.Pin = Unquote(rest.Substring(4));
                else if (rest.StartsWith("address "))
                    entry.Address = Unquote(rest.Substring(8));
                else if (rest.StartsWith("class "))
                {
                    var body = rest.Substring(6);
                    int space = body.IndexOf(' ');
                    if (space > 0)
                    {
                        entry.ClassCode = body.Substring(0, space);
                        entry.ClassName = Unquote(body.Substring(space + 1));
                    }
                }
                else if (rest.StartsWith("occupancy "))
                {
                    switch (rest.Substring(10).Trim())
                    {
                        case "owner": entry.Occupied = Occupancy.Owner; break;
                        case "absentee": entry.Occupied = Occupancy.Absentee; break;
                        default: entry.Occupied = Occupancy.Other; break;
                    }
                }
                else if (rest == "over65")
                    entry.Over65 = true;
                else if (rest.StartsWith("assessed "))
                {
                    var nums = rest.Substring(9).Split(' ');
                    if (nums.Length >= 2 && int.TryParse(nums[0], out int home)
                                         && int.TryParse(nums[1], out int dwell))
                    {
                        entry.HomesiteValue = home;
                        entry.DwellingValue = dwell;
                    }
                }
                else if (rest.StartsWith("acres "))
                {
                    if (float.TryParse(rest.Substring(6), NumberStyles.Float,
                                       CultureInfo.InvariantCulture, out float acres))
                        entry.Acres = acres;
                }
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
