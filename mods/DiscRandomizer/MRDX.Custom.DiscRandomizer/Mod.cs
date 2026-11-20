using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using MRDX.Base.Mod.Interfaces;
using MRDX.Custom.DiscRandomizer.Configuration;
using MRDX.Custom.DiscRandomizer.Template;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X86;
using Reloaded.Mod.Interfaces;
using CallingConventions = Reloaded.Hooks.Definitions.X86.CallingConventions;

namespace MRDX.Custom.DiscRandomizer;

// Las firmas y offsets del santuario vienen del mod de la comunidad "MRDX.Game.MoreMonsters"
// (ranchercommunity/mrdx_reloaded). Orden de llamadas al crear un monstruo desde un disco:
//   1. EarlyShrine            -> el jugador confirma la canción (songID en self+0xCC)
//   2. ShrineUnlockedChecker  -> ¿está desbloqueado? (0 = el santuario falla)
//   3. WriteSDATAMemory       -> antes de la animación: raza que se usa para el modelo
//   4. MysteryStatUpdate      -> después de cargar los stats: aquí escribimos los nuestros

[HookDef(BaseGame.Mr2, Region.Us,
    "55 8B EC 6A FF 68 ?? ?? ?? ?? 64 A1 ?? ?? ?? ?? 50 83 EC 50 A1 ?? ?? ?? ?? 33 C5 89 45 ?? 53 56 57 50 8D 45 ?? 64 A3 ?? ?? ?? ?? 8B F9")]
[Function(CallingConventions.MicrosoftThiscall)]
public delegate void H_EarlyShrine(nuint self, nuint p2);

[HookDef(BaseGame.Mr2, Region.Us, "55 8B EC 53 8B 5D ?? 8A C3")]
[Function(CallingConventions.MicrosoftThiscall)]
public delegate nuint H_ShrineMonsterUnlockedChecker(nuint self, int unk1, int unk2);

[HookDef(BaseGame.Mr2, Region.Us, "55 8B EC 83 EC 28 0F B6 41 ??")]
[Function(CallingConventions.Fastcall)]
public delegate void H_WriteSDATAMemory(nuint self);

[HookDef(BaseGame.Mr2, Region.Us, "56 8B F1 57 8A 86 ?? ?? ?? ??")]
[Function(CallingConventions.Fastcall)]
public delegate int H_MysteryStatUpdate(nuint self);

// Diagnóstico: se llama al pasar el monstruo actual al congelador (firma de MoreMonsters/FreezerHandler).
[HookDef(BaseGame.Mr2, Region.Us, "56 33 D2 8B F1")]
[Function(CallingConventions.Fastcall)]
public delegate void H_FreezerWriteFreezer(nuint self, int unk1, int unk2);

public class Mod : ModBase
{
    private const int MonsterOffset = 0x37667C; // dirección del monstruo actual (desde el inicio del .exe)

    private readonly ILogger _logger = null!;
    private readonly IModConfig _modConfig = null!;
    private Config _config = null!;

    private readonly IGame? _game;
    private readonly nuint _exeBase;

    private IHook<H_EarlyShrine>? _hookEarlyShrine;
    private IHook<H_ShrineMonsterUnlockedChecker>? _hookUnlocked;
    private IHook<H_WriteSDATAMemory>? _hookWriteSdata;
    private IHook<H_MysteryStatUpdate>? _hookStatUpdate;
    private IHook<H_FreezerWriteFreezer>? _hookFreezer;

    // Congelador: 20 huecos de 524 bytes; un hueco está libre si el byte en +0x170 vale 0xFF (según MoreMonsters).
    private const int FreezerOffset = 0x3768BC;
    private const int FreezerSlots = 20;
    private const int FreezerEntrySize = 524;
    private const int FreezerFreeMarker = 0x170;

