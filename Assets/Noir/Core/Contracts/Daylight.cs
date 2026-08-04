namespace Noir.Core.Contracts
{
    /// <summary>
    /// How light it is outside. The order is deliberate: Night &lt; Dawn/Dusk &lt; Day, so a
    /// witness rule can say "brighter than Night" without listing cases.
    /// </summary>
    public enum LightLevel : byte
    {
        /// <summary>Sun more than 6 degrees below the horizon. You cannot recognise a face.</summary>
        Night = 0,
        /// <summary>Civil twilight, morning. Light enough to see by, not light enough to be sure.</summary>
        Dawn = 1,
        /// <summary>Civil twilight, evening.</summary>
        Dusk = 2,
        /// <summary>Sun above the horizon.</summary>
        Day = 3,
    }

    /// <summary>
    /// Sunrise and sunset for the Chicago x Attica crossing, 40.3793 N, -87.66897 W.
    ///
    /// THIS IS IN CORE ON PURPOSE. Darkness is not lighting - it is the game's central mechanic.
    /// WHO-SEES-WHOM.md is built on the observation that the same walk home is witnessed in June
    /// and unwitnessed in December, and October is the month of least visibility partly because
    /// sunset moves from 18:35 to 16:49 across it. Anything that reasons about what a person could
    /// have seen has to know whether it was dark, so the answer has to live where the simulation
    /// can reach it and has to be identical on every machine forever.
    ///
    /// WHY A TABLE AND NOT THE FORMULA. The NOAA solar position algorithm needs Sin, Cos, Tan,
    /// Asin, Acos and Atan2. Core bans every one of them - their results are implementation-
    /// defined and have changed between .NET runtimes, which would silently break replay. So the
    /// algorithm was run once, offline, and its output is embedded below. Exact, deterministic,
    /// no transcendentals at runtime, and the derivation is in the git history of this file.
    ///
    /// MINUTES ARE LOCAL STANDARD TIME - CST, UTC-6 - ALL YEAR. The simulation clock is a
    /// monotonic tick counter, so every day must be exactly 1440 minutes or DayOfWeek, TickOn and
    /// every schedule in the content break. Daylight saving therefore never moves the simulation;
    /// it is a fact about the date that presentation applies on top. See GameClock.IsDaylightSaving
    /// and GameClock.WallClockMinute.
    ///
    /// AGAINST THE RESEARCH. Seven of the eight rows in THE-YEAR.md's daylight table reproduce
    /// here to within one minute. The eighth, 21 March, is off by exactly an hour: that table gives
    /// 06:54/19:03, which is daylight time, but 21 March 1991 was NOT on daylight time. The US rule
    /// in force 1987-2006 starts DST on the FIRST SUNDAY IN APRIL - in 1991 that is 7 April. The
    /// second-Sunday-in-March rule everyone now has in their heads only begins in 2007, a year
    /// after this game ends. The research figure was computed with the modern rule; the correct
    /// answer for the game's era is 05:54/18:02 CST. The other seven rows are unaffected because
    /// they are all either in winter or far enough inside the DST window for both rules to agree.
    /// </summary>
    public static class Daylight
    {
        public const double Latitude = 40.3793;
        public const double Longitude = -87.66897;

        /// <summary>Standard-time offset from UTC, in hours. Central.</summary>
        public const int StandardOffsetHours = -6;

        /// <summary>Days elapsed before the first of each month in a common year.</summary>
        private static readonly int[] DayBeforeMonth =
            { 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334 };

        /// <summary>
        /// Index into the tables below, 0..364. Common-year day-of-year, with 29 February folded
        /// onto 28 February - indexing by raw day-of-year would slide every date after February
        /// by a day in a leap year, which is a 1-2 minute error every fourth year for no reason.
        /// </summary>
        public static int IndexOf(int month, int dayOfMonth)
        {
            if (month == 2 && dayOfMonth == 29) return 58;              // 28 February
            return DayBeforeMonth[month - 1] + dayOfMonth - 1;
        }

        /// <summary>Sunrise in minutes after local standard midnight.</summary>
        public static int Sunrise(int month, int dayOfMonth) => SunriseTable[IndexOf(month, dayOfMonth)];

        /// <summary>Sunset in minutes after local standard midnight.</summary>
        public static int Sunset(int month, int dayOfMonth) => SunsetTable[IndexOf(month, dayOfMonth)];

        /// <summary>Start of civil twilight, in minutes after local standard midnight.</summary>
        public static int Dawn(int month, int dayOfMonth)
        {
            int i = IndexOf(month, dayOfMonth);
            return SunriseTable[i] - TwilightTable[i];
        }

        /// <summary>End of civil twilight, in minutes after local standard midnight.</summary>
        public static int Dusk(int month, int dayOfMonth)
        {
            int i = IndexOf(month, dayOfMonth);
            return SunsetTable[i] + TwilightTable[i];
        }

        /// <summary>Minutes between sunrise and sunset. 557 at the solstice, 904 at the other.</summary>
        public static int Length(int month, int dayOfMonth)
        {
            int i = IndexOf(month, dayOfMonth);
            return SunsetTable[i] - SunriseTable[i];
        }

        /// <summary>
        /// How light it is at a given minute of a given date. minuteOfDay is local STANDARD time,
        /// which is what the simulation clock counts.
        /// </summary>
        public static LightLevel At(int month, int dayOfMonth, int minuteOfDay)
        {
            int i = IndexOf(month, dayOfMonth);
            int rise = SunriseTable[i], set = SunsetTable[i], tw = TwilightTable[i];

            if (minuteOfDay >= rise && minuteOfDay < set) return LightLevel.Day;
            if (minuteOfDay >= rise - tw && minuteOfDay < rise) return LightLevel.Dawn;
            if (minuteOfDay >= set && minuteOfDay < set + tw) return LightLevel.Dusk;
            return LightLevel.Night;
        }

        // ---- the tables ------------------------------------------------------------------
        //
        // Generated from the NOAA general solar position algorithm at the coordinates above, for
        // a common year, in minutes after local standard midnight. Sunrise and sunset use the
        // conventional 90.833 degree zenith - half a solar diameter plus 34 arcminutes of
        // refraction. Twilight is the gap to the 96 degree (civil) crossing; it is symmetric
        // morning to evening within one minute on every day of the year, so one array serves
        // both ends. The +/-1 wobble along it is decimal rounding, not astronomy.
        //
        // These are TRUE SOLAR TIMES FOR THIS TOWN, not for the time zone. Rossville sits 2.33
        // degrees east of the 90 W meridian that defines CST, so its solar noon falls about nine
        // minutes before twelve o'clock - which is why sunrise and sunset both read early against
        // a table computed for the zone.

        private static readonly short[] SunriseTable =
        {
            434, 434, 434, 434, 434, 434, 434, 434, 434, 434, 433, 433, 433, 432, 432, 432, 431, 431, 430, 430, 429, 429, 428, 427, 427,
            426, 425, 424, 424, 423, 422, 421, 420, 419, 418, 417, 416, 415, 414, 413, 411, 410, 409, 408, 407, 405, 404, 403, 401, 400,
            399, 397, 396, 395, 393, 392, 390, 389, 387, 386, 384, 383, 381, 380, 378, 377, 375, 373, 372, 370, 369, 367, 365, 364, 362,
            360, 359, 357, 356, 354, 352, 351, 349, 347, 346, 344, 342, 341, 339, 338, 336, 334, 333, 331, 329, 328, 326, 325, 323, 321,
            320, 318, 317, 315, 314, 312, 311, 309, 308, 306, 305, 303, 302, 300, 299, 298, 296, 295, 294, 292, 291, 290, 289, 287, 286,
            285, 284, 283, 281, 280, 279, 278, 277, 276, 275, 274, 273, 272, 272, 271, 270, 269, 268, 268, 267, 266, 266, 265, 265, 264,
            264, 263, 263, 262, 262, 262, 261, 261, 261, 260, 260, 260, 260, 260, 260, 260, 260, 260, 260, 260, 260, 261, 261, 261, 261,
            262, 262, 262, 263, 263, 263, 264, 264, 265, 265, 266, 267, 267, 268, 268, 269, 270, 270, 271, 272, 273, 273, 274, 275, 276,
            277, 277, 278, 279, 280, 281, 282, 283, 284, 284, 285, 286, 287, 288, 289, 290, 291, 292, 293, 294, 295, 296, 297, 298, 299,
            300, 301, 302, 303, 304, 305, 306, 307, 307, 308, 309, 310, 311, 312, 313, 314, 315, 316, 317, 318, 319, 320, 321, 322, 323,
            324, 325, 326, 327, 328, 329, 330, 331, 332, 333, 333, 334, 335, 336, 337, 338, 339, 340, 341, 342, 343, 344, 345, 346, 347,
            348, 349, 350, 351, 352, 353, 354, 355, 356, 357, 358, 359, 361, 362, 363, 364, 365, 366, 367, 368, 369, 370, 371, 373, 374,
            375, 376, 377, 378, 379, 381, 382, 383, 384, 385, 386, 387, 389, 390, 391, 392, 393, 394, 396, 397, 398, 399, 400, 401, 403,
            404, 405, 406, 407, 408, 409, 410, 411, 412, 413, 415, 416, 416, 417, 418, 419, 420, 421, 422, 423, 424, 424, 425, 426, 427,
            427, 428, 429, 429, 430, 430, 431, 431, 432, 432, 432, 433, 433, 433, 433
        };

        private static readonly short[] SunsetTable =
        {
            994, 995, 996, 997, 997, 998, 999, 1000, 1001, 1002, 1003, 1004, 1005, 1006, 1007, 1009, 1010, 1011, 1012, 1013, 1014, 1015, 1017, 1018, 1019,
            1020, 1021, 1023, 1024, 1025, 1026, 1027, 1029, 1030, 1031, 1032, 1034, 1035, 1036, 1037, 1038, 1040, 1041, 1042, 1043, 1044, 1046, 1047, 1048, 1049,
            1050, 1052, 1053, 1054, 1055, 1056, 1057, 1058, 1060, 1061, 1062, 1063, 1064, 1065, 1066, 1067, 1068, 1070, 1071, 1072, 1073, 1074, 1075, 1076, 1077,
            1078, 1079, 1080, 1081, 1082, 1083, 1084, 1085, 1087, 1088, 1089, 1090, 1091, 1092, 1093, 1094, 1095, 1096, 1097, 1098, 1099, 1100, 1101, 1102, 1103,
            1104, 1105, 1106, 1107, 1108, 1109, 1110, 1111, 1112, 1113, 1114, 1115, 1116, 1117, 1119, 1120, 1121, 1122, 1123, 1124, 1125, 1126, 1127, 1128, 1129,
            1130, 1131, 1132, 1133, 1134, 1135, 1136, 1137, 1138, 1139, 1140, 1141, 1142, 1143, 1143, 1144, 1145, 1146, 1147, 1148, 1149, 1150, 1150, 1151, 1152,
            1153, 1154, 1154, 1155, 1156, 1156, 1157, 1158, 1158, 1159, 1159, 1160, 1161, 1161, 1161, 1162, 1162, 1163, 1163, 1163, 1164, 1164, 1164, 1164, 1164,
            1165, 1165, 1165, 1165, 1165, 1165, 1165, 1165, 1165, 1164, 1164, 1164, 1164, 1163, 1163, 1163, 1162, 1162, 1161, 1161, 1160, 1160, 1159, 1159, 1158,
            1157, 1157, 1156, 1155, 1154, 1153, 1153, 1152, 1151, 1150, 1149, 1148, 1147, 1146, 1145, 1144, 1142, 1141, 1140, 1139, 1138, 1136, 1135, 1134, 1133,
            1131, 1130, 1129, 1127, 1126, 1124, 1123, 1122, 1120, 1119, 1117, 1116, 1114, 1113, 1111, 1110, 1108, 1106, 1105, 1103, 1102, 1100, 1098, 1097, 1095,
            1093, 1092, 1090, 1089, 1087, 1085, 1084, 1082, 1080, 1079, 1077, 1075, 1074, 1072, 1070, 1068, 1067, 1065, 1063, 1062, 1060, 1058, 1057, 1055, 1054,
            1052, 1050, 1049, 1047, 1045, 1044, 1042, 1041, 1039, 1037, 1036, 1034, 1033, 1031, 1030, 1028, 1027, 1025, 1024, 1022, 1021, 1020, 1018, 1017, 1016,
            1014, 1013, 1012, 1010, 1009, 1008, 1007, 1006, 1004, 1003, 1002, 1001, 1000, 999, 998, 997, 996, 995, 995, 994, 993, 992, 991, 991, 990,
            989, 989, 988, 988, 987, 987, 986, 986, 986, 985, 985, 985, 985, 984, 984, 984, 984, 984, 984, 984, 984, 984, 985, 985, 985,
            985, 986, 986, 986, 987, 987, 988, 988, 989, 990, 990, 991, 992, 992, 993
        };

        private static readonly byte[] TwilightTable =
        {
            31, 30, 30, 30, 31, 31, 31, 31, 31, 31, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 29, 29, 30, 29, 30, 29, 30, 29, 29, 29, 29, 29, 29, 29, 28, 28, 28, 29, 28, 28, 28, 28, 28,
            28, 28, 28, 28, 28, 28, 27, 28, 28, 28, 28, 28, 28, 27, 28, 27, 28, 27, 28, 28, 28, 28, 27, 27, 27, 28, 27, 27, 27, 27, 27, 27, 27, 28, 28, 28, 28, 28, 27, 28, 27, 27, 28, 27, 28,
            28, 27, 28, 28, 27, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 29, 28, 29, 28, 29, 28, 29, 28, 28, 29, 29, 29, 30, 29, 29, 30, 30, 29, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 31,
            31, 31, 31, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 33, 32, 32, 33, 32, 32, 33, 32, 33, 33, 32, 33, 33, 33, 33, 33, 33, 33, 33, 34, 33, 33, 34, 34, 33, 33, 34, 33,
            33, 33, 33, 32, 33, 33, 33, 32, 33, 32, 32, 33, 32, 32, 32, 33, 32, 32, 32, 32, 32, 31, 31, 32, 32, 32, 31, 31, 31, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30,
            30, 30, 30, 29, 29, 29, 29, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 27, 27, 27, 27, 27, 28, 27, 27, 28, 27,
            27, 28, 27, 27, 27, 27, 27, 27, 27, 28, 27, 27, 27, 27, 28, 27, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 27, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 29, 29, 29,
            29, 29, 29, 29, 29, 29, 29, 30, 29, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 31, 31, 31, 30, 30, 31, 31, 30, 31, 31, 31, 31, 31, 31, 31, 31, 30,
            30, 31, 30, 30, 30
        };
    }
}
