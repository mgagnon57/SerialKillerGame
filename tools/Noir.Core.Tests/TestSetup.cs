using NUnit.Framework;

namespace Noir.Core.Tests
{
    /// <summary>
    /// Installs the kinds table once, before any test in the assembly runs.
    ///
    /// Core cannot read a file, so somebody has to hand it the table, and until this existed
    /// each fixture that needed it called EnsureKinds itself. That works right up until a fixture
    /// forgets — and then it passes or fails depending on which OTHER fixture NUnit happened to
    /// run first, which is the worst kind of test failure: real, intermittent, and nothing to do
    /// with the code under test.
    ///
    /// A SetUpFixture in the global namespace would cover everything; this one is scoped to the
    /// project's own namespace, which is every test there is.
    /// </summary>
    [SetUpFixture]
    public sealed class TestSetup
    {
        [OneTimeSetUp]
        public void InstallTheKindsTable() => TestContent.EnsureKinds();
    }
}
