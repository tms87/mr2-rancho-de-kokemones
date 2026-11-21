using System.Diagnostics;
using System.Text.Json.Nodes;

namespace EditorKokemon;

/// <summary>Editor de contenido: rivales, torneos, banners y subida a GitHub.</summary>
public sealed class Ventana : Form
{
    private readonly Repo _repo;

    public Ventana(Repo repo)
    {
        _repo = repo;
        Text = $"Editor Kokemon (v2: WebP) — {repo.Raiz}";
        Width = 1180;
        Height = 760;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(PestanaRivales());
        tabs.TabPages.Add(PestanaTorneos());
        tabs.TabPages.Add(PestanaBanners());
        tabs.TabPages.Add(PestanaSubir());
        tabs.SelectedIndexChanged += (_, _) =>
        {
            if (tabs.SelectedTab?.Text == "Subir a GitHub") ActualizarEstadoGit();
            if (tabs.SelectedTab?.Text == "Torneos") RecargarListasTorneo();
        };
        Controls.Add(tabs);
    }

    // =====================================================================================
    // Rivales
    // =====================================================================================

    private DataGridView _gridExportados = null!, _gridLista = null!;
    private TextBox _carpetaExportados = null!;
    private List<Rival> _exportados = [], _lista = [];

    private static string CarpetaExportadosPorDefecto() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"MR2-Rancho-de-Kokemones\Reloaded-II\User\Mods\MRDX.Custom.Leagues\Exportados");

    private TabPage PestanaRivales()
    {
        var p = new TabPage("Rivales");
        var tabla = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        tabla.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tabla.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        tabla.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tabla.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tabla.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        tabla.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var arriba = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        arriba.Controls.Add(new Label { Text = "Exportados:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        _carpetaExportados = new TextBox { Width = 700, Text = CarpetaExportadosPorDefecto() };
        arriba.Controls.Add(_carpetaExportados);
        arriba.Controls.Add(Boton("Cambiar…", () =>
        {
            using var d = new FolderBrowserDialog { SelectedPath = _carpetaExportados.Text };
            if (d.ShowDialog() == DialogResult.OK) { _carpetaExportados.Text = d.SelectedPath; CargarRivales(); }
        }));
        arriba.Controls.Add(Boton("Recargar", CargarRivales));
        tabla.Controls.Add(arriba);

        _gridExportados = Grilla();
        _gridExportados.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Agregar", HeaderText = "Agregar", Width = 60 });
        _gridExportados.Columns.Add(new DataGridViewTextBoxColumn { Name = "Nombre", HeaderText = "Nombre", Width = 140 });
        _gridExportados.Columns.Add(Solo("Raza", 170));
        _gridExportados.Columns.Add(Solo("Total", 60));
        _gridExportados.Columns.Add(Solo("Exportado", 75));
        _gridExportados.Columns.Add(new DataGridViewComboBoxColumn { Name = "Liga", HeaderText = "Liga", DataSource = Datos.Rangos, Width = 55 });
        _gridExportados.Columns.Add(Solo("Estado", 420));
        tabla.Controls.Add(_gridExportados);

        var medio = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        medio.Controls.Add(Boton("Agregar los marcados a la lista de rivales", AgregarRivales));
        medio.Controls.Add(new Label
        {
            AutoSize = true, Margin = new Padding(12, 8, 3, 3),
            Text = "Liga: la que alcanzó en el juego; si sus stats no encajan con los originales de esa liga, se propone otra."
        });
        tabla.Controls.Add(medio);

        tabla.Controls.Add(new Label { Text = "Lista de rivales (jugadores):", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        _gridLista = Grilla();
        _gridLista.Columns.Add(new DataGridViewTextBoxColumn { Name = "Nombre", HeaderText = "Nombre", Width = 140 });
        _gridLista.Columns.Add(Solo("Raza", 170));
        _gridLista.Columns.Add(Solo("Total", 60));
        _gridLista.Columns.Add(new DataGridViewComboBoxColumn { Name = "Liga", HeaderText = "Liga", DataSource = Datos.Rangos, Width = 55 });
        _gridLista.Columns.Add(Solo("Archivo", 360));
        tabla.Controls.Add(_gridLista);

        var abajo = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        abajo.Controls.Add(Boton("Guardar cambios (nombre y liga)", GuardarLista));
        abajo.Controls.Add(Boton("Quitar el seleccionado", QuitarRival));
        tabla.Controls.Add(abajo);

        p.Controls.Add(tabla);
        CargarRivales();
        return p;
    }

    private void CargarRivales()
    {
        _lista = _repo.LeerRivales(_repo.Jugadores).OrderBy(r => r.Liga).ThenByDescending(r => r.Total).ToList();
        var originales = _repo.LeerRivales(_repo.Originales);
        var rangos = _repo.RangosOriginales();
        _exportados = _repo.LeerRivales(_carpetaExportados.Text).OrderBy(r => r.Nombre).ToList();

        _gridExportados.Rows.Clear();
        foreach (var r in _exportados)
        {
            var sugerida = LigaSugerida(r.Total, r.Liga, rangos);
            var estado = EstadoRival(r, originales);
            var i = _gridExportados.Rows.Add(estado == "" , r.Nombre, r.Raza, r.Total, r.Liga, sugerida,
                estado == "" ? (sugerida != r.Liga ? $"Nuevo (sus stats encajan en {sugerida})" : "Nuevo") : estado);
            _gridExportados.Rows[i].Tag = r;
            if (estado != "") _gridExportados.Rows[i].DefaultCellStyle.ForeColor = Color.Gray;
        }

        _gridLista.Rows.Clear();
        foreach (var r in _lista)
        {
            var i = _gridLista.Rows.Add(r.Nombre, r.Raza, r.Total, Datos.Rangos.Contains(r.Liga) ? r.Liga : "E", Path.GetFileName(r.Archivo));
            _gridLista.Rows[i].Tag = r;
        }
    }

    /// <summary>La liga alcanzada si sus stats encajan con los originales de esa liga; si no, la que encaja.</summary>
    private static string LigaSugerida(int total, string liga, Dictionary<string, (int Min, int Max)> rangos)
    {
        if (rangos.TryGetValue(liga, out var r) && total >= r.Min && total <= r.Max) return liga;
        foreach (var l in Datos.Rangos) // de S a E: la más alta cuyo mínimo alcanza
            if (rangos.TryGetValue(l, out var x) && total >= x.Min) return l;
        return "E";
    }

    /// <summary>Vacío si se puede agregar; si no, por qué no (ya está, nombre repetido, mismo monstruo).</summary>
    private string EstadoRival(Rival r, List<Rival> originales)
    {
        var todos = _lista.Concat(originales).ToList();
        if (_lista.Any(x => string.Equals(Path.GetFileName(x.Archivo), r.NombreArchivo(), StringComparison.OrdinalIgnoreCase)
                            && x.Total == r.Total)) return "Ya está en la lista";
        var mismo = _lista.FirstOrDefault(x => x.Raza == r.Raza && x.Especiales == r.Especiales && x.Especiales != 0);
        if (mismo != null) return $"Parece el mismo monstruo que {mismo.Nombre} (misma raza y especiales); se puede agregar cambiando el nombre";
        if (todos.Any(x => string.Equals(x.Nombre, r.Nombre, StringComparison.OrdinalIgnoreCase)))
            return "Nombre repetido: cambialo para poder agregarlo";
        return "";
    }

    private void AgregarRivales()
    {
        _gridExportados.EndEdit();
        var nombres = _lista.Select(x => x.Nombre.ToLowerInvariant()).ToHashSet();
        var agregados = new List<string>();
        var problemas = new List<string>();
        foreach (DataGridViewRow fila in _gridExportados.Rows)
        {
            if (fila.Tag is not Rival r || fila.Cells["Agregar"].Value is not true) continue;
            var nombre = (fila.Cells["Nombre"].Value as string ?? "").Trim();
            if (nombre == "" || nombre.Length > 12) { problemas.Add($"{r.Nombre}: el nombre tiene que tener de 1 a 12 letras"); continue; }
            if (nombres.Contains(nombre.ToLowerInvariant())) { problemas.Add($"{nombre}: ya hay un rival con ese nombre"); continue; }
            var copia = new Rival { Archivo = "", Datos = r.Datos.DeepClone().AsObject() };
            copia.Nombre = nombre;
            copia.Liga = fila.Cells["Liga"].Value as string ?? r.Liga;
            var destino = Path.Combine(_repo.Jugadores, copia.NombreArchivo());
            if (File.Exists(destino)) { problemas.Add($"{nombre}: ya existe {Path.GetFileName(destino)}"); continue; }
            Directory.CreateDirectory(_repo.Jugadores);
            copia.Guardar(destino);
            nombres.Add(nombre.ToLowerInvariant());
            agregados.Add($"{nombre} (liga {copia.Liga})");
        }

        CargarRivales();
        MessageBox.Show((agregados.Count > 0 ? $"Agregados: {string.Join(", ", agregados)}." : "No se agregó ninguno.")
                        + (problemas.Count > 0 ? "\n\nNo se agregaron:\n" + string.Join("\n", problemas) : ""), "Rivales");
    }

    private void GuardarLista()
    {
        _gridLista.EndEdit();
        var cambios = 0;
        foreach (DataGridViewRow fila in _gridLista.Rows)
        {
            if (fila.Tag is not Rival r) continue;
            var nombre = (fila.Cells["Nombre"].Value as string ?? "").Trim();
            var liga = fila.Cells["Liga"].Value as string ?? r.Liga;
            if (nombre == r.Nombre && liga == r.Liga) continue;
            if (nombre == "" || nombre.Length > 12) { MessageBox.Show($"{r.Nombre}: el nombre tiene que tener de 1 a 12 letras."); continue; }
            r.Nombre = nombre;
            r.Liga = liga;
            var nuevo = Path.Combine(_repo.Jugadores, r.NombreArchivo());
            r.Guardar(nuevo);
            if (!string.Equals(nuevo, r.Archivo, StringComparison.OrdinalIgnoreCase)) File.Delete(r.Archivo);
            cambios++;
        }

        CargarRivales();
        MessageBox.Show($"{cambios} rival(es) guardado(s).", "Rivales");
    }

    private void QuitarRival()
    {
        if (_gridLista.CurrentRow?.Tag is not Rival r) return;
        if (MessageBox.Show($"¿Quitar a {r.Nombre} de la lista de rivales?", "Rivales", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        File.Delete(r.Archivo);
        CargarRivales();
    }

    // =====================================================================================
    // Torneos
    // =====================================================================================

    private ArchivoTorneos _torneos = null!;
    private ListBox _listaTorneos = null!;
    private TextBox _tNombre = null!, _tParticipantes = null!, _tGrupos = null!;
    private ComboBox _tRango = null!, _tPlantilla = null!, _tModoPart = null!, _tModoBonus = null!, _tItem = null!, _tBanner = null!;
    private NumericUpDown[] _tPremios = null!;
    private RadioButton _tSorteo = null!, _tFecha = null!;
    private NumericUpDown _tMes = null!, _tSemana = null!;
    private ListBox _tRivalesLiga = null!;
    private Label _tAviso = null!;

    private TabPage PestanaTorneos()
    {
        var p = new TabPage("Torneos");
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 330 };

        var izq = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        izq.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        izq.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _listaTorneos = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        _listaTorneos.SelectedIndexChanged += (_, _) => MostrarTorneo();
        izq.Controls.Add(_listaTorneos);
        var bIzq = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        bIzq.Controls.Add(Boton("Nuevo", () => AgregarTorneo(false)));
        bIzq.Controls.Add(Boton("Duplicar", () => AgregarTorneo(true)));
        bIzq.Controls.Add(Boton("Borrar", BorrarTorneo));
        izq.Controls.Add(bIzq);
        split.Panel1.Controls.Add(izq);

        var f = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new Padding(8) };
        f.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        f.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _tNombre = new TextBox { Width = 320 };
        Fila(f, "Nombre", _tNombre);

        _tRango = Combo(Datos.Rangos);
        _tPlantilla = Combo([]);
        _tPlantilla.Width = 520;
        _tRango.SelectedIndexChanged += (_, _) =>
        {
            _tPlantilla.Items.Clear();
            foreach (var pl in Datos.Plantillas.Where(x => x.Rango == (string?)_tRango.SelectedItem)) _tPlantilla.Items.Add(pl);
            if (_tPlantilla.Items.Count > 0) _tPlantilla.SelectedIndex = 0;
            RecargarRivalesLiga();
        };
        Fila(f, "Rango", _tRango);
        Fila(f, "Copia de", _tPlantilla, "Formato, estadio, música, trofeo y (si no los cambiás) premio y bonus.");

        _tModoPart = Combo(["Aleatorios (sorteo semanal de su liga)", "Los mejores de su liga", "Lista: nombres, \"mejor N\", \"peor N\""]);
        _tModoPart.Width = 320;
        _tModoPart.SelectedIndexChanged += (_, _) => _tParticipantes.Enabled = _tModoPart.SelectedIndex == 2;
        Fila(f, "Rivales", _tModoPart);
        _tParticipantes = new TextBox { Width = 520 };
        Fila(f, "", _tParticipantes, "Separados por coma. Doble clic en un rival de abajo para agregarlo.");
        _tRivalesLiga = new ListBox { Width = 320, Height = 110, IntegralHeight = false };
        _tRivalesLiga.DoubleClick += (_, _) =>
        {
            if (_tRivalesLiga.SelectedItem is not string s) return;
            var nombre = s.Split(" (")[0];
            _tModoPart.SelectedIndex = 2;
            _tParticipantes.Text = string.Join(", ", _tParticipantes.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Append(nombre));
        };
        Fila(f, "", _tRivalesLiga);

        var premios = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        _tPremios = Enumerable.Range(0, 4).Select(_ => new NumericUpDown { Maximum = 65000, Increment = 50, Width = 80 }).ToArray();
        foreach (var (n, k) in _tPremios.Select((n, k) => (n, k)))
        {
            premios.Controls.Add(new Label { Text = $"{k + 1}º", AutoSize = true, Margin = new Padding(6, 6, 2, 0) });
            premios.Controls.Add(n);
        }
        premios.Controls.Add(Boton("Los de la plantilla", () => { foreach (var n in _tPremios) n.Value = 0; }));
        Fila(f, "Premios (G)", premios, "En 0 = los de la plantilla.");

        _tModoBonus = Combo(["El de la plantilla", "Un ítem", "Al azar: grupos de ítems"]);
        _tModoBonus.SelectedIndexChanged += (_, _) =>
        {
            _tItem.Enabled = _tModoBonus.SelectedIndex == 1;
            _tGrupos.Enabled = _tModoBonus.SelectedIndex == 2;
        };
        Fila(f, "Bonus", _tModoBonus);
        _tItem = Combo(Datos.Items.OrderBy(x => x).ToArray());
        _tItem.Width = 220;
        Fila(f, "", _tItem);
        _tGrupos = new TextBox { Multiline = true, Width = 520, Height = 70, ScrollBars = ScrollBars.Vertical };
        var gruposPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown };
        gruposPanel.Controls.Add(_tGrupos);
        gruposPanel.Controls.Add(Boton("Agregar un grupo con los 38 Disc Chips", () =>
        {
            _tModoBonus.SelectedIndex = 2;
            _tGrupos.AppendText((_tGrupos.Text.Length > 0 && !_tGrupos.Text.EndsWith('\n') ? Environment.NewLine : "") + string.Join(", ", Datos.DiscChips));
        }));
        Fila(f, "", gruposPanel, "Una línea por grupo, ítems separados por coma. Primero sale un grupo y después un ítem del grupo.");

        var cuando = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        _tSorteo = new RadioButton { Text = "Sorteo anual de su rango", AutoSize = true, Checked = true };
        _tFecha = new RadioButton { Text = "Fecha fija:", AutoSize = true };
        _tMes = new NumericUpDown { Minimum = 1, Maximum = 12, Width = 50, Value = 12 };
        _tSemana = new NumericUpDown { Minimum = 1, Maximum = 4, Width = 50, Value = 2 };
        cuando.Controls.AddRange([_tSorteo, _tFecha, new Label { Text = "mes", AutoSize = true, Margin = new Padding(3, 6, 0, 0) }, _tMes,
            new Label { Text = "semana", AutoSize = true, Margin = new Padding(3, 6, 0, 0) }, _tSemana]);
        Fila(f, "Cuándo", cuando);

        _tBanner = Combo([]);
        _tBanner.Width = 320;
        Fila(f, "Banner", _tBanner, "Las imágenes se cargan en la pestaña Banners.");

        var botones = new FlowLayoutPanel { AutoSize = true };
        botones.Controls.Add(Boton("Aplicar cambios a este torneo", AplicarTorneo));
        botones.Controls.Add(Boton("Guardar torneos.json", GuardarTorneos));
        Fila(f, "", botones);
        _tAviso = new Label { AutoSize = true, ForeColor = Color.DarkRed, MaximumSize = new Size(700, 0) };
        Fila(f, "", _tAviso);

        split.Panel2.Controls.Add(f);
        p.Controls.Add(split);
        CargarTorneos();
        return p;
    }

    private void CargarTorneos()
    {
        _torneos = ArchivoTorneos.Leer(_repo.Torneos);
        RecargarListasTorneo();
        ListarTorneos();
    }

    private void RecargarListasTorneo()
    {
        var actual = _tBanner.SelectedItem as string;
        _tBanner.Items.Clear();
        _tBanner.Items.Add("(el de la plantilla)");
        if (Directory.Exists(_repo.Imagenes))
            foreach (var f in Directory.GetFiles(_repo.Imagenes, "*.png").OrderBy(x => x)) _tBanner.Items.Add("imagenes/" + Path.GetFileName(f));
        _tBanner.SelectedItem = actual ?? "(el de la plantilla)";
        if (_tBanner.SelectedIndex < 0) _tBanner.SelectedIndex = 0;
        RecargarRivalesLiga();
    }

    private void RecargarRivalesLiga()
    {
        if (_tRivalesLiga == null) return;
        _tRivalesLiga.Items.Clear();
        var rango = _tRango.SelectedItem as string ?? "";
        foreach (var r in _repo.LeerRivales(_repo.Originales).Concat(_repo.LeerRivales(_repo.Jugadores))
                     .Where(x => x.Liga == rango).OrderByDescending(x => x.Total))
            _tRivalesLiga.Items.Add($"{r.Nombre} ({r.Total})");
    }

    private void ListarTorneos()
    {
        var sel = _listaTorneos.SelectedIndex;
        _listaTorneos.Items.Clear();
        foreach (var t in _torneos.Torneos.OfType<JsonObject>())
        {
            var pl = Datos.Plantillas.FirstOrDefault(x => x.Id == (int?)t["plantilla"]);
            var cuando = t["fecha"] is JsonObject fe ? $"{fe["mes"]}/{fe["semana"]}" : t["id"] != null ? "fijo" : "sorteo";
            _listaTorneos.Items.Add($"[{pl?.Rango ?? "?"}] {(string?)t["nombre"]} — {cuando}");
        }

        if (_listaTorneos.Items.Count > 0) _listaTorneos.SelectedIndex = Math.Clamp(sel, 0, _listaTorneos.Items.Count - 1);
    }

    private JsonObject? TorneoActual() =>
        _listaTorneos.SelectedIndex >= 0 ? _torneos.Torneos[_listaTorneos.SelectedIndex] as JsonObject : null;

    private void MostrarTorneo()
    {
        var t = TorneoActual();
        _tAviso.Text = "";
        if (t == null) return;
        _tNombre.Text = (string?)t["nombre"] ?? "";
        var pl = Datos.Plantillas.FirstOrDefault(x => x.Id == (int?)t["plantilla"]);
        _tRango.SelectedItem = pl?.Rango ?? "A";
        if (pl != null) _tPlantilla.SelectedItem = _tPlantilla.Items.Cast<Plantilla>().FirstOrDefault(x => x.Id == pl.Id);
        else _tAviso.Text = $"La plantilla {t["plantilla"]} no está en la lista del editor (torneo especial): no la cambies.";

        switch (t["participantes"])
        {
            case JsonValue v when ((string?)v)?.Trim().ToLowerInvariant() == "mejores":
                _tModoPart.SelectedIndex = 1; _tParticipantes.Text = ""; break;
            case JsonArray a when a.All(x => x is JsonValue xv && xv.TryGetValue<int>(out _)):
                _tModoPart.SelectedIndex = 0; _tParticipantes.Text = string.Join(", ", a); break;
            case JsonArray a:
                _tModoPart.SelectedIndex = 2; _tParticipantes.Text = string.Join(", ", a.Select(x => x?.ToString())); break;
            default:
                _tModoPart.SelectedIndex = 0; _tParticipantes.Text = ""; break;
        }

        var pr = t["premios"] as JsonArray;
        for (var k = 0; k < 4; k++) _tPremios[k].Value = pr != null && k < pr.Count ? (int)pr[k]! : 0;

        _tGrupos.Text = "";
        switch (t["bonus"])
        {
            case null:
                _tModoBonus.SelectedIndex = 0; break;
            case JsonValue v:
                _tModoBonus.SelectedIndex = 1; _tItem.SelectedItem = Datos.Items.FirstOrDefault(i => Datos.Clave(i) == Datos.Clave(v.ToString())); break;
            case JsonArray a when a.Any(x => x is JsonArray):
                _tModoBonus.SelectedIndex = 2;
                _tGrupos.Text = string.Join(Environment.NewLine, a.Select(g => g is JsonArray ga ? string.Join(", ", ga) : g?.ToString())); break;
            case JsonArray a:
                _tModoBonus.SelectedIndex = 2; _tGrupos.Text = string.Join(", ", a); break;
        }

        if (t["fecha"] is JsonObject f)
        {
            _tFecha.Checked = true;
            _tMes.Value = (int?)f["mes"] ?? 12;
            _tSemana.Value = (int?)f["semana"] ?? 2;
        }
        else _tSorteo.Checked = true;

        _tBanner.SelectedItem = (string?)t["banner"] ?? "(el de la plantilla)";
        if (_tBanner.SelectedIndex < 0) _tBanner.SelectedIndex = 0;
        if (t["id"] != null) _tAviso.Text = "Torneo con número fijo (\"id\"): la fecha se define en \"calendario\" de torneos.json.";
    }

    private void AplicarTorneo()
    {
        var t = TorneoActual();
        if (t == null) return;
        var errores = new List<string>();
        var nombre = _tNombre.Text.Trim();
        if (nombre == "" || nombre.Length > 28) errores.Add("El nombre tiene que tener de 1 a 28 letras.");
        if (_tPlantilla.SelectedItem is not Plantilla pl) { errores.Add("Elegí de qué torneo copia."); pl = null!; }

        JsonNode? participantes = null;
        if (pl != null)
            switch (_tModoPart.SelectedIndex)
            {
                case 0:
                    participantes = new JsonArray(Datos.HuecosAleatorios(pl.Rango).Select(x => (JsonNode)x).ToArray()); break;
                case 1:
                    participantes = "mejores"; break;
                default:
                    var lista = _tParticipantes.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (lista.Length is < 1 or > 7) errores.Add("La lista de rivales tiene que tener de 1 a 7.");
                    participantes = new JsonArray(lista.Select(x => int.TryParse(x, out var n) ? (JsonNode)n : x).ToArray());
                    break;
            }

        JsonNode? bonus = null;
        if (_tModoBonus.SelectedIndex == 1)
        {
            if (_tItem.SelectedItem is not string item) errores.Add("Elegí el ítem del bonus.");
            else bonus = item;
        }
        else if (_tModoBonus.SelectedIndex == 2)
        {
            var grupos = _tGrupos.Lines.Select(l => l.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(g => g.Length > 0).ToList();
            var malos = grupos.SelectMany(g => g).Where(i => !Datos.ItemValido(i)).Distinct().ToList();
            if (grupos.Count == 0) errores.Add("Escribí al menos un ítem en los grupos.");
            if (malos.Count > 0) errores.Add($"Estos ítems no existen: {string.Join(", ", malos)}.");
            JsonNode Item(string i) => int.TryParse(i, out var n) ? n : i;
            bonus = grupos.Count == 1
                ? new JsonArray(grupos[0].Select(Item).ToArray())
                : new JsonArray(grupos.Select(g => (JsonNode)new JsonArray(g.Select(Item).ToArray())).ToArray());
        }

        if (errores.Count > 0) { _tAviso.Text = string.Join("\n", errores); return; }

        // Se reescribe el torneo con un orden fijo, conservando campos avanzados ("id", "meses", "imagenNombre", ...).
        var nuevo = new JsonObject { ["nombre"] = nombre, ["plantilla"] = pl!.Id };
        if (t["id"] != null) nuevo["id"] = t["id"]!.DeepClone();
        if (_tFecha.Checked && t["id"] == null) nuevo["fecha"] = new JsonObject { ["mes"] = (int)_tMes.Value, ["semana"] = (int)_tSemana.Value };
        if (_tBanner.SelectedIndex > 0) nuevo["banner"] = (string)_tBanner.SelectedItem!;
        var premios = _tPremios.Select(n => (int)n.Value).ToList();
        while (premios.Count > 0 && premios[^1] == 0) premios.RemoveAt(premios.Count - 1);
        if (premios.Count > 0) nuevo["premios"] = new JsonArray(premios.Select(x => (JsonNode)x).ToArray());
        if (bonus != null) nuevo["bonus"] = bonus;
        nuevo["participantes"] = participantes;
        foreach (var extra in new[] { "meses", "imagenNombre", "sorteo" })
            if (t[extra] != null) nuevo[extra] = t[extra]!.DeepClone();

        _torneos.Torneos[_listaTorneos.SelectedIndex] = nuevo;
        ListarTorneos();
        _tAviso.ForeColor = Color.DarkGreen;
        _tAviso.Text = "Aplicado. Acordate de \"Guardar torneos.json\".";
        _tAviso.ForeColor = Color.DarkGreen;
    }

    private void AgregarTorneo(bool duplicar)
    {
        JsonObject nuevo;
        if (duplicar && TorneoActual() is { } t)
        {
            nuevo = t.DeepClone().AsObject();
            nuevo["nombre"] = (string?)t["nombre"] + " 2";
            nuevo.Remove("id");
        }
        else
            nuevo = new JsonObject
            {
                ["nombre"] = "Nuevo torneo", ["plantilla"] = 0x3D,
                ["participantes"] = new JsonArray(Datos.HuecosAleatorios("E").Select(x => (JsonNode)x).ToArray())
            };

        _torneos.Torneos.Add(nuevo);
        ListarTorneos();
        _listaTorneos.SelectedIndex = _listaTorneos.Items.Count - 1;
    }

    private void BorrarTorneo()
    {
        if (TorneoActual() is not { } t) return;
        if (MessageBox.Show($"¿Borrar \"{t["nombre"]}\"?", "Torneos", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        _torneos.Torneos.RemoveAt(_listaTorneos.SelectedIndex);
        ListarTorneos();
    }

    private void GuardarTorneos()
    {
        var problemas = ValidarTorneos();
        if (problemas.Count > 0 &&
            MessageBox.Show("Hay problemas:\n" + string.Join("\n", problemas) + "\n\n¿Guardar igual?", "Torneos", MessageBoxButtons.YesNo) != DialogResult.Yes)
            return;
        _torneos.Guardar(_repo.Torneos);
        MessageBox.Show("torneos.json guardado.", "Torneos");
    }

    private List<string> ValidarTorneos()
    {
        var res = new List<string>();
        var nombresRivales = _repo.LeerRivales(_repo.Originales).Concat(_repo.LeerRivales(_repo.Jugadores))
            .SelectMany(r => new[] { r.Nombre.ToLowerInvariant(), Path.GetFileNameWithoutExtension(r.Archivo).ToLowerInvariant() }).ToHashSet();
        foreach (var t in _torneos.Torneos.OfType<JsonObject>())
        {
            var n = (string?)t["nombre"];
            if (t["banner"] is JsonValue b && !File.Exists(Path.Combine(Path.GetDirectoryName(_repo.Torneos)!, b.ToString().Replace('/', '\\'))))
                res.Add($"{n}: no existe la imagen {b}");
            if (t["participantes"] is JsonArray a)
                foreach (var x in a.OfType<JsonValue>().Select(v => v.ToString()))
                    if (!int.TryParse(x, out _) && !x.StartsWith("mejor ", StringComparison.OrdinalIgnoreCase)
                                                && !x.StartsWith("peor ", StringComparison.OrdinalIgnoreCase) && !nombresRivales.Contains(x.ToLowerInvariant()))
                        res.Add($"{n}: \"{x}\" no está en la lista de rivales");
        }

        return res;
    }

    // =====================================================================================
    // Banners
    // =====================================================================================

    private PictureBox _bVista = null!;
    private TextBox _bNombre = null!;
    private ComboBox _bTorneo = null!;
    private Label _bEstado = null!;
    private Bitmap? _bResultado;

    private TabPage PestanaBanners()
    {
        var p = new TabPage("Banners");
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        t.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(1100, 0),
            Text = "Abrí la imagen que hizo la IA con fondo liso MAGENTA (o verde si el logo tiene violeta/rosa). " +
                   "El editor saca el fondo, la deja en 480×192 con transparencia y la guarda en imagenes/. " +
                   "La vista muestra el resultado sobre gris, negro, blanco y azul: si se ve el fondo viejo o un halo, pedile otra a la IA."
        });
        var fila = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        fila.Controls.Add(Boton("Abrir imagen…", AbrirBanner));
        fila.Controls.Add(new Label { Text = "Guardar como:", AutoSize = true, Margin = new Padding(12, 8, 3, 3) });
        _bNombre = new TextBox { Width = 220 };
        fila.Controls.Add(_bNombre);
        fila.Controls.Add(new Label { Text = ".png   para el torneo:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        _bTorneo = Combo([]);
        _bTorneo.Width = 300;
        fila.Controls.Add(_bTorneo);
        fila.Controls.Add(Boton("Guardar", GuardarBanner));
        t.Controls.Add(fila);
        _bVista = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(30, 30, 30) };
        t.Controls.Add(_bVista);
        _bEstado = new Label { AutoSize = true };
        t.Controls.Add(_bEstado);
        p.Controls.Add(t);
        p.Enter += (_, _) =>
        {
            _bTorneo.Items.Clear();
            _bTorneo.Items.Add("(ninguno, solo guardar la imagen)");
            foreach (var x in _listaTorneos.Items) _bTorneo.Items.Add(x!);
            _bTorneo.SelectedIndex = 0;
        };
        return p;
    }

    private void AbrirBanner()
    {
        using var d = new OpenFileDialog
        {
            Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.jfif;*.webp;*.bmp|Todos|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads"
        };
        if (d.ShowDialog() != DialogResult.OK) return;
        try
        {
            Cursor = Cursors.WaitCursor;
            var (res, fondo) = Croma.Procesar(d.FileName);
            _bResultado?.Dispose();
            _bResultado = res;
            _bVista.Image?.Dispose();
            _bVista.Image = Croma.Vista(res);
            var baseNombre = Path.GetFileNameWithoutExtension(d.FileName).ToLowerInvariant();
            foreach (var quitar in new[] { " magenta", " verde" }) baseNombre = baseNombre.Replace(quitar, "");
            _bNombre.Text = new string(baseNombre.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
            _bEstado.Text = $"Fondo {fondo} quitado. Revisá la vista y guardá.";
        }
        catch (Exception e)
        {
            _bEstado.Text = "No se pudo procesar: " + e.Message;
            Cursor = Cursors.Default;
            MessageBox.Show($"No se pudo procesar {Path.GetFileName(d.FileName)}:\n\n{e.Message}", "Banners", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { Cursor = Cursors.Default; }
    }

    private void GuardarBanner()
    {
        if (_bResultado == null) { _bEstado.Text = "Primero abrí una imagen."; return; }
        var nombre = new string(_bNombre.Text.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());
        if (nombre == "") { _bEstado.Text = "Poné un nombre de archivo."; return; }
        Directory.CreateDirectory(_repo.Imagenes);
        var archivo = Path.Combine(_repo.Imagenes, nombre + ".png");
        if (File.Exists(archivo) && MessageBox.Show($"Ya existe {nombre}.png. ¿Reemplazarla?", "Banners", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        _bResultado.Save(archivo, System.Drawing.Imaging.ImageFormat.Png);
        var msg = $"Guardada imagenes/{nombre}.png.";
        if (_bTorneo.SelectedIndex > 0 && _torneos.Torneos[_bTorneo.SelectedIndex - 1] is JsonObject t)
        {
            t["banner"] = $"imagenes/{nombre}.png";
            _torneos.Guardar(_repo.Torneos);
            msg += $" Asignada a \"{t["nombre"]}\" y torneos.json guardado.";
            ListarTorneos();
        }

        RecargarListasTorneo();
        _bEstado.Text = msg;
    }

    // =====================================================================================
    // Subir a GitHub
    // =====================================================================================

    private TextBox _gEstado = null!, _gMensaje = null!;

    private TabPage PestanaSubir()
    {
        var p = new TabPage("Subir a GitHub");
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8) };
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(1100, 0),
            Text = "Sube a la rama main solo el contenido (rivales, torneos.json e imágenes). " +
                   "Tus amigos lo reciben la próxima vez que abran el juego. Cambios en el código de los mods necesitan publicar una versión."
        });
        _gEstado = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9.5f), WordWrap = false };
        t.Controls.Add(_gEstado);
        var fila = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        fila.Controls.Add(new Label { Text = "Descripción:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        _gMensaje = new TextBox { Width = 600, Text = "Contenido: rivales y torneos" };
        fila.Controls.Add(_gMensaje);
        fila.Controls.Add(Boton("Actualizar", ActualizarEstadoGit));
        fila.Controls.Add(Boton("Subir a GitHub", Subir));
        t.Controls.Add(fila);
        p.Controls.Add(t);
        return p;
    }

    private (int Codigo, string Salida) Git(params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = _repo.Raiz, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var proc = Process.Start(psi)!;
            var salida = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return (proc.ExitCode, salida.TrimEnd());
        }
        catch (Exception e)
        {
            return (-1, "No se pudo ejecutar git: " + e.Message + "\r\n(¿Está instalado Git?)");
        }
    }

    private void ActualizarEstadoGit()
    {
        var (_, estado) = Git(["status", "--porcelain", "--", .. _repo.Contenido]);
        var (_, rama) = Git("branch", "--show-current");
        _gEstado.Text = $"Rama: {rama}\r\n\r\n" + (estado == "" ? "No hay cambios de contenido para subir." :
            "Cambios para subir (M = modificado, ?? = nuevo, D = borrado):\r\n" + estado.Replace("\n", "\r\n"));
    }

    private void Subir()
    {
        var problemas = ValidarTorneos();
        if (problemas.Count > 0) { MessageBox.Show("Antes de subir, corregí:\n" + string.Join("\n", problemas), "Subir"); return; }
        var (_, rama) = Git("branch", "--show-current");
        if (rama != "main") { MessageBox.Show($"Estás en la rama \"{rama}\". El lanzador baja el contenido de main.", "Subir"); return; }
        var (_, estado) = Git(["status", "--porcelain", "--", .. _repo.Contenido]);
        if (estado == "") { MessageBox.Show("No hay cambios de contenido para subir.", "Subir"); return; }
        if (MessageBox.Show("¿Subir estos cambios a GitHub (main)?\n\n" + estado, "Subir", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

        Cursor = Cursors.WaitCursor;
        var log = new List<string>();
        try
        {
            var mensaje = _gMensaje.Text.Trim() == "" ? "Contenido: rivales y torneos" : _gMensaje.Text.Trim();
            var pasos = new List<string[]>();
            pasos.Add(new[] { "add", "-A", "--" }.Concat(_repo.Contenido).ToArray());
            pasos.Add(new[] { "commit", "-m", mensaje, "--" }.Concat(_repo.Contenido).ToArray());
            pasos.Add(new[] { "pull", "--rebase", "--autostash" });
            pasos.Add(new[] { "push", "origin", "main" });
            foreach (var paso in pasos)
            {
                var (codigo, salida) = Git(paso);
                log.Add($"> git {string.Join(' ', paso.Take(3))}\r\n{salida}");
                if (codigo != 0) throw new Exception($"git {paso[0]} falló");
            }

            MessageBox.Show("Listo: subido a GitHub. Tus amigos lo reciben la próxima vez que abran el juego.", "Subir");
        }
        catch (Exception e)
        {
            MessageBox.Show("No se pudo subir: " + e.Message + "\n\nMirá el detalle en la pestaña.", "Subir");
        }
        finally
        {
            Cursor = Cursors.Default;
            ActualizarEstadoGit();
            _gEstado.AppendText("\r\n\r\n" + string.Join("\r\n\r\n", log));
        }
    }

    // =====================================================================================
    // Ayudas de interfaz
    // =====================================================================================

    private static Button Boton(string texto, Action accion)
    {
        var b = new Button { Text = texto, AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
        b.Click += (_, _) => accion();
        return b;
    }

    private static ComboBox Combo(object[] items)
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        c.Items.AddRange(items);
        if (items.Length > 0) c.SelectedIndex = 0;
        return c;
    }

    private static DataGridView Grilla() => new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, EditMode = DataGridViewEditMode.EditOnEnter
    };

    private static DataGridViewTextBoxColumn Solo(string nombre, int ancho) =>
        new() { Name = nombre, HeaderText = nombre, Width = ancho, ReadOnly = true };

    private static void Fila(TableLayoutPanel t, string etiqueta, Control c, string? ayuda = null)
    {
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(new Label { Text = etiqueta, AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
        if (ayuda == null) { t.Controls.Add(c); return; }
        var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        panel.Controls.Add(c);
        panel.Controls.Add(new Label { Text = ayuda, AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(700, 0) });
        t.Controls.Add(panel);
    }
}
