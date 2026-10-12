using System.Collections.Generic;

namespace RomesteadContentForge;

// JSON shape of a content pack (one *.json file). Deserialized with IncludeFields + case-insensitive,
// so the JSON keys can be camelCase ("maxStack") and bind to these fields. This is the canonical format:
// the mod loads it, the validator checks it, and a future GUI editor reads/writes the same files.
internal sealed class ContentPack
{
    // --- Manifest (identity + compatibility; lets many authors' packs coexist) ---
    public string Id;                              // pack id, required, unique across all installed packs (logs/deps)
    public string Namespace;                       // your prefix, e.g. "acme". If set, every content id MUST be
                                                   // "<namespace>:..." — the loader warns on any id that isn't, so two
                                                   // authors never collide. Defaults to Id when omitted.
    public string Author;                          // shown in logs / load summary
    public string Version = "1.0.0";               // your pack's version (for co-op mismatch warnings)
    public string Description;                     // free text, shown in the load summary
    public List<string> Dependencies;              // pack Ids this pack needs (must be installed; loaded before this
                                                   // one). Missing dep -> this pack is skipped with a clear warning.
    public int ApiVersion = 1;                     // content-format version this pack targets (future-proofing).

    // --- Content (each section is optional; new kinds — furniture/npcs/quests — slot in here later) ---
    public Dictionary<string, string> Strings;     // localized text: "cf:flameblade:name" -> "Flameblade"
    public Dictionary<string, string> Icons;       // custom icons: iconId -> .png filename next to this pack
    public List<SpellDef> Spells;                  // custom spells (scrolls/tomes cast these by id)
    public List<ItemDef> Items;
    public List<RecipeDef> Recipes;                // crafting recipes (makes items craftable)
    public List<PaletteDef> Palettes;              // custom color palettes (dye ramps) for recoloring held looks
    public List<VfxDef> Vfx;                        // custom sprite-animation VFX (hit flashes, impacts, auras)
    public List<DropDef> Drops;                     // add items to mob/chest loot tables with a drop chance
    public List<ClassDef> Classes;                  // new playable classes (appear in character creation)
    public List<QuestDef> Quests;                   // world quests (auto-activate; reward crafting recipes)
    public List<PlaceableDef> Furniture;            // placeable decorations (build menu, reuse vanilla art)
    public List<PlaceableDef> Structures;           // ALIAS — same shape; use whichever name fits your content
    public List<PlaceableDef> Placeables;           // CANONICAL name for the same section: anything buildable
                                                   // from the construction menu (decor, shrines, levers, ...)
    public List<CreatureDef> Creatures;             // custom creatures (clone + restat/rename/recolor a vanilla one)
    public List<MapDef> Maps;                       // custom dungeon rooms (a Tiled .tmx shipped next to the pack)
    public List<ShopDef> Shops;                     // new market stock sections (sold by the merchant at the Market)
    public List<ShopAddDef> ShopAdditions;          // add items into an EXISTING vanilla shop section
    public List<ParticleDef> Particles;             // custom particle sprites (referenced by emitters)
    public List<EmitterDef> Emitters;               // custom particle emitters (spawn the particles)
    public List<CropDef> Crops;                     // custom plantable crops (seed item auto-generated)
    public List<NpcDef> Npcs;                       // dialogue NPCs standing in this pack's maps
    public List<ArenaDef> Arenas;                   // duel arenas bound to this pack's pvp maps
    public List<SkillDef> Skills;                   // new player skills (appear in the skills screen, level 0-100)
    public PlayerSkinsDef PlayerSkins;              // reskins / new hair / new worn parts / worn looks
    public List<FavourDef> Favours;                 // new favour-tree nodes (attached under existing nodes)
    public Dictionary<string, string> Sounds;       // raw audio next to the pack (.wav/.ogg/.mp3/.flac):
                                                    // "ding" -> "ding.wav", played as "cf:sound:<ns>:ding"
    public List<EventDef> Events;                   // server events (wave defense, boss rush, hunts, scheduled raids)
    public List<CitizenDef> Citizens;               // custom citizen types (town job-workers, arrive organically)
    public Dictionary<string, CitizenOverrideDef> CitizenOverrides; // edit existing citizen types by id
    public List<TownBuildingDef> TownBuildings;     // town job-buildings (bakery/forge tier) cloned from a vanilla one
    public List<WorkRecipeDef> WorkRecipes;         // citizen work recipes (what a baker bakes / a smith forges)
    public List<SpacDef> Spacs;                     // custom sprite-animation collections: clone+reskin/retime a vanilla spac
    public Dictionary<string, PlayerAnimDef> PlayerAnimations;  // "<player anim state name>" -> retime (47 states, see PACKS.md)
}

// A new favour-tree node: shows in the gods' favour tree attached under `parent`, costs 1 favour point, and
// carries ONE effect: `statBonuses` (permanent stat aura) OR `aura` (an existing aura id) OR `recipe`
// (unlocks a crafting recipe) OR `flagId` (advanced: a game flag vanilla code reacts to).
// UNINSTALL-SAFE: learned nodes are stripped from the save file (point auto-refunded) and re-granted at login.
internal sealed class FavourDef
{
    public string Id;                              // required, namespaced (e.g. "mymod:ember_heart")
    public string Name;                            // node name (tooltip title)
    public Dictionary<string, string> NameLocalized;        // optional langcode -> name
    public string Description;                     // tooltip text; {0}/{1} expand to flagValue1/flagValue2.
                                                   // Omit it with statBonuses and the stat lines auto-describe.
    public Dictionary<string, string> DescriptionLocalized; // optional langcode -> description
    public string Icon = "sam";                    // icon id (vanilla or your custom icon)
    public string Parent;                          // required: the favour id to attach under (vanilla "<god>_favour:<name>" or another of YOUR nodes).
                                                   // With screen:true the parent is optional (empty = root) and must be another screen node.
    public int[] Position;                         // required [column,row] tree-grid cell. Vanilla fills
                                                   // columns 0-14 of rows 0-9; row 10+ is always free.
                                                   // With screen:true the cell is on the pack screen's OWN grid (start at [0,0]).
    public bool Screen;                            // true = the node lives on the custom "Pack Favours" fullscreen
                                                   // tree (its own button in the Skills & Favours window), not the vanilla tree
    public string God = "minerva";                 // which god's tab lists it in the skills window
    public Dictionary<string, StatBonusDef> StatBonuses; // permanent stat bonuses while learned (same keys as gear)
    public string Aura;                            // OR: an existing entity-aura id applied while learned
    public string Recipe;                          // OR: a crafting recipe id unlocked while learned
    public string FlagId;                          // OR (advanced): a vanilla character flag to set
    public float FlagValue1;                       //   its Value1 (also fills {0} in the description)
    public float FlagValue2;                       //   its Value2 (also fills {1})
}

// Player-skin content: repaint vanilla character art, add char-creation hair, compose worn armor looks.
internal sealed class PlayerSkinsDef
{
    public List<SkinReskinDef> Reskins;         // overwrite a vanilla part's sheet (1:1 repaint)
    public List<SkinHairDef> Hair;              // new hair styles (appear in character creation)
    public List<SkinPartDef> Parts;             // new worn parts (referenced by looks' fragments)
    public List<SkinLookDef> Looks;             // CharacterDisplayData an item's equippable.displayId points at
    public SkinRigDef Rig;                      // body rig repaint: vanilla player spac sheets -> your PNGs (+ tint)
}

internal sealed class SkinRigDef
{
    public Dictionary<string, string> Sheets;   // vanilla player-spac sheet basename -> pack PNG (1:1 repaint)
    public int[] Tint;                          // optional [r,g,b] 0-255 multiplied over the body sheets at load
}

internal sealed class SkinReskinDef
{
    public string Tag;                          // "hair","eyes","tunic","armour","shoulders","decoration",
                                                // "sleeves","legwear","footwear","headwearBack/Middle/Front","back","shield"
    public string Part;                         // vanilla part name (the game's file name, e.g. "Braid", "Pants_metal")
    public string Sheet;                        // base PNG ("" variation) — must match the vanilla sheet's pixel size
    public string Sheet2;                       // optional "-2" facing-variation PNG
}

