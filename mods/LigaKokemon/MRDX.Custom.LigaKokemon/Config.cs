using System.ComponentModel;
using MRDX.Custom.LigaKokemon.Template.Configuration;

namespace MRDX.Custom.LigaKokemon.Configuration;

public class Config : Configurable<Config>
{
    [Category("1. Liga Kokemon")]
    [DisplayName("Activado")]
    [Description("Agrega los torneos nuevos y aplica los cambios de calendario de torneos.json.")]
    [DefaultValue(true)]
    public bool Enabled { get; set; } = true;

    [Category("1. Liga Kokemon")]
    [DisplayName("Sortear torneos chicos cada año")]
    [Description("Cada año cambian las fechas de los torneos chicos de rango A a E. Los oficiales, los 4 grandes, " +
                 "FIMBA y los especiales quedan como en el original.")]
    [DefaultValue(true)]
    public bool ShuffleSmall { get; set; } = true;

    [Category("1. Liga Kokemon")]
    [DisplayName("Semilla")]
    [Description("Con la misma semilla, todos tienen el mismo calendario cada año.")]
    [DefaultValue(24)]
    public int Seed { get; set; } = 24;

    [Category("2. Avanzado")]
    [DisplayName("Registrar archivos leídos")]
    [Description("Escribe en el registro cada archivo de datos que lee el juego (para investigar).")]
    [DefaultValue(false)]
    public bool LogFileReads { get; set; }

    [Category("2. Avanzado")]
    [DisplayName("Registro detallado")]
    [DefaultValue(true)]
    public bool VerboseLog { get; set; } = true;
}

public class ConfiguratorMixin : ConfiguratorMixinBase
{
}
