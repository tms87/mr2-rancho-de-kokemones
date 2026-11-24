using System.Diagnostics;
using System.Drawing;
using MRDX.Base.Mod.Interfaces;
using MRDX.Custom.ItemTolerance.Configuration;
using MRDX.Custom.ItemTolerance.Template;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X86;
using Reloaded.Mod.Interfaces;
using CallingConventions = Reloaded.Hooks.Definitions.X86.CallingConventions;

namespace MRDX.Custom.ItemTolerance;

// Función del juego que se ejecuta al dar un objeto al monstruo.
// Firma tomada del mod de la comunidad "MRDX.Qol.MagicBananaStatic" (ranchercommunity/mrdx_reloaded).
// El ID del objeto está en p1 + 76.
[HookDef(BaseGame.Mr2, Region.Us, "55 8B EC B9 01 00 00 00 83 EC 18")]
[Function(CallingConventions.Cdecl)]
public delegate void H_ItemUsed(int p1, uint p2, uint p3);

public partial class Mod : ModBase
{
    private const int SharedKey = -1;

    // Tras dar el objeto, durante cuánto tiempo atribuimos los cambios del monstruo al objeto.
    // Medido en el juego: el efecto llega 30-80 ms después de que el jugador confirma con "Sí", ya en la animación de
    // comer, durante la cual no puede hacer nada (ni pasar de semana). 1 s sobra y nunca alcanza una acción del jugador.
    private const int EffectWindowMs = 1000;

    // Si el efecto llegó justo antes de la llamada al hook, lo aceptamos si tiene menos de esto (antes de confirmar,
    // el jugador navegó el menú: un cambio de semana no puede caer en este medio segundo).
    private const int RecentChangeMs = 500;

    private readonly ILogger _logger = null!;
    private readonly IModConfig _modConfig = null!;
    private readonly IModLoader _modLoader = null!;
    private Config _config = null!;

    private readonly IGame? _game;
    private IHook<H_ItemUsed>? _hookItemUsed;
    private IHook<FrameStart>? _hookFrameStart;

    private readonly Random _rng = new();
    private readonly string _dataDir = "";
    private string _slot = "sin-ranura";
    private SlotData _data = new();

    private Pending? _pending;
    private readonly Stopwatch _sinceItem = new(); // medición: tiempo desde el último objeto dado
    private readonly Stopwatch _devolverReloj = new(); // diagnóstico: devolver el baúl un rato después de cargar
    private long _week = -1;
    private Change? _recent;

    private sealed class Pending
    {
        public int ItemId;
        public long Week; // semana en que se dio: un cambio de otra semana nunca es del objeto
        public Tipo Kind;
        public Efecto? Effect;
        public bool LifeDone;
        public string ItemName = "";
        public readonly Stopwatch Clock = Stopwatch.StartNew();
        public int ToleranceBefore;
        public double Mult;
        public bool Counted;
        public bool FatigueDone;
        public bool StressDone;

        // Magic Banana: lo que cambió (sin el recorte del mod) para saber cuál de sus 3 resultados salió.
        public int FatigueDrop, StressDrop, FearDelta, SpoilDelta, FormDelta;
        public int FatigueBefore = -1;
        public bool AnyEffect;
    }

    private sealed class Change
    {
        public readonly Stopwatch Clock = Stopwatch.StartNew();
        public byte PrevFatigue, CurFatigue;
        public sbyte PrevStress, CurStress;
        public ushort PrevLife, CurLife;
        public int FearDelta, SpoilDelta, FormDelta;
        public bool OtherChanged;
    }

