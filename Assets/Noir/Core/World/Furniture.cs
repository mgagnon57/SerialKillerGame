using System.Collections.Generic;
using Noir.Core.Contracts;

namespace Noir.Core.World
{
    public enum FurnitureKind : byte
    {
        Bed = 0,
        Wardrobe,
        Dresser,
        Table,
        Chair,
        Sofa,
        Cooker,
        Sink,
        Counter,
        Bath,
        Basin,
        Hearth,
        Desk,

        // ---- what is in the buildings that are not houses ----
        Bench,          // a pew, a waiting-room bench, a row of cinema seats
        Shelf,          // shop and stock-room shelving
        Cabinet,        // filing cabinet, instrument cupboard
        Screen,         // the cinema screen
        Altar,
        Stage,
        Couch,          // an examination couch, which is not a bed and must not look like one
        Blackboard,
        Barrel,
    }

    /// <summary>
    /// One piece of furniture: a footprint on the grid and a height.
    ///
    /// Deliberately abstract. It is not a model, it is a box of the right size in the right
    /// place, which is enough to know a kitchen from a bedroom at a glance from above. When
    /// real models arrive they slot onto exactly these footprints - the box IS the spec.
    ///
    /// It is also simulation data, not decoration. A bed is where somebody sleeps, a cooker is
    /// where a meal happens. That matters more later than it does now.
    /// </summary>
    public readonly struct Furniture
    {
        public readonly FurnitureKind Kind;
        public readonly TileRect Footprint;
        public readonly RoomId Room;

        public Furniture(FurnitureKind kind, TileRect footprint, RoomId room)
        {
            Kind = kind;
            Footprint = footprint;
            Room = room;
        }

        /// <summary>Height in metres. Reading a room from above is mostly about height.</summary>
        public float Height
        {
            get
            {
                switch (Kind)
                {
                    case FurnitureKind.Bed: return 0.55f;
                    case FurnitureKind.Sofa: return 0.80f;
                    case FurnitureKind.Chair: return 0.90f;
                    case FurnitureKind.Table: return 0.75f;
                    case FurnitureKind.Desk: return 0.75f;
                    case FurnitureKind.Counter: return 0.90f;
                    case FurnitureKind.Sink: return 0.90f;
                    case FurnitureKind.Cooker: return 0.90f;
                    case FurnitureKind.Basin: return 0.80f;
                    case FurnitureKind.Bath: return 0.60f;
                    case FurnitureKind.Dresser: return 1.20f;
                    case FurnitureKind.Wardrobe: return 1.90f;
                    case FurnitureKind.Hearth: return 1.10f;
                    case FurnitureKind.Bench: return 0.45f;
                    case FurnitureKind.Couch: return 0.60f;
                    case FurnitureKind.Stage: return 0.30f;
                    case FurnitureKind.Barrel: return 0.90f;
                    case FurnitureKind.Altar: return 1.00f;
                    case FurnitureKind.Blackboard: return 1.20f;
                    case FurnitureKind.Cabinet: return 1.30f;
                    case FurnitureKind.Shelf: return 1.80f;
                    case FurnitureKind.Screen: return 3.00f;
                    default: return 0.80f;
                }
            }
        }

        public override string ToString() => $"{Kind} {Footprint}";
    }

    /// <summary>
    /// Where a piece of furniture wants to be.
    ///
    /// <see cref="Wall"/> is the whole of domestic furnishing and was the whole of this file:
    /// real rooms are furnished round their edges with the middle left clear. But a lecture
    /// theatre is not, and neither is a classroom or a church - in those rooms the middle IS
    /// the room, and pushing the seating against the walls produces something that reads as a
    /// waiting area with a screen in it.
    /// </summary>
    public enum Placement : byte
    {
        /// <summary>Against a wall, wherever it first fits.</summary>
        Wall = 0,

        /// <summary>Repeated round the walls with a gap between: ward beds, a row of basins.</summary>
        Along,

        /// <summary>Ranks across the floor, facing the focus: pews, desks, cinema seats.</summary>
        Rows,

