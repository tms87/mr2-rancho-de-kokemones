using System.Diagnostics;
using System.Text.Json.Nodes;

namespace EditorKokemon;

/// <summary>Editor de contenido: rivales, torneos, banners y subida a GitHub.</summary>
public sealed partial class Ventana : Form
{
    private readonly Repo _repo;

    public Ventana(Repo repo)
    {
        _repo = repo;
        Text = $"Editor Kokemon — {repo.Raiz}";
        Width = 1180;
        Height = 760;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(PestanaRivales());
        tabs.TabPages.Add(PestanaTorneos());
        tabs.TabPages.Add(PestanaBanners());
        tabs.TabPages.Add(PestanaCompetencias());
        tabs.TabPages.Add(PestanaSubir());
        tabs.SelectedIndexChanged += (_, _) =>
        {
            if (tabs.SelectedTab?.Name == "subir") ActualizarEstadoGit();
            if (tabs.SelectedTab?.Name == "torneos") RecargarListasTorneo();
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
        var p = new TabPage(T("Rivales", "Rivals")) { Name = "rivales" };
        var tabla = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        tabla.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tabla.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        tabla.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tabla.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tabla.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        tabla.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var arriba = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        arriba.Controls.Add(new Label { Text = T("Exportados:", "Exports:"), AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        _carpetaExportados = new TextBox { Width = 560, Text = HayRecibidos() ? CarpetaRecibidos : CarpetaExportadosPorDefecto() };
        arriba.Controls.Add(_carpetaExportados);
        arriba.Controls.Add(Boton(T("Cambiar…", "Change…"), () =>
        {
            using var d = new FolderBrowserDialog { SelectedPath = _carpetaExportados.Text };
            if (d.ShowDialog() == DialogResult.OK) { _carpetaExportados.Text = d.SelectedPath; CargarRivales(); }
        }));
        arriba.Controls.Add(Boton(T("Recibidos (GitHub)", "Received (GitHub)"), () => { _carpetaExportados.Text = CarpetaRecibidos; CargarRivales(); }));
        arriba.Controls.Add(Boton(T("Mis exportados", "My exports"), () => { _carpetaExportados.Text = CarpetaExportadosPorDefecto(); CargarRivales(); }));
        arriba.Controls.Add(Boton(T("Recargar", "Reload"), CargarRivales));
        tabla.Controls.Add(arriba);

        _gridExportados = Grilla();
        _gridExportados.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Agregar", HeaderText = T("Agregar", "Add"), Width = 60 });
        _gridExportados.Columns.Add(Solo("Nombre", 110));
        _gridExportados.Columns.Add(ColumnaVersion(_gridExportados));
        _gridExportados.Columns.Add(Solo("Entrenador", 90));
        _gridExportados.Columns.Add(Solo("Raza", 170));
        _gridExportados.Columns.Add(Solo("Total", 60));
        _gridExportados.Columns.Add(Solo("Exportado", 75));
        _gridExportados.Columns.Add(new DataGridViewComboBoxColumn { Name = "Liga", HeaderText = T("Liga", "League"), DataSource = Datos.Rangos, Width = 55 });
        _gridExportados.Columns.Add(Solo("Estado", 330));
        _gridExportados.Columns.Add(Solo("Archivo", 260));
        tabla.Controls.Add(_gridExportados);

        var medio = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        medio.Controls.Add(Boton(T("Agregar los marcados a la lista de rivales", "Add the checked ones to the rivals list"), AgregarRivales));
        medio.Controls.Add(Boton(T("Descartar el archivo del seleccionado", "Discard the selected one's file"), DescartarArchivo));
        medio.Controls.Add(new Label
        {
            AutoSize = true, Margin = new Padding(12, 8, 3, 3),
            Text = T("Liga: la que alcanzó en el juego; si sus stats no encajan con los originales de esa liga, se propone otra.",
                "League: the one it reached in the game; if its stats don't fit that league's originals, another one is suggested.")
        });
        tabla.Controls.Add(medio);

        tabla.Controls.Add(new Label { Text = T("Lista de rivales (jugadores):", "Rivals list (players):"), AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        _gridLista = Grilla();
        _gridLista.Columns.Add(new DataGridViewTextBoxColumn { Name = "Nombre", HeaderText = T("Nombre", "Name"), Width = 110 });
        _gridLista.Columns.Add(ColumnaVersion(_gridLista));
        _gridLista.Columns.Add(Solo("Entrenador", 90));
        _gridLista.Columns.Add(Solo("Raza", 170));
        _gridLista.Columns.Add(Solo("Total", 60));
        _gridLista.Columns.Add(new DataGridViewComboBoxColumn { Name = "Liga", HeaderText = T("Liga", "League"), DataSource = Datos.Rangos, Width = 55 });
        _gridLista.Columns.Add(Solo("Origen", 60));
        _gridLista.Columns.Add(Solo("Archivo", 300));
        tabla.Controls.Add(_gridLista);

        var abajo = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        abajo.Controls.Add(Boton(T("Guardar cambios (versión y liga)", "Save changes (version and league)"), GuardarLista));
        abajo.Controls.Add(Boton(T("Quitar el seleccionado", "Remove the selected one"), QuitarRival));
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
        var todos = _repo.LeerRivales(_carpetaExportados.Text);
        _exportados = EsCarpetaRecibidos
            // Recibidos: cada archivo completo (se revisa entero y después se descarta), archivo por archivo.
            ? todos.OrderBy(r => Path.GetFileName(r.Archivo)).ToList()
            // Mis exportados: de cada monstruo, el exportado más nuevo (los archivos empiezan con la fecha del juego).
            : todos.OrderByDescending(r => r.Datos["crudo"] != null).ThenByDescending(r => Path.GetFileName(r.Archivo))
                .DistinctBy(r => (r.Entrenador, r.Nombre, r.Raza)).OrderBy(r => r.Nombre).ToList();

        _gridExportados.Rows.Clear();
        foreach (var r in _exportados)
        {
            var sugerida = LigaSugerida(r.Total, r.Liga, rangos);
            var estado = EstadoRival(r, originales);
            // Siempre desmarcados: se eligen uno por uno.
            var i = _gridExportados.Rows.Add(false, r.Nombre, VersionEnColumna(_gridExportados, r.Version), r.Entrenador, r.Raza, r.Total,
                r.Liga, sugerida, estado == "" ? (sugerida != r.Liga ? T($"Nuevo (sus stats encajan en {sugerida})", $"New (its stats fit {sugerida})") : T("Nuevo", "New")) : estado,
                Path.GetFileName(r.Archivo));
            _gridExportados.Rows[i].Tag = r;
            if (estado != "") _gridExportados.Rows[i].DefaultCellStyle.ForeColor = Color.Gray;
        }

        _gridLista.Rows.Clear();
        foreach (var r in _lista)
        {
            var i = _gridLista.Rows.Add(r.Nombre, VersionEnColumna(_gridLista, r.Version), r.Entrenador, r.Raza, r.Total,
                Datos.Rangos.Contains(r.Liga) ? r.Liga : "E", r.Origen, Path.GetFileName(r.Archivo));
            _gridLista.Rows[i].Tag = r;
            // Los que vienen de un exportado (con el monstruo completo) conservan el nombre del juego.
            _gridLista.Rows[i].Cells["Nombre"].ReadOnly = r.Completo;
            if (r.Completo) _gridLista.Rows[i].Cells["Nombre"].Style.ForeColor = Color.DimGray;
        }
    }

    // ---- Versión: desplegable con valores fijos, pero se puede escribir otra ----

    private DataGridViewComboBoxColumn ColumnaVersion(DataGridView grilla)
    {
        var col = new DataGridViewComboBoxColumn
        {
            Name = "Version", HeaderText = T("Versión", "Version"), Width = 110, FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox
        };
        col.Items.AddRange(Datos.Versiones.Cast<object>().ToArray());
        grilla.EditingControlShowing += (_, e) =>
        {
            if (grilla.CurrentCell?.OwningColumn == col && e.Control is ComboBox cb) cb.DropDownStyle = ComboBoxStyle.DropDown;
        };
        grilla.CellValidating += (_, e) =>
        {
            if (grilla.Columns[e.ColumnIndex] != col) return;
            var texto = (e.FormattedValue as string ?? "").Trim();
            if (!col.Items.Contains(texto)) col.Items.Add(texto);
            grilla.Rows[e.RowIndex].Cells[e.ColumnIndex].Value = texto;
        };
        grilla.DataError += (_, e) => e.ThrowException = false;
        return col;
    }

    /// <summary>Una versión escrita a mano tiene que estar entre las opciones de la columna para poder mostrarse.</summary>
    private static string VersionEnColumna(DataGridView grilla, string version)
    {
        if (grilla.Columns["Version"] is DataGridViewComboBoxColumn col && !col.Items.Contains(version)) col.Items.Add(version);
        return version;
    }

    /// <summary>La liga alcanzada si sus stats encajan con los originales de esa liga; si no, la que encaja.</summary>
    private static string LigaSugerida(int total, string liga, Dictionary<string, (int Min, int Max)> rangos)
    {
        if (rangos.TryGetValue(liga, out var r) && total >= r.Min && total <= r.Max) return liga;
        foreach (var l in Datos.Rangos) // de S a E: la más alta cuyo mínimo alcanza
            if (rangos.TryGetValue(l, out var x) && total >= x.Min) return l;
        return "E";
    }

    private static string Clave(string nombre, string version) => $"{nombre.Trim()}|{version.Trim()}".ToLowerInvariant();

    /// <summary>Vacío si se puede agregar; si no, por qué no (ya está, nombre + versión repetidos).</summary>
    private string EstadoRival(Rival r, List<Rival> originales)
    {
        // El mismo monstruo exacto (mismos bytes) ya está, con cualquier versión.
        var igual = r.Completo ? _lista.FirstOrDefault(x => (string?)x.Datos["crudo"] == (string?)r.Datos["crudo"]) : null;
        if (igual != null) return T($"Ya está en la lista como {igual.Etiqueta}", $"Already in the list as {igual.Etiqueta}");
        if (_lista.Any(x => string.Equals(Path.GetFileName(x.Archivo), r.NombreArchivo(), StringComparison.OrdinalIgnoreCase)
                            && x.Total == r.Total)) return T("Ya está en la lista", "Already in the list");
        if (_lista.Concat(originales).Any(x => Clave(x.Nombre, x.Version) == Clave(r.Nombre, r.Version)))
            return T("Ya hay un rival con ese nombre y versión: elegí otra versión para agregarlo", "There is already a rival with that name and version: pick another version to add it");
        return "";
    }

    private void AgregarRivales()
    {
        _gridExportados.EndEdit();
        var claves = _lista.Concat(_repo.LeerRivales(_repo.Originales)).Select(x => Clave(x.Nombre, x.Version)).ToHashSet();
        var agregados = new List<string>();
        var problemas = new List<string>();
        foreach (DataGridViewRow fila in _gridExportados.Rows)
        {
            if (fila.Tag is not Rival r || fila.Cells["Agregar"].Value is not true) continue;
            var copia = new Rival { Archivo = "", Datos = r.Datos.DeepClone().AsObject() };
            copia.Version = fila.Cells["Version"].Value as string ?? "";
            copia.Liga = fila.Cells["Liga"].Value as string ?? r.Liga;
            if (!claves.Add(Clave(copia.Nombre, copia.Version)))
            {
                problemas.Add(T($"{copia.Etiqueta}: ya hay un rival con ese nombre y versión (elegí otra versión)", $"{copia.Etiqueta}: there is already a rival with that name and version (pick another version)"));
                continue;
            }

            var destino = Path.Combine(_repo.Jugadores, copia.NombreArchivo());
            if (File.Exists(destino)) { problemas.Add(T($"{copia.Etiqueta}: ya existe {Path.GetFileName(destino)}", $"{copia.Etiqueta}: {Path.GetFileName(destino)} already exists")); continue; }
            Directory.CreateDirectory(_repo.Jugadores);
            copia.Guardar(destino);
            agregados.Add(T($"{copia.Etiqueta} (liga {copia.Liga})", $"{copia.Etiqueta} (league {copia.Liga})"));
        }

        CargarRivales();
        MessageBox.Show((agregados.Count > 0 ? T($"Agregados: {string.Join(", ", agregados)}.", $"Added: {string.Join(", ", agregados)}.") : T("No se agregó ninguno.", "None was added."))
                        + (problemas.Count > 0 ? T("\n\nNo se agregaron:\n", "\n\nNot added:\n") + string.Join("\n", problemas) : ""), T("Rivales", "Rivals"));
    }

    private void GuardarLista()
    {
        _gridLista.EndEdit();
        var cambios = 0;
        var claves = _lista.Concat(_repo.LeerRivales(_repo.Originales)).Select(x => Clave(x.Nombre, x.Version)).ToList();
        foreach (DataGridViewRow fila in _gridLista.Rows)
        {
            if (fila.Tag is not Rival r) continue;
            var nombre = r.Completo ? r.Nombre : (fila.Cells["Nombre"].Value as string ?? "").Trim();
            var version = (fila.Cells["Version"].Value as string ?? "").Trim();
            var liga = fila.Cells["Liga"].Value as string ?? r.Liga;
            if (nombre == r.Nombre && version == r.Version && liga == r.Liga) continue;
            if (nombre == "" || nombre.Length > 12) { MessageBox.Show(T($"{r.Nombre}: el nombre tiene que tener de 1 a 12 letras.", $"{r.Nombre}: the name must have 1 to 12 letters.")); continue; }
            var antes = Clave(r.Nombre, r.Version);
            var despues = Clave(nombre, version);
            if (despues != antes && claves.Contains(despues))
            {
                MessageBox.Show(T($"{nombre} ({version}): ya hay un rival con ese nombre y versión.", $"{nombre} ({version}): there is already a rival with that name and version."));
                continue;
            }

            claves.Remove(antes);
            claves.Add(despues);
            r.Nombre = nombre;
            r.Version = version;
            r.Liga = liga;
            var nuevo = Path.Combine(_repo.Jugadores, r.NombreArchivo());
            r.Guardar(nuevo);
            if (!string.Equals(nuevo, r.Archivo, StringComparison.OrdinalIgnoreCase)) File.Delete(r.Archivo);
            cambios++;
        }

        CargarRivales();
        MessageBox.Show(T($"{cambios} rival(es) guardado(s).", $"{cambios} rival(s) saved."), T("Rivales", "Rivals"));
    }

    private void QuitarRival()
    {
        if (_gridLista.CurrentRow?.Tag is not Rival r) return;
        if (MessageBox.Show(T($"¿Quitar a {r.Etiqueta} de la lista de rivales?", $"Remove {r.Etiqueta} from the rivals list?"), T("Rivales", "Rivals"), MessageBoxButtons.YesNo) != DialogResult.Yes) return;
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
        var p = new TabPage(T("Torneos", "Tournaments")) { Name = "torneos" };
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 330 };

        var izq = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        izq.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        izq.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _listaTorneos = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        _listaTorneos.SelectedIndexChanged += (_, _) => MostrarTorneo();
        izq.Controls.Add(_listaTorneos);
        var bIzq = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        bIzq.Controls.Add(Boton(T("Nuevo", "New"), () => AgregarTorneo(false)));
        bIzq.Controls.Add(Boton(T("Duplicar", "Duplicate"), () => AgregarTorneo(true)));
        bIzq.Controls.Add(Boton(T("Borrar", "Delete"), BorrarTorneo));
        izq.Controls.Add(bIzq);
        split.Panel1.Controls.Add(izq);

        var f = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new Padding(8) };
        f.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        f.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _tNombre = new TextBox { Width = 320 };
        Fila(f, T("Nombre", "Name"), _tNombre);

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
        Fila(f, T("Rango", "Rank"), _tRango);
        Fila(f, T("Copia de", "Copy of"), _tPlantilla, T("Formato, estadio, música, trofeo y (si no los cambiás) premio y bonus.", "Format, stadium, music, trophy and (unless you change them) prize and bonus."));

        _tModoPart = Combo([T("Aleatorios (sorteo semanal de su liga)", "Random (weekly draw of its league)"), T("Los mejores de su liga", "The best of its league"),
            T("Lista: nombres, \"mejor N\", \"peor N\"", "List: names, \"mejor N\" (best), \"peor N\" (worst)")]);
        _tModoPart.Width = 320;
        _tModoPart.SelectedIndexChanged += (_, _) => _tParticipantes.Enabled = _tModoPart.SelectedIndex == 2;
        Fila(f, T("Rivales", "Rivals"), _tModoPart);
        _tParticipantes = new TextBox { Width = 520 };
        Fila(f, "", _tParticipantes, T("Separados por coma. Doble clic en un rival de abajo para agregarlo.", "Comma separated. Double-click a rival below to add it."));
        _tRivalesLiga = new ListBox { Width = 320, Height = 110, IntegralHeight = false };
        _tRivalesLiga.DoubleClick += (_, _) =>
        {
            if (_tRivalesLiga.SelectedItem is not ItemRival s) return;
            var nombre = s.Referencia;
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
        premios.Controls.Add(Boton(T("Los de la plantilla", "The template's"), () => { foreach (var n in _tPremios) n.Value = 0; }));
        Fila(f, T("Premios (G)", "Prizes (G)"), premios, T("En 0 = los de la plantilla.", "0 = the template's."));

        _tModoBonus = Combo([T("El de la plantilla", "The template's"), T("Un ítem", "One item"), T("Al azar: grupos de ítems", "Random: item groups")]);
        _tModoBonus.SelectedIndexChanged += (_, _) =>
        {
            _tItem.Enabled = _tModoBonus.SelectedIndex == 1;
            _tGrupos.Enabled = _tModoBonus.SelectedIndex == 2;
        };
        Fila(f, T("Bonus", "Bonus"), _tModoBonus);
        _tItem = Combo(Datos.Items.OrderBy(x => x).ToArray());
        _tItem.Width = 220;
        Fila(f, "", _tItem);
        _tGrupos = new TextBox { Multiline = true, Width = 520, Height = 70, ScrollBars = ScrollBars.Vertical };
        var gruposPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown };
        gruposPanel.Controls.Add(_tGrupos);
        gruposPanel.Controls.Add(Boton(T("Agregar un grupo con los 38 Disc Chips", "Add a group with the 38 Disc Chips"), () =>
        {
            _tModoBonus.SelectedIndex = 2;
            _tGrupos.AppendText((_tGrupos.Text.Length > 0 && !_tGrupos.Text.EndsWith('\n') ? Environment.NewLine : "") + string.Join(", ", Datos.DiscChips));
        }));
        Fila(f, "", gruposPanel, T("Una línea por grupo, ítems separados por coma. Primero sale un grupo y después un ítem del grupo.", "One line per group, items comma separated. First a group is drawn, then an item from it."));

        var cuando = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        _tSorteo = new RadioButton { Text = T("Sorteo anual de su rango", "Yearly draw of its rank"), AutoSize = true, Checked = true };
        _tFecha = new RadioButton { Text = T("Fecha fija:", "Fixed date:"), AutoSize = true };
        _tMes = new NumericUpDown { Minimum = 1, Maximum = 12, Width = 50, Value = 12 };
        _tSemana = new NumericUpDown { Minimum = 1, Maximum = 4, Width = 50, Value = 2 };
        cuando.Controls.AddRange([_tSorteo, _tFecha, new Label { Text = "mes", AutoSize = true, Margin = new Padding(3, 6, 0, 0) }, _tMes,
            new Label { Text = "semana", AutoSize = true, Margin = new Padding(3, 6, 0, 0) }, _tSemana]);
        Fila(f, T("Cuándo", "When"), cuando);

        _tBanner = Combo([]);
        _tBanner.Width = 320;
        Fila(f, "Banner", _tBanner, T("Las imágenes se cargan en la pestaña Banners.", "Images are loaded in the Banners tab."));

        var botones = new FlowLayoutPanel { AutoSize = true };
        botones.Controls.Add(Boton(T("Aplicar cambios a este torneo", "Apply changes to this tournament"), AplicarTorneo));
        botones.Controls.Add(Boton(T("Guardar torneos.json", "Save torneos.json"), GuardarTorneos));
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
            _tRivalesLiga.Items.Add(new ItemRival(r.Referencia, $"{r.Etiqueta} ({r.Total})"));
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
        else _tAviso.Text = T($"La plantilla {t["plantilla"]} no está en la lista del editor (torneo especial): no la cambies.", $"Template {t["plantilla"]} is not in the editor's list (special tournament): don't change it.");

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
        if (t["id"] != null) _tAviso.Text = T("Torneo con número fijo (\"id\"): la fecha se define en \"calendario\" de torneos.json.", "Tournament with a fixed number (\"id\"): its date is set in torneos.json's \"calendario\".");
    }

    private void AplicarTorneo()
    {
        var t = TorneoActual();
        if (t == null) return;
        var errores = new List<string>();
        var nombre = _tNombre.Text.Trim();
        if (nombre == "" || nombre.Length > 28) errores.Add(T("El nombre tiene que tener de 1 a 28 letras.", "The name must have 1 to 28 letters."));
        if (_tPlantilla.SelectedItem is not Plantilla pl) { errores.Add(T("Elegí de qué torneo copia.", "Pick which tournament it copies.")); pl = null!; }

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
                    if (lista.Length is < 1 or > 7) errores.Add(T("La lista de rivales tiene que tener de 1 a 7.", "The rivals list must have 1 to 7."));
                    participantes = new JsonArray(lista.Select(x => int.TryParse(x, out var n) ? (JsonNode)n : x).ToArray());
                    break;
            }

        JsonNode? bonus = null;
        if (_tModoBonus.SelectedIndex == 1)
        {
            if (_tItem.SelectedItem is not string item) errores.Add(T("Elegí el ítem del bonus.", "Pick the bonus item."));
            else bonus = item;
        }
        else if (_tModoBonus.SelectedIndex == 2)
        {
            var grupos = _tGrupos.Lines.Select(l => l.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(g => g.Length > 0).ToList();
            var malos = grupos.SelectMany(g => g).Where(i => !Datos.ItemValido(i)).Distinct().ToList();
            if (grupos.Count == 0) errores.Add(T("Escribí al menos un ítem en los grupos.", "Write at least one item in the groups."));
            if (malos.Count > 0) errores.Add(T($"Estos ítems no existen: {string.Join(", ", malos)}.", $"These items don't exist: {string.Join(", ", malos)}."));
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
        _tAviso.Text = T("Aplicado. Acordate de \"Guardar torneos.json\".", "Applied. Remember to \"Save torneos.json\".");
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
                ["nombre"] = T("Nuevo torneo", "New tournament"), ["plantilla"] = 0x3D,
                ["participantes"] = new JsonArray(Datos.HuecosAleatorios("E").Select(x => (JsonNode)x).ToArray())
            };

        _torneos.Torneos.Add(nuevo);
        ListarTorneos();
        _listaTorneos.SelectedIndex = _listaTorneos.Items.Count - 1;
    }

    private void BorrarTorneo()
    {
        if (TorneoActual() is not { } t) return;
        if (MessageBox.Show(T($"¿Borrar \"{t["nombre"]}\"?", $"Delete \"{t["nombre"]}\"?"), T("Torneos", "Tournaments"), MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        _torneos.Torneos.RemoveAt(_listaTorneos.SelectedIndex);
        ListarTorneos();
    }

    private void GuardarTorneos()
    {
        var problemas = ValidarTorneos();
        if (problemas.Count > 0 &&
            MessageBox.Show(T("Hay problemas:\n", "There are problems:\n") + string.Join("\n", problemas) + T("\n\n¿Guardar igual?", "\n\nSave anyway?"), T("Torneos", "Tournaments"), MessageBoxButtons.YesNo) != DialogResult.Yes)
            return;
        _torneos.Guardar(_repo.Torneos);
        MessageBox.Show(T("torneos.json guardado.", "torneos.json saved."), T("Torneos", "Tournaments"));
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
                res.Add(T($"{n}: no existe la imagen {b}", $"{n}: image {b} doesn't exist"));
            if (t["participantes"] is JsonArray a)
                foreach (var x in a.OfType<JsonValue>().Select(v => v.ToString()))
                    if (!int.TryParse(x, out _) && !x.StartsWith("mejor ", StringComparison.OrdinalIgnoreCase)
                                                && !x.StartsWith("peor ", StringComparison.OrdinalIgnoreCase) && !nombresRivales.Contains(x.ToLowerInvariant()))
                        res.Add(T($"{n}: \"{x}\" no está en la lista de rivales", $"{n}: \"{x}\" is not in the rivals list"));
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
        var p = new TabPage("Banners") { Name = "banners" };
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        t.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(1100, 0),
            Text = T("Abrí la imagen que hizo la IA con fondo liso MAGENTA (o verde si el logo tiene violeta/rosa). " +
                   "El editor saca el fondo, la deja en 480×192 con transparencia y la guarda en imagenes/. " +
                   "La vista muestra el resultado sobre gris, negro, blanco y azul: si se ve el fondo viejo o un halo, pedile otra a la IA.",
                   "Open the AI-made image with a flat MAGENTA background (or green if the logo has purple/pink). " +
                   "The editor removes the background, makes it 480×192 with transparency and saves it in imagenes/. " +
                   "The preview shows the result over grey, black, white and blue: if you see the old background or a halo, ask the AI for another one.")
        });
        var fila = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        fila.Controls.Add(Boton(T("Abrir imagen…", "Open image…"), AbrirBanner));
        fila.Controls.Add(new Label { Text = T("Guardar como:", "Save as:"), AutoSize = true, Margin = new Padding(12, 8, 3, 3) });
        _bNombre = new TextBox { Width = 220 };
        fila.Controls.Add(_bNombre);
        fila.Controls.Add(new Label { Text = T(".png   para el torneo:", ".png   for the tournament:"), AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        _bTorneo = Combo([]);
        _bTorneo.Width = 300;
        fila.Controls.Add(_bTorneo);
        fila.Controls.Add(Boton(T("Guardar", "Save"), GuardarBanner));
        t.Controls.Add(fila);
        _bVista = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(30, 30, 30) };
        t.Controls.Add(_bVista);
        _bEstado = new Label { AutoSize = true };
        t.Controls.Add(_bEstado);
        p.Controls.Add(t);
        p.Enter += (_, _) =>
        {
            _bTorneo.Items.Clear();
            _bTorneo.Items.Add(T("(ninguno, solo guardar la imagen)", "(none, just save the image)"));
            foreach (var x in _listaTorneos.Items) _bTorneo.Items.Add(x!);
            _bTorneo.SelectedIndex = 0;
        };
        return p;
    }

    private void AbrirBanner()
    {
        using var d = new OpenFileDialog
        {
            Filter = T("Imágenes", "Images") + "|*.png;*.jpg;*.jpeg;*.jfif;*.webp;*.bmp|" + T("Todos", "All") + "|*.*",
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
            _bEstado.Text = T($"Fondo {fondo} quitado. Revisá la vista y guardá.", $"Background {fondo} removed. Check the preview and save.");
        }
        catch (Exception e)
        {
            _bEstado.Text = T("No se pudo procesar: ", "Could not process: ") + e.Message;
            Cursor = Cursors.Default;
            MessageBox.Show(T($"No se pudo procesar {Path.GetFileName(d.FileName)}:\n\n{e.Message}", $"Could not process {Path.GetFileName(d.FileName)}:\n\n{e.Message}"), "Banners", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { Cursor = Cursors.Default; }
    }

    private void GuardarBanner()
    {
        if (_bResultado == null) { _bEstado.Text = T("Primero abrí una imagen.", "Open an image first."); return; }
        var nombre = new string(_bNombre.Text.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());
        if (nombre == "") { _bEstado.Text = T("Poné un nombre de archivo.", "Enter a file name."); return; }
        Directory.CreateDirectory(_repo.Imagenes);
        var archivo = Path.Combine(_repo.Imagenes, nombre + ".png");
        if (File.Exists(archivo) && MessageBox.Show(T($"Ya existe {nombre}.png. ¿Reemplazarla?", $"{nombre}.png already exists. Replace it?"), "Banners", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        _bResultado.Save(archivo, System.Drawing.Imaging.ImageFormat.Png);
        var msg = T($"Guardada imagenes/{nombre}.png.", $"Saved imagenes/{nombre}.png.");
        if (_bTorneo.SelectedIndex > 0 && _torneos.Torneos[_bTorneo.SelectedIndex - 1] is JsonObject t)
        {
            t["banner"] = $"imagenes/{nombre}.png";
            _torneos.Guardar(_repo.Torneos);
            msg += T($" Asignada a \"{t["nombre"]}\" y torneos.json guardado.", $" Assigned to \"{t["nombre"]}\" and torneos.json saved.");
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
        var p = new TabPage(T("Subir a GitHub", "Upload to GitHub")) { Name = "subir" };
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8) };
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(1100, 0),
            Text = T("Sube a la rama main el contenido de la liga (rivales, torneos, banners, competencias y liga.json). " +
                   "Los jugadores lo reciben la próxima vez que abran el juego.",
                   "Uploads the league content (rivals, tournaments, banners, competitions and liga.json) to the main branch. " +
                   "Players get it the next time they open the game.")
        });
        _gEstado = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9.5f), WordWrap = false };
        t.Controls.Add(_gEstado);
        var fila = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        fila.Controls.Add(new Label { Text = T("Descripción:", "Description:"), AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        _gMensaje = new TextBox { Width = 600, Text = T("Contenido de la liga", "League content") };
        fila.Controls.Add(_gMensaje);
        fila.Controls.Add(Boton(T("Actualizar", "Refresh"), ActualizarEstadoGit));
        fila.Controls.Add(Boton(T("Subir a GitHub", "Upload to GitHub"), Subir));
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
            return (-1, T("No se pudo ejecutar git: ", "Could not run git: ") + e.Message + T("\r\n(¿Está instalado Git?)", "\r\n(Is Git installed?)"));
        }
    }

    private void ActualizarEstadoGit()
    {
        var (_, estado) = Git(["status", "--porcelain", "--", .. _repo.Contenido]);
        var (_, rama) = Git("branch", "--show-current");
        _gEstado.Text = T($"Rama: {rama}\r\n\r\n", $"Branch: {rama}\r\n\r\n") + (estado == "" ? T("No hay cambios de contenido para subir.", "There are no content changes to upload.") :
            T("Cambios para subir (M = modificado, ?? = nuevo, D = borrado):\r\n", "Changes to upload (M = modified, ?? = new, D = deleted):\r\n") + estado.Replace("\n", "\r\n"));
    }

    private void Subir()
    {
        var problemas = ValidarTorneos();
        if (problemas.Count > 0) { MessageBox.Show(T("Antes de subir, corregí:\n", "Before uploading, fix:\n") + string.Join("\n", problemas), T("Subir", "Upload")); return; }
        var (_, rama) = Git("branch", "--show-current");
        if (rama != "main") { MessageBox.Show(T($"Estás en la rama \"{rama}\". El lanzador baja el contenido de main.", $"You are on branch \"{rama}\". The launcher downloads the content from main."), T("Subir", "Upload")); return; }
        var (_, estado) = Git(["status", "--porcelain", "--", .. _repo.Contenido]);
        if (estado == "") { MessageBox.Show(T("No hay cambios de contenido para subir.", "There are no content changes to upload."), T("Subir", "Upload")); return; }
        if (MessageBox.Show(T("¿Subir estos cambios a GitHub (main)?\n\n", "Upload these changes to GitHub (main)?\n\n") + estado, T("Subir", "Upload"), MessageBoxButtons.YesNo) != DialogResult.Yes) return;

        Cursor = Cursors.WaitCursor;
        var log = new List<string>();
        try
        {
            var mensaje = _gMensaje.Text.Trim() == "" ? T("Contenido de la liga", "League content") : _gMensaje.Text.Trim();
            var pasos = new List<string[]>();
            pasos.Add(new[] { "add", "-A", "--" }.Concat(_repo.Contenido).ToArray());
            pasos.Add(new[] { "commit", "-m", mensaje, "--" }.Concat(_repo.Contenido).ToArray());
            pasos.Add(new[] { "pull", "--rebase", "--autostash" });
            pasos.Add(new[] { "push", "origin", "main" });
            foreach (var paso in pasos)
            {
                var (codigo, salida) = Git(paso);
                log.Add($"> git {string.Join(' ', paso.Take(3))}\r\n{salida}");
                if (codigo != 0) throw new Exception(T($"git {paso[0]} falló", $"git {paso[0]} failed"));
            }

            MessageBox.Show(T("Listo: subido a GitHub. Los jugadores lo reciben la próxima vez que abran el juego.", "Done: uploaded to GitHub. Players get it the next time they open the game."), T("Subir", "Upload"));
        }
        catch (Exception e)
        {
            MessageBox.Show(T("No se pudo subir: ", "Could not upload: ") + e.Message + T("\n\nMirá el detalle en la pestaña.", "\n\nSee the details in the tab."), T("Subir", "Upload"));

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

    // Columnas de solo lectura: el nombre interno queda en español; el encabezado, en el idioma de la liga.
    private static readonly Dictionary<string, string> EncabezadosEn = new()
    {
        ["Archivo"] = "File", ["Entrenador"] = "Trainer", ["Estado"] = "Status", ["Exportado"] = "Exported", ["Nombre"] = "Name",
        ["Origen"] = "Origin", ["Rango"] = "Rank", ["Raza"] = "Breed"
    };

    private static DataGridViewTextBoxColumn Solo(string nombre, int ancho) =>
        new() { Name = nombre, HeaderText = T(nombre, EncabezadosEn.GetValueOrDefault(nombre, nombre)), Width = ancho, ReadOnly = true };

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

/// <summary>Un rival en la lista del torneo: se muestra "Angie (Campeón E) (812)" y se agrega por su referencia.</summary>
public sealed record ItemRival(string Referencia, string Texto)
{
    public override string ToString() => Texto;
}