    public Mod(ModContext context)
    {
        _modLoader = context.ModLoader;
        _logger = context.Logger;
        _modConfig = context.ModConfig;
        _config = context.Configuration;

        _dataDir = Path.Combine(_modLoader.GetModConfigDirectory(_modConfig.ModId), "partidas");
        Directory.CreateDirectory(_dataDir);

        IHooks? hooks = null;
        _modLoader.GetController<IHooks>()?.TryGetTarget(out hooks);
        _modLoader.GetController<IGame>()?.TryGetTarget(out _game);
        var saveCtl = _modLoader.GetController<ISaveFile>();

        if (hooks == null || _game == null)
        {
            Log("No se encontró MRDX.Base.Mod (hooks/juego). ¿Está activado y por encima de este mod?", Color.Red);
            return;
        }

        hooks.AddHook<H_ItemUsed>(OnItemUsed).ContinueWith(r => _hookItemUsed = r.Result);
        hooks.AddHook<FrameStart>(OnFrameStart).ContinueWith(r => _hookFrameStart = r.Result);
        InstalarBaul(context.Hooks);
        InstalarComidaNueva(context.Hooks);
        _game.OnWeekChange += OnWeekChange;
        _game.OnMonsterChanged += OnMonsterChanged;

        if (saveCtl != null && saveCtl.TryGetTarget(out var save))
        {
            save.OnLoad += OnLoad;
            save.OnSave += OnSave;
        }
        else
        {
            Log("No se pudo enganchar el guardado: el acostumbramiento antifatiga no se conservará entre sesiones.", Color.Orange);
        }

        Log("Cargado.", Color.LightGreen);
    }

    // Constructor vacío requerido por la plantilla de Reloaded.
    public Mod()
    {
    }

    public override void ConfigurationUpdated(Config configuration)
    {
        _config = configuration;
        Log("Configuración actualizada.");
    }

    // ------------------------------------------------------------------
    // Detección del objeto
    // ------------------------------------------------------------------

    private unsafe void OnItemUsed(int p1, uint p2, uint p3)
    {
        try
        {
            var itemId = *(byte*)(p1 + 76);
            _sinceItem.Restart();
            var m = _game!.Monster;

            if (_config.VerboseLog)
                Log($"Objeto dado: ID {itemId} (ya comió esta semana: {m.ItemUsed}). " +
                    $"Cansancio {m.Fatigue}, Estrés {m.Stress}, Mimo {m.LoyalSpoil}, Miedo {m.LoyalFear}");

            var tipo = Objetos.TipoDe(itemId, _config.NutsOilId, _config.MagicBananaId);
            var efecto = tipo == Tipo.Otro ? Objetos.EfectoDe(itemId) : null;
            if ((tipo != Tipo.Otro || efecto != null) && !m.ItemUsed)
            {
                var rec = _data.For(m);
                rec.Tolerance.TryGetValue(KeyFor(tipo), out var tolerance);
                var mult = Math.Max(_config.MinimumEffectPercent, 100 - _config.ToleranceStepPercent * tolerance);

                _pending = new Pending
                {
                    ItemId = itemId,
                    Week = _week,
                    Kind = tipo,
                    Effect = efecto,
                    ItemName = efecto?.Nombre ?? (tipo == Tipo.NutsOil ? "Nuts Oil" : "Magic Banana"),
                    ToleranceBefore = tolerance,
                    Mult = Math.Clamp(mult, 0, 100) / 100.0
                };

                // Por si el juego aplicó el efecto justo antes de llamar a esta función.
                if (_recent != null && _recent.Clock.ElapsedMilliseconds <= RecentChangeMs)
                {
                    if (_config.VerboseLog) Log("El efecto llegó antes del aviso del objeto; se aplica igualmente.");
                    Process(_pending, _recent);
                }

                _recent = null;
            }
        }
        catch (Exception e)
        {
            Log($"Error al leer el objeto: {e.Message}", Color.Red);
        }

        _hookItemUsed!.OriginalFunction(p1, p2, p3);
    }

