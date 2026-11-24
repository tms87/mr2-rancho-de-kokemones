using System.Diagnostics;
using System.Drawing;
using MRDX.Base.Mod.Interfaces;

namespace MRDX.Custom.ItemTolerance;

/// <summary>
///     Diagnóstico para comprobar efectos de objetos (opción "Preparar prueba de objetos", desactivada por defecto).
///     Solo actúa al cargar una grabación de competencia (ranuras 8 a 14), para no tocar la partida normal:
///     deja al monstruo sano y con vida de sobra, llena el inventario con los objetos a probar y registra cada
///     cambio del monstruo.
/// </summary>
public unsafe partial class Mod
{
    // Torneo (Teromeamn, Kasseitan, Nageel), entrenamiento (Troron, Paradoxine) y desintoxicantes
    // (Gold Peach, Silver Peach, Quack Doll armado con plano).
    private static readonly uint[] ObjetosPrueba = [34, 37, 39, 38, 41, 26, 27, 177];
    private const ushort VidaPrueba = 150;
    private readonly Stopwatch _pruebaReloj = new();

    private bool EsRanuraCompetencia => int.TryParse(_slot, out var s) && s is >= 7 and <= 13;

    private void PrepararPrueba()
    {
        var inv = Inventario;
        for (var i = 0; i < Lugares; i++) inv[i] = i < ObjetosPrueba.Length ? ObjetosPrueba[i] : Vacio;
        if (_game?.Monster is { } m)
        {
            m.Fatigue = 0; // sano, para que no se escape
            m.Stress = 0;
            if (m.Lifespan < VidaPrueba) m.Lifespan = VidaPrueba; // lejos de morirse
            Log($"Prueba de objetos: cansancio 0, estrés 0, vida {m.Lifespan}, intoxicación {_data.For(m).LifetimeCount}.",
                Color.Orange);
        }

        Log($"Prueba de objetos: inventario: {string.Join(", ", ObjetosPrueba.Select(id => $"#{id} {Objetos.NombreDe((int)id)}"))}.",
            Color.Orange);
    }

    // Prueba de comidas: (nombre, racha del Green Tea, meses desde que la tocó por última vez, acostumbramiento
    // antifatiga, intoxicación). Los meses se cuentan hacia atrás desde el mes en que va a comer.
    private static readonly (string Nombre, int Racha, int MesesAtras, int Acostumbramiento, int Intoxicacion)[] EscenariosComida =
    [
        ("base", 0, 0, 0, 5),
        ("racha 4", 4, 1, 0, 10),
        ("racha 2", 2, 1, 0, 10),
        ("racha 1", 1, 1, 0, 10),
        ("racha 4 con 2 meses salteados", 4, 3, 0, 10),
        ("acostumbramiento antifatiga 6", 0, 0, 6, 5)
    ];

    private readonly Stopwatch _pruebaComidaReloj = new();

    private void PrepararPruebaComidas()
    {
        var n = _config.PruebaComidas;
        if (n < 1 || n > EscenariosComida.Length || _game?.Monster is not { } m) return;
        var e = EscenariosComida[n - 1];

        m.Stress = 50;
        m.LoyalFear = 50;
        m.LoyalSpoil = 50;
        m.FormRaw = 0;
        m.Fatigue = 60;
        if (m.Lifespan < VidaPrueba) m.Lifespan = VidaPrueba;

        var rec = _data.For(m);
        rec.LifetimeCount = e.Intoxicacion;
        rec.Tolerance[KeyFor(Tipo.NutsOil)] = e.Acostumbramiento;
        rec.ConstanciaComidas.Remove(ClaveTe);
        rec.MesesComidas.Remove(ClaveTe);

        // Mes en que va a comer: este si estamos en la semana 1, si no el que viene.
        var detalle = "";
        if (e.Racha > 0)
        {
            if (_week < 0)
                detalle = " (¡semana desconocida: no se pudo poner la racha!)";
            else
            {
                var mesComida = MesDe(_week) + ((_week + 12) % 4 == 0 ? 0 : 1);
                rec.ConstanciaComidas[ClaveTe] = e.Racha;
                rec.MesesComidas[ClaveTe] = mesComida - e.MesesAtras;
                detalle = $", último mes con té: {e.MesesAtras} antes del de la comida";
            }
        }

        Log($"Prueba de comidas, escenario {n} ({e.Nombre}): estrés, miedo y mimo 50, forma 0, cansancio 60, " +
            $"intoxicación {e.Intoxicacion}, acostumbramiento antifatiga {e.Acostumbramiento}, racha del Green Tea {e.Racha}{detalle}. " +
            $"Semana {_week} ({((_week + 12) % 4 == 0 ? "es la semana 1 del mes" : "la comida del mes sale al empezar el próximo mes")}).",
            Color.Orange);
    }

    private void RegistrarCambioPrueba(IMonsterChange change)
    {
        var a = change.Previous;
        var b = change.Current;
        var partes = new List<string>();
        void Par(string n, int x, int y)
        {
            if (x != y) partes.Add($"{n} {x}→{y}");
        }

        Par("cansancio", a.Fatigue, b.Fatigue);
        Par("estrés", a.Stress, b.Stress);
        Par("forma", a.FormRaw, b.FormRaw);
        Par("miedo", a.LoyalFear, b.LoyalFear);
        Par("mimo", a.LoyalSpoil, b.LoyalSpoil);
        Par("vida", a.Lifespan, b.Lifespan);
        if (partes.Count > 0) Log($"[prueba] semana {_week}: {string.Join(", ", partes)}", Color.Orange);
    }
}
