using System.ComponentModel;
using MRDX.Custom.Sonda.Template.Configuration;

namespace MRDX.Custom.Sonda.Configuration;

public class Config : Configurable<Config>
{
    [Category("Sonda")]
    [DisplayName("Activada")]
    [Description("Registra qué instrucciones del juego leen o escriben el inventario. Solo para desarrollo.")]
    [DefaultValue(true)]
    public bool Enabled { get; set; } = true;
}

public class ConfiguratorMixin : ConfiguratorMixinBase
{
}