    private void OnMonsterChanged(IMonsterChange change)
    {
        try
        {
            if ((_config.PruebaObjetos || _config.PruebaComidas > 0 || _comidaReloj.IsRunning && _comidaReloj.ElapsedMilliseconds < 20000) && EsRanuraCompetencia) RegistrarCambioPrueba(change);
            var prev = change.Previous;
            var cur = change.Current;
            var c = new Change
            {
                PrevFatigue = prev.Fatigue, CurFatigue = cur.Fatigue,
                PrevStress = prev.Stress, CurStress = cur.Stress,
                PrevLife = prev.Lifespan, CurLife = cur.Lifespan,
                FearDelta = cur.LoyalFear - prev.LoyalFear, SpoilDelta = cur.LoyalSpoil - prev.LoyalSpoil,
                FormDelta = cur.FormRaw - prev.FormRaw,
                OtherChanged = prev.LoyalSpoil != cur.LoyalSpoil || prev.LoyalFear != cur.LoyalFear
            };

            var relevant = c.CurFatigue != c.PrevFatigue || c.CurStress != c.PrevStress || c.OtherChanged ||
                           c.CurLife != c.PrevLife || c.FormDelta != 0;
            if (!relevant) return;

            var p = _pending;
            if (p == null)
            {
                // Medición: cualquier cambio fuera de la ventana, con el tiempo desde el último objeto y la semana.
                if (_config.VerboseLog)
                    Log($"  (fuera de ventana) +{_sinceItem.ElapsedMilliseconds}ms desde el último objeto, semana {_week}: " +
                        $"Cansancio {c.PrevFatigue}→{c.CurFatigue}, Estrés {c.PrevStress}→{c.CurStress}, " +
                        $"Mimo {prev.LoyalSpoil}→{cur.LoyalSpoil}, Miedo {prev.LoyalFear}→{cur.LoyalFear}, Vida {c.PrevLife}→{c.CurLife}");
                _recent = c;
                return;
            }

            if (_config.VerboseLog)
                Log($"  +{p.Clock.ElapsedMilliseconds}ms cambio: Cansancio {c.PrevFatigue}→{c.CurFatigue}, " +
                    $"Estrés {c.PrevStress}→{c.CurStress}, Mimo {prev.LoyalSpoil}→{cur.LoyalSpoil}, " +
                    $"Miedo {prev.LoyalFear}→{cur.LoyalFear}");

            if (_week != p.Week)
            {
                if (_config.VerboseLog) Log("  Cambio de otra semana: no es del objeto.");
                return;
            }

            Process(p, c);
        }
        catch (Exception e)
        {
            Log($"Error procesando cambio del monstruo: {e.Message}", Color.Red);
        }
    }

    private int OnFrameStart()
    {
        var result = _hookFrameStart!.OriginalFunction();

        // Diagnóstico: devolver el baúl 3 s después de cargar (antes, el juego todavía no puso el inventario).
        if (_devolverReloj.IsRunning && _devolverReloj.ElapsedMilliseconds > 3000)
        {
            _devolverReloj.Reset();
            DevolverBaulAlInventario();
        }

        if (_pruebaReloj.IsRunning && _pruebaReloj.ElapsedMilliseconds > 3000)
        {
            _pruebaReloj.Reset();
            PrepararPrueba();
        }

        if (_pruebaComidaReloj.IsRunning && _pruebaComidaReloj.ElapsedMilliseconds > 3000)
        {
            _pruebaComidaReloj.Reset();
            PrepararPruebaComidas();
        }

        var p = _pending;
        if (p != null && p.Clock.ElapsedMilliseconds > EffectWindowMs)
        {
            _pending = null;
            Finish(p);
        }

        return result;
    }

