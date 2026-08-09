using System;
using System.Text;

// ---------------------------------------------------------------------------------------------
//  LIVE. THE GAME PRODUCES THESE AND PRINTS THEM — corrected 2026-08-08.
//
//  This block used to open "STAGED, NOT LIVE. NOTHING CONSTRUCTS A PersonDescription", dated
//  2026-07-29 and true on that date. It went on saying so after it stopped being true, and a
//  confident header is believed rather than checked: it has already sent one auditor to a wrong
//  finding about dead code.
//
//  Witness/Degradation.cs fills these bands, Witness/Recollection.cs puts them into Sightings,
//  and VillageHost.AskWhatTheySaw hands the sentences to VillageUI — press T on a neighbour. So
//  "every band declared below is unreachable" is false. Measure before deleting anything here
//  for being unused.
//
//  The firewall in Sighting.cs's header is the constraint this type is shaped by, and that part
//  was always true and still is.
// ---------------------------------------------------------------------------------------------

namespace Noir.Core.Observation
{
    // Every band below has Unnoticed = 0, so `default(PersonDescription)` is "somebody was
    // there and I couldn't tell you a thing about them" — which is the most common witness
    // statement there is, and the safest default for a type that must never over-claim.
    //
    // Bands, not values. A witness does not report 178 cm or citizen #47; they report "tall".
    // Every band is wide enough to hold a good fraction of the village, which is what stops a
    // description being an identity. That property is a matter of tuning, not of type safety:
    // the assembly boundary guarantees a description cannot *name* anyone, and the coarseness
    // here decides how many people it narrows to. If a future band makes a single villager
    // uniquely describable, that is a design bug in the band, and it belongs in this file.

    /// <summary>What the observer took the figure to be. Wrong surprisingly often at night.</summary>
    public enum ApparentSex : byte
    {
        Unnoticed = 0,
        Man = 1,
        Woman = 2,
    }

    public enum HeightBand : byte
    {
        Unnoticed = 0,
        Short = 1,
        Average = 2,
        Tall = 3,
    }

    public enum BuildBand : byte
    {
        Unnoticed = 0,
        Slight = 1,
        Average = 2,
        Heavy = 3,
    }

    /// <summary>Apparent age, guessed from a distance. Four bands is already generous.</summary>
    public enum AgeBand : byte
    {
        Unnoticed = 0,
        Child = 1,
        Young = 2,
        MiddleAged = 3,
        Old = 4,
    }

    /// <summary>
    /// Tone rather than colour. Under sodium light and at 1979 street-lighting levels, colour
    /// is the first thing a witness gets wrong and the first thing they will swear to.
    /// </summary>
    public enum ClothingTone : byte
    {
        Unnoticed = 0,
        Dark = 1,
        Mid = 2,
        Light = 3,
    }

    /// <summary>
    /// What they had with them. Cheap to notice and disproportionately useful, because it
    /// describes the *purpose* of a journey without describing the person — the same reason
    /// carried items are worth rendering at all.
    /// </summary>
    public enum CarriedThing : byte
    {
        Unnoticed = 0,
        NothingSeen = 1,    // hands seen, and empty. Not the same as not having looked.
        Bag = 2,
        Case = 3,
        Bundle = 4,
        LongObject = 5,
    }

