using System.Collections.Generic;
using System.Text;
using Noir.Core.Contracts;

namespace Noir.Core.Observation
{
    /// <summary>
    /// A sighting, said out loud.
    ///
    /// WHY THIS IS THE MISSING PIECE. Everything under Noir.Core.Observation and
    /// Noir.Core.Witness has been finished, firewalled and tested for weeks, and no part of the
    /// game has ever consumed a single Sighting - the investigation layer produced evidence that
    /// nothing could read. A Sighting is six enum bands, a minute and a tile; that is the right
    /// shape to reason about and the wrong shape to show anybody. This turns one into the
    /// sentence a neighbour would actually say, which is the last thing standing between the
    /// witness engine and being playable.
    ///
    /// IT LIVES HERE, in the assembly that references Contracts and nothing else, for the same
    /// reason Sighting does: rendering testimony must not be able to look up who it was. There is
    /// no citizen, no world and no population in scope here, so the sentence can only ever be
    /// built from what the witness registered. If this had gone in Unity next to the panel that
    /// shows it, the first person wanting to write "you saw Mrs Vaughan" would have found
    /// everything needed to do it.
    ///
    /// MOST DESCRIPTIONS ARE MOSTLY UNNOTICED, and the English has to carry that honestly. A
    /// witness who registered nothing says "somebody"; one who registered a single band says
    /// "somebody tall". Padding that into a fluent sentence would be inventing evidence, which is
    /// the one thing this whole layer is built to make impossible.
    /// </summary>
    public static class Testimony
    {
        /// <summary>
        /// One sighting as a sentence. Never empty, never invented.
        ///
        /// Shaped as time, then certainty, then description - the order a person actually answers
        /// "what did you see?" in. The clock is 24-hour because the town's own clock is.
        /// </summary>
        public static string InEnglish(Sighting sighting)
        {
            var sb = new StringBuilder();

            sb.Append(Clock(sighting.MinuteOfDay));
            sb.Append(", ");
            sb.Append(Saw(sighting.Clarity));
            sb.Append(' ');
            sb.Append(Figure(sighting.Description));
            sb.Append('.');
            return sb.ToString();
        }

        /// <summary>Several sightings, in the order they happened, one line each.</summary>
        public static string[] InEnglish(IReadOnlyList<Sighting> sightings)
        {
            if (sightings == null || sightings.Count == 0) return System.Array.Empty<string>();

            var lines = new string[sightings.Count];
            for (int i = 0; i < sightings.Count; i++) lines[i] = InEnglish(sightings[i]);
            return lines;
        }

        /// <summary>An event, said out loud. Same shape as a person sighting: time, then
        /// certainty, then what was seen — and the certainty verb carries the hedge.</summary>
        public static string InEnglish(EventSighting e)
        {
            var sb = new StringBuilder();
            sb.Append(Clock(e.MinuteOfDay));
            sb.Append(", ");
            sb.Append(e.Clarity switch
            {
                SightingClarity.Clear => "I watched",
                SightingClarity.Partial => "I saw",
                _ => "I think I saw",
            });
            sb.Append(' ');
            sb.Append(e.Act == EventAct.CarStruckSomebody || e.Act == EventAct.CarsCollided
                ? Car(e.Car) : "somebody");
            sb.Append(e.Act switch
            {
                EventAct.CarStruckSomebody      => " hit somebody",
                EventAct.SomebodyAskedQuestions => " going around asking questions",
                EventAct.CarsCollided           => " and another car come together",
                _                               => " do something",
            });
            sb.Append('.');
            return sb.ToString();
        }

        /// <summary>"a dark pickup", "an ordinary-looking van", "a car". Blank is still a car —
        /// the witness saw the event; it is the DESCRIPTION that failed to register. Mid is NOT
        /// the same silence as Unnoticed: a witness who registered Mid looked at the car and
        /// found nothing about its colour worth remarking on, which is itself a fact — the same
        /// distinction Clothing(Mid) and Build(Average) already draw for a person.</summary>
        private static string Car(CarDescription c)
        {
            string tone = c.Tone switch
            {
                CarTone.Dark => "dark ",
                CarTone.Light => "light-coloured ",
                CarTone.Mid => "ordinary-looking ",
                _ => "",
            };
            string shape = c.Shape switch
            {
                CarShape.Pickup => "pickup", CarShape.Van => "van", _ => "car",
            };
            return Article(tone.Length > 0 ? tone : shape) + " " + tone + shape;
        }

