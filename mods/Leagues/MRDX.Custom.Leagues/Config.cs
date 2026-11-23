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
    [Description("Los monstruos de la lista con especies de More Monsters pueden salir como rivales\n" +
                 "(More Monsters se encarga de su modelo en la batalla). Desactivado: se saltean.")]
    [DefaultValue(true)]
    public bool IncludeMoreMonsters { get; set; } = true;

    [Category("2. Exportar")]
    [DisplayName("Exportar al guardar")]
    [Description("Al empezar cada año (primer guardado de enero, junto con la copia anual), tu monstruo y los del\n" +
                 "congelador se exportan a la carpeta \"Exportados\" (en la configuración de este mod): un archivo\n" +
                 "\"fecha Entrenador.json\" con todos sus datos, listo para compartirlo.")]
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