internal sealed class SkinHairDef { public string Name; public string Sheet; public string Sheet2; }
internal sealed class SkinPartDef { public string Tag; public string Name; public string Sheet; public string Sheet2; }

internal sealed class SkinLookDef
{
    public string Id;                           // e.g. "cdd:mypack:obsidianplate" — items reference it via equippable.displayId
    public List<SkinFragmentDef> Fragments;
    public bool HideHair;                       // helmets: hides hair (SpacTagsToHide = [6])
}

internal sealed class SkinFragmentDef
{
    public string Tag; public string Part;      // which registered part to draw
    public string Palette; public int Row;      // optional dye ramp + row; omit palette = drawn as authored
}

// A new player skill: shows in the skills screen with your icon, levels 0-100 on the shared XP curve, and
// (optionally) scales damage of weapons/spells that grant it. Convention: id starts with "skill:".
internal sealed class SkillDef
{
    public string Id;                              // required, e.g. "skill:runeology" (keep the skill: prefix)
    public string Name;                            // shown in the skills screen
    public Dictionary<string, string> NameLocalized;
    public string Description;
    public Dictionary<string, string> DescriptionLocalized;
    public string Icon = "sam";                    // vanilla icon id or your custom icon
    public float Value = 0.5f;                     // per-level effect scalar (damage bonus = Value * level/100)
    public float XpGainFactor = 1f;                // XP multiplier for this skill
}

// A dialogue NPC standing in one of this pack's maps. Reuses the vanilla unkillable "neutral_satyr"
// doodad (controller yarnneutralnpc); the dialogue itself is a YarnSpinner 2.x .yarn next to the pack.
internal sealed class NpcDef
{
    public string Id;                               // required, namespaced (e.g. "mymod:hermit")
    public string Map;                              // required: id of a map in THIS pack
    public float[] Position;                        // required [x,y] px from the map's top-left
    public string Yarn;                             // required: .yarn filename next to the pack
    public string StartNode = "Start";              // node the dialogue opens at
    public float DialogueDistance = 32f;            // walk-away distance that closes the window
    public Dictionary<string, string> Translations; // locale ("de_DE") -> csv file: "line:abc123,text" per line
    public Dictionary<string, string> Actors;       // <<Actor key>> -> speaker display name
    public Dictionary<string, Dictionary<string, string>> ActorsLocalized; // langcode -> (key -> name)
    public Dictionary<string, string> Sheets;  // unique body: satyr sheet basename -> your PNG (1:1 repaint). Satyr has ONE: "no_weapons" (48x48)
    public int[] Tint;                         // unique body: [r,g,b] 0-255 multiplied over the body sheet
}

// A duel arena bound to one of this pack's maps (which must be flagged pvp:true so PvpFeature lifts the
// friendly-fire gate there and reports kills). v1 semantics: the arena IS its map's world(s) — no lobbies,
// no signup; walk in to fight. With durationSeconds > 0 a timed match runs whenever enough players are
// inside; with durationSeconds == 0 the arena is an endless free-for-all (kill announcements, and — if
// winnerReward is set — the reward is paid per kill to the killer instead of to a match winner).
internal sealed class ArenaDef
{
    public string Id;                       // required, namespaced (e.g. "mymod:colosseum")
    public string Name;                     // shown in chat lines; defaults to the id's tail after ':'
    public string Map;                      // required: id of a map in THIS pack with pvp:true
    public bool AnnounceKills = true;       // chat-broadcast "A slew B in <arena>" lines
    public string WinnerReward;             // item id granted to the winner (or per kill when endless)
    public int RewardAmount = 1;
    public int MinPlayers = 2;              // players that must be in the arena world to start a match
    public int DurationSeconds = 300;       // match length; 0 = endless free-for-all (no matches)
}

// A custom crop: plant the (auto-generated) seed on farmland, watch it grow through stages, harvest the
// drops. Farmsteads farm it automatically — a crop whose drops include its own seed is self-sustaining.
internal sealed class CropDef
{
    public string Id;                              // required, namespaced (e.g. "mymod:crop:lavender")
    public string Name;                            // informational only — crops have no name UI string
    public float DryGrowthMinutes = 40f;            // total growth time unwatered
    public float WetGrowthMinutes = 20f;            // total growth time watered
    public int StageCount;                          // growth stages (display.stages must match!)
    public List<string> Biomes;                    // biome CATEGORIES ("plains"…); omit = any biome
    public bool AnyDirt;                            // plantable on any dirt (herb-style), not just farmland
    public bool Repeatable;                         // regrows after harvest (grapes/olives style)
    public string CropType = "Plant";               // Plant / Mushroom / Tree / FruitTree / Flower
    public List<CropDropDef> Drops;                 // farmed harvest yield
    public List<CropDropDef> WildDrops;             // optional wild-harvest yield
    public CropDisplayDef Display;                  // required: growth-stage sprites
    public SeedDef Seed;                            // required: the plantable seed item (auto-generated)
}

internal sealed class CropDropDef
{
    public string Item;                            // item id dropped (yours or vanilla)
    public int Min = 1;
    public int Max = 1;
    public float Chance = 100f;                     // percent
}

internal sealed class CropDisplayDef
{
    public string Sheet;                           // vanilla sheet path (e.g. "media/sprites/flora/wheat_animated") — OR:
    public string File;                            // your own PNG next to the pack (a grid of frames)
    public int FrameWidth = 16;
    public int FrameHeight = 32;
    public float[] Offset3D;                       // whole-crop draw offset (wheat uses [0,-1,-1])
    public bool DisableRandomFlip;
    public List<CropStageDef> Stages;              // one per growth stage — count MUST equal stageCount
}

internal sealed class CropStageDef
{
    public int[] Frames;                           // sheet cell indices ([5] = static frame, [0,1,2,3] = sway anim)
    public float Duration;                          // total animation seconds (0 = static)
    public bool Horizontal;                         // drawn flat on the ground (stage 0 = seeds)
    public float[] Offset3D;                       // per-stage draw offset
}

internal sealed class SeedDef
{
    public string Id;                              // required, namespaced (e.g. "mymod:seed:lavender")
    public string Name;                            // item name shown in inventory/shop
    public Dictionary<string, string> NameLocalized;        // optional langcode -> name
    public Dictionary<string, string> DescriptionLocalized; // optional langcode -> description
    public string Icon = "sam";                    // icon id (vanilla or your custom icon)
    public int Price = 10;                          // buy price (sell = 20%)
    public int Tier = 1;
    public int MaxStack = 100;
    public string Description;
}

// A custom particle: one sprite (your PNG or a vanilla sheet) plus optional per-particle behaviors. Each
// behavior block you include flips the matching simulation flag; omit it and the particle won't pay for it.
internal sealed class ParticleDef
{
    public string Id;                              // required, namespaced (e.g. "mymod:ember")
    public string Texture;                         // PNG sheet next to the pack (a grid of frames) — OR:
    public string VanillaSheet;                    // reuse a vanilla sheet by path (e.g. "media/particles/dust3")
    public int FrameWidth = 8;                     // cell size in the sheet
    public int FrameHeight = 8;
    public int[] Frames;                           // optional: cell indices in play order ([0,1,2,...]) = animated
    public float FrameDuration = 0.06f;            // seconds per frame
    public bool Loop = false;                      // loop the animation (else it plays once)
    public bool RemoveOnFinish = true;             // animated only: despawn when the animation ends
    public float[] Color;                          // [r,g,b,a] 0..1 tint (values >1 = HDR glow); default white
    public float Emissiveness;                     // 0..1 self-illumination
    public bool Vertical = true;                   // billboard standing up (false = flat on the ground)
    public float MaxLife = 1.5f;                   // seconds before despawn (animation may remove it earlier)
    public ParticleFadeDef Fade;                   // optional alpha fade
    public float Gravity;                          // optional gravity scale (1 = normal fall; 0/omitted = none)
    public ParticleFrictionDef Friction;           // optional velocity damping
    public ParticleLightDef Light;                 // optional attached light source
    public ParticleHslDriftDef HslDrift;           // optional hue/sat/lum shift over time
}

