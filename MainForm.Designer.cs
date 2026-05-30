namespace WhisperLauncher;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        grpEnv        = new GroupBox();
        pnlMedia      = new GroupBox();
        pnlOpts       = new Panel();
        pnlLogArea    = new Panel();
        pnlBottom     = new Panel();

        lblWhisperPath   = new Label();
        txtWhisperPath   = new TextBox();
        btnBrowseWhisper = new Button();
        lblWhisperStatus = new Label();

        lblModelPath   = new Label();
        txtModelPath   = new TextBox();
        btnBrowseModel = new Button();
        lblModelStatus = new Label();

        lblFFmpegPath    = new Label();
        txtFFmpegPath    = new TextBox();
        btnBrowseFFmpeg  = new Button();
        lblFFmpegStatus  = new Label();

        lblPlatform = new Label();
        cboPlatform = new ComboBox();

        lblSource       = new Label();
        txtSourcePath   = new TextBox();
        btnBrowseSource = new Button();
        rdoSingleFile   = new RadioButton();
        rdoBatchFolder  = new RadioButton();
        lstFiles        = new ListBox();

        lblLanguage = new Label();
        cboLanguage = new ComboBox();
        lblOutput   = new Label();
        rdoSRT      = new RadioButton();
        rdoTXT      = new RadioButton();
        rdoVTT      = new RadioButton();
        chkDetail   = new CheckBox();

        rtbLog = new RichTextBox();

        btnRun        = new Button();
        btnStop       = new Button();
        btnEditConfig = new Button();          // item 7
        picFFmpeg     = new PictureBox();
        picWhisper    = new PictureBox();
        picGgml       = new PictureBox();

        const int LBL_COL = 132;   // label column width
        const int BTN_COL = 90;    // Browse button column width

        // =====================================================================
        //  grpEnv — Environment  (GroupBox, Dock=Top)
        //  Rows: Whisper-CLI | status | Model | model-status | FFmpeg | status | Platform
        // =====================================================================
        var tlpEnv = new TableLayoutPanel();
        tlpEnv.Dock        = DockStyle.Top;
        tlpEnv.Height      = 156;
        tlpEnv.ColumnCount = 3;
        tlpEnv.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LBL_COL));
        tlpEnv.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tlpEnv.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, BTN_COL));
        tlpEnv.RowCount = 6;
        tlpEnv.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); // Whisper-CLI
        tlpEnv.RowStyles.Add(new RowStyle(SizeType.Absolute, 18)); // Whisper status
        tlpEnv.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); // Model
        tlpEnv.RowStyles.Add(new RowStyle(SizeType.Absolute, 18)); // Model status
        tlpEnv.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); // FFmpeg
        tlpEnv.RowStyles.Add(new RowStyle(SizeType.Absolute, 18)); // FFmpeg status
        tlpEnv.Padding = new Padding(6, 2, 6, 4);

        TlpLbl(tlpEnv, lblWhisperPath, "Whisper-CLI Path:", 0, 0);
        TlpTxt(tlpEnv, txtWhisperPath, 1, 0);
        TlpBtn(tlpEnv, btnBrowseWhisper, 2, 0);

        StyleStatus(lblWhisperStatus, "Not verified");
        tlpEnv.SetColumnSpan(lblWhisperStatus, 3);
        tlpEnv.Controls.Add(lblWhisperStatus, 0, 1);

        TlpLbl(tlpEnv, lblModelPath, "GGML Model:", 0, 2);
        TlpTxt(tlpEnv, txtModelPath, 1, 2);
        TlpBtn(tlpEnv, btnBrowseModel, 2, 2);

        StyleStatus(lblModelStatus, "Not set");
        tlpEnv.SetColumnSpan(lblModelStatus, 3);
        tlpEnv.Controls.Add(lblModelStatus, 0, 3);

        TlpLbl(tlpEnv, lblFFmpegPath, "FFmpeg Path:", 0, 4);
        TlpTxt(tlpEnv, txtFFmpegPath, 1, 4);
        TlpBtn(tlpEnv, btnBrowseFFmpeg, 2, 4);

        StyleStatus(lblFFmpegStatus, "Searching...");
        tlpEnv.SetColumnSpan(lblFFmpegStatus, 3);
        tlpEnv.Controls.Add(lblFFmpegStatus, 0, 5);

        // Platform — dedicated panel below tlpEnv (avoids TLP 6th-row rendering issue)
        var pnlPlatform = new Panel();
        pnlPlatform.Dock      = DockStyle.Top;
        pnlPlatform.Height    = 32;
        pnlPlatform.BackColor = SystemColors.Control;

        lblPlatform.Text      = "Platform:";
        lblPlatform.Location  = new Point(14, 6);
        lblPlatform.Size      = new Size(LBL_COL - 6, 20);
        lblPlatform.TextAlign = ContentAlignment.MiddleLeft;

        cboPlatform.DropDownStyle = ComboBoxStyle.DropDownList;
        cboPlatform.Location      = new Point(LBL_COL + 6, 4);
        cboPlatform.Size          = new Size(160, 24);
        cboPlatform.Anchor        = AnchorStyles.Left | AnchorStyles.Top;

        pnlPlatform.Controls.Add(lblPlatform);
        pnlPlatform.Controls.Add(cboPlatform);

        // Add pnlPlatform first (lower z-order → processed last → appears below tlpEnv)
        // Add tlpEnv second (higher z-order → processed first → appears at top)
        grpEnv.Text   = "Environment";
        grpEnv.Dock   = DockStyle.Top;
        grpEnv.Height = 218;
        grpEnv.Controls.Add(pnlPlatform);
        grpEnv.Controls.Add(tlpEnv);

        // =====================================================================
        //  pnlMedia — Media File  (GroupBox, matches Environment style)
        //  row 0 = Source File(s)  row 1 = radios  row 2 = batch list
        // =====================================================================
        tlpMedia = new TableLayoutPanel();
        tlpMedia.Dock        = DockStyle.Fill;
        tlpMedia.ColumnCount = 3;
        tlpMedia.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LBL_COL));
        tlpMedia.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tlpMedia.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, BTN_COL));
        tlpMedia.RowCount = 3;
        tlpMedia.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); // source path
        tlpMedia.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); // radios
        tlpMedia.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));  // batch list (hidden)
        tlpMedia.Padding = new Padding(6, 2, 6, 4);

        // Row 0: Source File(s)
        TlpLbl(tlpMedia, lblSource, "Source File(s):", 0, 0);
        TlpTxt(tlpMedia, txtSourcePath, 1, 0);
        txtSourcePath.AllowDrop = true;
        TlpBtn(tlpMedia, btnBrowseSource, 2, 0);

        // Row 1: radio buttons
        var pnlRadios = new Panel { Dock = DockStyle.Fill };
        rdoSingleFile.Text     = "Single File";
        rdoSingleFile.Location = new Point(0, 4);
        rdoSingleFile.Size     = new Size(110, 20);
        rdoSingleFile.Checked  = true;
        rdoBatchFolder.Text     = "Batch (all files in folder)";
        rdoBatchFolder.Location = new Point(118, 4);
        rdoBatchFolder.Size    = new Size(220, 20);
        pnlRadios.Controls.AddRange(new Control[] { rdoSingleFile, rdoBatchFolder });
        tlpMedia.Controls.Add(new Label(), 0, 1);
        tlpMedia.SetColumnSpan(pnlRadios, 2);
        tlpMedia.Controls.Add(pnlRadios, 1, 1);

        // Row 2: batch file list
        lstFiles.Dock          = DockStyle.Fill;
        lstFiles.Margin        = new Padding(0, 2, 0, 2);
        lstFiles.SelectionMode = SelectionMode.None;
        lstFiles.Font          = new Font("Segoe UI", 8.5f);
        tlpMedia.Controls.Add(new Label(), 0, 2);
        tlpMedia.SetColumnSpan(lstFiles, 2);
        tlpMedia.Controls.Add(lstFiles, 1, 2);

        // GroupBox title provides "Media File" header; rows 32+32+0=64 + pad 6 + overhead ~20 = 90
        pnlMedia.Text   = "Media File";
        pnlMedia.Dock   = DockStyle.Top;
        pnlMedia.Height = 90;
        pnlMedia.Controls.Add(tlpMedia);

        // =====================================================================
        //  pnlOpts — Language · Output 
        // =====================================================================
        TableLayoutPanel tlpOpts = new TableLayoutPanel();
        tlpOpts.Dock    = DockStyle.Fill;
        tlpOpts.Padding = new Padding(14, 0, 14, 0);

        // 定义 7 列：Label(74) + Combo(150) + 间距(30) + Label(56) + Radio(60) + Radio(60) + Radio(60)
        tlpOpts.ColumnCount = 7;
        tlpOpts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74F));
        tlpOpts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
        tlpOpts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30F)); 
        tlpOpts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56F));
        tlpOpts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60F));
        tlpOpts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60F));
        tlpOpts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60F));

        tlpOpts.RowCount = 1;
        tlpOpts.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        // 1. Language Label
        lblLanguage.Text = "Language:";
        lblLanguage.Anchor = AnchorStyles.Left;
        lblLanguage.AutoSize = true;
        tlpOpts.Controls.Add(lblLanguage, 0, 0);

        // 2. Language ComboBox
        cboLanguage.DropDownStyle = ComboBoxStyle.DropDownList;
        cboLanguage.Anchor = AnchorStyles.Left;
        cboLanguage.Width = 150;
        cboLanguage.Items.AddRange(new object[] {
            "auto","en","zh","ja","ko","fr","de","es","it","pt",
            "ru","ar","hi","nl","pl","tr","uk","vi","sv","cs",
            "fi","ro","hu","th","id","da","no","he","bg","el"
        });
        cboLanguage.SelectedIndex = 1; // "en"
        tlpOpts.Controls.Add(cboLanguage, 1, 0);

        // 3. 空白占位格 (第 2 列不用管，自动留出 30px 间距)

        // 4. Output Label
        lblOutput.Text = "Output:";
        lblOutput.Anchor = AnchorStyles.Left;
        lblOutput.AutoSize = true;
        tlpOpts.Controls.Add(lblOutput, 3, 0);

        // 5. Radio Buttons
        rdoSRT.Text = ".srt";
        rdoSRT.Anchor = AnchorStyles.Left;
        rdoSRT.AutoSize = true;
        rdoSRT.Checked = true;
        tlpOpts.Controls.Add(rdoSRT, 4, 0);

        rdoTXT.Text = ".txt";
        rdoTXT.Anchor = AnchorStyles.Left;
        rdoTXT.AutoSize = true;
        tlpOpts.Controls.Add(rdoTXT, 5, 0);

        rdoVTT.Text = ".vtt";
        rdoVTT.Anchor = AnchorStyles.Left;
        rdoVTT.AutoSize = true;
        tlpOpts.Controls.Add(rdoVTT, 6, 0);

        pnlOpts.Dock      = DockStyle.Top;
        pnlOpts.Height    = 44;
        pnlOpts.BackColor = SystemColors.ControlLight;
        pnlOpts.Controls.Add(tlpOpts);

        // =====================================================================
        //  pnlLogArea — "Log Output" header + Detail mode + RichTextBox
        // =====================================================================
        pnlLogArea.Dock = DockStyle.Fill;
        pnlLogArea.Padding = new Padding(4, 0, 4, 4);
        var tlpLogHead = new TableLayoutPanel();
        tlpLogHead.Dock        = DockStyle.Top;
        tlpLogHead.Height      = 26;
        tlpLogHead.ColumnCount = 2;
        tlpLogHead.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tlpLogHead.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tlpLogHead.RowCount  = 1;
        tlpLogHead.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tlpLogHead.Margin    = new Padding(0);
        tlpLogHead.BackColor = SystemColors.Control;

        var lblLogTitle = new Label();
        lblLogTitle.Text      = "Log Output";
        lblLogTitle.Dock      = DockStyle.Fill;
        lblLogTitle.Font      = new Font("Segoe UI", 9f, FontStyle.Bold);
        lblLogTitle.TextAlign = ContentAlignment.MiddleLeft;
        lblLogTitle.Margin    = new Padding(2, 0, 0, 0);

        chkDetail.Text   = "Detail mode";
        chkDetail.Dock   = DockStyle.Fill;
        chkDetail.Font   = new Font("Segoe UI", 8.5f);
        chkDetail.Margin = new Padding(0, 4, 8, 4);

        tlpLogHead.Controls.Add(lblLogTitle, 0, 0);
        tlpLogHead.Controls.Add(chkDetail, 1, 0);

        rtbLog.Dock       = DockStyle.Fill;
        rtbLog.ReadOnly   = true;
        rtbLog.Font       = new Font("Consolas", 9f);
        rtbLog.BackColor  = Color.FromArgb(20, 20, 20);
        rtbLog.ForeColor  = Color.LightGray;
        rtbLog.ScrollBars = RichTextBoxScrollBars.Vertical;
        rtbLog.WordWrap   = false;

        pnlLogArea.Controls.Add(rtbLog);       // index 0 — Fill
        pnlLogArea.Controls.Add(tlpLogHead);   // index 1 — Top (topmost)

        // =====================================================================
        //  pnlBottom — [Run] [Stop] [Edit Config]  ···  [logos]   (item 7)
        // =====================================================================
        pnlBottom.Dock      = DockStyle.Bottom;
        pnlBottom.Height    = 72;
        pnlBottom.BackColor = SystemColors.Control;

        StyleRunBtn(btnRun,  "▶  Run",       12,  Color.FromArgb(0, 120, 215));
        StyleRunBtn(btnStop, "■  Stop",      130, Color.FromArgb(196, 43, 28));
        btnStop.Enabled = false;

        // Edit Config button (item 7)
        btnEditConfig.Text      = "⚙  Settings";
        btnEditConfig.Location  = new Point(248, 17);
        btnEditConfig.Size      = new Size(110, 38);
        btnEditConfig.Anchor    = AnchorStyles.Left | AnchorStyles.Top;
        btnEditConfig.Font      = new Font("Segoe UI", 10f, FontStyle.Bold);
        btnEditConfig.FlatStyle = FlatStyle.Flat;
        btnEditConfig.BackColor = Color.FromArgb(80, 96, 112);
        btnEditConfig.ForeColor = Color.White;
        btnEditConfig.FlatAppearance.BorderSize = 0;

        // =====================================================================
        //  pnlLogos — Logos right-docked (彻底干掉循环和局部变量，拯救设计器)
        // =====================================================================
        pnlLogos = new FlowLayoutPanel();
        pnlLogos.Dock = DockStyle.Right;
        pnlLogos.Width = 296; // 88 * 3 + 8 * 2 + 14 = 296 像素静态硬编码
        pnlLogos.BackColor = SystemColors.Control;
        pnlLogos.Padding = new Padding(0, 14, 10, 14);
        pnlLogos.FlowDirection = FlowDirection.LeftToRight;
        pnlLogos.WrapContents = false;

        // 1. picFFmpeg 属性显式拆解赋值
        picFFmpeg.Size = new Size(88, 44);
        picFFmpeg.SizeMode = PictureBoxSizeMode.Zoom;
        picFFmpeg.Cursor = Cursors.Hand;
        picFFmpeg.Margin = new Padding(0, 0, 8, 0);
        pnlLogos.Controls.Add(picFFmpeg);

        // 2. picWhisper 属性显式拆解赋值
        picWhisper.Size = new Size(88, 44);
        picWhisper.SizeMode = PictureBoxSizeMode.Zoom;
        picWhisper.Cursor = Cursors.Hand;
        picWhisper.Margin = new Padding(0, 0, 8, 0);
        pnlLogos.Controls.Add(picWhisper);

        // 3. picGgml 属性显式拆解赋值
        picGgml.Size = new Size(88, 44);
        picGgml.SizeMode = PictureBoxSizeMode.Zoom;
        picGgml.Cursor = Cursors.Hand;
        picGgml.Margin = new Padding(0, 0, 8, 0);
        pnlLogos.Controls.Add(picGgml);

        // 将布局面板与按钮按正确顺序加入底层 pnlBottom
        pnlBottom.Controls.Add(pnlLogos);       // Dock=Right 优先挂载
        pnlBottom.Controls.Add(btnEditConfig);
        pnlBottom.Controls.Add(btnStop);
        pnlBottom.Controls.Add(btnRun);
        // =====================================================================
        //  Form assembly (Bottom → Fill → Top in reverse visual order)
        // =====================================================================
        this.SuspendLayout();
        this.Controls.Add(pnlLogArea);   // Fill
        this.Controls.Add(pnlOpts);      // Top row 3
        this.Controls.Add(pnlMedia);     // Top row 2
        this.Controls.Add(grpEnv);       // Top row 1 (topmost)
        this.Controls.Add(pnlBottom);    // Bottom

        this.AutoScaleDimensions = new SizeF(96F, 96F);
        this.AutoScaleMode       = AutoScaleMode.Dpi;
        this.Text            = "Whisper.cpp Launcher  V1.0";
        this.Size            = new Size(940, 840);
        this.MinimumSize     = new Size(780, 660);
        this.StartPosition   = FormStartPosition.CenterScreen;
        this.Font            = new Font("Segoe UI", 9f);
        this.AllowDrop       = true;

        this.ResumeLayout(false);
        this.PerformLayout();
    }

    // ── Layout helpers ────────────────────────────────────────────────────────
    private static void TlpLbl(TableLayoutPanel t, Label l, string text, int col, int row)
    {
        l.Text = text; l.Dock = DockStyle.Fill;
        l.TextAlign = ContentAlignment.MiddleLeft;
        l.Margin = new Padding(8, 0, 0, 0);
        t.Controls.Add(l, col, row);
    }
    private static void TlpTxt(TableLayoutPanel t, TextBox txt, int col, int row)
    {
        txt.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        txt.Margin = new Padding(0, 5, 4, 5);
        t.Controls.Add(txt, col, row);
    }
    private static void TlpBtn(TableLayoutPanel t, Button b, int col, int row)
    {
        b.Text = "Browse..."; b.Dock = DockStyle.Fill;
        b.Margin = new Padding(0, 3, 6, 3);
        t.Controls.Add(b, col, row);
    }
    private static void StyleStatus(Label l, string text)
    {
        l.Text = text; l.Dock = DockStyle.Fill;
        l.Font = new Font("Segoe UI", 8f);
        l.ForeColor = Color.Gray;
        l.Margin = new Padding(10, 0, 0, 2);
    }
    private static void AddLabel(Control parent, Label l, string text,
                                 int x, int y, int w, int h)
    {
        l.Text = text; l.Location = new Point(x, y);
        l.Size = new Size(w, h); l.TextAlign = ContentAlignment.MiddleLeft;
        parent.Controls.Add(l);
    }
    private static void Rdo(Control parent, RadioButton r, string text, ref int x, int y)
    {
        r.Text = text; r.Location = new Point(x, y + 1);
        r.Size = new Size(52, 20); parent.Controls.Add(r); x += 54;
    }
    private static void StyleRunBtn(Button b, string text, int x, Color bg)
    {
        b.Text = text; b.Location = new Point(x, 17); b.Size = new Size(110, 38);
        b.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        b.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        b.BackColor = bg; b.ForeColor = Color.White;
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
    }

    // ── Field declarations ────────────────────────────────────────────────────
    private GroupBox  grpEnv      = null!;
    private GroupBox  pnlMedia    = null!;
    private Panel     pnlOpts     = null!;
    private Panel     pnlLogArea  = null!;
    private Panel     pnlBottom   = null!;
    private TableLayoutPanel tlpMedia  = null!;
    private FlowLayoutPanel  pnlLogos  = null!;

    private Label   lblWhisperPath   = null!;
    private TextBox txtWhisperPath   = null!;
    private Button  btnBrowseWhisper = null!;
    private Label   lblWhisperStatus = null!;

    private Label   lblModelPath   = null!;
    private TextBox txtModelPath   = null!;
    private Button  btnBrowseModel = null!;
    private Label   lblModelStatus = null!;

    private Label   lblFFmpegPath    = null!;
    private TextBox txtFFmpegPath    = null!;
    private Button  btnBrowseFFmpeg  = null!;
    private Label   lblFFmpegStatus  = null!;

    private Label    lblPlatform = null!;
    private ComboBox cboPlatform = null!;

    private Label       lblSource       = null!;
    private TextBox     txtSourcePath   = null!;
    private Button      btnBrowseSource = null!;
    private RadioButton rdoSingleFile   = null!;
    private RadioButton rdoBatchFolder  = null!;
    private ListBox     lstFiles        = null!;

    private Label       lblLanguage = null!;
    private ComboBox    cboLanguage = null!;
    private Label       lblOutput   = null!;
    private RadioButton rdoSRT      = null!;
    private RadioButton rdoTXT      = null!;
    private RadioButton rdoVTT      = null!;
    private CheckBox    chkDetail   = null!;

    private RichTextBox rtbLog = null!;

    private Button     btnRun        = null!;
    private Button     btnStop       = null!;
    private Button     btnEditConfig = null!;
    private PictureBox picFFmpeg     = null!;
    private PictureBox picWhisper    = null!;
    private PictureBox picGgml       = null!;
}