    // Razas posibles, agrupadas por raza principal, en orden fijo (para que el sorteo sea reproducible).
    private List<List<MonsterBreed>>? _pool;
    private int _poolSourceCount = -1; // tamaño de MonsterBreed.AllBreeds cuando se armó el sorteo
    private bool _poolIncludesMm;
    private bool _poolTestOnlyBonus;
    private (int Min, int Max) _growthRange = (0, 4);

    // ---------------- Integración con More Monsters (MRDX.Game.MoreMonsters) ----------------
    private const string MoreMonstersId = "MRDX.Game.MoreMonsters";
    private const int MoreMonstersRandomDisc = 1272522; // su disco "All Random"
    private const int MmOffsetAlternate = 0x164; // variante de color, relativa a MonsterOffset (como en More Monsters)
    private readonly IModLoader? _loader;
    private MoreMonstersInfo? _mm;

    private sealed record MoreMonstersInfo(bool Active, HashSet<int> SongIds, HashSet<(int Main, int Sub)> Breeds);

    private int _songId;
    // Raza pendiente de aplicar. Caduca a los 60 s por si el jugador sale del santuario sin crear nada,
    // para que no se aplique luego en otro sitio (p. ej. una combinación).
    private MonsterBreed? _activeRaw;
    private readonly Stopwatch _activeClock = new();

    private MonsterBreed? _active
    {
        get => _activeClock.ElapsedMilliseconds > 60_000 ? null : _activeRaw;
        set
        {
            _activeRaw = value;
            _activeClock.Restart();
        }
    }

    public Mod(ModContext context)
    {
        _logger = context.Logger;
        _modConfig = context.ModConfig;
        _config = context.Configuration;
        var loader = context.ModLoader;
        _loader = loader;

        _exeBase = (nuint)Process.GetCurrentProcess().MainModule!.BaseAddress;

        IHooks? hooks = null;
        loader.GetController<IHooks>()?.TryGetTarget(out hooks);
        loader.GetController<IGame>()?.TryGetTarget(out _game);

        if (hooks == null || _game == null)
        {
            Log("No se encontró MRDX.Base.Mod (hooks/juego). ¿Está activado y por encima de este mod?", Color.Red);
            return;
        }

        EnsureSeed();

        hooks.AddHook<H_EarlyShrine>(OnEarlyShrine).ContinueWith(r => _hookEarlyShrine = r.Result);
        hooks.AddHook<H_ShrineMonsterUnlockedChecker>(OnUnlockedCheck).ContinueWith(r => _hookUnlocked = r.Result);
        hooks.AddHook<H_WriteSDATAMemory>(OnWriteSdata).ContinueWith(r => _hookWriteSdata = r.Result);
        hooks.AddHook<H_MysteryStatUpdate>(OnStatUpdate).ContinueWith(r => _hookStatUpdate = r.Result);
        if (_config.FreezerDiagnostics)
            hooks.AddHook<H_FreezerWriteFreezer>(OnFreezerWrite).ContinueWith(r => _hookFreezer = r.Result);

        Log($"Cargado. Semilla: {_config.Seed}.", Color.LightGreen);
    }

    public Mod()
    {
    }

    public override void ConfigurationUpdated(Config configuration)
    {
        _config = configuration;
        EnsureSeed();
        Log($"Configuración actualizada. Semilla: {_config.Seed}.");
    }

    private void EnsureSeed()
    {
        if (_config.Seed != 0) return;
        _config.Seed = Random.Shared.Next(1, int.MaxValue);
        try { _config.Save?.Invoke(); }
        catch (Exception e) { Log($"No se pudo guardar la semilla nueva: {e.Message}", Color.Orange); }
    }

    // ------------------------------------------------------------------
    // 1. El jugador confirma la canción
    // ------------------------------------------------------------------

