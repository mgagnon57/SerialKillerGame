using NUnit.Framework;
using Noir.Core.World;

namespace Noir.Core.Tests
{
    /// <summary>
    /// The word-match from an authored room's name to the RoomKind the sim understands.
    /// The table is deliberately small: the enum's own header forbids synonym kinds, so
    /// "dining" and "family room" both furnish as the front room.
    /// </summary>
    public class RoomWordsTests
    {
        [TestCase("Bedroom 1", RoomKind.Bedroom)]
        [TestCase("bed 2", RoomKind.Bedroom)]
        [TestCase("Kitchen + dining", RoomKind.Kitchen)]
        [TestCase("Bath 2 (addition)", RoomKind.Bathroom)]
        [TestCase("Half bath", RoomKind.Bathroom)]
        [TestCase("Living room", RoomKind.Living)]
        [TestCase("Family room", RoomKind.Living)]
        [TestCase("Dining / entry", RoomKind.Living)]
        [TestCase("Hall", RoomKind.Hall)]
        [TestCase("Back hall", RoomKind.Hall)]
        [TestCase("Laundry", RoomKind.Scullery)]
        [TestCase("utility", RoomKind.Scullery)]
        [TestCase("Office", RoomKind.Workroom)]
        [TestCase("Sun porch", RoomKind.Living)]      // unmatched -> the front-room default
        public void NamesResolveToTheKindsTheSimKnows(string name, RoomKind expected)
            => Assert.That(RoomWords.KindFor(name), Is.EqualTo(expected));
    }
}
