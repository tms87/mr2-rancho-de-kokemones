namespace EditorKokemon;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        // La carpeta del repositorio llega como argumento (Editor Kokemon.bat) o se busca desde el programa.
        var raiz = (args.Length > 0 ? Repo.Buscar(args[0]) : null) ?? Repo.Buscar(AppContext.BaseDirectory);
        if (raiz == null)
        {
            using var d = new FolderBrowserDialog { Description = "Elegí la carpeta del repositorio de tu liga (la que tiene liga.json) / Pick your league repository folder (the one with liga.json)" };
            if (d.ShowDialog() != DialogResult.OK) return;
            raiz = Repo.Buscar(d.SelectedPath);
            if (raiz == null)
            {
                MessageBox.Show("Esa carpeta no es el repositorio de una liga (no tiene liga.json).\nThat folder is not a league repository (no liga.json).", "Editor Kokemon");
                return;
            }
        }

        var repo = new Repo(raiz);
        Textos.Idioma = repo.Idioma;

        Application.Run(new Ventana(repo));
    }
}
