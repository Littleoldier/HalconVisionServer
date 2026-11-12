using System.Windows.Forms;
using System.Drawing;

namespace HalconVisionServer.Forms
{
    partial class HalconVisionServerForm
    {
        private System.ComponentModel.IContainer components = null;

        // 顶部状态栏
        private Panel topPanel;
        private Label lblTitle;
        private Label lblState;
        private Label lblModbusStatus;
        private Label lblCameraStatus;

        // 主布局
        private TableLayoutPanel mainLayout;
        private HalconDotNet.HWindowControl hWindowControl1;

        // 右侧控制面板
        private Panel rightPanel;
        private TableLayoutPanel rightLayout;

        // 相机控制
        private GroupBox grpCamera;
        private ComboBox cmbCamera;
        private Button btnConnectCamera;
        private Button btnDisconnectCamera;
        private Button btnRefreshCamera;

        // 曝光/对焦控制
        private GroupBox grpExposure;
        private NumericUpDown numExposure;
        private Button btnApplyExposure;
        private Button btnAutoExposure;
        private Label lblCameraInfo;
        private Button btnRefreshCameraInfo;

        // 配方管理
        private GroupBox grpRecipe;
        private ComboBox cmbRecipe;
        private Button btnApplyRecipe;

        // 手动触发
        private GroupBox grpControl;
        private Button btnManualTrigger;

        // 重复性测试
        private GroupBox grpTest;
        private Button btnRepeatabilityTest;

        // 运行状态
        private GroupBox grpStatus;
        private Label lblStatusValue;
        private Label lblLastResultValue;
        private Label lblProcessCountValue;
        private Label lblFailCountValue;
        private Label lblModbusDetail;

        // Modbus 控制
        private GroupBox grpModbus;
        private Button btnStartModbus;
        private Button btnStopModbus;
        private Button btnModbusConfig;

        // 算法参数配置
        private GroupBox grpAlgorithm;
        private NumericUpDown numThresholdMin;
        private NumericUpDown numThresholdMax;
        private NumericUpDown numCalibration;
        private NumericUpDown numStandardDiameter;
        private NumericUpDown numTolerance;
        private Button btnSaveParams;

        // 底部日志
        private TabControl tabLog;
        private TabPage tabSystemLog;
        private TextBox txtLog;
        private TabPage tabModbusLog;
        private DataGridView dgvModbusLog;

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            this.Text = "GTS 视觉服务器 (HALCON版) v1.0";
            this.Size = new Size(1600, 950);
            this.MinimumSize = new Size(1400, 900);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(240, 240, 245);
            this.Font = new Font("Segoe UI", 9F);

            // ================================================
            //  1. 顶部状态栏
            // ================================================
            topPanel = new Panel();
            topPanel.Dock = DockStyle.Top;
            topPanel.Height = 36;
            topPanel.BackColor = Color.FromArgb(30, 30, 46);
            topPanel.Padding = new Padding(12, 0, 12, 0);

            lblTitle = new Label();
            lblTitle.Text = "GTS 视觉服务器 (HALCON)";
            lblTitle.ForeColor = Color.White;
            lblTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            lblTitle.Location = new Point(12, 8);
            lblTitle.AutoSize = true;

            lblState = new Label();
            lblState.Text = "🟢 空闲";
            lblState.ForeColor = Color.White;
            lblState.BackColor = Color.LightGreen;
            lblState.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            lblState.Location = new Point(280, 6);
            lblState.Size = new Size(100, 24);
            lblState.TextAlign = ContentAlignment.MiddleCenter;

            lblModbusStatus = new Label();
            lblModbusStatus.Text = "○ Modbus 已停止";
            lblModbusStatus.ForeColor = Color.FromArgb(180, 180, 200);
            lblModbusStatus.Font = new Font("Segoe UI", 9F);
            lblModbusStatus.Location = new Point(400, 10);
            lblModbusStatus.AutoSize = false;
            lblModbusStatus.Width = 150;
            lblModbusStatus.TextAlign = ContentAlignment.MiddleLeft;

