namespace EditorKokemon;

/// <summary>Idioma del editor: el de la liga (liga.json, "idioma": "es" o "en").</summary>
internal static class Textos
{
    public static string Idioma { get; set; } = "es";

    /// <summary>El texto en el idioma de la liga.</summary>
    public static string T(string es, string en) => Idioma == "en" ? en : es;
}
