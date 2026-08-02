using System.Collections.Generic;

namespace Noir.Editor
{
    /// <summary>
    /// Undo/redo for the sculpt tool, as whole-grid snapshots rather than per-cell diffs.
    ///
    /// The grid this undoes is 71x81 floats - about 23KB - so snapshotting all of it on every
    /// stroke costs nothing worth optimising away, and it means undo can never drift from what a
    /// stroke actually did: there is no incremental state to get out of sync.
    ///
    /// Deliberately not Unity's own Undo system, which only tracks UnityEngine.Objects - wiring
    /// this into it would mean wrapping the delta grid in a ScriptableObject purely to get
    /// Undo.RegisterCompleteObjectUndo to see it, for a stack that is simpler and easier to test
    /// on its own, and that cannot collide with the user's own scene-edit undo history sharing
    /// the same Ctrl+Z.
    /// </summary>
    public sealed class SculptUndoStack
    {
        private readonly List<float[,]> _undo = new List<float[,]>();
        private readonly List<float[,]> _redo = new List<float[,]>();

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        /// <summary>Call BEFORE a stroke changes the grid, with the grid as it stood going in.
        /// Starting a new stroke clears redo - the same rule any undo history follows once you
        /// branch off from the point you rewound to.</summary>
        public void RecordBeforeStroke(float[,] snapshotBeforeStroke)
        {
            _undo.Add(snapshotBeforeStroke);
            _redo.Clear();
        }

        /// <summary>Pops the most recent stroke and returns the grid to restore. Call only when
        /// CanUndo is true.</summary>
        public float[,] Undo(float[,] currentGrid)
        {
            float[,] restore = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            _redo.Add(currentGrid);
            return restore;
        }

        /// <summary>Reapplies the most recently undone stroke. Call only when CanRedo is true.</summary>
        public float[,] Redo(float[,] currentGrid)
        {
            float[,] restore = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            _undo.Add(currentGrid);
            return restore;
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }
    }
}
