using System.Diagnostics;
using System.Text.RegularExpressions;

namespace EditorKokemon;

/// <summary>
///     Exportados recibidos: los que suben los jugadores con "MR2 Subir exportados" quedan como issues (etiqueta
///     "exportado") y Editor Kokemon.bat los baja a recibidos\ ("#numero titulo.json"). Descartar un archivo cierra su
///     issue (queda en el historial de GitHub, pero no vuelve a aparecer) y lo borra de recibidos\.
/// </summary>
public sealed partial class Ventana
{
    // El repositorio de la liga (remoto origin): ahí llegan los issues de exportados.
    private string RepoGitHub => _repo.RepoGitHub ?? throw new InvalidOperationException(
        T("La liga no tiene un repositorio de GitHub (remoto origin).", "The league has no GitHub repository (origin remote)."));

    private string CarpetaRecibidos => Path.Combine(_repo.Raiz, "recibidos");

    private bool EsCarpetaRecibidos
    {
        get
        {
            try
            {
                return string.Equals(Path.GetFullPath(_carpetaExportados.Text.TrimEnd('\\')), Path.GetFullPath(CarpetaRecibidos),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    private bool HayRecibidos() => Directory.Exists(CarpetaRecibidos) && Directory.GetFiles(CarpetaRecibidos, "#*.json").Length > 0;

    private void DescartarArchivo()
    {
        if (_gridExportados.CurrentRow?.Tag is not Rival r) { MessageBox.Show("Elegí un monstruo del archivo a descartar.", "Rivales"); return; }
        var archivo = r.Archivo;
        var nombre = Path.GetFileName(archivo);
        var monstruos = _exportados.Where(x => x.Archivo == archivo).Select(x => x.Nombre).ToList();
        var issue = Regex.Match(nombre, @"^#(\d+) ");
        var texto = $"¿Descartar {nombre}?\n\nMonstruos: {string.Join(", ", monstruos)}.\n\n" +
                    (issue.Success
                        ? $"Se cierra el issue #{issue.Groups[1].Value} en GitHub (queda en el historial) y el archivo deja de aparecer. " +
                          "Los que ya agregaste como rivales se quedan."
                        : "Es un archivo local (no vino de GitHub): se mueve a la subcarpeta \"descartados\".");
        if (MessageBox.Show(texto, "Descartar", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

        if (issue.Success)
        {
            var (codigo, salida) = Gh("issue", "close", issue.Groups[1].Value, "--repo", RepoGitHub, "--comment",
                "Revisado en el Editor Kokemon: " + (monstruos.Any(m => _lista.Any(x => x.Nombre == m)) ? "incorporado." : "descartado."));
            if (codigo != 0)
            {
                MessageBox.Show("No se pudo cerrar el issue en GitHub:\n" + salida + "\n\n(¿Está instalado GitHub CLI y con la sesión iniciada? gh auth login)", "Descartar");
                return;
            }
        }

        if (issue.Success) File.Delete(archivo);
        else
        {
            var descartados = Path.Combine(Path.GetDirectoryName(archivo)!, "descartados");
            Directory.CreateDirectory(descartados);
            File.Move(archivo, Path.Combine(descartados, nombre), true);
        }

        CargarRivales();
    }

    private (int Codigo, string Salida) Gh(params string[] args)
    {
        var psi = new ProcessStartInfo("gh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var proc = Process.Start(psi)!;
            var salida = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return (proc.ExitCode, salida.Trim());
        }
        catch (Exception e)
        {
            return (-1, "No se pudo ejecutar gh: " + e.Message);
        }
    }
}
