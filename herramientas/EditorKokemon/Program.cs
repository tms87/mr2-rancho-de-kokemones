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
            using var d = new FolderBrowserDialog { Description = "Elegí la carpeta del repositorio mr2-rancho-de-kokemones (la que tiene actualizacion.json)" };
            if (d.ShowDialog() != DialogResult.OK) return;
            raiz = Repo.Buscar(d.SelectedPath);
            if (raiz == null)
            {
                MessageBox.Show("Esa carpeta no es el repositorio (no tiene actualizacion.json).", "Editor Kokemon");
                return;
            }
        }

        Application.Run(new Ventana(new Repo(raiz)));
    }
}