        /// <summary>
        /// What somebody says when they saw nothing at all.
        ///
        /// A SENTENCE RATHER THAN AN EMPTY LIST, because "no sightings" and "I was not asked" look
        /// identical to a caller holding an empty array, and to a player they are completely
        /// different answers. An alibi is evidence too.
        /// </summary>
        public const string SawNothing = "Nothing. I never saw anybody.";

        private static string Clock(int minuteOfDay)
        {
            int h = minuteOfDay / 60, m = minuteOfDay % 60;
            return h.ToString("00") + ":" + m.ToString("00");
        }

        /// <summary>
        /// The verb carries the certainty, which is how people actually hedge - nobody says "I
        /// saw, with low clarity, a man". They say they caught sight of somebody.
        /// </summary>
        private static string Saw(SightingClarity clarity) => clarity switch
        {
            SightingClarity.Clear => "I got a good look at",
            SightingClarity.Partial => "I saw",
            _ => "I caught sight of",
        };

        private static string Figure(PersonDescription d)
        {
            // "somebody" and not "a person": it is what a witness says, and it stays true when
            // every band below is Unnoticed, which is the common case rather than the edge one.
            if (d.IsBlank) return "somebody";

            var before = new List<string>();          // adjectives, in front of the noun
            var after = new List<string>();           // clauses, behind it

            if (d.Age != AgeBand.Unnoticed) before.Add(Age(d.Age));
            if (d.Height != HeightBand.Unnoticed) before.Add(Height(d.Height));
            if (d.Build != BuildBand.Unnoticed) before.Add(Build(d.Build));

            string noun = d.Sex switch
            {
                ApparentSex.Man => "man",
                ApparentSex.Woman => "woman",
                _ => "somebody",
            };

            // "a tall somebody" is not English. When the sex did not register, the adjectives
            // move behind: "somebody tall, heavy-set".
            bool named = d.Sex != ApparentSex.Unnoticed;

            if (d.Clothing != ClothingTone.Unnoticed) after.Add("in " + Clothing(d.Clothing));
            if (d.Carrying != CarriedThing.Unnoticed) after.Add(Carrying(d.Carrying));

            var sb = new StringBuilder();
            if (named)
            {
                sb.Append(Article(before.Count > 0 ? before[0] : noun)).Append(' ');
                foreach (string word in before) sb.Append(word).Append(' ');
                sb.Append(noun);
            }
            else
            {
                sb.Append(noun);
                if (before.Count > 0) sb.Append(' ').Append(string.Join(", ", before));
            }

            for (int i = 0; i < after.Count; i++) sb.Append(i == 0 ? ", " : ", ").Append(after[i]);
            return sb.ToString();
        }

        /// <summary>"a" or "an", decided by the word that actually follows it.</summary>
        private static string Article(string next) =>
            next.Length > 0 && "aeiou".IndexOf(char.ToLowerInvariant(next[0])) >= 0 ? "an" : "a";

        private static string Age(AgeBand a) => a switch
        {
            AgeBand.Child => "young",
            AgeBand.Young => "youngish",
            AgeBand.MiddleAged => "middle-aged",
            AgeBand.Old => "elderly",
            _ => "",
        };

        private static string Height(HeightBand h) => h switch
        {
            HeightBand.Short => "short",
            HeightBand.Average => "average-height",
            HeightBand.Tall => "tall",
            _ => "",
        };

        private static string Build(BuildBand b) => b switch
        {
            BuildBand.Slight => "slight",
            BuildBand.Average => "ordinary-build",
            BuildBand.Heavy => "heavy-set",
            _ => "",
        };

        /// <summary>Tone, never colour - see ClothingTone. A witness who says "blue" is guessing.</summary>
        private static string Clothing(ClothingTone c) => c switch
        {
            ClothingTone.Dark => "something dark",
            ClothingTone.Mid => "nothing that stood out",
            ClothingTone.Light => "something light",
            _ => "",
        };

        private static string Carrying(CarriedThing t) => t switch
        {
            CarriedThing.NothingSeen => "empty-handed",
            CarriedThing.Bag => "carrying a bag",
            CarriedThing.Case => "carrying a case",
            CarriedThing.Bundle => "carrying a bundle",
            CarriedThing.LongObject => "carrying something long",
            _ => "",
        };
    }
}
