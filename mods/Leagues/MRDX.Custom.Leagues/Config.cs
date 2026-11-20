using System.ComponentModel;
using MRDX.Custom.Leagues.Template.Configuration;

namespace MRDX.Custom.Leagues.Configuration;

public class Config : Configurable<Config>
{
    [Category("1. Ligas")]
    [DisplayName("Sortear rivales")]
    [Description("Cada semana, los rivales de los torneos de liga (E a S) salen sorteados de la lista de cada liga.\n" +
                 "Desactivado: los torneos tienen los rivales originales.")]
    [DefaultValue(true)]
    public bool Enabled { get; set; } = true;

    [Category("1. Ligas")]
    [DisplayName("Semilla")]
    [Description("Con la misma semilla y la misma lista, todos ven los mismos rivales en la misma semana del juego.")]
    [DefaultValue(20261120)]
    public int Seed { get; set; } = 20261120;

    [Category("1. Ligas")]
    [DisplayName("Permitir especies de More Monsters")]
    [Description("Las especies de More Monsters como rivales todavía no están probadas (podrían cerrar el juego).\n" +
                 "Desactivado: los monstruos de la lista con esas especies se saltean.")]
    [DefaultValue(false)]
    public bool AllowMoreMonsters { get; set; }

    [Category("2. Exportar")]
    [DisplayName("Exportar al guardar")]
    [Description("Al guardar, tu monstruo y los del congelador se exportan a la carpeta \"Exportados\" (en la\n" +
                 "configuración de este mod), con la liga del rango que alcanzaron, listos para compartirlos.")]
    [DefaultValue(true)]
    public bool ExportOnSave { get; set; } = true;

    [Category("3. Avanzado")]
    [DisplayName("Registro detallado")]
    [DefaultValue(true)]
    public bool VerboseLog { get; set; } = true;
}

public class ConfiguratorMixin : ConfiguratorMixinBase
{
}
