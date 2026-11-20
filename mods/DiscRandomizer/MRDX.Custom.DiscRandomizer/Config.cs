using System.ComponentModel;
using MRDX.Custom.DiscRandomizer.Template.Configuration;

namespace MRDX.Custom.DiscRandomizer.Configuration;

public class Config : Configurable<Config>
{
    // ---------------- Raza por disco ----------------

    [Category("1. Raza por disco")]
    [DisplayName("Semilla")]
    [Description("Decide qué raza da cada disco. Mismo disco + misma semilla = misma raza, siempre.\n" +
                 "0 = el mod genera una semilla nueva la primera vez y la guarda aquí.\n" +
                 "Cámbiala para repartir todos los discos de nuevo.")]
    [DefaultValue(0)]
    public int Seed { get; set; } = 0;

    [Category("1. Raza por disco")]
    [DisplayName("Activar raza aleatoria")]
    [Description("Si se desactiva, cada disco da su raza original y solo se aplica la variación de stats.")]
    [DefaultValue(true)]
    public bool RandomizeBreed { get; set; } = true;

    [Category("1. Raza por disco")]
    [DisplayName("Incluir especies de More Monsters")]
    [Description("Si el mod More Monsters está activo, sus especies nuevas también pueden salir de los discos.\n" +
                 "Sus discos propios (título \"More Monsters\") también se sortean: no sirven para elegir especie.\n" +
                 "Ojo: activar o desactivar esto (o instalar More Monsters) cambia qué raza da cada disco.")]
    [DefaultValue(true)]
    public bool IncludeMoreMonsters { get; set; } = true;

    [Category("1. Raza por disco")]
    [DisplayName("Incluir especies bonus de More Monsters")]
    [Description("Spamtong, Chunky Ape, Holiday Zuum, etc. Requiere activar \"Bonus Monsters\" en More Monsters.")]
    [DefaultValue(true)]
    public bool IncludeBonusSpecies { get; set; } = true;

    [Category("3. Avanzado")]
    [DisplayName("PRUEBA: solo especies bonus")]
    [Description("Todos los discos dan una especie bonus de More Monsters. Solo para probar; dejar desactivado.")]
    [DefaultValue(false)]
    public bool TestOnlyBonus { get; set; }

    [Category("3. Avanzado")]
    [DisplayName("PRUEBA: forzar especie")]
    [Description("Todos los discos dan esta especie, escrita como \"principal/sub\" con números (ej. 10/1). Solo para probar; dejar vacío.")]
    [DefaultValue("")]
    public string TestForceBreed { get; set; } = "";

    // ---------------- Variación en cada creación ----------------

    [Category("2. Variación en cada creación")]
    [DisplayName("Variación de stats iniciales (±%)")]
    [Description("Vida, Poder, Inteligencia, Habilidad, Velocidad y Defensa varían este porcentaje\n" +
                 "alrededor de la base de la raza, distinto cada vez que se usa el disco.")]
    [DefaultValue(20)]
    public int StatVariancePercent { get; set; } = 20;

    [Category("2. Variación en cada creación")]
    [DisplayName("Variación de crecimiento (± niveles)")]
    [Description("Cuántos niveles puede subir o bajar el ritmo de crecimiento de cada stat.")]
    [DefaultValue(1)]
    public int GrowthVarianceSteps { get; set; } = 1;

    [Category("2. Variación en cada creación")]
    [DisplayName("Variación de esperanza de vida (±%)")]
    [DefaultValue(10)]
    public int LifespanVariancePercent { get; set; } = 10;

    [Category("2. Variación en cada creación")]
    [DisplayName("Variación de naturaleza (± puntos)")]
    [Description("La naturaleza va de -100 (mala) a 100 (buena).")]
    [DefaultValue(30)]
    public int NatureVariance { get; set; } = 30;

    // ---------------- Avanzado ----------------

    [Category("3. Avanzado")]
    [DisplayName("Registro detallado")]
    [Description("Escribe en la consola de Reloaded el disco, la raza original, la nueva y los valores generados.")]
    [DefaultValue(true)]
    public bool VerboseLog { get; set; } = true;

    [Category("3. Avanzado")]
    [DisplayName("Diagnóstico del congelador")]
    [Description("Registra qué huecos del congelador están ocupados antes y después de congelar un monstruo.\n" +
                 "No modifica nada. Requiere reiniciar el juego para activarlo o desactivarlo.")]
    [DefaultValue(true)]
    public bool FreezerDiagnostics { get; set; } = true;
}

public class ConfiguratorMixin : ConfiguratorMixinBase
{
}
