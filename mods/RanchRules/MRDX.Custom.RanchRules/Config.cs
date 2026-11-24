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
                 "(autoguardado); la elegida no se toca. Las copias anuales usan las ranuras 1 a 7 (8 a 14: competencias).\n" +
                 "Desactivado: guardás libremente como en el original y las copias anuales usan las ranuras 8 a 13.")]
    [DefaultValue(false)]
    public bool IronmanMode { get; set; }

    [Category("2. Guardado")]
    [DisplayName("Guardar en grabaciones de competencia")]
    [Description("Desactivado: con una grabación de competencia (ranuras 8 a 14) cargada no se guarda nada, ni el\n" +
                 "autoguardado; al volver a cargarla está igual. Activado: lo que se guarda queda en esa misma ranura\n" +
                 "(sirve para mover una grabación de prueba a otra fecha). Dejarlo desactivado.")]
    [DefaultValue(false)]
    public bool SaveCompetitions { get; set; }

    [Category("2. Guardado")]
    [DisplayName("Copia anual")]
    [Description("El primer autoguardado de cada año nuevo (enero) se copia en la siguiente ranura de copias,\n" +
                 "rotando y sobrescribiendo la más antigua. Requiere el autoguardado activado en el juego.")]
    [DefaultValue(true)]
    public bool YearlyBackup { get; set; } = true;

    // ---------------- Torneos ----------------

    [Category("3. Torneos")]
    [DisplayName("Bloquear torneos de rango inferior")]
    [Description("Un monstruo no puede entrar a torneos de un rango más bajo que el suyo (los libres, F, siempre se pueden).")]
    [DefaultValue(true)]
    public bool BlockLowerRankTournaments { get; set; } = true;

    [Category("3. Torneos")]
    [DisplayName("Rangos inferiores permitidos")]
    [Description("Cuántos rangos por debajo del suyo puede jugar: 0 = solo su rango (o más alto), 1 = uno abajo, etc.")]
    [DefaultValue(0)]
    public int LowerRanksAllowed { get; set; }

    // ---------------- Avanzado ----------------

    [Category("4. Avanzado")]
    [DisplayName("Registro detallado")]
    [DefaultValue(true)]
    public bool VerboseLog { get; set; } = true;
}

public class ConfiguratorMixin : ConfiguratorMixinBase
{
}