    /// <summary>Cierra el objeto dado: la Magic Banana se resuelve acá (ya llegaron todos sus cambios).</summary>
    private void Finish(Pending p)
    {
        if (p.Kind == Tipo.MagicBanana && !p.Counted)
        {
            p.Counted = true;
            FinishBanana(p);
        }
        else if (p.Effect is { } ef && (ef.Desintoxicar != null || ef.IntoxMax > 0) && !p.Counted)
        {
            p.Counted = true;
            // Siempre se aplica: el objeto solo se cuenta si se dio sin haber comido en la semana, y en ese caso el
            // monstruo nunca lo rechaza (aunque no se vea efecto, p. ej. Quack Doll con cansancio y estrés en 0).
            var m = _game!.Monster;

            var rec = _data.For(m);
            if (ef.IntoxMax > 0)
            {
                // Intoxica: cada punto suma 1 y tira el riesgo de vida por separado.
                var puntos = ef.Intoxica(_rng);
                Log($"{p.ItemName}: intoxica {puntos} (intoxicación {rec.LifetimeCount}→{rec.LifetimeCount + puntos}).", Color.Yellow);
                for (var i = 0; i < puntos; i++) Intoxicar(p.ItemName, m, rec);
                return;
            }

            var antes = rec.LifetimeCount;
            rec.LifetimeCount = Math.Clamp(ef.Desintoxicar!(_rng, antes), 0, antes);
            Log(rec.LifetimeCount < antes
                    ? $"{p.ItemName}: desintoxica. Intoxicación {antes}→{rec.LifetimeCount}."
                    : $"{p.ItemName}: esta vez no desintoxicó (intoxicación: {antes}).",
                rec.LifetimeCount < antes ? Color.LightGreen : null);
        }
        else if (!p.Counted && p.Kind == Tipo.NutsOil)
        {
            // La comió aunque no se vea efecto (cansancio en 0, miedo y mimo al tope): cuenta igual.
            p.Counted = true;
            if (_config.VerboseLog) Log($"{p.ItemName}: sin efecto visible, pero la comió: cuenta igual.");
            Count(p, _game!.Monster, sumaTolerancia: true);
        }
    }

    /// <summary>
    ///     Magic Banana (DX), 3 resultados (tabla de LegendCup):
    ///       1) miedo +10, mimo +10, forma -1, cansancio 0,   estrés -10 → desintoxica 1 (resta 1 al contador de vida);
    ///       2) miedo -10, mimo +10,           cansancio -15, estrés -5  → intoxica 1 (suma al contador y tira el dado);
    ///       3) miedo -10, mimo -10, forma +1, cansancio -30            → intoxica 1 y suma tolerancia.
    ///     Miedo, mimo, cansancio y estrés pueden estar en el tope y no moverse: un cambio que ocurrió descarta los
    ///     resultados que no lo producen; uno que no ocurrió no descarta nada (salvo el cansancio: si había y no bajó,
    ///     no fue ni el 2 ni el 3). La forma también cuenta (baja en el 1, sube en el 3). Si quedan varios, o si la
    ///     comió y no se movió nada (todo en el tope), se toma el 2 (intoxica sin sumar tolerancia).
    /// </summary>
    private void FinishBanana(Pending p)
    {
        if (!p.AnyEffect)
        {
            if (!_game!.Monster.ItemUsed)
            {
                if (_config.VerboseLog) Log("Magic Banana: no la comió (no hubo efecto). No cuenta.");
                return;
            }

            Log("Magic Banana: la comió pero no se movió nada (valores en el tope); se toma el resultado 2.", Color.Orange);
            Count(p, _game.Monster, sumaTolerancia: false);
            return;
        }

        var posibles = new HashSet<int> { 1, 2, 3 };
        if (p.FormDelta < 0) posibles.IntersectWith([1]);
        if (p.FormDelta > 0) posibles.IntersectWith([3]);
        if (p.FearDelta > 0) posibles.IntersectWith([1]);
        if (p.FearDelta < 0) posibles.IntersectWith([2, 3]);
        if (p.SpoilDelta > 0) posibles.IntersectWith([1, 2]);
        if (p.SpoilDelta < 0) posibles.IntersectWith([3]);
        if (p.FatigueDrop > 15) posibles.IntersectWith([3]);
        else if (p.FatigueDrop > 0) posibles.IntersectWith([2, 3]);
        else if (p.FatigueBefore > 0) posibles.IntersectWith([1]); // tenía cansancio y no bajó
        if (p.StressDrop > 5) posibles.IntersectWith([1]);
        else if (p.StressDrop > 0) posibles.IntersectWith([1, 2]);

        var resultado = posibles.Count == 1 ? posibles.First() : 2;
        if (posibles.Count != 1)
            Log($"Magic Banana: no se pudo saber el resultado ({(posibles.Count == 0 ? "cambios contradictorios" : "valores en el tope")}); " +
                "se toma el 2.", Color.Orange);

        var m = _game!.Monster;
        var rec = _data.For(m);
        if (resultado == 1)
        {
            var antes = rec.LifetimeCount;
            rec.LifetimeCount = Math.Max(0, rec.LifetimeCount - 1);
            Log($"Magic Banana (resultado 1, sin bajar el cansancio): desintoxica. Intoxicación {antes}→{rec.LifetimeCount}.",
                Color.LightGreen);
            return;
        }

        Count(p, m, sumaTolerancia: resultado == 3);
        if (_config.VerboseLog)
            Log($"Magic Banana: resultado {resultado} (miedo {p.FearDelta:+0;-0;0}, mimo {p.SpoilDelta:+0;-0;0}, forma {p.FormDelta:+0;-0;0}, cansancio -{p.FatigueDrop}).");
    }