    /// <summary>
    /// A description of a figure, as a witness could give it.
    ///
    /// This type deliberately cannot say who it was. There is no subject id, no reference to a
    /// person, and no way to get one from here — the assembly this lives in cannot see the types
    /// that would let it. Matching a description to a villager is the investigation's job, it is
    /// probabilistic, and it is allowed to be wrong. That is the entire game.
    ///
    /// Most descriptions are mostly Unnoticed. A figure crossing a lane at eleven at night is
    /// "somebody, tallish, dark coat" and nothing more; the fact that this reads as sparse is
    /// the type being accurate rather than the type being unfinished.
    /// </summary>
    public readonly struct PersonDescription : IEquatable<PersonDescription>
    {
        public readonly ApparentSex Sex;
        public readonly HeightBand Height;
        public readonly BuildBand Build;
        public readonly AgeBand Age;
        public readonly ClothingTone Clothing;
        public readonly CarriedThing Carrying;

        public PersonDescription(ApparentSex sex, HeightBand height, BuildBand build,
                                 AgeBand age, ClothingTone clothing, CarriedThing carrying)
        {
            Sex = sex;
            Height = height;
            Build = build;
            Age = age;
            Clothing = clothing;
            Carrying = carrying;
        }

        /// <summary>Somebody was there. Nothing about them registered.</summary>
        public static readonly PersonDescription Nothing = default;

        /// <summary>How many bands the observer actually registered, 0..6.</summary>
        public int NoticedCount
        {
            get
            {
                int n = 0;
                if (Sex != ApparentSex.Unnoticed) n++;
                if (Height != HeightBand.Unnoticed) n++;
                if (Build != BuildBand.Unnoticed) n++;
                if (Age != AgeBand.Unnoticed) n++;
                if (Clothing != ClothingTone.Unnoticed) n++;
                if (Carrying != CarriedThing.Unnoticed) n++;
                return n;
            }
        }

        public bool IsBlank => NoticedCount == 0;

        public bool Equals(PersonDescription other) =>
            Sex == other.Sex && Height == other.Height && Build == other.Build &&
            Age == other.Age && Clothing == other.Clothing && Carrying == other.Carrying;

        public override bool Equals(object obj) => obj is PersonDescription o && Equals(o);

        public override int GetHashCode() =>
            (int)Sex | ((int)Height << 3) | ((int)Build << 6) | ((int)Age << 9)
                     | ((int)Clothing << 12) | ((int)Carrying << 15);

        /// <summary>
        /// The statement as prose, for the inspector and the eventual case board.
        ///
        /// Bands the witness registered as unremarkable are left out, the way a real statement
        /// leaves them out — nobody says "of average height" unless pressed. Read the fields,
        /// not this string, for anything that has to be exact.
        /// </summary>
        public override string ToString()
        {
            var sb = new StringBuilder();

            AppendWord(sb, Height == HeightBand.Short ? "short" : Height == HeightBand.Tall ? "tall" : null);
            AppendWord(sb, Build == BuildBand.Slight ? "slight" : Build == BuildBand.Heavy ? "heavy" : null);
            AppendWord(sb, Age == AgeBand.Young ? "young"
                         : Age == AgeBand.MiddleAged ? "middle-aged"
                         : Age == AgeBand.Old ? "elderly" : null);
            AppendWord(sb, Noun());

            if (Clothing != ClothingTone.Unnoticed)
            {
                sb.Append(" in ");
                sb.Append(Clothing == ClothingTone.Dark ? "dark" : Clothing == ClothingTone.Light ? "light" : "mid-toned");
                sb.Append(" clothing");
            }

            switch (Carrying)
            {
                case CarriedThing.NothingSeen: sb.Append(", empty-handed"); break;
                case CarriedThing.Bag: sb.Append(", carrying a bag"); break;
                case CarriedThing.Case: sb.Append(", carrying a case"); break;
                case CarriedThing.Bundle: sb.Append(", carrying a bundle"); break;
                case CarriedThing.LongObject: sb.Append(", carrying something long"); break;
            }

            return sb.ToString();
        }

        private string Noun()
        {
            if (Age == AgeBand.Child)
                return Sex == ApparentSex.Man ? "boy" : Sex == ApparentSex.Woman ? "girl" : "child";
            return Sex == ApparentSex.Man ? "man" : Sex == ApparentSex.Woman ? "woman" : "figure";
        }

        private static void AppendWord(StringBuilder sb, string word)
        {
            if (word == null) return;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(word);
        }
    }
}
