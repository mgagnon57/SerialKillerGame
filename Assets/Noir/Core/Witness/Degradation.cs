using Noir.Core.Contracts;
using Noir.Core.Observation;

namespace Noir.Core.Witness
{
    /// <summary>
    /// What actually stuck. The narrowing this whole assembly exists to perform.
    ///
    /// Everything above this call knows a height in centimetres and a person's real sex. What
    /// comes out is bands, most of them Unnoticed, and there is no way back. The bands are wide
    /// on purpose — see the header of PersonDescription — because a description that narrows to
    /// one villager is an identity with extra steps.
    ///
    /// EVERY ROLL IS SEEDED ON THE WITNESS AND THE MINUTE, never on a running RNG. Ask the same
    /// question twice and you get the same answer, which is what separates a fallible witness
    /// from a slot machine the player pulls until the answer suits them. It also means testimony
    /// needs no storage at all: the memory IS the seed.
    /// </summary>
    public static class Degradation
    {
        private static readonly ulong Purpose = Rolls.Purpose("witness.degradation");

        /// <summary>How many of the six bands survive, by how good the look was.</summary>
        private static int BandsThatSurvive(SightingClarity clarity) =>
            clarity == SightingClarity.Clear ? 5 : clarity == SightingClarity.Partial ? 3 : 1;

        public static PersonDescription WhatRegistered(
            SightingClarity clarity, Visibly looked,
            bool subjectIsMale, int subjectAge, int heightCm, int buildIndex,
            CitizenKey witness, int minute, ulong seed)
        {
            int surviving = BandsThatSurvive(clarity);

            // Which bands stuck. A seeded shuffle of the six, then take the first N — so two
            // people who got an equally good look still remember different halves of it, which
            // is the whole reason multiple witnesses are worth interviewing.
            var order = new int[] { 0, 1, 2, 3, 4, 5 };
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = Rolls.Int(seed, Purpose, witness.Value, minute, (ulong)i, 0, i + 1);
                int swap = order[i]; order[i] = order[j]; order[j] = swap;
            }

            var sex = ApparentSex.Unnoticed;
            var height = HeightBand.Unnoticed;
            var build = BuildBand.Unnoticed;
            var age = AgeBand.Unnoticed;
            var clothing = ClothingTone.Unnoticed;
            var carrying = CarriedThing.Unnoticed;

            for (int i = 0; i < surviving; i++)
            {
                switch (order[i])
                {
                    case 0: sex = ApparentSexOf(subjectIsMale, clarity, witness, minute, seed); break;
                    case 1: height = HeightBandOf(heightCm); break;
                    case 2: build = BuildBandOf(buildIndex); break;
                    case 3: age = AgeBandOf(subjectAge); break;

                    // Tone rather than colour, and rolled rather than read: nothing upstream
                    // models what the player is wearing yet. When it does, pass it in — the
                    // signature is where that belongs, not a lookup from in here.
                    case 4:
                        clothing = (ClothingTone)(1 + Rolls.Int(seed, Purpose, witness.Value,
                                                                minute, 0xC10DUL, 0, 3));
                        break;

                    case 5: carrying = CarriedOf(looked); break;
                }
            }

            return new PersonDescription(sex, height, build, age, clothing, carrying);
        }

        /// <summary>
        /// Wrong 1 in 5 at a glimpse, 1 in 12 at partial, never in a clear look. A coat and a bad
        /// street lamp is all it takes, and the enum's own comment promises this happens.
        /// </summary>
        private static ApparentSex ApparentSexOf(bool male, SightingClarity clarity,
                                                 CitizenKey witness, int minute, ulong seed)
        {
            int oddsAgainst = clarity == SightingClarity.Clear ? 0
                            : clarity == SightingClarity.Partial ? 12 : 5;

            bool mistaken = oddsAgainst > 0 &&
                Rolls.Int(seed, Purpose, witness.Value, minute, 0x5E11UL, 0, oddsAgainst) == 0;

            bool reported = mistaken ? !male : male;
            return reported ? ApparentSex.Man : ApparentSex.Woman;
        }

        private static HeightBand HeightBandOf(int cm) =>
            cm < 165 ? HeightBand.Short : cm > 180 ? HeightBand.Tall : HeightBand.Average;

        private static BuildBand BuildBandOf(int index) =>
            index <= 0 ? BuildBand.Slight : index >= 2 ? BuildBand.Heavy : BuildBand.Average;

        private static AgeBand AgeBandOf(int years) =>
            years < 16 ? AgeBand.Child : years < 30 ? AgeBand.Young
                                       : years < 60 ? AgeBand.MiddleAged : AgeBand.Old;

        /// <summary>
        /// NothingSeen is not the same as not having looked, so this is only ever reached when
        /// the carrying band survived — an absence somebody checked.
        /// </summary>
        private static CarriedThing CarriedOf(Visibly looked) =>
            (looked & Visibly.Carrying) != 0 ? CarriedThing.Bag : CarriedThing.NothingSeen;
    }
}