    // ------------------------------------------------------------------
    // Lógica principal
    // ------------------------------------------------------------------

    private void Process(Pending p, Change c)
    {
        var m = _game!.Monster;
        var any = c.OtherChanged;
        if (p.FatigueBefore < 0) p.FatigueBefore = c.PrevFatigue;
        p.FearDelta += c.FearDelta;
        p.SpoilDelta += c.SpoilDelta;
        p.FormDelta += c.FormDelta;
        if (c.OtherChanged || c.CurFatigue != c.PrevFatigue || c.CurStress != c.PrevStress || c.CurLife != c.PrevLife ||
            c.FormDelta != 0)
            p.AnyEffect = true;

        // Objetos que restan vida: la resta del juego se reemplaza por una al azar del rango del mod.
        if (p.Effect is { CambiaVida: true } ef && !p.LifeDone && c.CurLife < c.PrevLife)
        {
            p.LifeDone = true;
            // Parte de la vida que ya consumió: semanas vividas sobre vividas + las que le quedaban antes del objeto.
            var vivida = m.Age + c.PrevLife > 0 ? (double)m.Age / (m.Age + c.PrevLife) : 0.5;
            var resta = ef.RestaVida(_rng, vivida);
            var target = (ushort)Math.Max(1, c.PrevLife - resta);
            if (target != c.CurLife) m.Lifespan = target;
            Log(ef.SinVida
                    ? $"{p.ItemName}: no resta vida (sin mod: -{c.PrevLife - c.CurLife})."
                    : $"{p.ItemName}: vida {c.PrevLife}→{target} (-{resta} de {ef.VidaMin}-{ef.VidaMax}, vida consumida {vivida:P0}; sin mod: -{c.PrevLife - c.CurLife}).",
                Color.Yellow);
        }

        // Los demás objetos no tienen tolerancia: el recorte y la cuenta son solo de Nuts Oil y Magic Banana.
        if (p.Kind == Tipo.Otro) return;

        // 1) Efecto decreciente: solo se recorta la bajada de cansancio. La de estrés no se toca (solo se anota, para
        //    reconocer el resultado de la Magic Banana).
        if (!p.FatigueDone && c.CurFatigue < c.PrevFatigue)
        {
            p.FatigueDone = true;
            any = true;
            var reduction = c.PrevFatigue - c.CurFatigue;
            p.FatigueDrop = reduction;
            var target = (byte)(c.PrevFatigue - (int)Math.Round(reduction * p.Mult));
            if (target != c.CurFatigue) m.Fatigue = target;
            Log($"{p.ItemName}: cansancio {c.PrevFatigue}→{target} (sin mod: {c.CurFatigue}), " +
                $"efecto al {p.Mult * 100:0}% (acostumbramiento antifatiga {p.ToleranceBefore}).", Color.Yellow);
        }

        if (!p.StressDone && c.CurStress < c.PrevStress)
        {
            p.StressDone = true;
            any = true;
            p.StressDrop = c.PrevStress - c.CurStress;
            if (_config.VerboseLog) Log($"{p.ItemName}: estrés {c.PrevStress}→{c.CurStress} (sin cambios por acostumbramiento antifatiga).");
        }

        // La Nuts Oil cuenta (tolerancia y riesgo) con cualquier efecto. La Magic Banana se resuelve al cerrar el
        // objeto (Finish), cuando ya se sabe cuál de sus 3 resultados salió.
        if (p.Kind == Tipo.NutsOil && any && !p.Counted)
        {
            p.Counted = true;
            Count(p, m, sumaTolerancia: true);
        }
    }

