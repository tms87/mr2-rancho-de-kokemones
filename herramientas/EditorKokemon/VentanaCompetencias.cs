using System.Text;
using System.Text.Json.Nodes;

namespace EditorKokemon;

/// <summary>
///     Pestaña Competencias: armar las grabaciones de competencia de las ranuras 8 a 14
///     (Competencias\competenciaNN.json del mod Reglas del rancho).
///
///     Cada competencia tiene nombre, rango opcional y reglas en texto libre (el mod no las usa), y una o más
///     grabaciones numeradas (#1, #2…) con sus participantes, que comparten nombre, rango y reglas.
/// </summary>
public sealed partial class Ventana
{
    private ComboBox _cRanura = null!, _cSlot = null!, _cNombre = null!, _cRango = null!;
    private NumericUpDown _cNumero = null!;
    private DataGridView _cGrabacion = null!, _cCompetencia = null!;
    private TextBox _cEntrenador = null!, _cReglas = null!;
    private Label _cAviso = null!;
    private readonly List<MonstruoGuardado> _cParticipantes = [];
    private bool _cCargando;
    private const int MaxParticipantes = 20;
    private static string SinRango => T("Sin rango", "No rank");

    /// <summary>Lo que dice cada archivo de competencia.</summary>
    private sealed record GrabacionCompetencia(int Ranura, string Nombre, int Numero, int Participantes, string? Rango, string Reglas);

