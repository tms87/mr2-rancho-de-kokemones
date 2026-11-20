using System.ComponentModel;
using MRDX.Custom.AutoBattle.Template.Configuration;

namespace MRDX.Custom.AutoBattle.Configuration;

public class Config : Configurable<Config>
{
    [Category("Batallas")]
    [DisplayName("Batallas automáticas")]
    [Description("En los torneos y batallas del juego, a la pregunta de antes de la batalla cualquier respuesta\n" +
                 "deja que el monstruo pelee solo. El modo VS no cambia.\n" +
                 "Requiere reiniciar el juego para activarlo o desactivarlo.")]
    [DefaultValue(true)]
    public bool AutoBattle { get; set; } = true;

    [Category("Diagnóstico")]
    [DisplayName("Registrar menús")]
    [Description("Registra qué parte del juego lee la opción elegida en cada menú. Solo para desarrollo.")]
    [DefaultValue(false)]
    public bool MenuDiagnostics { get; set; }
}

public class ConfiguratorMixin : ConfiguratorMixinBase
{
}