    private void Count(Pending p, IMonster m, bool sumaTolerancia)
    {
        var rec = _data.For(m);
        var key = KeyFor(p.Kind);

        // 2) Subir el acostumbramiento antifatiga: +2 las bajadas fuertes (Nuts Oil, -30 de la Magic Banana), +1 el -15.
        //    Cada semana baja 1 siempre (OnWeekChange), así que en neto: +1 y 0. El tope se aplica después de esa
        //    bajada: acá se deja pasar 1 de más.
        var suma = sumaTolerancia ? 2 : 1;
        rec.Tolerance[key] = Math.Min(_config.MaxTolerance + 1, p.ToleranceBefore + suma);

        // 3) Riesgo de acortar la vida, creciente con la intoxicación acumulada en la vida del monstruo.
        Intoxicar(p.ItemName, m, rec);
        if (_config.VerboseLog) Log($"Acostumbramiento antifatiga ahora: {rec.Tolerance.GetValueOrDefault(key)}.");
    }

    /// <summary>
    ///     Suma 1 de intoxicación y tira el riesgo de vida con la intoxicación nueva (cada punto, su tirada):
    ///     base + extra por punto, con tope.
    /// </summary>
    private void Intoxicar(string nombre, IMonster m, MonsterRecord rec)
    {
        rec.LifetimeCount++;
        var chance = Math.Min(_config.LifespanMaxChancePercent,
            _config.LifespanBaseChancePercent + _config.LifespanChancePerItemPercent * rec.LifetimeCount);
        var roll = _rng.Next(100);

        if (roll < chance && _config.LifespanWeeksLost > 0)
        {
            var before = m.Lifespan;
            m.Lifespan = (ushort)Math.Max(1, before - _config.LifespanWeeksLost);
            Log($"¡{nombre} le ha sentado mal! Vida {before}→{m.Lifespan} " +
                $"(probabilidad {chance}%, intoxicación {rec.LifetimeCount}).", Color.OrangeRed);
        }
        else
        {
            Log($"Riesgo de vida superado (probabilidad {chance}%, intoxicación {rec.LifetimeCount}).");
        }
    }

    // Clave de tolerancia: compartida, o el número de la configuración de cada fruta (como en las partidas guardadas).
    private int KeyFor(Tipo tipo) => _config.SharedTolerance ? SharedKey : tipo == Tipo.NutsOil ? _config.NutsOilId : _config.MagicBananaId;