internal sealed class ParticleFadeDef
{
    public string Type = "fadeOut";                // fadeOverTime / fadeOut / fadeIn / fadeInAndOut
    public float Time = 0.5f;                      // seconds the fade takes
}

internal sealed class ParticleFrictionDef
{
    public float Ground;                           // damping while on the ground
    public float Air;                              // damping while airborne
}

internal sealed class ParticleLightDef
{
    public float Radius;                           // light radius in px
    public float[] Color;                          // [r,g,b] 0..1 (default white)
}

internal sealed class ParticleHslDriftDef
{
    public float H; public float S; public float L;            // initial hue/sat/lum offset
    public float HDrift; public float SDrift; public float LDrift; // per-second drift
}

// A custom particle emitter: spews weighted particles from a spawn area. Reference it anywhere the game
// takes an emitter id (a spell's particleTrail, etc.). Particle ids may be yours OR vanilla ("dust:small").
internal sealed class EmitterDef
{
    public string Id;                              // required, namespaced (e.g. "mymod:ember_fountain")
    public List<ParticleWeightDef> Particles;      // required: which particles to spawn, weighted
    public float SpawnFrequency = 0.05f;           // SECONDS between spawn ticks (<= 0 = every frame)
    public float EmitterLifetime = -1f;            // seconds before the emitter stops (-1 = forever)
    public int AmountSpawned = 1;                  // particles per spawn tick
    public float[] Offset;                         // [x,y,z] px offset from the emitter position (z = up)
    public SpawnAreaDef SpawnArea;                 // where particles appear (default: a single point)
    public VelocityDef Velocity;                   // optional initial velocity span
    public RotationDef Rotation;                   // optional initial sprite rotation span
    public AccelerationDef Acceleration;           // optional constant acceleration span
}

internal sealed class ParticleWeightDef
{
    public string Id;                              // particle id (yours or vanilla)
    public int Weight = 100;                       // relative pick weight
}

internal sealed class SpawnAreaDef
{
    public string Type = "point";                  // point / cube / cylinder / line / sphere
    public float Radius;                           // cylinder/sphere radius (px)
    public float[] Size;                           // cube/line: [x,y,z]; cylinder: [_,_,height]
}

internal sealed class VelocityDef
{
    public float Min;                              // px/sec along Direction
    public float Max;
    public float[] Direction = { 0f, 0f, 1f };     // default straight up (z = up)
}

internal sealed class RotationDef
{
    public float MinDeg;                           // initial rotation span, degrees
    public float MaxDeg;
}

internal sealed class AccelerationDef
{
    public float Min;                              // px/sec^2 along Direction
    public float Max;
    public float[] Direction = { 0f, 0f, 1f };
}

// A new stock section at the Market building (the merchant citizen sells it). Every shop in the database is
// offered automatically once the merchant's level/biome gates pass — no per-vendor assignment needed.
// Items must have a `price` (Tradable) to be buyable.
internal sealed class ShopDef
{
    public string Id;                              // required, namespaced (e.g. "mymod:wares:1")
    public List<string> Items;                     // item ids for sale (yours or vanilla; need a price)
    public int MerchantLevel = 1;                   // min merchant job level to unlock this section
    public string BiomeCategory;                    // optional: only markets in this biome category sell it
    public bool ExtraGoodsWorship;                  // requires the market_extra_goods worship unlock
}

// Append items to an existing vanilla shop section (e.g. "all:1" = the tier-1 anywhere stock).
internal sealed class ShopAddDef
{
    public string Shop;                            // required: existing shop id (e.g. "all:1", "forest:2")
    public List<string> Items;                     // item ids to add
}

// A custom dungeon room. Author the map in the FREE Tiled editor (mapeditor.org): orthogonal, 16x16 tiles,
// a tile layer named `ground` (tileset `world_tile_ground`) and optionally `structure` (tileset
// `world_tile_structure`) for walls/collision. The mod generates the binary world file, registers the room,
// and (if `biomes` is set) adds an overworld dungeon entrance that NEW worlds can roll like any vanilla POI.
internal sealed class MapDef
{
    public string Id;                              // required, namespaced (e.g. "mymod:crypt")
    public string Name;                            // display name (dungeon icon tooltip)
    public Dictionary<string, string> NameLocalized;        // optional langcode -> name
    public string Tmx;                             // required: .tmx filename next to this pack
    public List<string> Biomes;                    // overworld biomes that can roll the entrance, e.g. ["biome:forest"].
                                                   // Empty/omitted = room registered but no auto-entrance (reachable via quests/cheat).
    public float Weight = 100f;                     // POI roll weight (vanilla dungeons ~100)
    public string Icon = "dungeon_skull";           // map icon
    public string LootTable;                        // optional dungeon loot-table id
    public int ExitDirection = 1;                   // which overworld side the entrance faces: 0=E 1=S 2=W 3=N
    public float[] ExitNode;                        // [x,y,w,h] px — the walk-into-it exit box. Default: bottom-center
    public bool Exit = true;                        // false = no overworld exit (an inner room, reached via doors)
    public bool Pvp;                                // players can damage each other in this map's world; dying there spawns NO grave
    // ----- atmosphere: make the map feel like its own world -----
    public string Music;                            // FMOD music event (e.g. "event:/music/...") played in this map
    public string Ambience;                         // FMOD ambience event
    public float MusicVolume = 1f;
    public float[] AmbientLight;                    // [r,g,b] 0..1 ambient light color (dark realm = low values)
    public List<DoorDef> Doors;                     // doors into OTHER rooms of this pack (multi-room dungeons)
    public string ExteriorMap = "maps/poi/forest/td_animal_den"; // vanilla exterior map containing the entrance prop
    public bool Retrofit;                           // also place the entrance into EXISTING saves (once per save,
                                                    // on world load, in a clear spot of a listed biome). Needs `biomes`.
    public string Replace;                          // vanilla dungeon room id whose INTERIOR this map replaces
    public List<DoodadDef> Doodads;                 // props pre-placed in this map (entities or pack PNGs)
}

// A door linking two custom rooms. Put a door on one room's edge and a matching door on the target room's
// OPPOSITE edge (north door in room A <-> south door in room B), and leave wall gaps at both. A room reached
// only by doors should use `"exit": false` and an `exitDirection` pointing at a side with no door.
internal sealed class DoorDef
{
    public int Edge;                               // which side of THIS room the door sits on: 0=E 1=S 2=W 3=N
    public string Target;                           // required: the map id this door leads to (e.g. "mymod:crypt_inner")
    public float[] Node;                            // optional [x,y,w,h] px trigger box. Default: centered on that edge
}

// A custom creature: clones a vanilla creature's entity (its AI/animations/abilities) under a new id, then
// overrides a few things — name, max health, color tint. Summon it by `id` from a spell's `summon.creature`.
// NOTE (Tier 1): art is the base creature's (only a color tint is data-settable; a true new sprite is not).
// The base creature's AI is reused as-is.
internal sealed class CreatureDef
{
    public string Id;                  // required, namespaced (e.g. "necro:tough_skeleton") — summon by this id
    public string Base;                // required: vanilla creature to clone — see reference/creature-ids.txt (e.g. "SkeletonWarrior")
    public string Name;                // display name
    public Dictionary<string, string> NameLocalized; // optional langcode -> name (picked at load by game language)
    public float HealthMultiplier = 1f; // scale the base creature's max health (e.g. 3 = 3x)
    public int[] Color;                // optional [r,g,b] (0-255) multiplicative tint on the sprite
    public string Sheet;               // REPAINT the rig: PNG beside your pack replacing the base spac's FIRST
                                       // sprite sheet (1:1 layout — same size + frame grid). Single-sheet bases only.
    public Dictionary<string, string> Sheets; // multi-sheet rigs: vanilla sheet basename -> your PNG
                                       // (e.g. "skeletonidle": "bonelord_idle.png"); the mod logs the base
                                       // sheet names + sizes on the creature's first appearance.
    public string Controller;          // CUSTOM AI: a server controller name — a vanilla one
                                       // ("skeletonarcher", "wolf", ...) or one your addon DLL registered.
                                       // The base's CLIENT controller stays — visuals/animations unchanged.
    public Dictionary<string, string> ControllerParams; // overrides merged over the base controller's params
    public string ClientController;    // CLIENT visuals controller (e.g. "forge_creature_anim" for data-driven
                                       // animations — pairs with a cross-family `controller`). Unset = keep the
                                       // base's client controller (visuals unchanged). Anim params ride controllerParams.
}

