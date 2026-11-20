using System.ComponentModel;
using MRDX.Custom.RanchRules.Template.Configuration;

namespace MRDX.Custom.RanchRules.Configuration;

public class Config : Configurable<Config>
{
    // ---------------- Santuario ----------------

    [Category("1. Santuario")]
    [DisplayName("Limitar invocaciones por año")]
    [DefaultValue(true)]
    public bool ShrineLimitEnabled { get; set; } = true;

    [Category("1. Santuario")]
    [DisplayName("Invocaciones por año")]
    [Description("Monstruos que se pueden crear desde discos en cada año del juego.")]
    [DefaultValue(5)]
    public int ShrinePerYear { get; set; } = 5;

    // ---------------- Guardado ----------------

    [Category("2. Guardado")]
    [DisplayName("Modo ironman")]
    [Description("Activado: al guardar a mano, elijas la ranura que elijas, el juego escribe en la ranura 15\n" +
                 "(autoguardado); la elegida no se toca. Las copias anuales usan las ranuras 1 a 14.\n" +
                 "Desactivado: guardás libremente como en el original y las copias anuales usan las ranuras 8 a 13.")]
    [DefaultValue(false)]
    public bool IronmanMode { get; set; }

    [Category("2. Guardado")]
    [DisplayName("Copia anual")]
    [Description("El primer autoguardado de cada año nuevo (enero) se copia en la siguiente ranura de copias,\n" +
                 "rotando y sobrescribiendo la más antigua. Requiere el autoguardado activado en el juego.")]
    [DefaultValue(true)]
    public bool YearlyBackup { get; set; } = true;

    // ---------------- Avanzado ----------------

    [Category("3. Avanzado")]
    [DisplayName("Registro detallado")]
    [DefaultValue(true)]
    public bool VerboseLog { get; set; } = true;
}

public class ConfiguratorMixin : ConfiguratorMixinBase
{
}
