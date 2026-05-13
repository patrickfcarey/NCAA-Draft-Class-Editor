using System.ComponentModel;
using System.Windows.Forms;
using NcaaDraftEditor.Core;

namespace NcaaDraftEditor.WinForms;

public sealed class MainForm : Form
{
    private DraftClassFile? _dc;
    private string? _currentPath;
    private readonly BindingList<PlayerView> _binding = new();

    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false };
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly MenuStrip _menu = new();

    public MainForm()
    {
        Text = "NCAA 06 Draft Class Editor";
        Width = 1400;
        Height = 850;

        // Menu
        var menuFile = new ToolStripMenuItem("&File");
        var miOpen   = new ToolStripMenuItem("&Open...", null, (_,__) => OpenFile());
        var miSave   = new ToolStripMenuItem("&Save", null, (_,__) => SaveFile(false));
        var miSaveAs = new ToolStripMenuItem("Save &As...", null, (_,__) => SaveFile(true));
        var miExport = new ToolStripMenuItem("&Export CSV...", null, (_,__) => ExportCsv());
        var miExit   = new ToolStripMenuItem("E&xit", null, (_,__) => Close());
        menuFile.DropDownItems.AddRange(new ToolStripItem[] { miOpen, miSave, miSaveAs, new ToolStripSeparator(), miExport, new ToolStripSeparator(), miExit });
        _menu.Items.Add(menuFile);
        MainMenuStrip = _menu;


        // Names
        AddTextCol("FirstName", "First", 110);
        AddTextCol("LastName", "Last", 140);

        // Combos from catalogs
        AddCombo("TGID", "College", CollegeCatalog.AsList(), 180);
        AddCombo("PPOS", "Position", PositionCatalog.AsList(), 100);


        // Identifiers
        AddNumCol("TGIDBucket", 90, header:"TGID Bucket");
        AddNumCol("RandomSeed", 90);

        // Core
        AddNumCol("POVR", 60);
        AddNumCol("PYER", 60);
        AddNumCol("PRSD", 60);
        AddNumCol("PJEN", 60);

        // Ratings (raw + 99-view)
        
        foreach (var f in new[] { "PWGT","PHGT","PSTR","PAGI","PSPD","PACC","PAWR","PCTH","PCAR","PTHP","PTHA","PKPR","PKAC","PBTK","PTAK","PIMP","PPBK","PRBK","PPOE","PTEN","PJMP","PINJ","PSTA" })
            AddNumCol(f, 60);

        // Appearance combos from embedded catalog
        AddCombo("PSKI", "Skin", AppearanceCatalog.AsList(AppearanceCatalog.PSKI));
        AddCombo("PFMP", "Face", AppearanceCatalog.AsList(AppearanceCatalog.PFMP), 150);
        AddCombo("PHCL", "Hair Color", AppearanceCatalog.AsList(AppearanceCatalog.PHCL), 130);
        AddCombo("PHED", "Hair Style", AppearanceCatalog.AsList(AppearanceCatalog.PHED), 150);

        AddCombo("HELM", "Helmet", AppearanceCatalog.AsList(AppearanceCatalog.HELM), 150);
        AddCombo("PFMK", "Facemask", AppearanceCatalog.AsList(AppearanceCatalog.PFMK), 180);
        AddCombo("PVIS", "Visor", AppearanceCatalog.AsList(AppearanceCatalog.PVIS), 120);
        AddCombo("PEYE", "Eye Black", AppearanceCatalog.AsList(AppearanceCatalog.PEYE), 120);
        AddCombo("PBRE", "Nasal Strip", AppearanceCatalog.AsList(AppearanceCatalog.PBRE), 120);
        AddCombo("PNEK", "Neck Pad", AppearanceCatalog.AsList(AppearanceCatalog.PNEK), 120);

        AddCombo("PREB", "R Elbow", AppearanceCatalog.AsList(AppearanceCatalog.PREB), 120);
        AddCombo("PLEB", "L Elbow", AppearanceCatalog.AsList(AppearanceCatalog.PLEB), 120);
        AddCombo("PRWS", "R Wrist", AppearanceCatalog.AsList(AppearanceCatalog.PRWS), 120);
        AddCombo("PLWS", "L Wrist", AppearanceCatalog.AsList(AppearanceCatalog.PLWS), 120);
        AddCombo("PRHN", "R Hand", AppearanceCatalog.AsList(AppearanceCatalog.PRHN), 120);
        AddCombo("PLHN", "L Hand", AppearanceCatalog.AsList(AppearanceCatalog.PLHN), 120);

        AddCombo("PSLT", "Sleeves On/Off", AppearanceCatalog.AsList(AppearanceCatalog.PSLT), 140);
        AddCombo("PSLO", "Sleeve Color", AppearanceCatalog.AsList(AppearanceCatalog.PSLO), 140);

        // Body sliders
        AddIntCol("PMSH", 60, -1, 255);
        AddIntCol("PFSH", 60, -1, 255);
        //AddNumCol("Unused84", 70, header:"byte_84");
        AddIntCol("PSSH", 60, -1, 255);

        _grid.DataSource = _binding;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;

        _status.Items.Add(_statusLabel);
        Controls.Add(_grid);
        Controls.Add(_menu);
        Controls.Add(_status);
        _status.Dock = DockStyle.Bottom;
        _menu.Dock = DockStyle.Top;

        UpdateStatus("Ready");
    }

    private DataGridViewComboBoxColumn AddCombo(string prop, string header, List<CodeName> src, int width=120)
    {
        var col = new DataGridViewComboBoxColumn
        {
            DataPropertyName = prop,
            HeaderText = header,
            DisplayMember = "Name",
            ValueMember = "Code",
            DataSource = src,
            Width = width,
            FlatStyle = FlatStyle.Popup
        };
        _grid.Columns.Add(col);
        return col;
    }

    private void AddTextCol(string prop, string? header = null, int width = 100)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = prop,
            HeaderText = header ?? prop,
            Width = width
        });
    }

    private void AddNumCol(string prop, int width = 60, string? header = null)
        => _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = prop, HeaderText = header ?? prop, Width = width });

    private void AddIntCol(string prop, int width = 60, int min = 0, int max = 255, string? header = null)
    {
        var col = new DataGridViewTextBoxColumn { DataPropertyName = prop, HeaderText = header ?? prop, Width = width };
        _grid.Columns.Add(col);
        _grid.EditingControlShowing += (s, e) =>
        {
            if (_grid.CurrentCell?.OwningColumn == col && e.Control is TextBox tb)
            {
                tb.KeyPress -= OnlyIntKeyPress;
                tb.KeyPress += OnlyIntKeyPress;
            }
        };
        void OnlyIntKeyPress(object? _, KeyPressEventArgs ke)
        {
            if (!char.IsControl(ke.KeyChar) && !char.IsDigit(ke.KeyChar) && !(ke.KeyChar == '-' && !_grid.EditingControl.Text.Contains('-')))
                ke.Handled = true;
        }
    }

    private void OpenFile()
    {
        using var dlg = new OpenFileDialog { Filter = "Draft class files|*.*" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _dc = DraftClassFile.Load(dlg.FileName);
        _currentPath = dlg.FileName;

        _binding.Clear();
        foreach (var p in _dc.Players)
            _binding.Add(new PlayerView(p));

        UpdateStatus($"Loaded {_dc.Players.Count} players from {Path.GetFileName(dlg.FileName)}");

    }

    private void SaveFile(bool saveAs)
    {
        if (_dc is null)
        {
            MessageBox.Show(this, "Nothing to save. Open a draft class first.", "Save", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var path = _currentPath;
        if (saveAs || string.IsNullOrWhiteSpace(path))
        {
            using var dlg = new SaveFileDialog { Filter = "Draft class files|*.*", FileName = Path.GetFileName(path ?? "draft.edited") };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            path = dlg.FileName;
            _currentPath = path;
        }

        _dc.Save(path!);
        UpdateStatus($"Saved → {Path.GetFileName(path)}");
    }

    private void ExportCsv()
    {
        if (_dc is null)
        {
            MessageBox.Show(this, "Open a draft class first.", "Export CSV", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "draft_export.csv" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _dc.ExportCsv(dlg.FileName);
        UpdateStatus($"Exported CSV → {Path.GetFileName(dlg.FileName)}");
    }

    private void UpdateStatus(string text) => _statusLabel.Text = text;

    private sealed class PlayerView
    {
        private readonly PlayerRecord _p;
        public PlayerView(PlayerRecord p) { _p = p; }

        public string FirstName { get => _p.FirstName; set => _p.FirstName = value; }
        public string LastName { get => _p.LastName; set => _p.LastName = value; }

        public byte TGID { get => _p.TGID; set => _p.TGID = value; }
        public byte TGIDBucket { get => _p.TGIDBucket; set => _p.TGIDBucket = value; }
        public byte RandomSeed { get => _p.RandomSeed; set => _p.RandomSeed = value; }
        public byte PPOS { get => _p.PPOS; set => _p.PPOS = value; }
        public byte POVR { get => _p.POVR; set => _p.POVR = value; }
        public byte PYER { get => _p.PYER; set => _p.PYER = value; }
        public byte PRSD { get => _p.PRSD; set => _p.PRSD = value; }
        public byte PJEN { get => _p.PJEN; set => _p.PJEN = value; }

        public byte PWGT { get => _p.PWGT; set => _p.PWGT = value; }
        public byte PHGT { get => _p.PHGT; set => _p.PHGT = value; }
        public byte PSTR { get => _p.PSTR; set => _p.PSTR = value; }
        public byte PAGI { get => _p.PAGI; set => _p.PAGI = value; }
        public byte PSPD { get => _p.PSPD; set => _p.PSPD = value; }
        public byte PACC { get => _p.PACC; set => _p.PACC = value; }
        public byte PAWR { get => _p.PAWR; set => _p.PAWR = value; }
        public byte PCTH { get => _p.PCTH; set => _p.PCTH = value; }
        public byte PCAR { get => _p.PCAR; set => _p.PCAR = value; }
        public byte PTHP { get => _p.PTHP; set => _p.PTHP = value; }
        public byte PTHA { get => _p.PTHA; set => _p.PTHA = value; }
        public byte PKPR { get => _p.PKPR; set => _p.PKPR = value; }
        public byte PKAC { get => _p.PKAC; set => _p.PKAC = value; }
        public byte PBTK { get => _p.PBTK; set => _p.PBTK = value; }
        public byte PTAK { get => _p.PTAK; set => _p.PTAK = value; }
        public byte PIMP { get => _p.PIMP; set => _p.PIMP = value; }
        public byte PPBK { get => _p.PPBK; set => _p.PPBK = value; }
        public byte PRBK { get => _p.PRBK; set => _p.PRBK = value; }
        public byte PPOE { get => _p.PPOE; set => _p.PPOE = value; }
        public byte PTEN { get => _p.PTEN; set => _p.PTEN = value; }
        public byte PJMP { get => _p.PJMP; set => _p.PJMP = value; }
        public byte PINJ { get => _p.PINJ; set => _p.PINJ = value; }
        public byte PSTA { get => _p.PSTA; set => _p.PSTA = value; }

        public byte PHED { get => _p.PHED; set => _p.PHED = value; }
        public byte PHCL { get => _p.PHCL; set => _p.PHCL = value; }
        public byte PSKI { get => _p.PSKI; set => _p.PSKI = value; }
        public byte PFMP { get => _p.PFMP; set => _p.PFMP = value; }
        public byte HELM { get => _p.HELM; set => _p.HELM = value; }
        public byte PFMK { get => _p.PFMK; set => _p.PFMK = value; }
        public byte PVIS { get => _p.PVIS; set => _p.PVIS = value; }
        public byte PEYE { get => _p.PEYE; set => _p.PEYE = value; }
        public byte PBRE { get => _p.PBRE; set => _p.PBRE = value; }
        public byte PNEK { get => _p.PNEK; set => _p.PNEK = value; }
        public byte PREB { get => _p.PREB; set => _p.PREB = value; }
        public byte PLEB { get => _p.PLEB; set => _p.PLEB = value; }
        public byte PSLT { get => _p.PSLT; set => _p.PSLT = value; }
        public byte PSLO { get => _p.PSLO; set => _p.PSLO = value; }
        public byte PRWS { get => _p.PRWS; set => _p.PRWS = value; }
        public byte PLWS { get => _p.PLWS; set => _p.PLWS = value; }
        public byte PRHN { get => _p.PRHN; set => _p.PRHN = value; }
        public byte PLHN { get => _p.PLHN; set => _p.PLHN = value; }
        public byte PRSH { get => _p.PRSH; set => _p.PRSH = value; }
        public byte PLSH { get => _p.PLSH; set => _p.PLSH = value; }

        public byte PMSH { get => _p.PMSH; set => _p.PMSH = value; }
        public byte PFSH { get => _p.PFSH; set => _p.PFSH = value; }
        //public byte Unused84 { get => _p.Unused84; set => _p.Unused84 = value; }
        public byte PSSH { get => _p.PSSH; set => _p.PSSH = value; }

    }

    //Fix Body Size Tables before Saving
    private void FixBodySizes()
    {
        if (_dc is null) return;
        foreach (var p in _dc.Players)
        {
            if (p.PMSH == 255 || p.PMSH < 0) p.PMSH = 0;
            if (p.PFSH == 255 || p.PFSH < 0) p.PFSH = 0;
            if (p.PSSH == 255 || p.PSSH < 0) p.PSSH = 0;
        }
        
    }   

    //Set College to 1;
    private void SetCollege()
    {
        foreach (var p in _dc.Players)
        {
            p.TGID = 1;

        }
    }


}