// A placeable decoration buildable from the construction menu. Tier 1: it reuses an existing vanilla decoration's
// art (spawnedId) — no custom sprite needed. The recipe auto-unlocks on login so it always shows in the menu.
internal sealed class PlaceableDef
{
    public string Id;                              // required, namespaced (e.g. "necro:furniture:altar")
    public string Name;                            // display name in the build menu
    public string Description;                      // optional tooltip text
    public string SpawnedId;                        // required: vanilla decoration to place — see reference/decoration-ids.txt
    public string Icon = "sam";                     // build-menu icon (vanilla icon id or your custom icon)
    public Dictionary<string, int> Materials;       // build cost: resourceId -> amount (e.g. "resource:stone": 5). Empty = free
    public int Width = 2;                            // tile footprint width
    public int Height = 2;                           // tile footprint height
    public Dictionary<string, string> NameLocalized;        // optional langcode -> name
    public Dictionary<string, string> DescriptionLocalized; // optional langcode -> description
    // ----- Tier 3 (interactive furniture): set `controller` and the construction places a real ENTITY -----
    public string CloneEntity;                     // vanilla entity to clone — an EntityIds field name or a raw Guid
    public string Station;                         // CRAFTING STATION: opens a crafting window listing recipes
                                                   // whose `station` equals this id (comma list = multiple tabs).
                                                   // Needs cloneEntity; controller defaults to "forge_station".
    public string Controller;                      // preset ("forge_shrine" / "forge_lever" / "forge_dialogue") or a registered name
    public Dictionary<string, string> ControllerParams; // controller parameters (e.g. "heal": "25", "cooldown": "30")
    public bool Indestructible;                     // can't be demolished or destroyed (quest shrines, temples).
                                                   // Unload/save still works; only player removal is blocked.
    public string Yarn;                            // TALKING furniture: a .yarn dialogue file next to the pack
    public string StartNode = "Start";              // dialogue node it opens at
    public Dictionary<string, string> Actors;       // <<Actor key>> -> speaker display name
    public Dictionary<string, Dictionary<string, string>> ActorsLocalized; // langcode -> (key -> name)
    // ----- Tier 2 (custom sprite): ship a PNG next to the pack and the placed entity renders it -----
    public string Sprite;                     // PNG filename next to the pack (replaces the clone base's art)
    public int SpriteFrameWidth;              // frame cell width px (0/omitted = whole PNG is one frame)
    public int SpriteFrameHeight;             // frame cell height px
    public int[] SpriteFrames;                // optional looping ambient animation: sheet cell indices in play order
    public float SpriteFrameDuration = 0.15f; // seconds each animation frame shows
    public float[] SpriteOffset3D;            // optional render offset [x, up, depth] px
    // ----- COMPOSITE (multi-part building as ONE buildable): several PNG layers + per-part collision -----
    public List<PartDef> Parts;               // draw order = list order (later parts in front). Can't be
                                              // combined with yarn/station/controller. Part 0's collision
                                              // sits on the placed entity itself; parts 1+ with collision
                                              // become invisible blockers placed/demolished with it.
}

// One layer of a composite placeable (placeables[].parts).
internal sealed class PartDef
{
    public string Sprite;                     // required: PNG next to the pack (this layer's art)
    public float[] Offset;                    // [x, yUp] px from the anchor tile center, bottom-anchored
                                              // (flat parts: [x, depth] on the ground plane). Default [0,0].
    public float Depth;                       // draw-sort bias px between this composite's parts and nearby
                                              // entities (positive = sorts deeper/behind). Default 0.
    public float Scale = 1f;                  // render scale
    public float Opacity = 1f;                // 0..1
    public int Angle;                         // degrees, clockwise
    public bool Flip;                         // mirror horizontally
    public bool Flat;                         // lay flat on the ground (rugs, floors, shadows)
    public int FrameWidth;                    // sheet cell size px (0/omitted = whole PNG is one frame)
    public int FrameHeight;
    public int Frame;                         // which sheet cell to draw (default 0)
    public float[] Collision;                 // [w, h] px box this part blocks (omit = walk-through layer)
    public float[] CollisionOffset;           // [x, y] px WORLD offset of that box (y = down-screen/south).
                                              // Default: [offset.x, 0].
}

// A world quest: it activates automatically, tracks objectives, and (optionally) unlocks crafting recipes on
// completion. No dialogue/NPC needed.
internal sealed class QuestDef
{
    public string Id;                              // required, namespaced
    public string Name;                            // quest title
    public string Description;                     // quest text
    public string Icon;                            // optional icon id
    public Dictionary<string, string> NameLocalized;        // optional langcode -> name
    public Dictionary<string, string> DescriptionLocalized; // optional langcode -> description
    public List<ObjectiveDef> Objectives;          // required: one or more goals (all must be met)
    public List<string> RewardRecipes;             // crafting recipe ids unlocked when finished
    public Dictionary<string, int> RewardItems;    // itemId -> amount granted on completion (server-side)
    public bool Repeatable;
}

internal sealed class ObjectiveDef
{
    public string Type = "kill";                   // kill / collect / loot / craft / harvest
    public string Target;                          // kill: a creature name (creature-ids.txt); others: an item id
    public int Amount = 1;                          // how many
}

// A new playable class — appears in the character-creation "profession" picker. Its effect is applied once at
// character creation (skill head-start + starting gear/favour); there's no persistent per-class behavior.
internal sealed class ClassDef
{
    public string Id;                              // required, namespaced (e.g. "necro:class:necromancer")
    public string Name;                            // display name shown in the picker
    public Dictionary<string, string> NameLocalized;        // optional langcode -> name
    public string BonusSkill = "skill:swords";     // a skill set to level 5. MUST be a valid skill id (else char
                                                   // creation errors) — e.g. skill:swords, skill:woodcutting, skill:mining.
    public Dictionary<string, int> SkillBonuses;   // extra skillId -> starting level
    public Dictionary<string, int> StartingInventory; // itemId -> amount (yours or vanilla)
    public List<string> StartingClothes;           // equipped clothing/armor ids at start
    public int? StartingFavourPoints;              // favour points to start with
}

// Add an item to an existing loot table (a mob's, chest's, etc.) with a percentage chance. Server-authoritative
// (the host's drop tables) — fits the co-op model. The table must already exist (it's the mob's table id).
internal sealed class DropDef
{
    public string Table;                           // required: existing drop-table id (e.g. "enemy:fallen") — see reference/droptable-ids.txt
    public string Item;                            // required: item id to drop (yours or vanilla)
    public float Chance = 5f;                      // drop chance in PERCENT (e.g. 5 = 5%)
    public int Min = 1;                            // min stack dropped
    public int Max = 1;                            // max stack dropped
    public string Aura;                            // optional: aura id applied to the dropped item
}

// A custom sprite-animation effect: a PNG sheet of frames + the order to play them. Register a NEW id (for
// future triggers) OR a VANILLA vfx id to OVERRIDE/reskin it (visible immediately — e.g. "blood_explosion").
internal sealed class VfxDef
{
    public string Id;                              // required: register id (new, or a vanilla vfx id to override)
    public string Sheet;                           // required: PNG sheet next to the pack (a grid of frames)
    public int FrameWidth = 48;                    // cell size in the sheet
    public int FrameHeight = 48;
    public int[] Frames;                           // sheet cell indices, in play order (e.g. [0,1,2,3,4])
    public int FrameDuration = 3;                  // ticks each frame shows (60 ticks/sec)
    public float Scale = 1f;                       // render scale
}