    private unsafe void OnEarlyShrine(nuint self, nuint p2)
    {
        _hookEarlyShrine!.OriginalFunction(self, p2);

        _active = null;
        if (!_config.RandomizeBreed && !HasStatVariation()) return;

        try
        {
            _songId = *(int*)(self + 0xCC);

            // Los discos de More Monsters (título "More Monsters" y su "All Random") también son aleatorios:
            // se oculta el número de disco para que More Monsters, que lo lee justo después (este mod carga antes y
            // queda "por dentro"), no imponga su especie. Se restaura en el siguiente paso del santuario.
            var mm = MoreMonsters();
            if (_config.RandomizeBreed && mm.Active &&
                (mm.SongIds.Contains(_songId) || _songId == MoreMonstersRandomDisc))
            {
                _hiddenSongSlot = self + 0xCC;
                *(int*)_hiddenSongSlot = HiddenSongId;
                if (_config.VerboseLog) Log($"Disco #{_songId} es de More Monsters: también se sortea.");
            }

            var breed = _config.RandomizeBreed ? PickBreed(_songId) : null;
            _active = breed ?? NoBreedChange;

            if (_config.VerboseLog)
                Log(breed != null
                    ? $"Disco #{_songId} → {BreedLabel(breed)}."
                    : $"Disco #{_songId} → raza original (solo variación de stats).");
        }
        catch (Exception e)
        {
            _active = null;
            Log($"Error leyendo el disco: {e.Message}", Color.Red);
        }
    }

    // Disco de More Monsters ocultado temporalmente (ver OnEarlyShrine).
    // Tiene que ser un disco REAL del juego base (con 0 el juego se cierra: lee ese número en ese momento)
    // y que no sea de More Monsters. #997997 = Golem/Monol en la base de datos de canciones del juego.
    private const int HiddenSongId = 997997;
    private nuint _hiddenSongSlot;

    /// <summary>Devuelve el número de disco original si lo ocultamos. Se llama en el siguiente paso del santuario.</summary>
    private unsafe void RestoreHiddenSong()
    {
        if (_hiddenSongSlot == 0) return;
        var slot = _hiddenSongSlot;
        _hiddenSongSlot = 0;
        if (*(int*)slot == HiddenSongId) *(int*)slot = _songId;
    }

    // Marcador: no cambiar la raza, pero sí aplicar la variación.
    private static readonly MonsterBreed NoBreedChange = new() { Name = "(original)" };

    private bool HasStatVariation() =>
        _config.StatVariancePercent > 0 || _config.GrowthVarianceSteps > 0 ||
        _config.LifespanVariancePercent > 0 || _config.NatureVariance > 0;

    private MonsterBreed? PickBreed(int songId)
    {
        var pool = GetPool();
        if (pool.Count == 0)
        {
            Log("Aún no se cargaron las razas del juego; este disco da su monstruo original.", Color.Orange);
            return null;
        }

        // Dos pasos: primero la raza principal (todas igual de probables), luego la sub-raza.
        var h = Mix((ulong)(uint)_config.Seed << 32 | (uint)songId);
        var mains = pool[(int)(h % (ulong)pool.Count)];
        var h2 = Mix(h ^ 0x9E3779B97F4A7C15UL);
        return mains[(int)(h2 % (ulong)mains.Count)];
    }

