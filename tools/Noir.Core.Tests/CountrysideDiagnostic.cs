using System;
using NUnit.Framework;
using Noir.Core.Contracts;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// Prints a year of the countryside as text. NOT AN ASSERTION - it is a way of looking at the
    /// thing, which is the only way some faults ever get found here. `CommercialRow`'s infill was
    /// laid under the lodge halls with all fourteen of its tests green, and `ResidentialLots`
    /// ignored edgeness entirely with all fourteen of its tests green. Both were caught by
    /// printing a row and reading it.
    ///
    /// [Explicit] so it stays out of normal runs. To look:
    ///
    ///     dotnet test -c Release tools/Noir.Core.Tests --filter "Name=PrintTheCropYear" ^
    ///                 -l "console;verbosity=detailed"
    ///
    /// What to look for, all of it claimed by THE-YEAR.md and WHO-SEES-WHOM.md:
    ///   - January, March and December bare, 0% opaque
    ///   - June open - the crops are in but nothing is tall enough to hide behind
    ///   - the first W in early July, and about 43% of the map opaque through August
    ///   - around 10 September, g against W: gold beans beside still-standing corn, before a
    ///     single field has been cut
    ///   - October a chessboard, and the dusk column losing an hour and three quarters across it
    /// </summary>
    [TestFixture, Explicit("Diagnostic - prints the crop year, asserts nothing")]
    public class CountrysideDiagnostic
    {
        [Test]
        public void PrintTheCropYear()
        {
            Console.WriteLine("  . beans stubble   , corn stubble   # tilled   - seedling");
            Console.WriteLine("  i growing, see over    I growing, blocks    o beans full    W CORN WALL");
            Console.WriteLine("  g beans gold      Y corn browning\n");
            Console.WriteLine("            " + new string('-', 14) + " 48 forties across the county " +
                              new string('-', 5));

            var dates = new[] { (1, 15), (3, 20), (4, 15), (4, 28), (5, 10), (5, 25), (6, 10), (6, 25),
                                (7, 5), (7, 20), (8, 10), (9, 1), (9, 10), (9, 20), (10, 1), (10, 10),
                                (10, 20), (11, 1), (11, 15), (12, 15) };

            foreach (var (month, day) in dates)
            {
                var t = new GameClock(GameClock.TickOn(1991, month, day, 12 * 60));
                var row = new char[48];
                int blocked = 0;

                for (int i = 0; i < row.Length; i++)
                {
                    var field = Fields.At(i * Fields.FortyAcres + 5f, 1500f, t.Year, t.DayOfYear);
                    row[i] = Symbol(field);
                    if (field.BlocksSightline) blocked++;
                }

                int dusk = Daylight.Dusk(t.Month, t.DayOfMonth) + (t.IsDaylightSaving ? 60 : 0);
                Console.WriteLine($"{t.DayOfMonth:00} {GameClock.MonthNames[t.Month - 1]}  {new string(row)}" +
                                  $"  {blocked * 100 / row.Length,3}% opaque   dark by {dusk / 60:00}:{dusk % 60:00}");
            }
        }

        private static char Symbol(FieldCondition c)
        {
            switch (c.State)
            {
                case FieldState.Stubble: return c.Crop == Crop.Corn ? ',' : '.';
                case FieldState.Tilled: return '#';
                case FieldState.Seedling: return '-';
                case FieldState.Growing: return c.BlocksSightline ? 'I' : 'i';
                case FieldState.Standing: return c.Crop == Crop.Corn ? 'W' : 'o';
                case FieldState.Turning: return c.Crop == Crop.Corn ? 'Y' : 'g';
                default: return '?';
            }
        }
    }
}