// A custom palette = a PNG color ramp: row 0 holds the SOURCE/key colors of the sheet you're recoloring,
// each row below holds the replacement colors for that variant. Reference via display.palette/paletteRow.
internal sealed class PaletteDef
{
    public string Id;                              // required, namespaced (e.g. "acme:fire"); use as display.palette
    public string Sheet;                           // required: PNG ramp next to the pack (rows = variants)
}

internal sealed class RecipeDef
{
    public string Id;                              // required, unique
    public string Crafted;                         // required: item id this recipe produces
    public int Amount = 1;                          // how many produced
    public string Station;                          // null = basic; or campfire / cauldron / clay-barrel / war-table
    public Dictionary<string, int> Ingredients;     // itemId -> count required
}

internal sealed class SpellDef
{
    public string Id;                              // required, unique (namespace it)
    public string DisplayName;                     // literal spell name (the default/fallback)
    public Dictionary<string, string> DisplayNameLocalized; // per-language name: { "tr_TR": "...", "de_DE": "..." }
    // A projectile damage spell by default — reuses a built-in spell behavior (don't invent new ones).
    public string SpellTypeId = "spelltype:single_projectile_attack";
    public string ProjectileDisplayId = "arrow_spell";  // reuse a vanilla display id (e.g. fire_spell_new)
    public string ParticleTrail;                   // optional emitter id (e.g. small_fireball_blue)
    public string CastSound;                       // optional FMOD event (e.g. event:/spells/cast_spell_cosmo)
    public float Duration = 1.5f;
    public float ProjectileVelocity = 320f;
    public float CollisionRadius = 6f;
    public AttackDef Attack;                        // the damage (omit = harmless projectile)
    // Chain: when THIS spell hits (or ends), cast other spells — e.g. an arrow that spawns lightning on impact,
    // a fireball that also ignites. Each chained spell is another spell id (yours or vanilla).
    public List<ChainDef> Chain;
    // Summon: makes this a SUMMON spell — spawns a creature that fights for the caster (a necromancer's skeletons).
    public SummonDef Summon;
    // If set, this becomes an instant single-target spell that applies an aura (e.g. a burn/poison DoT)
    // to the hit target. Use with a weapon's onHit proc for "ignite on hit" effects.
    // e.g. "aura:general:burning", "aura:general:weak_poisoned".
    public string ApplyAura;
    public SpellRequiresDef Requires;           // gate casting behind skill level / favour / quest / class
    // SpellsPlus behavior blocks — set EXACTLY ONE; overrides the default projectile behavior.
    // (summon / applyAura above still take priority.)
    public AoeDef Aoe;                          // lingering damage circle on the ground
    public ExplosionDef Explosion;              // instant blast at the target point
    public ConeDef Cone;                        // melee-style cone in front of the caster
    public LineDef Line;                        // damaging line/beam in the aim direction
    public MultiProjectileDef MultiProjectile;  // fan of projectiles
    public HealDef Heal;                        // self heal / regen (server-authoritative)
    public MassHealDef MassHeal;                // heal all players in a radius (server-authoritative)
}

internal sealed class SummonDef
{
    public string Creature;                        // required: a creature name from reference/creature-ids.txt (e.g. "JumpingSkull", "SkeletonWarrior")
    public int Count = 1;                           // how many to spawn per cast (e.g. 2 skeletons).
    public int MaxActive;                           // 0 = unlimited; else cap on how many of THIS summon stay alive
                                                   // (the oldest despawns when a new one would exceed the cap).
    public string Behavior = "guardian";            // "guardian" = stays where summoned (its native AI fights what
                                                   //   comes near). "companion" = teleports back to you when it has
                                                   //   no enemy nearby and you've wandered off, so it stays with you.
    public string EntityType = "PlayerSummon";     // PlayerSummon = fights for you. Hostile/Friendly/Neutral/HostileSummon also valid.
    public bool DismissOnUnequip;                   // despawn this spell's summons when the item that casts it is unequipped
    public Dictionary<string, string> Parameters;  // advanced: extra spawn params passed to the creature
}

internal sealed class ChainDef
{
    public string Event = "OnAnyHit";              // OnEntityHit / OnTerrainHit / OnAnyHit / OnSpellEnded
    public string Spell;                           // required: the spell to cast when the event fires
    public string Target = "Target";               // Target (what was hit) / Self / TargetGround / ProjectilePosition
}

internal sealed class AttackDef
{
    public float Multiplier = 1f;
    public string SkillId;                          // skill that scales this spell's damage + gains XP from it
    public string AttackType = "Magic";             // Magic / Melee / Ranged
    public bool CanCrit = true;
    public int MaxTargets = 1;
    public float Knockback;
    // Damage by type. Keys: true, slashing, piercing, bludgeoning, pyro(=fire), chloro(=nature),
    // aqua(=water), cosmo, necro. e.g. { "pyro": 12 }
    public Dictionary<string, float> MinDamage;
    public Dictionary<string, float> MaxDamage;
}

internal sealed class ItemDef
{
    public string Id;                              // required, unique (namespace it, e.g. "cf:flameblade")
    public string Name;                            // literal display name (the default/fallback)
    public Dictionary<string, string> NameLocalized;        // per-language name: { "tr_TR": "...", "de_DE": "..." }
    public string Description;                      // literal tooltip description (default/fallback)
    public Dictionary<string, string> DescriptionLocalized; // per-language description, keyed by language code
    public string Icon = "sam";                    // existing icon id to reuse ("sam" = placeholder)
    public int MaxStack = 1;
    public int? Tier;
    public string Flags;                            // ItemFlag name(s), comma-separated (e.g. "Food")
    public long? Price;                             // optional: sell/buy value (makes it tradable)
    public LightDef Light;                          // optional: makes it emit light (torch)
    public EquipDef Equippable;                     // optional: makes it wearable gear
    public UsableDef Usable;                        // optional: makes it a consumable / scroll
}

internal sealed class LightDef
{
    public float Radius = 128f;   // in pixels — the vanilla torch is 128
    public float Strength = 1f;
}

internal sealed class EquipDef
{
    public string EquipmentType;                    // Helmet/Armor/Boots/Trinket/Weapon/Offhand/Back/...
    public string Material = "Steel";               // Flint/Copper/Bronze/Iron/Silver/Gold/Steel/Cheat/Rusted
    public string EntityAuraId;                     // optional: existing aura id giving the stat effect
    // Stat bonuses by stat key, e.g. "Health", "CritChance", "MovementSpeed", "MeleeDamage", "Armor",
    // "AttackSpeed", "MagicDamage", "PickaxePower", "EnergyRegeneration", resistances, ...
    public Dictionary<string, StatBonusDef> StatBonuses;
    // Makes this an offhand spell-tome / scroll: equip it, then attack to cast SpellId (the working
    // pattern for player-cast spells). Set equipmentType to "Offhand".
    public TomeDef SpellTome;
    // Makes this a melee/ranged weapon (damage from WeaponStats). Melee: equipmentType "Weapon".
    // Bow/Crossbow: equipmentType "Offhand" (they use equipped ammo).
    public WeaponDef Weapon;
    public ShieldDef Shield;                        // makes it a shield (equip Offhand)
    public string DisplayId;                        // held/equipped visual: reuse a vanilla "cdd:..." id
    public DisplayDef Display;                       // OR compose a held look from an existing skin shape + palette recolor
}

// Custom held-weapon look: either (recolor) an EXISTING shape + palette, or (custom art) your own sprite sheet.
internal sealed class DisplayDef
{
    public string SkinName;                          // recolor: existing shape ("Sword","Spear","Hammer"...). Custom: the new skin's name (auto if blank).
    public int SkinTag = 8;                          // 8 = main-hand weapon, 11 = offhand
    public string Palette = "weapon";                // recolor only: weapon / metal / leather / fabric / details
    public int PaletteRow;                           // recolor only: 0-9 variant
    // Custom art: a .png next to the pack, rig-aligned. Main-hand: 1104x240, 48x48 frames (23x5).
    public string Sheet;
    public int FrameWidth = 48;
    public int FrameHeight = 48;
}