    private void OnWeekChange(IWeekChange change)
    {
        if (change.NewWeek != uint.MaxValue) _week = change.NewWeek;
        if (_config.VerboseLog) Log($"  Cambio de semana {change.OldWeek}→{change.NewWeek} (+{_sinceItem.ElapsedMilliseconds}ms desde el último objeto).");
        // Lo que ocurra después de pasar la semana ya no es efecto del objeto (la banana pendiente se resuelve ya).
        if (_pending is { } p) Finish(p);
        _pending = null;
        _recent = null;

        // Ignora saltos raros (carga de partida, inicio): solo avances de 1 a 12 semanas.
        if (change.OldWeek == uint.MaxValue || change.NewWeek <= change.OldWeek) return;
        var weeks = (int)(change.NewWeek - change.OldWeek);
        if (weeks > 12) return;

        // Mes nuevo: los disc chips del inventario pasan al baúl.
        if (EmpiezaMes(change.OldWeek, change.NewWeek))
        {
            try
            {
                GuardarChipsEnBaul();
            }
            catch (Exception e)
            {
                Log($"Error pasando los disc chips al baúl: {e.Message}", Color.Red);
            }
        }

        var m = _game?.Monster;
        if (m == null) return;

        var rec = _data.For(m);
        foreach (var key in rec.Tolerance.Keys.ToList())
        {
            // Baja 1 por semana, siempre (también la semana en que se usó el objeto).
            rec.Tolerance[key] = Math.Clamp(rec.Tolerance[key] - weeks, 0, _config.MaxTolerance);
        }

        if (_config.VerboseLog && rec.Tolerance.Count > 0)
            Log("Nueva semana. Acostumbramiento antifatiga: " +
                string.Join(", ", rec.Tolerance.Select(kv => $"{KeyName(kv.Key)}={kv.Value}")));
    }

    // ------------------------------------------------------------------
    // Guardado por ranura
    // ------------------------------------------------------------------

    private string FileFor(string slot) => Path.Combine(_dataDir, $"tolerancia_{slot}.json");

    private void OnLoad(ISaveFileEntry file)
    {
        try
        {
            _slot = file.Slot;
            _data = SlotData.Load(FileFor(_slot));
            _pending = null;
            Log($"Datos cargados para la ranura {_slot}.");
            if (_config.DevolverBaul) _devolverReloj.Restart(); // cuando el juego ya puso el inventario en memoria
            if (_config.PruebaObjetos && EsRanuraCompetencia) _pruebaReloj.Restart();
            if (_config.PruebaComidas > 0 && EsRanuraCompetencia) _pruebaComidaReloj.Restart();
        }
        catch (Exception e)
        {
            _data = new SlotData();
            Log($"No se pudieron leer los datos de la ranura {file.Slot}: {e.Message}", Color.Red);
        }
    }

    private void OnSave(ISaveFileEntry file)
    {
        // Con una grabación de competencia cargada (ranuras 8 a 14) Ranch Rules descarta el guardado: los datos del
        // mod tampoco se guardan, así la grabación vuelve a cargar como estaba.
        if (EsRanuraCompetencia)
        {
            if (_config.VerboseLog) Log($"Grabación de competencia (ranura {_slot}): datos sin guardar.");
            return;
        }

        try
        {
            _slot = file.Slot;
            _data.Save(FileFor(_slot));
            if (_config.VerboseLog) Log($"Datos guardados en la ranura {_slot}.");
        }
        catch (Exception e)
        {
            Log($"No se pudieron guardar los datos: {e.Message}", Color.Red);
        }
    }

    // ------------------------------------------------------------------

    private string? NameOf(int itemId)
    {
        if (itemId == _config.NutsOilId) return "Nuts Oil";
        if (itemId == _config.MagicBananaId) return "Magic Banana";
        return null;
    }

    private string KeyName(int key) => key == SharedKey ? "compartido" : NameOf(key) ?? key.ToString();

    private void Log(string msg, Color? color = null) =>
        _logger.WriteLine($"[{_modConfig.ModId}] {msg}", color ?? Color.White);
}
