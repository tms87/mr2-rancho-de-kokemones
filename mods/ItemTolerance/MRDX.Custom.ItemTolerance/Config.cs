using System.ComponentModel;
using MRDX.Custom.ItemTolerance.Template.Configuration;

namespace MRDX.Custom.ItemTolerance.Configuration;

public class Config : Configurable<Config>
{
    // ---------------- Tolerancia (efecto decreciente) ----------------

    [Category("1. Tolerancia")]
    [DisplayName("Pérdida de efecto por semana de uso (%)")]
    [Description("Cuánto efecto pierde el objeto por cada punto de tolerancia acumulado.\n" +
                 "Con 10: 100% → 90% → 80% → 70%...\n" +
                 "Cada semana seguida que se da el objeto suma 1 punto de tolerancia.")]
    [DefaultValue(10)]
    public int ToleranceStepPercent { get; set; } = 10;

    [Category("1. Tolerancia")]
    [DisplayName("Efecto mínimo (%)")]
    [Description("El efecto nunca baja de este porcentaje, por mucha tolerancia que haya.")]
    [DefaultValue(40)]
    public int MinimumEffectPercent { get; set; } = 40;

    [Category("1. Tolerancia")]
    [DisplayName("Tolerancia máxima")]
    [Description("Máximo de puntos de tolerancia acumulables. Cada semana sin dar el objeto resta 1 punto,\n" +
                 "así que se tarda en volver a la normalidad tantas semanas como se acumularon.")]
    [DefaultValue(10)]
    public int MaxTolerance { get; set; } = 10;

    [Category("1. Tolerancia")]
    [DisplayName("Tolerancia compartida")]
    [Description("Si está activado, la Nuts Oil y la Magic Banana comparten la misma tolerancia\n" +
                 "(alternarlas no sirve para esquivarla). Si está desactivado, cada objeto lleva la suya.")]
    [DefaultValue(true)]
    public bool SharedTolerance { get; set; } = true;

    // ---------------- Riesgo para la vida ----------------

    [Category("2. Riesgo de vida")]
    [DisplayName("Probabilidad base (%)")]
    [Description("Probabilidad fija de acortar la vida cada vez que el monstruo come uno de estos objetos.")]
    [DefaultValue(0)]
    public int LifespanBaseChancePercent { get; set; } = 0;

    [Category("2. Riesgo de vida")]
    [DisplayName("Probabilidad extra por objeto ingerido (%)")]
    [Description("Se suma por cada Nuts Oil o Magic Banana que el monstruo haya comido en toda su vida\n" +
                 "(incluyendo el actual). Con 2: el 1º tiene 2%, el 5º 10%, el 10º 20%...\n" +
                 "Magic Banana: si no baja el cansancio resta 1 (desintoxica); con -15 de cansancio suma 1 sin sumar\n" +
                 "tolerancia; con -30 cuenta igual que una Nuts Oil.")]
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
}

public class ConfiguratorMixin : ConfiguratorMixinBase
{
}