internal sealed class ShieldDef
{
    public float BlockStrength = 0.6f;              // 0..1 fraction of damage blocked
    public float BlockArcDegrees = 60f;
    public float StrongBlockArcDegrees = 50f;
    public float EnterCost;
}

internal sealed class WeaponDef
{
    public string Class = "Sword";                  // Sword/Spear/Dagger/Sledgehammer/Bow/Crossbow/Javelin/Fists/Shield
    public float SwingTimer = 1f;
    public float Knockback;
    public float AttackRange;
    public float EnergyCost;
    public Dictionary<string, float> MinDamage;     // by damage type (see AttackDef)
    public Dictionary<string, float> MaxDamage;
    public CastDef CastSpell;                        // weapon casts a spell — on hit (procs) or on use (staff swing)
    public string TrailColor;                        // tint the swing trail, e.g. "#ff5500" (fire) — your weapon, your color
    public string TrailSheet;                        // custom-SHAPE swing trail: path to a repainted slash sheet (PNG)
                                                     // beside your pack. Repaint the vanilla trail sheet the mod logs on
                                                     // your first swing (e.g. slash.png); keep the frame grid. Combine
                                                     // with TrailColor to also tint it. See PACKS.md "Custom trail art".
    public bool HideTrail;                           // suppress the swing-trail VFX entirely (e.g. a staff/wand that
                                                     // doesn't melee — no slash effect). Overrides TrailColor/TrailSheet.
    public float StunPower;                           // 0..1 stagger applied on hit (e.g. 0.5 = maces stagger). 0 = none.
    public float MovementFactor = 1f;                 // move-speed multiplier WHILE attacking/charging. 1 = free move,
                                                     // <1 = slower (heavy weapon), 0 = rooted. (Mainly affects charge.)
    public string DamageVfx = "Normal";               // hit-impact VFX size: Normal / Small / None.
    public string SkillId;                            // the skill this weapon trains + scales with (e.g. your
                                                     // pack's "skill:runeology"); default = the class's skill.
    public string HitVfx;                             // extra VFX played at the hit position when this weapon damages
                                                     // an enemy: one of your `vfx` ids, or a vanilla spac id (hit:crit…).
    public float HitVfxScale = 1f;                    // scale for hitVfx (sprite-animation vfx only)
    public float SpecialEnergyCost;                   // energy for the charged/special attack (see CastSpell.chargedSpellId).
    public bool TwoHanded;                            // main-hand only: while equipped, the offhand slot is kept
                                                      // empty — equipping either side ejects the offhand to the bag
                                                      // (bag full = dropped at your feet). Enforced host-side.
    public float AttackArcDegrees;                    // melee swing-cone width in DEGREES (vanilla = 144) — e.g. 300 =
                                                      // near-circle swipe. 0 = vanilla. Cone melee only (sword/dagger/
                                                      // sledge swings); spears stab in a line and ignore it.
    public float AttackArcOffsetPx;                   // forward offset (px) of the swing cone's origin — negative pulls
                                                      // the hit zone toward the body (vanilla daggers use -24). 0 = vanilla.
    public List<ComboStepDef> ComboSteps;             // per-combo-step tuning (index = step). Sword: 3 steps; dagger:
                                                      // the array CYCLES per stab; sledgehammer: only step 0 (normal
                                                      // swing; the charged smash stays vanilla). Spears: inert.
    // Stat bonuses ALSO work on weapons (same `statBonuses` block as gear) — e.g. +crit / +health while held.
}

internal sealed class CastDef
{
    public string SpellId;                          // spell the weapon casts
    // "OnHit" = procs when the attack connects (e.g. ignite-on-hit).
    // "OnUse" = casts on every attack/swing — use this for a STAFF (melee class = swing animation,
    //           launching a projectile spell each swing).
    public string CastType = "OnHit";
    public string Event = "OnEntityHit";            // OnEntityHit (enemy) or OnAnyHit (incl. terrain)
    public float? Probability;                       // proc chance 0..1 (e.g. 0.25 = 25%). Omit = always cast.
    public string Target = "Target";                 // who the cast affects: Target (the enemy hit) or Self (you —
                                                     // for heal-on-hit / haste-on-hit lifesteal-style effects).
    public string ChargedSpellId;                    // optional: a stronger spell cast by the CHARGED/special attack
                                                     // (hold the attack). Pair with weapon.specialEnergyCost + chargeTime.
    public float ChargeTime = 0.3f;                  // seconds to charge before the charged spell fires.
}

internal sealed class TomeDef
{
    public string SpellId;                          // required: spell id to cast on attack
    public string Target = "Target";                // where the spell lands: Target / Self / TargetGround
                                                   // (heal spells need "Self" — vanilla heal scrolls do this)
    public string ChargedSpellId;                   // optional: special-attack (charged) spell
    public float ChargeTime = 0.3f;
    public float SwingTimer = 0.5f;
    public float EnergyCost;                        // 0 = free to cast
    public float SpecialEnergyCost;
    public float MovementFactor = 0.5f;
}

internal sealed class StatBonusDef
{
    public float Additive;                          // flat add (most common: Health=100, CritChance=0.1)
    public float Multiplier;                        // final multiplier
    public float AdditiveMultiplier;
    public float BaseMultiplier;
    public float BonusMultiplier;
}

internal sealed class UsableDef
{
    public string Type = "Special";                 // Special or Healing
    public string SpellId;                          // explicit spell id to cast on use (advanced)
    public int UsesMax = 1;                         // 0 = infinite
    public float Cooldown;
    // Heal shortcut: if set (and Type=Healing), the item casts "spell:heal" with these amounts on use
    // (an instant heal/food potion — usable, hotbar-able, no separate eat animation).
    public float InstantHeal;
    public float SlowHeal;
    public float SlowHealTime;
}

// ---- JSON sub-blocks (referenced from SpellDef; field additions live in ContentPack.cs) ----

// Lingering damage circle (spelltype:area_of_effect). Damage = the spell's `attack` block, applied
// every tickSeconds to everything inside; `duration` is how long the circle lasts (vanilla: 10).
// Ground art: set projectileDisplayId to "aoe_fire" / "aoe_poison" and particleTrail to an emitter
// like "flame_aoe" / "poison_aoe_48". Tip: chain it off a projectile at target "TargetGround"
// (that's exactly how the vanilla fireball drops its fire pool).
internal sealed class AoeDef
{
    public float Radius = 48f;            // circle radius in pixels (vanilla fire pool: 48)
    public float Height = 16f;            // vertical reach of the circle (vanilla: 16)
    public float TickSeconds = 0.5f;      // seconds between damage ticks (vanilla: 0.5)
    public string ApplyAura;              // optional: aura applied on each tick (e.g. "aura:general:burning")
    public bool Lightsource = true;       // emit light (fire pools do)
    public bool AffectsEnvironment;       // also damage destructibles/terrain props
    public bool ExplosiveKnockback;       // knock targets away from the center
}

// Instant blast at the target point (spelltype:explosion). Damage/knockback = the `attack` block.
// The engine draws its own explosion (sparks, dirt, crack, light) sized to radius; spacVfx adds a
// sprite animation on top (e.g. "vfx_explosion1"). projectileDisplayId "no_vfx" mutes ALL visuals.
internal sealed class ExplosionDef
{
    public float Radius = 32f;            // blast radius in pixels (vanilla fireball explosion: 32)
    public string SpacVfx;                // optional extra spac animation (e.g. "vfx_explosion1")
}

// Melee-style cone in front of the caster (spelltype:cone_attack). Hits everything in the cone for
// the spell's duration (keep duration short, ~0.15-0.5s, like weapon swings). Invisible by itself —
// pair with castSound; range scales with the caster's attack-range stat.
internal sealed class ConeDef
{
    public float Range = 48f;             // cone length in pixels (boss rush: 48, wingflap: 160)
    public float AngleDegrees = 90f;      // full cone width in degrees (boss rush ~115°, wingflap ~34°)
    public float DistanceOffset;          // shift the cone forward/back (wingflap uses -208)
    public float TickSeconds;             // 0 = hit each target once; >0 = re-hit every N seconds
}