    // SplitMix64: hash estable entre ejecuciones (no usar GetHashCode, cambia en cada arranque).
    private static ulong Mix(ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    private List<List<MonsterBreed>> GetPool()
    {
        // More Monsters agrega sus especies a MonsterBreed.AllBreeds cuando el juego termina de cargar las razas,
        // así que el sorteo se rearma si la lista cambió de tamaño o si cambió la opción.
        var mm = MoreMonsters();
        var includeMm = mm.Active && _config.IncludeMoreMonsters;
        var source = MonsterBreed.AllBreeds;
        if (_pool != null && _pool.Count > 0 && source.Count == _poolSourceCount && includeMm == _poolIncludesMm && _config.TestOnlyBonus == _poolTestOnlyBonus)
            return _pool;

        
        var valid = source
            // Razas normales, o especies de More Monsters (las "bonus" usan géneros nuevos: 42, 43...).
            .Where(b => (b.GenusMain < MonsterGenus.Count && b.GenusSub < MonsterGenus.Count) ||
                        (includeMm && _config.IncludeBonusSpecies && mm.Breeds.Contains(((int)b.GenusMain, (int)b.GenusSub))))
            .Where(b => includeMm || !mm.Breeds.Contains(((int)b.GenusMain, (int)b.GenusSub)))
            // Prueba: solo especies con géneros nuevos (las bonus).
            .Where(b => !_config.TestOnlyBonus || b.GenusMain >= MonsterGenus.Count || b.GenusSub >= MonsterGenus.Count)
            .ToList();
        _poolSourceCount = source.Count;
        _poolTestOnlyBonus = _config.TestOnlyBonus;
        _poolIncludesMm = includeMm;
        var mmCount = valid.Count(b => mm.Breeds.Contains(((int)b.GenusMain, (int)b.GenusSub)));
        if (mm.Active) Log($"More Monsters activo: {(includeMm ? $"{mmCount} especies suyas entran en el sorteo" : "sus especies no se usan (opción desactivada)")}.");

        _pool = valid
            .GroupBy(b => b.GenusMain)
            .OrderBy(g => (int)g.Key)
            .Select(g => g.OrderBy(b => (int)b.GenusSub).ToList())
            .ToList();

        if (valid.Count > 0)
        {
            var growths = valid.SelectMany(b => new[]
            {
                b.GrowthRateLife, b.GrowthRatePower, b.GrowthRateIntelligence,
                b.GrowthRateSkill, b.GrowthRateSpeed, b.GrowthRateDefense
            }).ToList();
            _growthRange = (growths.Min(), growths.Max());
            Log($"{valid.Count} razas disponibles en {_pool.Count} grupos. Crecimiento entre {_growthRange.Min} y {_growthRange.Max}.");
        }

        return _pool;
    }

    /// <summary>
    ///     Detecta More Monsters (puede cargarse después que este mod, por eso se consulta al usarlo) y lee de sus
    ///     tablas qué discos y qué combinaciones de raza son suyos.
    /// </summary>
    private MoreMonstersInfo MoreMonsters()
    {
        if (_mm != null) return _mm;

        var songs = new HashSet<int>();
        var breeds = new HashSet<(int, int)>();
        var active = false;
        try
        {
            var mods = _loader?.GetActiveMods().Select(m => m.Generic.ModId).ToList() ?? [];
            active = mods.Contains(MoreMonstersId);
            if (active)
            {
                // Para poder sortear también sus discos, este mod tiene que cargarse ANTES que More Monsters.
                if (mods.IndexOf(MoreMonstersId) < mods.IndexOf(_modConfig.ModId))
                    Log("More Monsters se carga antes que este mod: sus discos \"More Monsters\" darán la especie " +
                        "elegida. En Reloaded, poné DiscRandomizer por encima de More Monsters.", Color.Orange);

                var dir = Path.Combine(_loader!.GetDirectoryForModId(MoreMonstersId), "NewMonsterData");
                foreach (var file in Directory.Exists(dir) ? Directory.GetFiles(dir, "*.csv") : [])
                foreach (var line in File.ReadAllLines(file))
                {
                    // Columnas: songID, variante, nombre, raza principal, sub-raza, ...
                    var c = line.Split('\t');
                    if (c.Length < 5) continue;
                    if (int.TryParse(c[0], out var song)) songs.Add(song);
                    if (int.TryParse(c[3], out var main) && int.TryParse(c[4], out var sub)) breeds.Add((main, sub));
                }

                Log($"More Monsters detectado: {breeds.Count} especies y {songs.Count} discos propios.");
            }
        }
        catch (Exception e)
        {
            Log($"No se pudieron leer los datos de More Monsters: {e.Message}", Color.Orange);
        }

        return _mm = new MoreMonstersInfo(active, songs, breeds);
    }

    private bool IsMoreMonstersBreed(MonsterBreed b) =>
        MoreMonsters().Breeds.Contains(((int)b.GenusMain, (int)b.GenusSub));

    /// <summary>
    ///     More Monsters elige texturas/modelo según la variante de color guardada en el monstruo actual. Al crear una
    ///     de sus especies desde un disco normal no la fija, así que la ponemos en 0 (la variante base, que siempre existe).
    /// </summary>
    private void ResetMoreMonstersVariant(MonsterBreed b)
    {
        if (!MoreMonsters().Active || !IsMoreMonstersBreed(b)) return;
        WriteBytes(_exeBase + MonsterOffset + MmOffsetAlternate, [0]);
    }

    // ------------------------------------------------------------------
    // 2. ¿Raza desbloqueada? Si la nueva no lo está, el santuario falla como en el original.
    // ------------------------------------------------------------------

    private nuint OnUnlockedCheck(nuint self, int unk1, int unk2)
    {
        RestoreHiddenSong();
        var ret = _hookUnlocked!.OriginalFunction(self, unk1, unk2);
        var b = _active;
        if (b == null || ReferenceEquals(b, NoBreedChange)) return ret;

        // Comprobado en el juego: unk1/unk2 son la raza principal/sub que ya escribió OnWriteSdata,
        // así que el propio juego evalúa el desbloqueo de la raza NUEVA. Solo respetamos su resultado.
        if (_config.VerboseLog)
            Log($"Comprobación de desbloqueo: {(MonsterGenus)unk1}/{(MonsterGenus)unk2} → {(ret != 0 ? "disponible" : "bloqueado")}.");

        if (ret == 0)
        {
            Log($"{BreedLabel(b)} todavía no está desbloqueado: el santuario no puede crearlo.", Color.Orange);
            _active = null;
        }

        return ret;
    }

    // ------------------------------------------------------------------
    // 3. Antes de la animación: raza para el modelo/textura
    // ------------------------------------------------------------------

    private unsafe void OnWriteSdata(nuint self)
    {
        RestoreHiddenSong();
        _hookWriteSdata!.OriginalFunction(self);

        var b = _active;
        if (b == null || ReferenceEquals(b, NoBreedChange)) return;

        try
        {
            var breedLoc = *(nuint*)(self + 0x44);
            *(byte*)(breedLoc + 0x48) = (byte)b.GenusMain;
            *(byte*)(breedLoc + 0x49) = (byte)b.GenusSub;
            ResetMoreMonstersVariant(b); // antes de que se cargue el modelo en la animación
        }
        catch (Exception e)
        {
            Log($"Error cambiando el modelo: {e.Message}", Color.Red);
        }
    }

    // ------------------------------------------------------------------
    // 4. Después de cargar los stats: escribimos raza + valores aleatorios
    // ------------------------------------------------------------------

    private int OnStatUpdate(nuint self)
    {
        var ret = _hookStatUpdate!.OriginalFunction(self);

        var b = _active;
        _active = null;
        if (b == null) return ret;

        try
        {
            var m = _game!.Monster;
            var original = $"{m.GenusMain}/{m.GenusSub}";

            // Si no cambiamos la raza, partimos de la base de la raza original.
            var breed = ReferenceEquals(b, NoBreedChange) ? MonsterBreed.GetBreed(m.GenusMain, m.GenusSub) : b;
            if (breed == null)
            {
                Log($"No se encontraron los datos de {original}; se deja el monstruo como está.", Color.Orange);
                return ret;
            }

            WriteMonster(m, breed, !ReferenceEquals(b, NoBreedChange));

            // (Aquí el juego ya cargó la raza nueva, porque OnWriteSdata la escribió antes.)
            Log($"Disco #{_songId}: {BreedLabel(breed)}. " +
                $"Vida {m.Life}, Pod {m.Power}, Int {m.Intelligence}, Hab {m.Skill}, Vel {m.Speed}, Def {m.Defense} | " +
                $"Crec {m.GrowthRateLife}/{m.GrowthRatePower}/{m.GrowthRateIntelligence}/{m.GrowthRateSkill}/" +
                $"{m.GrowthRateSpeed}/{m.GrowthRateDefense} | Esperanza {m.Lifespan} sem, Naturaleza {m.NatureBase}.",
                Color.Yellow);
        }
        catch (Exception e)
        {
            Log($"Error escribiendo el monstruo: {e.Message}", Color.Red);
        }

        return ret;
    }

    private void WriteMonster(IMonster m, MonsterBreed breed, bool breedChanged)
    {
        var rng = Random.Shared; // no sembrado: distinto en cada creación

        if (breedChanged)
        {
            m.GenusMain = breed.GenusMain;
            m.GenusSub = breed.GenusSub;
            m.LifeType = breed.LifeType;
            m.ArenaSpeed = breed.ArenaSpeed;
            m.GutsRate = breed.GutsRate;
            m.TrainBoost = breed.TrainBoost;
            WriteBreedExtras(breed);
            ResetMoreMonstersVariant(breed);
        }

        // Stats iniciales
        m.Life = Vary(breed.Life);
        m.Power = Vary(breed.Power);
        m.Intelligence = Vary(breed.Intelligence);
        m.Skill = Vary(breed.Skill);
        m.Speed = Vary(breed.Speed);
        m.Defense = Vary(breed.Defense);

        // Ritmos de crecimiento
        m.GrowthRateLife = Grow(breed.GrowthRateLife);
        m.GrowthRatePower = Grow(breed.GrowthRatePower);
        m.GrowthRateIntelligence = Grow(breed.GrowthRateIntelligence);
        m.GrowthRateSkill = Grow(breed.GrowthRateSkill);
        m.GrowthRateSpeed = Grow(breed.GrowthRateSpeed);
        m.GrowthRateDefense = Grow(breed.GrowthRateDefense);

        // Esperanza de vida
        var lv = _config.LifespanVariancePercent / 100.0;
        var life = (ushort)Math.Clamp(Math.Round(breed.Lifespan * (1 + (rng.NextDouble() * 2 - 1) * lv)), 10, 999);
        m.Lifespan = life;
        m.InitalLifespan = life;

        // Naturaleza (igual que MoreMonsters: NatureRaw y NatureBase con el valor base)
        var nv = Math.Max(0, _config.NatureVariance);
        var nature = (sbyte)Math.Clamp(breed.NatureBase + rng.Next(-nv, nv + 1), -100, 100);
        m.NatureRaw = nature;
        m.NatureBase = nature;

        ushort Vary(ushort baseValue)
        {
            var v = _config.StatVariancePercent / 100.0;
            return (ushort)Math.Clamp(Math.Round(baseValue * (1 + (rng.NextDouble() * 2 - 1) * v)), 1, 999);
        }

        byte Grow(byte baseValue)
        {
            var s = Math.Max(0, _config.GrowthVarianceSteps);
            return (byte)Math.Clamp(baseValue + rng.Next(-s, s + 1), _growthRange.Min, _growthRange.Max);
        }
    }

    /// <summary>
    ///     Datos que dependen de la raza y que la base de la comunidad no expone como propiedades.
    ///     Mismos offsets y correcciones que usa MoreMonsters al cambiar la raza de un monstruo del santuario.
    /// </summary>
    private void WriteBreedExtras(MonsterBreed breed)
    {
        // Ojo: los offsets de abajo son relativos a la dirección de MoreMonsters (exe + 0x37667C),
        // que está 4 bytes después de la que usa MRDX.Base.Mod. No mezclar.
        var mon = _exeBase + MonsterOffset;

        // Especiales de batalla
        WriteBytes(mon + 0x1D0, BitConverter.GetBytes(breed.BattleSpecialsRaw));

        // Técnicas iniciales de la nueva raza
        WriteBytes(mon + 0x192, IMonsterTechnique.SerializeTechsLearnedMemory(breed.TechsKnown));

        // Técnica elegida por rango: si no, el juego puede asignar huecos inválidos
        byte[] slots = [0xFF, 0xFF, 0xFF, 0xFF];
        foreach (var t in breed.TechsKnown)
        {
            var r = (int)t.Range;
            if (t.SlotPosition < slots[r]) slots[r] = t.SlotPosition;
        }

        for (var i = 0; i < 4; i++)
            if (slots[i] == 0xFF) slots[i] = 24;
        WriteBytes(mon + 0x1C8, slots);

        // Tipo de juego (play) según la raza principal
        byte[] playTypePerMain =
            [0, 0, 0, 1, 0, 0, 2, 2, 2, 2, 0, 0, 2, 2, 1, 0, 0, 0, 2, 0, 0, 1, 2, 1, 0, 0, 0, 1, 0, 0, 2, 0, 1, 0, 1, 0, 0, 0];
        WriteBytes(mon + 0xE5, [playTypePerMain[(int)breed.GenusMain]]);
    }

    // ------------------------------------------------------------------
    // Diagnóstico del congelador (solo registra; no modifica nada)
    // ------------------------------------------------------------------

    private void OnFreezerWrite(nuint self, int unk1, int unk2)
    {
        string before = "", after = "";
        try { before = FreezerMap(); } catch { /* solo diagnóstico */ }

        _hookFreezer!.OriginalFunction(self, unk1, unk2);

        try
        {
            after = FreezerMap();
            var m = _game!.Monster;
            var lost = Enumerable.Range(0, FreezerSlots).Where(i => before[i] == '#' && after[i] == '.').ToList();
            var added = Enumerable.Range(0, FreezerSlots).Where(i => before[i] == '.' && after[i] == '#').ToList();

            Log($"Congelador ({m.Name}, {m.GenusMain}/{m.GenusSub}, args {unk1}/{unk2}): antes [{before}] después [{after}]" +
                (added.Count > 0 ? $" → nuevo en hueco {string.Join(",", added)}" : " → ¡no se ocupó ningún hueco nuevo!"),
                added.Count == 1 && lost.Count == 0 ? Color.White : Color.Orange);

            if (lost.Count > 0)
                Log($"¡Huecos vaciados al congelar: {string.Join(",", lost)}! Por favor guarda este log.", Color.Red);
        }
        catch (Exception e)
        {
            Log($"Error en diagnóstico del congelador: {e.Message}", Color.Red);
        }
    }

    // '#' = ocupado, '.' = libre, un carácter por hueco.
    private unsafe string FreezerMap()
    {
        var baseAddr = _exeBase + FreezerOffset;
        var chars = new char[FreezerSlots];
        for (var i = 0; i < FreezerSlots; i++)
            chars[i] = *(byte*)(baseAddr + FreezerFreeMarker + (nuint)(FreezerEntrySize * i)) == 0xFF ? '.' : '#';
        return new string(chars);
    }

    // Los datos de raza de la base no traen nombre, así que mostramos "Principal/Sub".
    private string BreedLabel(MonsterBreed b) =>
        (string.IsNullOrWhiteSpace(b.Name) ? $"{b.GenusMain}/{b.GenusSub}" : $"{b.Name} ({b.GenusMain}/{b.GenusSub})") +
        (_mm != null && IsMoreMonstersBreed(b) ? " [More Monsters]" : "");

    private static void WriteBytes(nuint address, byte[] data) => Marshal.Copy(data, 0, (nint)address, data.Length);

    private void Log(string msg, Color? color = null) =>
        _logger.WriteLine($"[{_modConfig.ModId}] {msg}", color ?? Color.White);
}