            lblCameraStatus = new Label();
            lblCameraStatus.Text = "○ 相机未连接";
            lblCameraStatus.ForeColor = Color.FromArgb(180, 180, 200);
            lblCameraStatus.Font = new Font("Segoe UI", 9F);
            lblCameraStatus.Location = new Point(560, 10);
            lblCameraStatus.AutoSize = false;
            lblCameraStatus.Width = 130;
            lblCameraStatus.TextAlign = ContentAlignment.MiddleLeft;

            topPanel.Controls.Add(lblTitle);
            topPanel.Controls.Add(lblState);
            topPanel.Controls.Add(lblModbusStatus);
            topPanel.Controls.Add(lblCameraStatus);

            // ================================================
            //  2. 主布局
            // ================================================
            mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.ColumnCount = 2;
            mainLayout.RowCount = 2;
            mainLayout.Padding = new Padding(6);
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 175F));   // 底部日志

            hWindowControl1 = new HalconDotNet.HWindowControl();
            hWindowControl1.Dock = DockStyle.Fill;
            hWindowControl1.BackColor = Color.FromArgb(40, 40, 40);
            hWindowControl1.BorderStyle = BorderStyle.FixedSingle;
            mainLayout.Controls.Add(hWindowControl1, 0, 0);

            BuildRightPanel();
            BuildLogPanel();

            this.Controls.Add(mainLayout);
            this.Controls.Add(topPanel);

            // ================================================
            //  绑定事件
            // ================================================
            btnConnectCamera.Click += BtnConnectCamera_Click;
            btnDisconnectCamera.Click += BtnDisconnectCamera_Click;
            btnRefreshCamera.Click += BtnRefreshCamera_Click;

            btnApplyExposure.Click += BtnApplyExposure_Click;
            btnAutoExposure.Click += BtnAutoExposure_Click;
            btnRefreshCameraInfo.Click += BtnRefreshCameraInfo_Click;

            btnApplyRecipe.Click += BtnApplyRecipe_Click;

            btnManualTrigger.Click += BtnManualTrigger_Click;
            btnRepeatabilityTest.Click += BtnRepeatabilityTest_Click;
            btnStartModbus.Click += BtnStartModbus_Click;
            btnStopModbus.Click += BtnStopModbus_Click;
            btnModbusConfig.Click += BtnModbusConfig_Click;

            this.ResumeLayout(false);
        }

        // ============================================================
        //  右侧面板（无滚动条 + 固定行高 + 末尾 spacer）
        // ============================================================
        private void BuildRightPanel()
        {
            rightPanel = new Panel();
            rightPanel.Dock = DockStyle.Fill;
            rightPanel.Padding = new Padding(4);
            rightPanel.BackColor = Color.White;

            rightLayout = new TableLayoutPanel();
            rightLayout.Dock = DockStyle.Fill;
            rightLayout.ColumnCount = 1;
            rightLayout.RowCount = 9;           // 8 个分组 + 1 个 spacer
            rightLayout.AutoScroll = false;     // ⭐ 不要滚动条
            rightLayout.Padding = new Padding(2);
            rightLayout.BackColor = Color.White;

            // ⭐ 固定行高，末尾 spacer 吸收剩余空间
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));   // 0. 相机
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 95));   // 1. 曝光
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));   // 2. 配方
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));   // 3. 手动触发
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));   // 4. 重复性
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));   // 5. 运行状态
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));   // 6. Modbus
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 215));  // 7. 算法参数
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 8. spacer

            // ---- 0. 相机控制 ----
            grpCamera = new GroupBox { Text = "📸 相机控制", Dock = DockStyle.Fill, Padding = new Padding(6, 2, 6, 2), Font = new Font("Segoe UI", 9F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) };
            var cameraLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
            cameraLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            cameraLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            cameraLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            cameraLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18F));
            cmbCamera = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(2, 3, 2, 2) };
            btnConnectCamera = new Button { Text = "连接", Dock = DockStyle.Fill, BackColor = Color.FromArgb(92, 184, 92), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(2, 2, 2, 2) };
            btnDisconnectCamera = new Button { Text = "断开", Dock = DockStyle.Fill, Enabled = false, BackColor = Color.FromArgb(217, 83, 79), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(2, 2, 2, 2) };
            btnRefreshCamera = new Button { Text = "🔄", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, Margin = new Padding(2, 2, 2, 2) };
            cameraLayout.Controls.Add(cmbCamera, 0, 0);
            cameraLayout.Controls.Add(btnConnectCamera, 1, 0);
            cameraLayout.Controls.Add(btnDisconnectCamera, 2, 0);
            cameraLayout.Controls.Add(btnRefreshCamera, 3, 0);
            grpCamera.Controls.Add(cameraLayout);
            rightLayout.Controls.Add(grpCamera, 0, 0);

            // ---- 1. 曝光/对焦控制 ----
            grpExposure = new GroupBox { Text = "💡 曝光 / 对焦控制", Dock = DockStyle.Fill, Padding = new Padding(6, 2, 6, 2), Font = new Font("Segoe UI", 9F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) };
            var expLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2 };
            expLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58F));
            expLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            expLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            expLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            expLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            expLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));

            expLayout.Controls.Add(new Label { Text = "曝光值:", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, 0);
            numExposure = new NumericUpDown
            {
                Minimum = -20,
                Maximum = 100000,
                DecimalPlaces = 1,
                Increment = 1,
                Value = 0,
                Dock = DockStyle.Fill,
                Margin = new Padding(2, 3, 2, 3)
            };
            expLayout.Controls.Add(numExposure, 1, 0);

            btnApplyExposure = new Button
            {
                Text = "应用",
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(51, 122, 183),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(2, 2, 2, 2)
            };
            expLayout.Controls.Add(btnApplyExposure, 2, 0);

            btnAutoExposure = new Button
            {
                Text = "自动",
                Dock = DockStyle.Fill,
                BackColor = Color.LightGreen,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(2, 2, 2, 2)
            };
            expLayout.Controls.Add(btnAutoExposure, 3, 0);

            lblCameraInfo = new Label
            {
                Text = "相机: --",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                Margin = new Padding(2, 0, 2, 0)
            };
            expLayout.Controls.Add(lblCameraInfo, 0, 1);
            expLayout.SetColumnSpan(lblCameraInfo, 3);

            btnRefreshCameraInfo = new Button
            {
                Text = "刷新",
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8F),
                Margin = new Padding(2, 1, 2, 1)
            };
            expLayout.Controls.Add(btnRefreshCameraInfo, 3, 1);

            grpExposure.Controls.Add(expLayout);
            rightLayout.Controls.Add(grpExposure, 0, 1);

            // ---- 2. 配方管理 ----
            grpRecipe = new GroupBox { Text = "📋 配方管理", Dock = DockStyle.Fill, Padding = new Padding(6, 2, 6, 2), Font = new Font("Segoe UI", 9F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) };
            var recipeLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            recipeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            recipeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
            cmbRecipe = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(2, 3, 2, 2) };
            btnApplyRecipe = new Button { Text = "应用配方", Dock = DockStyle.Fill, BackColor = Color.FromArgb(92, 184, 92), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(2, 2, 2, 2) };
            recipeLayout.Controls.Add(cmbRecipe, 0, 0);
            recipeLayout.Controls.Add(btnApplyRecipe, 1, 0);
            grpRecipe.Controls.Add(recipeLayout);
            rightLayout.Controls.Add(grpRecipe, 0, 2);

            // ---- 3. 手动触发 ----
            grpControl = new GroupBox { Text = "🎯 手动触发", Dock = DockStyle.Fill, Padding = new Padding(6, 2, 6, 2), Font = new Font("Segoe UI", 9F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) };
            btnManualTrigger = new Button
            {
                Text = "📸 触发拍照",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                BackColor = Color.FromArgb(51, 122, 183),
                ForeColor = Color.White,
                Enabled = false,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(2)
            };
            grpControl.Controls.Add(btnManualTrigger);
            rightLayout.Controls.Add(grpControl, 0, 3);

            // ---- 4. 重复性测试 ----
            grpTest = new GroupBox { Text = "🔬 重复性测试", Dock = DockStyle.Fill, Padding = new Padding(6, 2, 6, 2), Font = new Font("Segoe UI", 9F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) };
            btnRepeatabilityTest = new Button
            {
                Text = "▶ 连续测量 50 次",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                BackColor = Color.FromArgb(155, 89, 182),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(2)
            };
            grpTest.Controls.Add(btnRepeatabilityTest);
            rightLayout.Controls.Add(grpTest, 0, 4);

            // ---- 5. 运行状态 ----
            grpStatus = new GroupBox { Text = "📊 运行状态", Dock = DockStyle.Fill, Padding = new Padding(4), Font = new Font("Segoe UI", 9F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) };
            var statusLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2 };
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));

            AddStatusItem(statusLayout, "状态:", ref lblStatusValue, "空闲", Color.Green, 0, 0);
            AddStatusItem(statusLayout, "结果:", ref lblLastResultValue, "--", Color.Gray, 1, 0);
            AddStatusItem(statusLayout, "处理:", ref lblProcessCountValue, "0", Color.FromArgb(0, 120, 215), 2, 0);
            AddStatusItem(statusLayout, "失败:", ref lblFailCountValue, "0", Color.Red, 3, 0);

            lblModbusDetail = new Label
            {
                Text = "🌐 Modbus: 未启动  |  端口: 503",
                ForeColor = Color.FromArgb(60, 60, 60),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(248, 248, 248),
                Padding = new Padding(6, 0, 0, 0),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular)
            };
            statusLayout.Controls.Add(lblModbusDetail, 0, 1);
            statusLayout.SetColumnSpan(lblModbusDetail, 4);
            grpStatus.Controls.Add(statusLayout);
            rightLayout.Controls.Add(grpStatus, 0, 5);

            // ---- 6. Modbus 控制 ----
            grpModbus = new GroupBox { Text = "🌐 Modbus 控制", Dock = DockStyle.Fill, Padding = new Padding(6, 2, 6, 2), Font = new Font("Segoe UI", 9F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) };
            var modbusLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            modbusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            modbusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            modbusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            btnStartModbus = new Button { Text = "▶ 启动", Dock = DockStyle.Fill, BackColor = Color.FromArgb(92, 184, 92), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(2) };
            btnStopModbus = new Button { Text = "⏹ 停止", Dock = DockStyle.Fill, Enabled = false, BackColor = Color.FromArgb(217, 83, 79), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(2) };
            btnModbusConfig = new Button { Text = "⚙", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, Margin = new Padding(2) };
            modbusLayout.Controls.Add(btnStartModbus, 0, 0);
            modbusLayout.Controls.Add(btnStopModbus, 1, 0);
            modbusLayout.Controls.Add(btnModbusConfig, 2, 0);
            grpModbus.Controls.Add(modbusLayout);
            rightLayout.Controls.Add(grpModbus, 0, 6);

            // ---- 7. 算法参数配置 ----
            grpAlgorithm = new GroupBox { Text = "⚙ 算法参数配置", Dock = DockStyle.Fill, Padding = new Padding(6, 2, 6, 2), Font = new Font("Segoe UI", 9F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) };
            var algLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6 };
            algLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            algLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            algLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            algLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            algLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            algLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            algLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            algLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));

            numThresholdMin = new NumericUpDown { Minimum = 0, Maximum = 255, Value = 0, Dock = DockStyle.Fill, Margin = new Padding(2, 3, 2, 3) };
            numThresholdMax = new NumericUpDown { Minimum = 0, Maximum = 255, Value = 80, Dock = DockStyle.Fill, Margin = new Padding(2, 3, 2, 3) };
            numCalibration = new NumericUpDown { Minimum = 0.00001m, Maximum = 10, Value = 0.1113m, DecimalPlaces = 5, Increment = 0.00001m, Dock = DockStyle.Fill, Margin = new Padding(2, 3, 2, 3) };
            numStandardDiameter = new NumericUpDown { Minimum = 0, Maximum = 1000, Value = 20.0m, DecimalPlaces = 2, Increment = 0.1m, Dock = DockStyle.Fill, Margin = new Padding(2, 3, 2, 3) };
            numTolerance = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 0.5m, DecimalPlaces = 2, Increment = 0.1m, Dock = DockStyle.Fill, Margin = new Padding(2, 3, 2, 3) };

            algLayout.Controls.Add(new Label { Text = "阈值下限:", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, 0);
            algLayout.Controls.Add(numThresholdMin, 1, 0);
            algLayout.Controls.Add(new Label { Text = "阈值上限:", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, 1);
            algLayout.Controls.Add(numThresholdMax, 1, 1);
            algLayout.Controls.Add(new Label { Text = "标定系数:", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, 2);
            algLayout.Controls.Add(numCalibration, 1, 2);
            algLayout.Controls.Add(new Label { Text = "标准直径(mm):", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, 3);
            algLayout.Controls.Add(numStandardDiameter, 1, 3);
            algLayout.Controls.Add(new Label { Text = "公差(mm):", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, 4);
            algLayout.Controls.Add(numTolerance, 1, 4);

            btnSaveParams = new Button
            {
                Text = "保存参数",
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(92, 184, 92),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(2, 5, 2, 2)
            };
            btnSaveParams.Click += (s, e) => SaveConfigFromUI();
            algLayout.Controls.Add(btnSaveParams, 0, 5);
            algLayout.SetColumnSpan(btnSaveParams, 2);

            grpAlgorithm.Controls.Add(algLayout);
            rightLayout.Controls.Add(grpAlgorithm, 0, 7);

            // ---- 8. spacer（吸收剩余空间） ----
            var spacer = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            rightLayout.Controls.Add(spacer, 0, 8);

            rightPanel.Controls.Add(rightLayout);
            mainLayout.Controls.Add(rightPanel, 1, 0);
        }

        // ============================================================
        //  状态项辅助
        // ============================================================
        private void AddStatusItem(TableLayoutPanel parent, string labelText, ref Label valueLabel, string defaultValue, Color valueColor, int col, int row)
        {
            var item = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            item.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            item.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));

            var lbl = new Label { Text = labelText, TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill, ForeColor = Color.DimGray, AutoSize = false, Font = new Font("Segoe UI", 8.5F) };
            valueLabel = new Label { Text = defaultValue, Font = new Font("Segoe UI", 9F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill, ForeColor = valueColor, AutoSize = false };

            item.Controls.Add(lbl, 0, 0);
            item.Controls.Add(valueLabel, 1, 0);

            parent.Controls.Add(item, col, row);
        }

        // ============================================================
        //  日志面板
        // ============================================================
        private void BuildLogPanel()
        {
            tabLog = new TabControl { Dock = DockStyle.Fill };

            tabSystemLog = new TabPage { Text = "📋 系统日志" };
            var logLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            logLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            logLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));

            txtLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                BackColor = Color.Black,
                ForeColor = Color.Lime,
                Font = new Font("Consolas", 10F),
                WordWrap = false
            };

            var btnClearLog = new Button { Text = "清空", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat };
            btnClearLog.Click += (s, e) => txtLog.Clear();
            logLayout.Controls.Add(txtLog, 0, 0);
            logLayout.Controls.Add(btnClearLog, 1, 0);
            tabSystemLog.Controls.Add(logLayout);

            tabModbusLog = new TabPage { Text = "📡 Modbus 监控" };
            var modbusLogLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            modbusLogLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            modbusLogLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));

            dgvModbusLog = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White
            };
            dgvModbusLog.Columns.Add("Time", "时间");
            dgvModbusLog.Columns.Add("Type", "类型");
            dgvModbusLog.Columns.Add("FC", "功能码");
            dgvModbusLog.Columns.Add("Address", "地址");
            dgvModbusLog.Columns.Add("Value", "值");

            var btnClearModbusLog = new Button { Text = "清空", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat };
            btnClearModbusLog.Click += (s, e) => dgvModbusLog.Rows.Clear();
            modbusLogLayout.Controls.Add(dgvModbusLog, 0, 0);
            modbusLogLayout.Controls.Add(btnClearModbusLog, 1, 0);
            tabModbusLog.Controls.Add(modbusLogLayout);

            tabLog.Controls.Add(tabSystemLog);
            tabLog.Controls.Add(tabModbusLog);

            mainLayout.Controls.Add(tabLog, 0, 1);
            mainLayout.SetColumnSpan(tabLog, 2);
        }
    }
}