// Damaging line/beam in the aim direction (spelltype:line_attack — the phoenix's firebreath).
// Set particleTrail to draw it (emitter spread along the line, e.g. "flame_aoe"). growSeconds > 0
// extends the line from 0 to its full length over that time (requires particleTrail).
internal sealed class LineDef
{
    public float Length = 128f;           // line length in pixels (firebreath grows to 256)
    public float Width = 25f;             // line width (firebreath: 25)
    public float GrowSeconds;             // 0 = full length immediately; >0 = firebreath-style growth
    public float TickSeconds = 0.6f;      // seconds between re-hits while standing in it (firebreath: 0.6)
}

// Fan of projectiles centered on the aim direction (spelltype:multiple_projectile_attack — the
// bow's multishot fires 3 over 24°). Each projectile uses the spell's normal projectile fields
// (projectileDisplayId, particleTrail, projectileVelocity, collisionRadius) and `attack` damage.
internal sealed class MultiProjectileDef
{
    public int Count = 3;                 // how many projectiles
    public float SpreadDegrees = 24f;     // total fan width (evenly spaced; bow multishot: 24)
}

// Self heal / regen (spelltype:heal — the Volumen Vitae scroll's regen). Server-authoritative,
// scaled by the caster's HealingDoneModifier. Cast it from an item with target "Self" (a weapon's
// castSpell target, or a usable) — heals need a target and self is the vanilla-scroll behavior.
internal sealed class HealDef
{
    public float Amount;                  // instant heal (HP)
    public float RegenAmount;             // total HP healed over regenSeconds (vanilla regen: 20)
    public float RegenSeconds;            // regen duration (vanilla: 10)
}

// Heal every PLAYER near the cast point (spelltype:mass_heal — the mass heal scrolls). Set amount
// (flat HP) or percentOfMaxHealth (0.25 = 25%, wins when both set). Players only — not summons.
internal sealed class MassHealDef
{
    public float Amount;                  // flat HP per player
    public float PercentOfMaxHealth;      // 0.25 = heal 25% of each player's max health (vanilla tier 0)
    public float Radius = 80f;            // radius around the cast point (vanilla: 80)
}

// Casting requirements for a spell — checked when the player casts (a blocked cast shows a red message).
// All conditions set must pass. Applies to the local player's casts of this spell via tomes/weapons.
internal sealed class SpellRequiresDef
{
    public string Skill;                        // skill id (vanilla or your pack's), with...
    public int SkillLevel = 1;                  // ...the minimum level
    public string Favour;                       // a favour-tree node the player must have unlocked
    public string Quest;                        // a quest (world or personal) the player must have finished
    public string Class;                        // a playable-class id the character must be
    public string Message;                      // optional custom "can't cast" text
}

// A server-side world event. `type` picks one of four behaviors; the other fields apply per type:
//   waveDefense  — needs map + waves;   bossRush — needs map + bosses (one at a time, next on kill);
//   huntContest  — durationSeconds kill contest, optional target creature, starts per schedule (or
//                  whenever minPlayers are online if schedule is omitted);
//   scheduledRaid — fires a VANILLA raid (raidData id) at the town nearest a player every everyNDays.
// Creature ids = a pack creature id or a Shared.EntityIds name (reference/creature-ids.txt) — same as
// a spell's summon.creature. Host/server-side only; clients just see chat + the spawns.
internal sealed class EventDef
{
    public string Id;                   // required, namespaced (e.g. "mymod:waves")
    public string Name;                 // shown in chat; defaults to the id tail after ':'
    public string Type;                 // required: waveDefense | bossRush | huntContest | scheduledRaid
    public string Map;                  // waveDefense/bossRush: a map in THIS pack (pvp NOT required)
    public List<WaveDef> Waves;         // waveDefense: the waves, in order
    public List<string> Bosses;         // bossRush: creature ids, fought one at a time
    public string Target;               // huntContest: creature id to hunt (omit = any creature kill counts)
    public int DurationSeconds = 600;   // huntContest: contest length
    public string Schedule;             // night | day | newDay — when it auto-starts (huntContest: omit =
                                        // run whenever minPlayers online; scheduledRaid default: newDay)
    public int EveryNDays = 1;          // scheduledRaid: at most one raid per N in-game days
    public string RaidData;             // scheduledRaid: vanilla raid id (e.g. "raid:satyr")
    public bool Immediate;              // scheduledRaid: war-declaration style (short approach, marches at once)
    public string Reward;               // item id — waves/boss: every survivor; hunt: the winner
    public int RewardAmount = 1;
    public int MinPlayers = 1;          // players needed (in the map's world for waves/boss; online for hunt/raid)
    public bool AnnounceWaves = true;   // chat lines per wave/boss
    public int CooldownSeconds = 300;   // huntContest without a schedule: pause between contests
    // EventsCompetitive (teamBattle / koth / race — see EventsCompetitive.cs):
    public int KillsToWin = 10;             // teamBattle: team kills needed to win
    public float[] Point;                   // koth: hill center [x,y] in map px (same space as doodads)
    public float HillRadius = 80f;          // koth: hill radius in px
    public int ScoreToWin = 60;             // koth: hill-seconds needed to win
    public List<float[]> Checkpoints;       // race: ordered [x,y] checkpoints in map px
    public float CheckpointRadius = 40f;    // race: "passed" within this many px
    public int TimeLimitSeconds = 300;      // race: abort if nobody finishes in time
}

internal sealed class WaveDef
{
    public Dictionary<string, int> Creatures;   // creature id -> count
    public int DelaySeconds = 10;               // breather before this wave spawns
}

// A custom CITIZEN TYPE (a town job-worker). Vanilla types are stat templates with an icon; on top of that
// we add: arrival weighting (vanilla only ever brings "citizen:generic" to town — we widen that roll),
// an optional FIXED person name (loc key "<id>*citizen:name" via the strings pipeline), starting job levels
// (like the legendary wild recruits), and an optional FIXED look (face/wardrobe/cdd: worn looks).
internal sealed class CitizenDef
{
    public string Id;                              // required, namespaced (e.g. "mymod:blacksmith_anna")
    public string Name;                            // optional FIXED person name ("Anna"); omit = vanilla random names
    public Dictionary<string, string> NameLocalized;   // optional langcode -> name
    public string Icon = "ui:citizen";             // slot/tooltip portrait icon (vanilla id or your pack icon)
    public Dictionary<string, float> Stats;        // the six citizen stats; shorthand ("efficiency", "expertise",
                                                   // "happiness", "foodCost", "experienceGain", "loyaltyGain") or the
                                                   // raw "Citizen_*" ids. Unset keys default to the pack-citizen baseline
                                                   // (8 / 8 / 0 / 10 / 1 / 1). Vanilla range is roughly 8-13.
    public int Weight = 100;                       // arrival chance vs vanilla generic (weight 100): each new-citizen
                                                   // roll picks proportionally. 0 = registered but never arrives.
    public Dictionary<string, int> JobLevels;      // starting job levels 0-4, e.g. { "job:blacksmith": 4 }
    public CitizenLookDef Look;                    // optional FIXED appearance; omit = the normal random face
}

// A fixed appearance. Every field is optional — set fields are pinned, unset fields keep the citizen's own
// Guid-seeded random roll (so you can pin just the wardrobe and leave the face to chance). Byte indexes are
// the same parts / palette rows as player character creation; displayIds are CharacterDisplayData ("cdd:")
// worn looks — vanilla ones or your own playerSkins.looks.
internal sealed class CitizenLookDef
{
    public bool? Male;                             // pins gender (body + name pool)
    public int? Hair;                              // hair style index (skin tag 0)
    public int? HairColor;                         //   "hair" palette row
    public int? EyeColor;                          //   "eyes" palette row
    public int? Body;                              // skin tone — "skin" palette row (vanilla rolls 0-6)
    public int? Tunic;                             // tunic style index (skin tag 4)
    public int? TunicColor;                        //   "fabric" palette row
    public int? ShoeColor;                         //   "leather" palette row
    public List<string> DisplayIds;                // worn looks drawn on top, e.g. ["cdd:armour:flamines"]
}