        /// <summary>Centred on the wall you face as you come in: a screen, an altar, a board.</summary>
        Focal,
    }

    /// <summary>What belongs in each kind of room, in the order it gets placed.</summary>
    public static class FurniturePlans
    {
        public readonly struct Item
        {
            public readonly FurnitureKind Kind;

            /// <summary>For <see cref="Placement.Focal"/>, W runs along the wall and H into the room.</summary>
            public readonly int W, H;

            public readonly Placement Where;

            /// <summary>How many to place. Zero means as many as the room will hold.</summary>
            public readonly int Count;

            /// <summary>Tiles left clear between one and the next.</summary>
            public readonly int Gap;

            public Item(FurnitureKind kind, int w, int h)
                : this(kind, w, h, Placement.Wall, 1, 0) { }

            public Item(FurnitureKind kind, int w, int h, Placement where, int count, int gap)
            {
                Kind = kind; W = w; H = h; Where = where; Count = count; Gap = gap;
            }
        }

        public static IReadOnlyList<Item> For(RoomKind room)
        {
            switch (room)
            {
                case RoomKind.Bedroom:
                    return new[]
                    {
                        new Item(FurnitureKind.Bed, 2, 1),
                        new Item(FurnitureKind.Wardrobe, 1, 1),
                        new Item(FurnitureKind.Dresser, 1, 1),
                    };

                case RoomKind.Kitchen:
                    return new[]
                    {
                        new Item(FurnitureKind.Cooker, 1, 1),
                        new Item(FurnitureKind.Sink, 1, 1),
                        new Item(FurnitureKind.Counter, 2, 1),
                        new Item(FurnitureKind.Table, 2, 1),
                    };

                case RoomKind.Bathroom:
                    return new[]
                    {
                        new Item(FurnitureKind.Bath, 2, 1),
                        new Item(FurnitureKind.Basin, 1, 1),
                    };

                case RoomKind.Living:
                    return new[]
                    {
                        new Item(FurnitureKind.Sofa, 2, 1),
                        new Item(FurnitureKind.Hearth, 1, 1),
                        new Item(FurnitureKind.Chair, 1, 1),
                        new Item(FurnitureKind.Table, 1, 1),
                    };

                case RoomKind.Scullery:
                    return new[]
                    {
                        new Item(FurnitureKind.Counter, 2, 1),
                        new Item(FurnitureKind.Sink, 1, 1),
                    };

                case RoomKind.Workroom:
                    return new[]
                    {
                        new Item(FurnitureKind.Desk, 2, 1),
                        new Item(FurnitureKind.Chair, 1, 1),
                    };

                // A ward is beds and nothing else, evenly spaced down both walls with a metre
                // between them. The washbasin goes first so that the beds do not take the whole
                // perimeter and leave the ward with nowhere to wash.
                case RoomKind.Ward:
                    return new[]
                    {
                        new Item(FurnitureKind.Basin, 1, 1),
                        new Item(FurnitureKind.Bed, 2, 1, Placement.Along, 0, 1),
                    };

                case RoomKind.Consulting:
                    return new[]
                    {
                        new Item(FurnitureKind.Couch, 2, 1),
                        new Item(FurnitureKind.Desk, 2, 1),
                        new Item(FurnitureKind.Chair, 1, 1),
                        new Item(FurnitureKind.Basin, 1, 1),
                        new Item(FurnitureKind.Cabinet, 1, 1),
                    };

                case RoomKind.Classroom:
                    return new[]
                    {
                        new Item(FurnitureKind.Blackboard, 6, 1, Placement.Focal, 1, 0),
                        new Item(FurnitureKind.Desk, 2, 1, Placement.Rows, 0, 1),
                        new Item(FurnitureKind.Desk, 2, 1),      // the teacher's, against a wall
                        new Item(FurnitureKind.Cabinet, 1, 1),
                    };

                case RoomKind.Waiting:
                    return new[]
                    {
                        new Item(FurnitureKind.Bench, 3, 1, Placement.Along, 4, 1),
                        new Item(FurnitureKind.Table, 1, 1),
                    };

                case RoomKind.Foyer:
                    return new[]
                    {
                        new Item(FurnitureKind.Counter, 3, 1),
                        new Item(FurnitureKind.Bench, 2, 1, Placement.Along, 2, 1),
                    };

                case RoomKind.Office:
                    return new[]
                    {
                        new Item(FurnitureKind.Desk, 2, 1),
                        new Item(FurnitureKind.Chair, 1, 1),
                        new Item(FurnitureKind.Cabinet, 1, 1),
                        new Item(FurnitureKind.Cabinet, 1, 1),
                    };

                case RoomKind.StaffRoom:
                    return new[]
                    {
                        new Item(FurnitureKind.Table, 2, 1),
                        new Item(FurnitureKind.Sink, 1, 1),
                        new Item(FurnitureKind.Counter, 2, 1),
                        new Item(FurnitureKind.Chair, 1, 1, Placement.Along, 3, 1),
                    };

                case RoomKind.ShopFloor:
                    return new[]
                    {
                        new Item(FurnitureKind.Counter, 6, 1, Placement.Focal, 1, 0),
                        new Item(FurnitureKind.Shelf, 3, 1, Placement.Along, 6, 1),
                    };

                case RoomKind.StockRoom:
                    return new[]
                    {
                        new Item(FurnitureKind.Shelf, 3, 1, Placement.Along, 8, 1),
                        new Item(FurnitureKind.Barrel, 1, 1),
                    };

                // A row of chairs facing the mirror shelf, and the bench you wait your turn on.
                case RoomKind.Salon:
                    return new[]
                    {
                        new Item(FurnitureKind.Counter, 8, 1, Placement.Focal, 1, 0),
                        new Item(FurnitureKind.Chair, 1, 1, Placement.Rows, 3, 1),
                        new Item(FurnitureKind.Basin, 1, 1, Placement.Along, 2, 1),
                        new Item(FurnitureKind.Bench, 2, 1, Placement.Along, 2, 1),
                    };

                case RoomKind.Bar:
                    return new[]
                    {
                        new Item(FurnitureKind.Counter, 8, 1, Placement.Focal, 1, 0),
                        new Item(FurnitureKind.Hearth, 1, 1),
                        new Item(FurnitureKind.Bench, 3, 1, Placement.Along, 3, 1),
                        new Item(FurnitureKind.Table, 1, 1, Placement.Rows, 0, 2),
                    };

                case RoomKind.Auditorium:
                    return new[]
                    {
                        new Item(FurnitureKind.Screen, 20, 1, Placement.Focal, 1, 0),
                        new Item(FurnitureKind.Bench, 5, 1, Placement.Rows, 0, 1),
                    };

                case RoomKind.Nave:
                    return new[]
                    {
                        new Item(FurnitureKind.Altar, 3, 1, Placement.Focal, 1, 0),
                        new Item(FurnitureKind.Bench, 5, 1, Placement.Rows, 0, 1),
                    };

                case RoomKind.Assembly:
                    return new[]
                    {
                        new Item(FurnitureKind.Stage, 8, 2, Placement.Focal, 1, 0),
                        new Item(FurnitureKind.Chair, 1, 1, Placement.Rows, 0, 1),
                        new Item(FurnitureKind.Table, 2, 1),
                    };

                case RoomKind.Workshop:
                    return new[]
                    {
                        new Item(FurnitureKind.Counter, 3, 1, Placement.Along, 3, 1),
                        new Item(FurnitureKind.Cabinet, 1, 1),
                        new Item(FurnitureKind.Barrel, 1, 1),
                    };

                case RoomKind.Washroom:
                    return new[]
                    {
                        new Item(FurnitureKind.Basin, 1, 1, Placement.Along, 4, 1),
                    };

                // A hall holds coats and nothing worth drawing, and a corridor holds even that
                // against the wall of somewhere else.
                default:
                    return System.Array.Empty<Item>();
            }
        }
    }
}
