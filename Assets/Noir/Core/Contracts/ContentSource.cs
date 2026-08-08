using System;

namespace Noir.Core.Contracts
{
    /// <summary>
    /// Where the Content files come from, asked for rather than reached for.
    ///
    /// WHY THIS EXISTS. Twenty-six files under Assets/Noir/Unity are pure domain logic - they
    /// parse text and do plane geometry and touch no engine type heavier than Vector2 - and every
    /// one of them is stranded in the Unity assembly, where `dotnet test` structurally cannot
    /// compile them. That is roughly 5,000 lines with no automated cover at all, including the
    /// parser for Content/parcel-1991.txt, the one file in this project that nothing can rebuild.
    ///
    /// The thing pinning them there was not Vector2. It was ContentLoader, which resolves its path
    /// from Application.dataPath and so drags UnityEngine in behind it. Twelve of the twenty-six
    /// depend on it and on nothing else that matters.
    ///
    /// So: the same seam this project already uses everywhere else. PlaceKindTable does not open
    /// files - VillageHost does, and hands it the text. TechnologyTable is the same, and so is
    /// VillageParser. This is that pattern, with the reading kept behind an interface so a test
    /// can supply the text directly and the survey layer can finally be run without an editor.
    /// </summary>
    public interface IContentSource
    {
        /// <summary>The whole file, by its bare name - "city.txt", "parcel-1991.txt".</summary>
        string Read(string fileName);

        /// <summary>
        /// When it was last written, for the callers that reparse on change.
        ///
        /// Several survey tables are edited by tools and by the browser map WHILE the editor is
        /// open, and reparse when the stamp moves. That behaviour is load-bearing, so it belongs
        /// in the seam rather than being lost in the move. Returns default(DateTime) if absent.
        /// </summary>
        DateTime WrittenAt(string fileName);
    }

    /// <summary>
    /// The one installed content source.
    ///
    /// A static installer rather than constructor injection because that is what this codebase
    /// already does - see PlaceKindTable.Install and TechnologyTable.Install - and because the
    /// survey tables are static caches by design: they are read from everywhere, hold one parse
    /// of one file, and reparse when it changes.
    ///
    /// NOT INSTALLED IS A LOUD FAILURE, not a quiet empty answer. Returning "" here would make a
    /// missing install look exactly like an empty file, and this project has already been bitten
    /// once by a read failure that looked like emptiness - see serve-viewer.py's read_verdicts,
    /// where it would have deleted every ruling in the file it was reading.
    /// </summary>
    public static class Content
    {
        private static IContentSource _source;

        public static void Install(IContentSource source) => _source = source;

        /// <summary>True when somebody has installed a source. Tests and tools may check.</summary>
        public static bool Ready => _source != null;

        public static string Read(string fileName)
        {
            if (_source == null)
                throw new InvalidOperationException(
                    $"No content source installed - cannot read '{fileName}'. The game installs " +
                    "ContentLoader in TownPipeline.Build; a test must install its own before " +
                    "touching anything that reads Content.");
            return _source.Read(fileName);
        }

        public static DateTime WrittenAt(string fileName) =>
            _source == null ? default : _source.WrittenAt(fileName);
    }
}
