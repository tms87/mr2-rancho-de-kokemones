using System.ComponentModel;
using MRDX.Custom.ItemTolerance.Template.Configuration;

namespace MRDX.Custom.ItemTolerance.Configuration;

public class Config : Configurable<Config>
{
    // ---------------- Acostumbramiento antifatiga (efecto decreciente) ----------------

    [Category("1. Acostumbramiento antifatiga")]
    [DisplayName("Pérdida de efecto por semana de uso (%)")]
    [Description("Cuánto pierde la bajada de cansancio por cada punto de acostumbramiento antifatiga (el estrés no cambia).\n" +
                 "Con 10: 100% → 90% → 80% → 70%...\n" +
                 "Nuts Oil y -30 de la Magic Banana suman 2 puntos; el -15, 1. Cada semana baja 1 (también la de uso).")]
    [DefaultValue(10)]
    public int ToleranceStepPercent { get; set; } = 10;

    [Category("1. Acostumbramiento antifatiga")]
    [DisplayName("Efecto mínimo (%)")]
    [Description("El efecto nunca baja de este porcentaje, por mucho acostumbramiento que haya.")]
    [DefaultValue(40)]
    public int MinimumEffectPercent { get; set; } = 40;

    [Category("1. Acostumbramiento antifatiga")]
    [DisplayName("Acostumbramiento máximo")]
    [Description("Máximo de puntos de acostumbramiento antifatiga acumulables. Cada semana resta 1 punto,\n" +
                 "así que se tarda en volver a la normalidad tantas semanas como se acumularon.")]
    [DefaultValue(10)]
    public int MaxTolerance { get; set; } = 10;

    [Category("1. Acostumbramiento antifatiga")]
    [DisplayName("Acostumbramiento compartido")]
    [Description("Si está activado, la Nuts Oil y la Magic Banana comparten el mismo acostumbramiento antifatiga\n" +
                 "(alternarlas no sirve para esquivarlo). Si está desactivado, cada objeto lleva el suyo.")]
    [DefaultValue(true)]
    public bool SharedTolerance { get; set; } = true;

    // ---------------- Riesgo para la vida ----------------

    [Category("2. Riesgo de vida")]
    [DisplayName("Probabilidad base (%)")]
    [Description("Probabilidad fija de acortar la vida en cada tirada (cada punto de intoxicación que suma un item tóxico).")]
    [DefaultValue(0)]
    public int LifespanBaseChancePercent { get; set; } = 0;

    [Category("2. Riesgo de vida")]
    [DisplayName("Probabilidad extra por punto de intoxicación (%)")]
    [Description("Se suma por cada punto de intoxicación acumulado en la vida del monstruo (incluyendo el nuevo).\n" +
                 "Con 2: con 1 punto 2%, con 5 10%, con 10 20%... Cada punto que suma un item tóxico tira por separado.\n" +
                 "Items tóxicos: Nuts Oil y Magic Banana (1), Teromeamn, Kasseitan y Nageel (1 a 5), Troron (2) y Paradoxine (6).\n" +
                 "Magic Banana: si no baja el cansancio desintoxica 1; con -15 suma 1 y 1 de acostumbramiento; con -30 suma 1 y 2 de acostumbramiento.")]
    [DefaultValue(2)]
    public int LifespanChancePerItemPercent { get; set; } = 2;

    [Category("2. Riesgo de vida")]
    [DisplayName("Probabilidad máxima (%)")]
    [Description("Tope de la probabilidad de acortar la vida.")]
    [DefaultValue(50)]
    public int LifespanMaxChancePercent { get; set; } = 50;

    [Category("2. Riesgo de vida")]
    [DisplayName("Semanas de vida perdidas")]
    [Description("Cuántas semanas de vida se pierden cuando salta el riesgo.")]
    [DefaultValue(1)]
    public int LifespanWeeksLost { get; set; } = 1;

    // ---------------- Avanzado ----------------

    [Category("3. Avanzado")]
    [DisplayName("ID de Nuts Oil")]
    [Description("ID interno del objeto. Si el mod no detecta la Nuts Oil, activa el registro\n" +
                 "y mira en la consola de Reloaded qué ID aparece al darla.")]
    [DefaultValue(23)]
    public int NutsOilId { get; set; } = 23;

    [Category("3. Avanzado")]
    [DisplayName("ID de Magic Banana")]
    [Description("ID interno del objeto (28 según el mod de la comunidad 'Magic Bananas - No More Randomness').")]
    [DefaultValue(28)]
    public int MagicBananaId { get; set; } = 28;

    [Category("3. Avanzado")]
    [DisplayName("Registro detallado")]
    [Description("Escribe en la consola de Reloaded el ID de cada objeto que se da y los cálculos del mod.")]
    [DefaultValue(true)]
    public bool VerboseLog { get; set; } = true;

    [Category("4. Diagnóstico")]
    [DisplayName("Devolver el baúl al inventario al cargar")]
    [Description("Al cargar una partida, los disc chips del baúl vuelven al inventario (hasta llenar sus lugares).\n" +
                 "Solo para pruebas: dejarlo desactivado.")]
    [DefaultValue(false)]
    public bool DevolverBaul { get; set; }

    [Category("4. Diagnóstico")]
    [DisplayName("Preparar prueba de objetos")]
    [Description("Al cargar una grabación de competencia (ranuras 8 a 14), pone cansancio y estrés en 100,\n" +
                 "llena el inventario con objetos de prueba y registra cada cambio del monstruo.\n" +
                 "Solo para pruebas: dejarlo desactivado.")]
    [DefaultValue(false)]
    public bool PruebaObjetos { get; set; }

    [Category("4. Diagnóstico")]
    [DisplayName("Preparar prueba de comidas (escenario)")]
    [Description("Al cargar una grabación de competencia (ranuras 8 a 14), deja al monstruo listo para probar las\n" +
                 "comidas del mes (estrés, miedo y mimo 50, forma 0, cansancio 60) y registra cada cambio. Escenarios:\n" +
                 "1: base (racha del Green Tea 0, intoxicación 5).  2: racha 4.  3: racha 2.  4: racha 1.\n" +
                 "5: racha 4 con 2 meses salteados (queda en 2).  6: acostumbramiento antifatiga 6 (Meat y Chicken).\n" +
                 "0: desactivado. Solo para pruebas: dejarlo en 0.")]
    [DefaultValue(0)]
    public int PruebaComidas { get; set; }
}

public class ConfiguratorMixin : ConfiguratorMixinBase
{
}