    private TabPage PestanaCompetencias()
    {
        var p = new TabPage(T("Competencias", "Competitions")) { Name = "competencias" };
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(6) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        t.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(1120, 0),
            Text = T("Elegí una ranura (8 a 14), poné el nombre de la competencia y agregá participantes desde una grabación o los " +
                   "exportados (hasta 20 por grabación). Subila con la pestaña \"Subir a GitHub\".",
                   "Pick a slot (8 to 14), enter the competition name and add participants from a save or the exports " +
                   "(up to 20 per recording). Upload it with the \"Upload to GitHub\" tab.")
        }, 0, 0);
        t.SetColumnSpan(t.GetControlFromPosition(0, 0)!, 2);

        // ---- Izquierda: de dónde salen los participantes ----
        var izq = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        izq.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        izq.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        izq.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var f1 = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        f1.Controls.Add(new Label { Text = T("Leer de:", "Read from:"), AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        _cRanura = Combo([]);
        _cRanura.Width = 330;
        f1.Controls.Add(_cRanura);
        f1.Controls.Add(Boton(T("Leer", "Read"), LeerGrabacion));
        f1.Controls.Add(Boton(T("Actualizar lista", "Refresh list"), CargarRanuras));
        izq.Controls.Add(f1);

        _cGrabacion = GrillaMonstruos();
        _cGrabacion.CellDoubleClick += (_, _) => AgregarParticipante(_cGrabacion.CurrentRow?.Tag as MonstruoGuardado);
        izq.Controls.Add(_cGrabacion);

        var f2 = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        f2.Controls.Add(Boton(T("Agregar como participante →", "Add as participant →"), () => AgregarParticipante(_cGrabacion.CurrentRow?.Tag as MonstruoGuardado)));
        izq.Controls.Add(f2);
        t.Controls.Add(izq, 0, 1);

        // ---- Derecha: la grabación de competencia ----
        var der = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
        for (var i = 0; i < 5; i++) der.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        der.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        der.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var g1 = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        g1.Controls.Add(new Label { Text = T("Ranura:", "Slot:"), AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        _cSlot = Combo([]);
        _cSlot.Width = 330;
        _cSlot.SelectedIndexChanged += (_, _) => { if (!_cCargando) CargarCompetencia(); };
        g1.Controls.Add(_cSlot);
        der.Controls.Add(g1);

        var g2 = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        g2.Controls.Add(new Label { Text = T("Competencia:", "Competition:"), AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        _cNombre = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 200 };
        _cNombre.SelectionChangeCommitted += (_, _) => BeginInvoke((Action)CompletarDesdeCompetencia);
        _cNombre.Leave += (_, _) => CompletarDesdeCompetencia();
        g2.Controls.Add(_cNombre);
        g2.Controls.Add(new Label { Text = T("Grabación #", "Recording #"), AutoSize = true, Margin = new Padding(9, 8, 0, 3) });
        _cNumero = new NumericUpDown { Minimum = 1, Maximum = 99, Value = 1, Width = 45 };
        g2.Controls.Add(_cNumero);
        der.Controls.Add(g2);

        var g3 = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        g3.Controls.Add(new Label { Text = T("Rango:", "Rank:"), AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        _cRango = Combo(new object[] { SinRango }.Concat(Datos.Rangos).ToArray());
        _cRango.Width = 90;
        g3.Controls.Add(_cRango);
        g3.Controls.Add(new Label { Text = T("Entrenador:", "Trainer:"), AutoSize = true, Margin = new Padding(9, 8, 3, 3) });
        _cEntrenador = new TextBox { Width = 90, MaxLength = 8, Text = "Kokemon" };
        g3.Controls.Add(_cEntrenador);
        der.Controls.Add(g3);

        der.Controls.Add(new Label { Text = T("Reglas (texto libre, para los jugadores):", "Rules (free text, for the players):"), AutoSize = true, Margin = new Padding(3, 6, 3, 0) });
        _cReglas = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 70, Dock = DockStyle.Fill, AcceptsReturn = true };
        der.Controls.Add(_cReglas);

        var participantes = new GroupBox { Text = T("Participantes", "Participants"), Dock = DockStyle.Fill, Padding = new Padding(3) };
        _cCompetencia = GrillaMonstruos();
        participantes.Controls.Add(_cCompetencia);
        der.Controls.Add(participantes);

        var g4 = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        g4.Controls.Add(Boton(T("Quitar", "Remove"), () =>
        {
            if (_cCompetencia.CurrentRow?.Tag is MonstruoGuardado m) { _cParticipantes.Remove(m); MostrarCompetencia(); }
        }));
        g4.Controls.Add(Boton("▲", () => Mover(-1)));
        g4.Controls.Add(Boton("▼", () => Mover(1)));
        g4.Controls.Add(Boton(T("Guardar grabación", "Save recording"), GuardarCompetencia));
        g4.Controls.Add(Boton(T("Borrar grabación", "Delete recording"), BorrarCompetencia));
        der.Controls.Add(g4);
        t.Controls.Add(der, 1, 1);

        _cAviso = new Label { AutoSize = true, ForeColor = Color.DimGray };
        t.Controls.Add(_cAviso, 0, 2);
        t.SetColumnSpan(_cAviso, 2);

        p.Controls.Add(t);
        CargarRanuras();
        CargarListaCompetencias(8);
        CargarCompetencia();
        return p;
    }

    private DataGridView GrillaMonstruos()
    {
        var g = Grilla();
        g.ReadOnly = true;
        g.MultiSelect = false;
        g.Columns.Add(Solo("Nombre", 120));
        g.Columns.Add(Solo("Raza", 150));
        g.Columns.Add(Solo("Rango", 50));
        g.Columns.Add(Solo("Total", 55));
        g.Columns.Add(Solo("Origen", 230));
        return g;
    }

    // ---- De dónde salen los participantes ----

    private void CargarRanuras()
    {
        var actual = _cRanura.SelectedItem as string;
        _cRanura.Items.Clear();
        _cRanura.Items.Add(ExportadosCompletos);
        for (var r = 1; r <= 15; r++)
        {
            var fecha = Grabaciones.Fecha(r);
            if (fecha == null) continue;
            _cRanura.Items.Add($"{(r == 15 ? "Auto" : T($"Ranura {r}", $"Slot {r}"))} — {fecha}");
        }

        if (_cRanura.Items.Count == 1) _cAviso.Text = T($"No se encontraron grabaciones en {Grabaciones.Carpeta}.", $"No saves found in {Grabaciones.Carpeta}.");
        _cRanura.SelectedIndex = actual != null && _cRanura.Items.Contains(actual) ? _cRanura.Items.IndexOf(actual) : Math.Min(1, _cRanura.Items.Count - 1);
    }

    private int RanuraElegida()
    {
        var s = _cRanura.SelectedItem as string ?? "";
        if (s.StartsWith("Auto")) return 15;
        return int.TryParse(s.Split(' ', '—')[1], out var n) ? n : 0;
    }

    private static string ExportadosCompletos => T("Exportados (con datos completos)", "Exports (with full data)");

    /// <summary>Exportados con el monstruo completo en "crudo" (los del formato viejo no lo traen y no aparecen).</summary>
    private void LeerExportadosCompletos()
    {
        _cGrabacion.Rows.Clear();
        var dir = _carpetaExportados.Text;
        if (!Directory.Exists(dir)) { _cAviso.Text = T($"No existe la carpeta de exportados {dir}.", $"The exports folder {dir} doesn't exist."); return; }
        foreach (var r in _repo.LeerRivales(dir).OrderByDescending(r => Path.GetFileName(r.Archivo)))
        {
            if (r.Datos["crudo"] == null) continue;
            var origen = (string?)r.Datos["origen"] ?? "exportado";
            var m = MonstruoGuardado.DeJson(r.Datos, origen);
            if (m.Crudo.Length != MonstruoGuardado.Tamano) continue;
            var i = _cGrabacion.Rows.Add(m.Nombre, m.Raza, m.Rango, m.Total, origen);
            _cGrabacion.Rows[i].Tag = m;
        }

        _cAviso.Text = T($"{_cGrabacion.Rows.Count} exportados con datos completos (los más nuevos primero).", $"{_cGrabacion.Rows.Count} exports with full data (newest first).");
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

            _cAviso.Text = T($"{_cGrabacion.Rows.Count} monstruos en la grabación (rancho y congelador).", $"{_cGrabacion.Rows.Count} monsters in the save (ranch and freezer).");
        }
        catch (Exception e)
        {
            _cAviso.Text = T("No se pudo leer la grabación: ", "Could not read the save: ") + e.Message;
        }
    }

    // ---- Grabaciones de competencia ----

    private int SlotElegido => _cSlot.SelectedIndex < 0 ? 8 : 8 + _cSlot.SelectedIndex;

    private string ArchivoCompetencia => ArchivoDeRanura(SlotElegido);

    private string ArchivoDeRanura(int ranura) => Path.Combine(_repo.Competencias, $"competencia{ranura:00}.json");

    private List<GrabacionCompetencia> LeerGrabacionesCompetencia()
    {
        var res = new List<GrabacionCompetencia>();
        for (var r = 8; r <= 14; r++)
        {
            var f = ArchivoDeRanura(r);
            if (!File.Exists(f)) continue;
            try
            {
                var o = JsonNode.Parse(File.ReadAllText(f))!.AsObject();
                res.Add(new GrabacionCompetencia(r, (string?)o["nombre"] ?? "", (int?)o["grabacion"] ?? 1,
                    o["participantes"]?.AsArray().Count ?? 0, (string?)o["rango"], (string?)o["reglas"] ?? ""));
            }
            catch
            {
                res.Add(new GrabacionCompetencia(r, T("(archivo inválido)", "(invalid file)"), 1, 0, null, ""));
            }
        }

        return res;
    }

    private static bool MismaCompetencia(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>La lista de ranuras con su competencia y los nombres de competencias ya usados.</summary>
    private void CargarListaCompetencias(int ranura)
    {
        _cCargando = true;
        var grabaciones = LeerGrabacionesCompetencia();
        _cSlot.Items.Clear();
        for (var r = 8; r <= 14; r++)
        {
            var g = grabaciones.FirstOrDefault(x => x.Ranura == r);
            _cSlot.Items.Add(g == null
                ? T($"{r} — (libre)", $"{r} — (free)")
                : $"{r} — {g.Nombre} #{g.Numero}{(g.Rango != null ? T($" (rango {g.Rango})", $" (rank {g.Rango})") : "")}, {g.Participantes} {T("participantes", "participants")}");
        }

        _cSlot.SelectedIndex = ranura - 8;
        var texto = _cNombre.Text;
        _cNombre.Items.Clear();
        _cNombre.Items.AddRange(grabaciones.Select(g => g.Nombre).Where(n => n != "").Distinct().OrderBy(n => n).Cast<object>().ToArray());
        _cNombre.Text = texto;
        _cCargando = false;
    }

    /// <summary>
    ///     En una ranura libre, al elegir una competencia que ya tiene grabaciones: propone el número siguiente y trae su
    ///     rango y sus reglas.
    /// </summary>
    private void CompletarDesdeCompetencia()
    {
        if (File.Exists(ArchivoCompetencia)) return;
        var nombre = _cNombre.Text.Trim();
        var otras = LeerGrabacionesCompetencia().Where(g => MismaCompetencia(g.Nombre, nombre)).ToList();
        if (otras.Count == 0) { _cNumero.Value = 1; return; }
        _cNumero.Value = Math.Min(_cNumero.Maximum, otras.Max(g => g.Numero) + 1);
        _cRango.SelectedItem = otras[0].Rango ?? SinRango;
        _cReglas.Text = otras[0].Reglas;
    }

    private void CargarCompetencia()
    {
        _cParticipantes.Clear();
        _cNombre.Text = "";
        _cNumero.Value = 1;
        _cRango.SelectedItem = SinRango;
        _cReglas.Text = "";
        _cEntrenador.Text = "Kokemon";
        if (File.Exists(ArchivoCompetencia))
        {
            try
            {
                var o = JsonNode.Parse(File.ReadAllText(ArchivoCompetencia))!.AsObject();
                _cNombre.Text = (string?)o["nombre"] ?? "";
                _cNumero.Value = Math.Clamp((int?)o["grabacion"] ?? 1, 1, 99);
                var rango = (string?)o["rango"];
                _cRango.SelectedItem = rango != null && Datos.Rangos.Contains(rango) ? rango : SinRango;
                _cReglas.Text = ((string?)o["reglas"] ?? "").ReplaceLineEndings("\r\n");
                _cEntrenador.Text = (string?)o["entrenador"] ?? "Kokemon";
                foreach (var m in o["participantes"]?.AsArray() ?? [])
                    if (m is JsonObject mo) _cParticipantes.Add(MonstruoGuardado.DeJson(mo, (string?)mo["origen"] ?? ""));
            }
            catch (Exception e)
            {
                _cAviso.Text = T($"No se pudo leer {Path.GetFileName(ArchivoCompetencia)}: {e.Message}", $"Could not read {Path.GetFileName(ArchivoCompetencia)}: {e.Message}");
            }
        }

        MostrarCompetencia();
    }

    private void MostrarCompetencia()
    {
        _cCompetencia.Rows.Clear();
        foreach (var m in _cParticipantes)
        {
            var i = _cCompetencia.Rows.Add(m.Nombre, m.Raza, m.Rango, m.Total, m.Lugar);
            _cCompetencia.Rows[i].Tag = m;
        }

        _cAviso.Text = File.Exists(ArchivoCompetencia)
            ? T($"Ranura {SlotElegido}: {_cParticipantes.Count} de {MaxParticipantes} participantes.", $"Slot {SlotElegido}: {_cParticipantes.Count} of {MaxParticipantes} participants.")
            : T($"Ranura {SlotElegido}: libre ({_cParticipantes.Count} de {MaxParticipantes} participantes, sin guardar).", $"Slot {SlotElegido}: free ({_cParticipantes.Count} of {MaxParticipantes} participants, not saved).");
    }

    private void AgregarParticipante(MonstruoGuardado? m)
    {
        if (m == null) { _cAviso.Text = T("Primero leé una grabación o los exportados y elegí un monstruo.", "First read a save or the exports and pick a monster."); return; }
        if (_cParticipantes.Count >= MaxParticipantes)
        {
            _cAviso.Text = T($"La grabación ya tiene {MaxParticipantes} participantes (el congelador no tiene más lugar): armá otra grabación de la misma competencia en una ranura libre.",
                $"The recording already has {MaxParticipantes} participants (the freezer has no more room): make another recording of the same competition in a free slot.");
            return;
        }

        if (_cParticipantes.Any(x => x.Crudo.AsSpan().SequenceEqual(m.Crudo))) { _cAviso.Text = T($"{m.Nombre} ya es participante de esta grabación.", $"{m.Nombre} is already a participant in this recording."); return; }
        _cParticipantes.Add(m);
        MostrarCompetencia();
    }

    private void Mover(int delta)
    {
        if (_cCompetencia.CurrentRow?.Tag is not MonstruoGuardado m) return;
        var i = _cParticipantes.IndexOf(m);
        var j = i + delta;
        if (j < 0 || j >= _cParticipantes.Count) return;
        (_cParticipantes[i], _cParticipantes[j]) = (_cParticipantes[j], _cParticipantes[i]);
        MostrarCompetencia();
        _cCompetencia.CurrentCell = _cCompetencia.Rows[j].Cells[0];
    }

    private void GuardarCompetencia()
    {
        if (_cParticipantes.Count == 0) { _cAviso.Text = T("La grabación no tiene participantes.", "The recording has no participants."); return; }
        var nombre = _cNombre.Text.Trim();
        if (nombre == "") { _cAviso.Text = T("Poné el nombre de la competencia.", "Enter the competition name."); return; }
        var numero = (int)_cNumero.Value;
        var otras = LeerGrabacionesCompetencia().Where(g => g.Ranura != SlotElegido && MismaCompetencia(g.Nombre, nombre)).ToList();
        if (otras.FirstOrDefault(g => g.Numero == numero) is { } repetida)
        {
            _cAviso.Text = T($"\"{nombre}\" ya tiene una grabación #{numero} en la ranura {repetida.Ranura}: cambiá el número.", $"\"{nombre}\" already has a recording #{numero} in slot {repetida.Ranura}: change the number.");
            return;
        }

        var rango = _cRango.SelectedItem as string is { } r && r != SinRango ? r : null;
        var reglas = _cReglas.Text.Trim().ReplaceLineEndings("\n");
        var arr = new JsonArray();
        foreach (var m in _cParticipantes) arr.Add(m.AJson());
        var entrenador = new string(_cEntrenador.Text.Where(char.IsAsciiLetterOrDigit).Take(8).ToArray());
        if (entrenador == "") entrenador = "Kokemon";
        _cEntrenador.Text = entrenador;
        var o = new JsonObject { ["nombre"] = nombre, ["grabacion"] = numero, ["entrenador"] = entrenador };
        if (rango != null) o["rango"] = rango;
        if (reglas != "") o["reglas"] = reglas;
        o["participantes"] = arr;
        Directory.CreateDirectory(_repo.Competencias);
        Escribir(ArchivoCompetencia, o);

        // Las demás grabaciones de la misma competencia comparten rango y reglas.
        var actualizadas = 0;
        foreach (var g in otras.Where(g => g.Rango != rango || g.Reglas != reglas))
        {
            var archivo = ArchivoDeRanura(g.Ranura);
            var otra = JsonNode.Parse(File.ReadAllText(archivo))!.AsObject();
            var participantesOtra = otra["participantes"];
            otra.Remove("participantes");
            otra.Remove("rango");
            otra.Remove("reglas");
            if (rango != null) otra["rango"] = rango;
            if (reglas != "") otra["reglas"] = reglas;
            otra["participantes"] = participantesOtra?.DeepClone();
            Escribir(archivo, otra);
            actualizadas++;
        }

        _cNombre.Text = nombre;
        CargarListaCompetencias(SlotElegido);
        MostrarCompetencia();
        MessageBox.Show(T($"Guardada la grabación #{numero} de \"{nombre}\" en la ranura {SlotElegido} ({_cParticipantes.Count} participantes).", $"Saved recording #{numero} of \"{nombre}\" in slot {SlotElegido} ({_cParticipantes.Count} participants).") +
                        (actualizadas > 0 ? T($"\nTambién se actualizaron el rango y las reglas en sus otras {actualizadas} grabaciones.", $"\nThe rank and rules were also updated in its other {actualizadas} recordings.") : "") +
                        T("\n\nSubila con la pestaña \"Subir a GitHub\". En el juego, la ranura se arma después del primer guardado de la sesión.", "\n\nUpload it with the \"Upload to GitHub\" tab. In the game, the slot is built after the session's first save."),
            T("Competencias", "Competitions"));
    }

    private static void Escribir(string archivo, JsonObject o) =>
        File.WriteAllText(archivo, o.ToJsonString(Grabaciones.Json) + "\n", new UTF8Encoding(false));

    private void BorrarCompetencia()
    {
        if (!File.Exists(ArchivoCompetencia)) { CargarCompetencia(); return; }
        if (MessageBox.Show(T($"¿Borrar la grabación de competencia de la ranura {SlotElegido}? La ranura queda como está en el juego (no se borra la grabación del juego).",
                            $"Delete the competition recording in slot {SlotElegido}? The slot stays as it is in the game (the game's save is not deleted)."), T("Competencias", "Competitions"), MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        File.Delete(ArchivoCompetencia);
        CargarListaCompetencias(SlotElegido);
        CargarCompetencia();
    }
}
