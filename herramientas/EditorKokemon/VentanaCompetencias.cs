using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EditorKokemon;

/// <summary>
///     Pestaña Competencias: leer monstruos de una grabación, guardarlos como campeones (campeones\RANGO\*.json) y armar
///     las competencias de las ranuras 8 a 14 (Competencias\competenciaNN.json del mod Reglas del rancho).
/// </summary>
public sealed partial class Ventana
{
    private ComboBox _cRanura = null!, _cRangoCampeon = null!, _cFiltro = null!, _cSlot = null!;
    private DataGridView _cGrabacion = null!, _cCampeones = null!, _cCompetencia = null!;
    private TextBox _cNombre = null!, _cEntrenador = null!;
    private Label _cAviso = null!;
    private readonly List<MonstruoGuardado> _cMonstruosCompetencia = [];
    private const int MaxCompetencia = 20;

    private sealed record Campeon(string Archivo, MonstruoGuardado M, string Rango, string Origen);

    private TabPage PestanaCompetencias()
    {
        var p = new TabPage("Competencias");
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(6) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        t.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(1120, 0),
            Text = "Guardá campeones leyendo cualquier grabación (por ejemplo una copia anual) y armá con ellos las competencias de " +
                   "las ranuras 8 a 14 (hasta 20 monstruos, van al congelador). El juego arma esa ranura después del primer guardado " +
                   "de la sesión (al pasar una semana); después elegila en el modo VS. Subila con la pestaña \"Subir a GitHub\"."
        }, 0, 0);
        t.SetColumnSpan(t.GetControlFromPosition(0, 0)!, 2);

        // ---- Izquierda: grabación y campeones ----
        var izq = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        izq.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        izq.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        izq.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        izq.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        izq.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        izq.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var f1 = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        f1.Controls.Add(new Label { Text = "Grabación:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        _cRanura = Combo([]);
        _cRanura.Width = 330;
        f1.Controls.Add(_cRanura);
        f1.Controls.Add(Boton("Leer", LeerGrabacion));
        f1.Controls.Add(Boton("Actualizar lista", CargarRanuras));
        izq.Controls.Add(f1);

        _cGrabacion = GrillaMonstruos(true);
        izq.Controls.Add(_cGrabacion);

        var f2 = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        f2.Controls.Add(Boton("Guardar el seleccionado como campeón de rango", GuardarCampeon));
        _cRangoCampeon = Combo(Datos.Rangos.Cast<object>().ToArray());
        _cRangoCampeon.Width = 55;
        f2.Controls.Add(_cRangoCampeon);
        f2.Controls.Add(Boton("Agregar a la competencia →", () => AgregarACompetencia(_cGrabacion.CurrentRow?.Tag as MonstruoGuardado)));
        izq.Controls.Add(f2);

        var f3 = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        f3.Controls.Add(new Label { Text = "Campeones:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        _cFiltro = Combo(new object[] { "Todos" }.Concat(Datos.Rangos).ToArray());
        _cFiltro.Width = 80;
        _cFiltro.SelectedIndexChanged += (_, _) => CargarCampeones();
        f3.Controls.Add(_cFiltro);
        izq.Controls.Add(f3);

        _cCampeones = GrillaMonstruos(true);
        izq.Controls.Add(_cCampeones);

        var f4 = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        f4.Controls.Add(Boton("Agregar a la competencia →", () => AgregarACompetencia((_cCampeones.CurrentRow?.Tag as Campeon)?.M)));
        f4.Controls.Add(Boton("Quitar campeón", QuitarCampeon));
        izq.Controls.Add(f4);
        t.Controls.Add(izq, 0, 1);

        // ---- Derecha: competencia ----
        var der = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        der.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        der.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        der.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        der.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var g1 = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        g1.Controls.Add(new Label { Text = "Competencia en la ranura:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        _cSlot = Combo(Enumerable.Range(8, 7).Select(i => (object)i.ToString()).ToArray());
        _cSlot.Width = 50;
        _cSlot.SelectedIndexChanged += (_, _) => CargarCompetencia();
        g1.Controls.Add(_cSlot);
        der.Controls.Add(g1);

        var g2 = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        g2.Controls.Add(new Label { Text = "Nombre:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        _cNombre = new TextBox { Width = 230 };
        g2.Controls.Add(_cNombre);
        g2.Controls.Add(new Label { Text = "Entrenador:", AutoSize = true, Margin = new Padding(9, 8, 3, 3) });
        _cEntrenador = new TextBox { Width = 90, MaxLength = 8, Text = "Kokemon" };
        g2.Controls.Add(_cEntrenador);
        der.Controls.Add(g2);

        _cCompetencia = GrillaMonstruos(false);
        der.Controls.Add(_cCompetencia);

        var g3 = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        g3.Controls.Add(Boton("Quitar", () =>
        {
            if (_cCompetencia.CurrentRow?.Tag is MonstruoGuardado m) { _cMonstruosCompetencia.Remove(m); MostrarCompetencia(); }
        }));
        g3.Controls.Add(Boton("▲", () => Mover(-1)));
        g3.Controls.Add(Boton("▼", () => Mover(1)));
        g3.Controls.Add(Boton("Guardar competencia", GuardarCompetencia));
        g3.Controls.Add(Boton("Borrar competencia", BorrarCompetencia));
        der.Controls.Add(g3);
        t.Controls.Add(der, 1, 1);

        _cAviso = new Label { AutoSize = true, ForeColor = Color.DimGray };
        t.Controls.Add(_cAviso, 0, 2);
        t.SetColumnSpan(_cAviso, 2);

        p.Controls.Add(t);
        CargarRanuras();
        CargarCampeones();
        CargarCompetencia();
        return p;
    }

    private DataGridView GrillaMonstruos(bool conLugar)
    {
        var g = Grilla();
        g.ReadOnly = true;
        g.MultiSelect = false;
        g.Columns.Add(Solo("Nombre", 120));
        g.Columns.Add(Solo("Raza", 150));
        g.Columns.Add(Solo("Rango", 50));
        g.Columns.Add(Solo("Total", 55));
        if (conLugar) g.Columns.Add(Solo("Origen", 230));
        return g;
    }

    private void CargarRanuras()
    {
        var actual = _cRanura.SelectedItem as string;
        _cRanura.Items.Clear();
        _cRanura.Items.Add(ExportadosCompletos);
        for (var r = 1; r <= 15; r++)
        {
            var fecha = Grabaciones.Fecha(r);
            if (fecha == null) continue;
            _cRanura.Items.Add($"{(r == 15 ? "Auto" : $"Ranura {r}")} — {fecha}");
        }

        if (_cRanura.Items.Count == 0) { _cAviso.Text = $"No se encontraron grabaciones en {Grabaciones.Carpeta}."; return; }
        _cRanura.SelectedIndex = actual != null && _cRanura.Items.Contains(actual) ? _cRanura.Items.IndexOf(actual) : Math.Min(1, _cRanura.Items.Count - 1);
    }

    private int RanuraElegida()
    {
        var s = _cRanura.SelectedItem as string ?? "";
        if (s.StartsWith("Auto")) return 15;
        return int.TryParse(s.Split(' ', '—')[1], out var n) ? n : 0;
    }

    private const string ExportadosCompletos = "Exportados (con datos completos)";

    /// <summary>Exportados con el monstruo completo en "crudo" (los del formato viejo no lo traen y no aparecen).</summary>
    private void LeerExportadosCompletos()
    {
        _cGrabacion.Rows.Clear();
        var dir = _carpetaExportados.Text;
        if (!Directory.Exists(dir)) { _cAviso.Text = $"No existe la carpeta de exportados {dir}."; return; }
        foreach (var r in _repo.LeerRivales(dir).OrderByDescending(r => Path.GetFileName(r.Archivo)))
        {
            if (r.Datos["crudo"] == null) continue;
            var origen = (string?)r.Datos["origen"] ?? "exportado";
            var m = MonstruoGuardado.DeJson(r.Datos, origen);
            if (m.Crudo.Length != MonstruoGuardado.Tamano) continue;
            var i = _cGrabacion.Rows.Add(m.Nombre, m.Raza, m.Rango, m.Total, origen);
            _cGrabacion.Rows[i].Tag = m;
        }

        _cAviso.Text = $"{_cGrabacion.Rows.Count} exportados con datos completos (los más nuevos primero).";
    }

    private void LeerGrabacion()
    {
        if (_cRanura.SelectedItem as string == ExportadosCompletos) { LeerExportadosCompletos(); return; }
        var r = RanuraElegida();
        if (r == 0) return;
        _cGrabacion.Rows.Clear();
        try
        {
            var fecha = Grabaciones.Fecha(r);
            foreach (var m in Grabaciones.Monstruos(r))
            {
                var origen = $"{(r == 15 ? "Auto" : $"ranura {r}")} ({fecha}), {m.Lugar}";
                var mm = new MonstruoGuardado { Crudo = m.Crudo, Lugar = origen };
                var i = _cGrabacion.Rows.Add(mm.Nombre, mm.Raza, mm.Rango, mm.Total, origen);
                _cGrabacion.Rows[i].Tag = mm;
            }

            _cAviso.Text = $"{_cGrabacion.Rows.Count} monstruos en la grabación (rancho y congelador).";
        }
        catch (Exception e)
        {
            _cAviso.Text = "No se pudo leer la grabación: " + e.Message;
        }
    }

    // ---- Campeones ----

    private List<Campeon> LeerCampeones()
    {
        var res = new List<Campeon>();
        if (!Directory.Exists(_repo.Campeones)) return res;
        foreach (var rango in Datos.Rangos)
        {
            var dir = Path.Combine(_repo.Campeones, rango);
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    var o = JsonNode.Parse(File.ReadAllText(f))!.AsObject();
                    res.Add(new Campeon(f, MonstruoGuardado.DeJson(o), rango, (string?)o["origen"] ?? ""));
                }
                catch
                {
                    // archivo inválido: se ignora
                }
            }
        }

        return res;
    }

    private void CargarCampeones()
    {
        _cCampeones.Rows.Clear();
        var filtro = _cFiltro.SelectedItem as string ?? "Todos";
        foreach (var c in LeerCampeones().Where(c => filtro == "Todos" || c.Rango == filtro)
                     .OrderBy(c => Array.IndexOf(Datos.Rangos, c.Rango)).ThenBy(c => c.M.Nombre))
        {
            var i = _cCampeones.Rows.Add(c.M.Nombre, c.M.Raza, c.Rango, c.M.Total, c.Origen);
            _cCampeones.Rows[i].Tag = c;
        }
    }

    private void GuardarCampeon()
    {
        if (_cGrabacion.CurrentRow?.Tag is not MonstruoGuardado m) { _cAviso.Text = "Primero leé una grabación y elegí un monstruo."; return; }
        var rango = _cRangoCampeon.SelectedItem as string ?? "E";
        var dir = Path.Combine(_repo.Campeones, rango);
        Directory.CreateDirectory(dir);
        var archivo = Path.Combine(dir, m.NombreArchivo());
        if (File.Exists(archivo) &&
            MessageBox.Show($"Ya hay un campeón {Path.GetFileName(archivo)} en el rango {rango}. ¿Reemplazarlo?", "Campeones",
                MessageBoxButtons.YesNo) != DialogResult.Yes) return;

        var o = m.AJson();
        o["rango"] = rango;
        o["guardado"] = DateTime.Now.ToString("yyyy-MM-dd");
        File.WriteAllText(archivo, o.ToJsonString(Grabaciones.Json) + "\n", new UTF8Encoding(false));
        _cAviso.Text = $"{m.Nombre} guardado como campeón de rango {rango}.";
        CargarCampeones();
    }

    private void QuitarCampeon()
    {
        if (_cCampeones.CurrentRow?.Tag is not Campeon c) return;
        if (MessageBox.Show($"¿Quitar a {c.M.Nombre} de los campeones de rango {c.Rango}?", "Campeones", MessageBoxButtons.YesNo) !=
            DialogResult.Yes) return;
        File.Delete(c.Archivo);
        CargarCampeones();
    }

    // ---- Competencia ----

    private string ArchivoCompetencia => Path.Combine(_repo.Competencias, $"competencia{int.Parse((string)_cSlot.SelectedItem!):00}.json");

    private void CargarCompetencia()
    {
        _cMonstruosCompetencia.Clear();
        _cNombre.Text = "";
        _cEntrenador.Text = "Kokemon";
        if (File.Exists(ArchivoCompetencia))
        {
            try
            {
                var o = JsonNode.Parse(File.ReadAllText(ArchivoCompetencia))!.AsObject();
                _cNombre.Text = (string?)o["nombre"] ?? "";
                _cEntrenador.Text = (string?)o["entrenador"] ?? "Kokemon";
                foreach (var m in o["monstruos"]?.AsArray() ?? [])
                    if (m is JsonObject mo) _cMonstruosCompetencia.Add(MonstruoGuardado.DeJson(mo, (string?)mo["origen"] ?? ""));
            }
            catch (Exception e)
            {
                _cAviso.Text = $"No se pudo leer {Path.GetFileName(ArchivoCompetencia)}: {e.Message}";
            }
        }

        MostrarCompetencia();
    }

    private void MostrarCompetencia()
    {
        _cCompetencia.Rows.Clear();
        foreach (var m in _cMonstruosCompetencia)
        {
            var i = _cCompetencia.Rows.Add(m.Nombre, m.Raza, m.Rango, m.Total);
            _cCompetencia.Rows[i].Tag = m;
        }

        _cAviso.Text = File.Exists(ArchivoCompetencia)
            ? $"Ranura {_cSlot.SelectedItem}: {_cMonstruosCompetencia.Count} de {MaxCompetencia} lugares."
            : $"Ranura {_cSlot.SelectedItem}: sin competencia ({_cMonstruosCompetencia.Count} de {MaxCompetencia} lugares, sin guardar).";
    }

    private void AgregarACompetencia(MonstruoGuardado? m)
    {
        if (m == null) { _cAviso.Text = "Elegí un monstruo de la grabación o de los campeones."; return; }
        if (_cMonstruosCompetencia.Count >= MaxCompetencia) { _cAviso.Text = $"La competencia ya tiene {MaxCompetencia} monstruos (el congelador no tiene más lugar)."; return; }
        if (_cMonstruosCompetencia.Any(x => x.Crudo.AsSpan().SequenceEqual(m.Crudo))) { _cAviso.Text = $"{m.Nombre} ya está en la competencia."; return; }
        _cMonstruosCompetencia.Add(m);
        MostrarCompetencia();
    }

    private void Mover(int delta)
    {
        if (_cCompetencia.CurrentRow?.Tag is not MonstruoGuardado m) return;
        var i = _cMonstruosCompetencia.IndexOf(m);
        var j = i + delta;
        if (j < 0 || j >= _cMonstruosCompetencia.Count) return;
        (_cMonstruosCompetencia[i], _cMonstruosCompetencia[j]) = (_cMonstruosCompetencia[j], _cMonstruosCompetencia[i]);
        MostrarCompetencia();
        _cCompetencia.CurrentCell = _cCompetencia.Rows[j].Cells[0];
    }

    private void GuardarCompetencia()
    {
        if (_cMonstruosCompetencia.Count == 0) { _cAviso.Text = "La competencia está vacía."; return; }
        var nombre = _cNombre.Text.Trim();
        if (nombre == "") nombre = $"Competencia ranura {_cSlot.SelectedItem}";
        var arr = new JsonArray();
        foreach (var m in _cMonstruosCompetencia) arr.Add(m.AJson());
        var entrenador = new string(_cEntrenador.Text.Where(char.IsAsciiLetterOrDigit).Take(8).ToArray());
        if (entrenador == "") entrenador = "Kokemon";
        _cEntrenador.Text = entrenador;
        var o = new JsonObject { ["nombre"] = nombre, ["entrenador"] = entrenador, ["monstruos"] = arr };
        Directory.CreateDirectory(_repo.Competencias);
        File.WriteAllText(ArchivoCompetencia, o.ToJsonString(Grabaciones.Json) + "\n", new UTF8Encoding(false));
        _cNombre.Text = nombre;
        MostrarCompetencia();
        MessageBox.Show($"Guardada la competencia \"{nombre}\" para la ranura {_cSlot.SelectedItem} ({_cMonstruosCompetencia.Count} monstruos).\n\n" +
                        "Subila con la pestaña \"Subir a GitHub\". En el juego, la ranura se arma después del primer guardado de la sesión.",
            "Competencias");
    }

    private void BorrarCompetencia()
    {
        if (!File.Exists(ArchivoCompetencia)) { CargarCompetencia(); return; }
        if (MessageBox.Show($"¿Borrar la competencia de la ranura {_cSlot.SelectedItem}? La ranura queda como está en el juego " +
                            "(no se borra la grabación).", "Competencias", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        File.Delete(ArchivoCompetencia);
        CargarCompetencia();
    }
}
