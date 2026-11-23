using System.Diagnostics;
using System.Drawing;
using MRDX.Base.Mod.Interfaces;

namespace MRDX.Custom.ItemTolerance;

/// <summary>
///     Diagnóstico para comprobar efectos de objetos (opción "Preparar prueba de objetos", desactivada por defecto).
///     Solo actúa al cargar una grabación de competencia (ranuras 8 a 14), para no tocar la partida normal:
///     pone cansancio y estrés en 100, llena el inventario con los objetos a probar y registra cada cambio del monstruo.
/// </summary>
public unsafe partial class Mod
{
    // Solo el Flower: así ningún otro pasivo se mezcla en la prueba.
    private static readonly uint[] ObjetosPrueba = [145];
    private readonly Stopwatch _pruebaReloj = new();

    private bool _pruebaLista; // ya pasó la carga (los saltos de semana de la carga no son un mes nuevo)

    private bool EsRanuraCompetencia => int.TryParse(_slot, out var s) && s is >= 7 and <= 13;

    private void PrepararPrueba()
    {
        var inv = Inventario;
        for (var i = 0; i < Lugares; i++) inv[i] = i < ObjetosPrueba.Length ? ObjetosPrueba[i] : Vacio;
        if (_game?.Monster is { } m)
        {
            m.Fatigue = 0; // sano hasta el mes nuevo, para que no se escape
            m.Stress = 0;
        }

        _pruebaLista = true;
        Log($"Prueba de objetos: cansancio y estrés en 0, inventario: {string.Join(", ", ObjetosPrueba.Select(id => $"#{id} {Objetos.NombreDe((int)id)}"))}. " +
            "Al empezar el mes, cansancio y estrés pasan a 100.", Color.Orange);
    }

    /// <summary>Al empezar un mes: cansancio y estrés en 100, para ver cuál de los dos baja el pasivo.</summary>
    private void PruebaInicioDeMes()
    {
        if (!_pruebaLista || _game?.Monster is not { } m) return;
        Log($"[prueba] empieza el mes: cansancio {m.Fatigue}, estrés {m.Stress} → 100 y 100.", Color.Orange);
        m.Fatigue = 100;
        m.Stress = 100;
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