// Edits to an EXISTING citizen type (vanilla ids: citizen:generic, citizen:blacksmith, citizen:lumberjack,
// citizen:leatherworker, citizen:merchant — or another pack's, if that pack is a dependency).
internal sealed class CitizenOverrideDef
{
    public string Icon;                            // replace the portrait icon
    public Dictionary<string, float> Stats;        // merged per-key into DefaultStats (same keys as CitizenDef.stats);
                                                   // affects citizens created AFTER the change (existing ones keep
                                                   // their saved stats)
    public int? Weight;                            // only meaningful on "citizen:generic": its share of the arrival
                                                   // roll (default 100). 0 = only pack citizens arrive.
}

// One combo step of a melee weapon (weapon.comboSteps[i] = the i-th hit of the chain). Each field 0 / omitted = vanilla.
internal sealed class ComboStepDef
{
    public float DamageMultiplier;     // absolute damage multiplier for THIS hit (replaces the class's stance
                                       // multiplier, e.g. the sword's 3rd-hit 1.25). 0 = vanilla.
    public float ArcDegrees;           // swing-cone width for THIS hit, in degrees (overrides the weapon's flat
                                       // attackArcDegrees for this step). 0 = flat arc / vanilla.
    public float SwingTimerScale = 1f; // scales THIS hit's swing duration (2 = slower, 0.5 = faster). 1 = vanilla.
}

// A new spac (sprite-animation collection) cloned from a vanilla one: swapped sheets and/or retimed/resequenced
// frames. The id becomes usable anywhere a vanilla spac/vfx id is accepted (hitVfx, spell spacVfx, ...).
internal sealed class SpacDef
{
    public string Id;                            // required, namespaced (e.g. "mymod:bigslash")
    public string Base;                          // required: the vanilla spac id to clone (e.g. "vfx_swordslash")
    public Dictionary<string, string> Sheets;    // sheet basename -> your PNG beside the pack (same frame grid);
                                                 // a wrong name logs the spac's real sheet names
    public float FrameDurationScale = 1f;        // multiplies every frame's duration (2 = half speed, 0.5 = double)
    public int[] Frames;                         // optional resequence: indices into the BASE frame list, in play
                                                 // order (meant for single-animation vfx spacs)
    // --- from-scratch additions (entries WITHOUT `base`): brand-new animations from your own PNGs ---
    public int Directions = 1;                         // 1 (no facing), 4 (rows E,S,W,N) or 8 (rows E,SE,S,SW,W,NW,N,NE)
    public int FrameWidth;                             // cell width px (0 = whole PNG is one frame)
    public int FrameHeight;                            // cell height px
    public int FeetRow;                                // px from cell TOP where the art's feet touch the ground
                                                       // (anchors the sprite; 0 = cell center, for centered art)
    public Dictionary<string, SpacAnimDef> Animations; // animation name -> frames/timing (SpacAnimDef in SpacForge.cs)
}

// Retime one player animation state (playerAnimations: { "attack1": { "frameDurationScale": 2 } }).
internal sealed class PlayerAnimDef
{
    public float FrameDurationScale = 1f;        // 2 = half speed, 0.5 = double speed (must be > 0, != 1)
}

// A prop pre-placed in one of this pack's maps: a vanilla/pack ENTITY by name, or a pack PNG that becomes
// an inert decor doodad (optionally with a collision box and an ambient animation). Consumed by DoodadsFeature.
internal sealed class DoodadDef
{
    public string Id;                  // optional stable id (else derived from the map id + list index)
    public string Entity;              // an EntityIds name ("CampFire"), a raw Guid, a pack creature id,
                                       // or a pack cloneEntity-placeable id — see reference/entity-ids.txt
    public string Sprite;              // OR: PNG filename next to the pack (the mod makes a doodad of it)
    public float[] Position;           // required [x,y] px from the map's top-left
    public float[] Collision;          // sprite doodads: [w,h] px blocking box, centered. Omit = walk-through
    public int SpriteFrameWidth;       // frame cell width px (0/omitted = whole PNG is one frame)
    public int SpriteFrameHeight;      // frame cell height px
    public int[] SpriteFrames;         // optional looping ambient animation: sheet cell indices in play order
    public float SpriteFrameDuration = 0.15f; // seconds each animation frame shows
    public float[] SpriteOffset3D;     // optional render offset [x, up, depth] px
}

// JSON DTO for spacs[].animations.<name> (referenced by SpacDef.Animations once that field is added).
internal sealed class SpacAnimDef
{
    public int[] Frames;          // cell indices, in play order (directions>1: column indices; see contract)
    public float TickLength = 8f; // ticks each frame shows (60 ticks = 1 second); 8 ≈ 7.5 fps
    public bool Loop;             // advisory only: looping is chosen where the spac is USED (vfx id playback
                                  // is always one-shot; forge_creature_anim uses anim-map :loop/:once flags
                                  // + the idle/walk/run/... name heuristic) — kept so authors can document intent
    public string Sheet;          // multi-sheet spacs: which `sheets` key this animation draws from
                                  // (default: the first sheet) — the vanilla one-sheet-per-animation pattern
}

// A new town job-building cloned from a vanilla one. The base fixes maps/footprint/slots/controller; you
// re-skin the identity and economy around it.
internal sealed class TownBuildingDef
{
    public string Id;                              // required, namespaced ("mymod:grand_bakery") — the construction id;
                                                   // the building data id becomes "building:<id>"
    public string Base;                            // required: vanilla BuildingData id to clone ("building:bakery:0",
                                                   // "building:blacksmith:0", "building:farmstead:1", ...)
    public string Name;                            // build-menu name (loc key "<id>*construction:name")
    public Dictionary<string, string> NameLocalized;
    public string Description;                     // build-menu description
    public Dictionary<string, string> DescriptionLocalized;
    public string Icon;                            // build-menu/building icon (vanilla id or pack icon); default: base's
    public Dictionary<string, int> Materials;      // construction cost, e.g. { "resource:lumber": 5 }; default: base's
    public float BuildProgress = -1f;              // hammering effort; default: base construction's
    public float Appeal = -1f;                     // MaxBuildingAppeal (decoration cap); default: base's
    public string Job;                             // optional: replace the slot job ("job:blacksmith") — the worker
                                                   // type employed inside; default: base's (bakery -> job:baker)
    public int? TownDefence;                       // optional town defence contribution; default: base construction's
}

// A new work recipe (what a citizen produces at its job-building), unlocked at a job level.
internal sealed class WorkRecipeDef
{
    public string Id;                              // required ("recipe:mymod:garum_bread" — vanilla convention is "recipe:")
    public string Job;                             // required: the job that learns it ("job:baker", "job:blacksmith", ...)
    public int Level = 1;                          // job level that unlocks it (appended to group "<job>:<level>")
    public string Output;                          // required: crafted item id (or construction resource id, see below)
    public int Amount = 1;                         // produced per completion
    public bool ResourceOutput;                    // true = Output is a construction RESOURCE ("resource:lumber" tier)
    public Dictionary<string, int> Ingredients;    // item id -> count, pulled from the building's storage
    public Dictionary<string, int> Resources;      // construction resource -> count (internal resource storage)
    public float Progress = 30f;                   // work effort (bread 30, armor ~60-150)
    public float Experience = 3f;                  // job XP per completion
    public bool Fuel;                              // true = needs the building's furnace lit (bakery/blacksmith have one)
    public string Name;                            // optional display name; default: the output item's name
    public Dictionary<string, string> NameLocalized;
    public string Description;                     // optional tooltip text
    public Dictionary<string, string> DescriptionLocalized;
    public string Icon;                            // optional; default: the output item's icon
    public bool Unrepeatable;                      // true = can't be set to repeat in the work queue
}
