namespace StarfighterCompanion.Core.Formats.Xwa;

/// <summary>
/// Original X-Wing Alliance craft-type names, indexed by the numeric craft ID stored in pilot statistics.
/// Source: OpenXWA modding/reference/craft_types.md (original SHIPLIST.TXT).
/// Hidden (*) and unused entries are deliberately left numeric, as are IDs outside the stock list.
/// </summary>
internal static class OriginalXwaCraftNames
{
    private static readonly string?[] Names =
    {
        null, // 0
        "X-wing", // 1
        "Y-wing", // 2
        "A-wing", // 3
        "B-wing", // 4
        "TIE Fighter", // 5
        "TIE Interceptor", // 6
        "TIE Bomber", // 7
        "TIE Advanced", // 8
        "TIE Defender", // 9
        "Authority IRD", // 10
        "Toscan Fighter", // 11
        "Missile Boat", // 12
        "T-wing", // 13
        "Z-95", // 14
        "R-41", // 15
        "Gunboat", // 16
        "Shuttle", // 17
        "Escort Shuttle", // 18
        "Sys Patrol Craft", // 19
        "Scout Craft", // 20
        "Stormtrpr Trans", // 21
        "Assault Trans", // 22
        "Escort Trans", // 23
        "Tug", // 24
        "Combat Util Vehicle", // 25
        "CN/A Brick", // 26
        "CN/B Hexbox", // 27
        "CN/C Tube", // 28
        "CN/D Pronged", // 29
        "Heavy Lifter", // 30
        "Mole Miner", // 31
        "Bulk Freighter", // 32
        "Cargo Ferry", // 33
        "Modular Conveyor", // 34
        "Container Trans", // 35
        "Rebel Med Transport", // 36
        "Muurian Transport", // 37
        "Corellian Trans", // 38
        "Millenium Falcon", // 39
        "Corvette", // 40
        "Modified Corvette", // 41
        "Nebulon B Frigate", // 42
        "Mod Neb B Frigate", // 43
        "Passenger Liner", // 44
        "Carrack Cruiser", // 45
        "Strike Cruiser", // 46
        "Escort Carrier", // 47
        "Dreadnaught", // 48
        "Calamari Cruiser", // 49
        "Lt Calamari Cruiser", // 50
        "Interdictor", // 51
        "Vict Star Dest", // 52
        "Imp Star Dest", // 53
        "SSD", // 54
        "CN/E Hemisphere", // 55
        "CN/F Slotted", // 56
        "CN/G Hourglass", // 57
        "CN/H Gem", // 58
        "CN/I Y shaped", // 59
        "Platform 1", // 60
        "Platform 2", // 61
        "Platform 3", // 62
        "Platform 4", // 63
        "Platform 5", // 64
        "Platform 6", // 65
        "Asteroid Hangar", // 66
        "Ast Laser Bat", // 67
        "Ast Warhd Launcher", // 68
        "Factory", // 69
        "Satellite1", // 70
        "Satellite2", // 71
        "Satellite3", // 72
        null, // 73
        null, // 74
        "Mine1", // 75
        "Mine2", // 76
        "Mine3", // 77
        "GunPlatform", // 78
        null, // 79
        "Probe1", // 80
        "Probe Capsule", // 81
        null, // 82
        "Buoy1", // 83
        "Buoy2", // 84
        "Hyper buoy", // 85
        "Asteroid", // 86
        "Planet", // 87
        "Rendezvous Buoy", // 88
        "Cargo Canister", // 89
        "Shipyard", // 90
        "Repair Yard", // 91
        null, // 92
        "Lancer Frigate", // 93
        "Bulk Cruiser", // 94
        "Assault Frigate", // 95
        "Corellian Gunship", // 96
        "Imp Landing Craft", // 97
        "Assault Shuttle", // 98
        "Marauder Corvette", // 99
        "Star Galleon", // 100
        "Imp Research Ship", // 101
        "Luxury Yacht 3000", // 102
        "Ferryboat Liner", // 103
        "Mod Action Trans", // 104
        "Mobquet Transport", // 105
        "Xiytiar Transport", // 106
        "Freight Transport/C", // 107
        "Freight Transport/H", // 108
        "Freight Transport/K", // 109
        "YT-2000", // 110
        "YT-2400", // 111
        "Suprosa", // 112
        "Skipray Blastboat", // 113
        "TIE Experimental M1", // 114
        "TIE Experimental M2", // 115
        "TIE Experimental M3", // 116
        "TIE Experimental M4", // 117
        "TIE Experimental M5", // 118
        "Cloakshape Fighter", // 119
        "Razor Fighter", // 120
        "Planetary Fighter", // 121
        "Supa Fighter", // 122
        "Pinook Fighter", // 123
        null, // 124
        "Preybird Fighter", // 125
        null, // 126
        "Firespray", // 127
        "Pursuer", // 128
        "Golan One", // 129
        "Golan Two", // 130
        "Golan Three", // 131
        "Derilyn Platform", // 132
        "Sensor Array", // 133
        "Comm Relay", // 134
        "Space Colony 1", // 135
        "Space Colony 2", // 136
        "Space Colony 3", // 137
        "Casino", // 138
        "Cargo Facility 1", // 139
        "Cargo Facility 2", // 140
        "Ast Mining Unit", // 141
        "Processing Plant", // 142
        "Rebel Platform", // 143
        "Imp Research Center", // 144
        "Family Base", // 145
        "Family Repair Yard", // 146
        "Pirate Ship Yard", // 147
        "Industrial Complex", // 148
        null, // 149
        "Escape Pod Type 1", // 150
        "Pressure Tank", // 151
        "CN/J Grande", // 152
        "CN/K Box", // 153
        "CN/L Sphere", // 154
        "CN/Hangar", // 155
        "Lrg Gun Emplace", // 156
        "Lrg Gun/Wrhd Empl", // 157
        "Proximity Mine A", // 158
        "Proximity Mine B", // 159
        null, // 160
        "Homing Mine B", // 161
        "New Laser battery", // 162
        "New Ion battery", // 163
        "Cargo Freighter", // 164
        null, // 165
        null, // 166
        null, // 167
        null, // 168
        "Cargo Tanker", // 169
        null, // 170
        null, // 171
        null, // 172
        null, // 173
        "Escape Pod Type 2", // 174
        null, // 175
        null, // 176
        null, // 177
        "ZeroG Stormtrooper", // 178
        "ZeroG Utility", // 179
        "Emkay", // 180
        "Astromech Droid", // 181
        "Worker Droid", // 182
        "BACKDROP", // 183
        null, // 184
        null, // 185
        null, // 186
        null, // 187
        null, // 188
        null, // 189
        null, // 190
        null, // 191
        null, // 192
        null, // 193
        null, // 194
        null, // 195
        null, // 196
        null, // 197
        null, // 198
        null, // 199
        null, // 200
        null, // 201
        null, // 202
        null, // 203
        null, // 204
        null, // 205
        null, // 206
        null, // 207
        null, // 208
        null, // 209
        null, // 210
        null, // 211
        null, // 212
        null, // 213
        null, // 214
        null, // 215
        null, // 216
        null, // 217
        null, // 218
        null, // 219
        null, // 220
        null, // 221
        null, // 222
        null, // 223
        null, // 224
        null, // 225
        null, // 226
        "Death Star II", // 227
        "Calamari Cruiser Type 2", // 228
        "Vict Star Dest II", // 229
        "Imp Star Dest II", // 230
        null, // 231
    };

    public static IReadOnlyList<string?> All => Names;

    public static string Label(int id) =>
        id >= 0 && id < Names.Length && Names[id] is { } name
            ? $"{name} (craft type {id})"
            : $"Craft type {id}";
}
