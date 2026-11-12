using HalconVisionServer.Services;
using HalconDotNet;
using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HalconVisionServer.Forms
{
    public partial class HalconVisionServerForm : Form
    {
        private VisionController _controller;
        private NotifyIcon _notifyIcon;
        private DateTime _lastImageUpdateTime = DateTime.MinValue;
        private readonly int _minUpdateIntervalMs = 66;
        private bool _isResizing = false;
        private HObject? _pendingImage = null;
        private readonly object _pendingImageLock = new object();
        private System.Windows.Forms.Timer _resizeEndTimer;

        private static string RecipeDir =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Recipes");

        public HalconVisionServerForm()
        {
            InitializeComponent();
            this.DoubleBuffered = true;
            this.Load += HalconVisionServerForm_Load;
            this.ResizeBegin += (s, e) => _isResizing = true;
            this.ResizeEnd += (s, e) => { _resizeEndTimer.Stop(); _resizeEndTimer.Start(); };

            _resizeEndTimer = new System.Windows.Forms.Timer { Interval = 300 };
            _resizeEndTimer.Tick += ResizeEndTimer_Tick;
        }

        private void ResizeEndTimer_Tick(object sender, EventArgs e)
        {
            _resizeEndTimer.Stop();
            _isResizing = false;
            hWindowControl1.HalconWindow.ClearWindow();
            lock (_pendingImageLock)
            {
                if (_pendingImage != null)
                {
                    var img = _pendingImage;
                    _pendingImage = null;
                    DisplayImageInternal(img);
                }
            }
        }

        private void HalconVisionServerForm_Load(object sender, EventArgs e)
        {
            try
            {
                AppendLog("🚀 HALCON 视觉服务器启动中...");
                var config = VisionConfig.Load() ?? new VisionConfig();

                _controller = new VisionController(config);
                _controller.LogMessage += AppendLog;
                _controller.ImageCaptured += OnImageCaptured;
                _controller.StateChanged += OnStateChanged;
                _controller.ModbusLog += OnModbusLog;

                RefreshCameraList();
                RefreshRecipeList();
                InitTrayIcon();
                UpdateUI();
                LoadConfigToUI(config);
                UpdateCameraInfo();

                // ⭐ 给 HalconWindow 设一个深色背景
                try
                {
                    hWindowControl1.HalconWindow.SetWindowParam("background_color", "white");
                }
                catch { }

                AppendLog("✅ 系统启动完成");
            }
            catch (Exception ex) { MessageBox.Show($"启动失败: {ex.Message}"); }
        }

        // ============================================================
        // 配方管理
        // ============================================================
        private void RefreshRecipeList()
        {
            try
            {
                if (cmbRecipe == null) return;

                if (!Directory.Exists(RecipeDir))
                    Directory.CreateDirectory(RecipeDir);

                var files = Directory.GetFiles(RecipeDir, "*.json");
                cmbRecipe.Items.Clear();

                foreach (var f in files)
                    cmbRecipe.Items.Add(Path.GetFileNameWithoutExtension(f));

                if (cmbRecipe.Items.Count > 0)
                {
                    cmbRecipe.SelectedIndex = 0;
                    AppendLog($"📋 已加载 {cmbRecipe.Items.Count} 个配方");
                }
                else
                {
                    AppendLog("⚠️ 未找到任何配方文件（Recipes 目录为空）");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"⚠️ 刷新配方列表失败: {ex.Message}");
            }
        }

        private void BtnApplyRecipe_Click(object sender, EventArgs e)
        {
            try
            {
                string recipeName = cmbRecipe?.SelectedItem?.ToString() ?? "";
                if (string.IsNullOrEmpty(recipeName))
                {
                    MessageBox.Show("请先选择一个配方！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string recipeFile = Path.Combine(RecipeDir, recipeName + ".json");
                if (!File.Exists(recipeFile))
                {
                    MessageBox.Show($"配方文件不存在:\n{recipeFile}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string json = File.ReadAllText(recipeFile);
                var recipe = System.Text.Json.JsonSerializer.Deserialize<VisionConfig>(json);
                if (recipe == null)
                {
                    MessageBox.Show("配方解析失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var current = VisionConfig.Load() ?? new VisionConfig();
                current.StandardDiameterMm = recipe.StandardDiameterMm;
                current.ToleranceMm = recipe.ToleranceMm;
                current.CalibrationFactor = recipe.CalibrationFactor;
                current.ThresholdMin = recipe.ThresholdMin;
                current.ThresholdMax = recipe.ThresholdMax;
                current.HomMat2D = recipe.HomMat2D;
                current.Save();

                _controller?.ReloadConfig();
                LoadConfigToUI(current);

                AppendLog($"✅ 配方 [{recipeName}] 已应用");
                AppendLog($"   标准直径={current.StandardDiameterMm}mm, 公差={current.ToleranceMm}mm, 标定系数={current.CalibrationFactor}");

                MessageBox.Show($"配方 [{recipeName}] 应用成功！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                AppendLog($"❌ 应用配方失败: {ex.Message}");
                MessageBox.Show($"应用配方失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ============================================================
        // 曝光控制
        // ============================================================
        private void BtnApplyExposure_Click(object sender, EventArgs e)
        {
            try
            {
                if (numExposure == null) return;
                _controller?.SetExposure((double)numExposure.Value);
                AppendLog($"📷 曝光已设置为: {numExposure.Value}");
                UpdateCameraInfo();
            }
            catch (Exception ex) { AppendLog($"❌ 设置曝光失败: {ex.Message}"); }
        }

        private void BtnAutoExposure_Click(object sender, EventArgs e)
        {
            try
            {
                _controller?.EnableAutoExposure();
                AppendLog("📷 已切换到自动曝光");
                UpdateCameraInfo();
            }
            catch (Exception ex) { AppendLog($"❌ 切换自动曝光失败: {ex.Message}"); }
        }

        private void BtnRefreshCameraInfo_Click(object sender, EventArgs e) => UpdateCameraInfo();

        private void UpdateCameraInfo()
        {
            try
            {
                if (lblCameraInfo == null) return;
                var diag = _controller?.GetCameraDiagnostics();
                if (diag.HasValue && diag.Value.Width > 0)
                {
                    var d = diag.Value;
                    lblCameraInfo.Text = $"相机: {d.Width}x{d.Height} @ {d.Fps:F1}fps | 曝光: {d.Exposure:F1}";
                }
                else
                {
                    lblCameraInfo.Text = "相机: 未连接";
                }
            }
            catch { }
        }

        // ============================================================
        // 通用
        // ============================================================
        private void SafeInvoke(Action action)
        {
            if (this.IsHandleCreated && !this.IsDisposed) this.Invoke(action);
            else action();
        }

        private void UpdateUI()
        {
            SafeInvoke(() =>
            {
                if (_controller == null) return;
                bool camConnected = _controller.IsCameraConnected;
                bool modbusRunning = _controller.IsModbusRunning;

                btnConnectCamera.Enabled = !camConnected;
                btnDisconnectCamera.Enabled = camConnected;
                btnManualTrigger.Enabled = camConnected;
                btnStartModbus.Enabled = !modbusRunning;
                btnStopModbus.Enabled = modbusRunning;

                lblProcessCountValue.Text = _controller.ProcessCount.ToString();
                lblFailCountValue.Text = _controller.FailCount.ToString();
                lblCameraStatus.Text = camConnected ? "● 相机已连接" : "○ 相机未连接";
                lblCameraStatus.ForeColor = camConnected ? Color.Green : Color.Gray;

                lblModbusStatus.Text = modbusRunning ? "● Modbus 运行中" : "○ Modbus 已停止";
                lblModbusStatus.ForeColor = modbusRunning ? Color.Green : Color.Gray;

                if (lblModbusDetail != null)
                    lblModbusDetail.Text = $"🌐 Modbus: {(modbusRunning ? "运行中" : "未启动")}  |  端口: {_controller.Port}";
            });
        }

        private void LoadConfigToUI(VisionConfig config)
        {
            if (numThresholdMin != null) numThresholdMin.Value = config.ThresholdMin;
            if (numThresholdMax != null) numThresholdMax.Value = config.ThresholdMax;
            if (numCalibration != null) numCalibration.Value = (decimal)config.CalibrationFactor;
            if (numStandardDiameter != null) numStandardDiameter.Value = (decimal)config.StandardDiameterMm;
            if (numTolerance != null) numTolerance.Value = (decimal)config.ToleranceMm;
        }

        private void SaveConfigFromUI()
        {
            var config = VisionConfig.Load();
            if (config == null) return;
            if (numThresholdMin != null) config.ThresholdMin = (int)numThresholdMin.Value;
            if (numThresholdMax != null) config.ThresholdMax = (int)numThresholdMax.Value;
            if (numCalibration != null) config.CalibrationFactor = (double)numCalibration.Value;
            if (numStandardDiameter != null) config.StandardDiameterMm = (double)numStandardDiameter.Value;
            if (numTolerance != null) config.ToleranceMm = (double)numTolerance.Value;
            config.Save();
            _controller?.ReloadConfig();
            AppendLog("💾 算法参数已保存并生效");
        }

        private void AppendLog(string msg)
        {
            if (txtLog == null || txtLog.IsDisposed) return;
            SafeInvoke(() =>
            {
                txtLog.AppendText(msg + Environment.NewLine);
                if (txtLog.Lines.Length > 500) txtLog.Lines = txtLog.Lines[100..];
                txtLog.ScrollToCaret();
            });
        }

        private void OnModbusLog(ModbusLogEntry entry)
        {
            SafeInvoke(() =>
            {
                if (dgvModbusLog == null) return;
                int idx = dgvModbusLog.Rows.Add();
                dgvModbusLog.Rows[idx].Cells[0].Value = entry.Time.ToString("HH:mm:ss.fff");
                dgvModbusLog.Rows[idx].Cells[1].Value = entry.Type;
                dgvModbusLog.Rows[idx].Cells[4].Value = entry.Value;
                if (dgvModbusLog.Rows.Count > 200) dgvModbusLog.Rows.RemoveAt(0);
                if (entry.Type == "连接") UpdateUI();
            });
        }

        private void OnImageCaptured(HObject image)
        {
            if (image == null || !image.IsInitialized())
            {
                image?.Dispose();
                return;
            }

            if (_isResizing)
            {
                lock (_pendingImageLock) { _pendingImage?.Dispose(); _pendingImage = image; }
                return;
            }

            var now = DateTime.Now;
            if ((now - _lastImageUpdateTime).TotalMilliseconds < _minUpdateIntervalMs)
            {
                image.Dispose();
                return;
            }
            _lastImageUpdateTime = now;
            DisplayImageInternal(image);
        }

        // ============================================================
        // ⭐ 关键修复：保持宽高比显示（避免图像被拉伸成长条）
        // ============================================================
        private void DisplayImageInternal(HObject image)
        {
            if (image == null) return;
            SafeInvoke(() =>
            {
                try
                {
                    if (hWindowControl1 == null || hWindowControl1.IsDisposed) return;

                    hWindowControl1.HalconWindow.ClearWindow();

                    HOperatorSet.GetImageSize(image, out HTuple width, out HTuple height);
                    if (width.I <= 0 || height.I <= 0) return;

                    double imgW = width.D;
                    double imgH = height.D;
                    double winW = hWindowControl1.ClientSize.Width;
                    double winH = hWindowControl1.ClientSize.Height;

                    if (winW <= 0 || winH <= 0) return;

                    // 计算保持宽高比的缩放系数
                    double scaleW = winW / imgW;
                    double scaleH = winH / imgH;
                    double scale = Math.Min(scaleW, scaleH);

                    // 计算显示区域（图像坐标系下的宽高）
                    double dispW = winW / scale;
                    double dispH = winH / scale;

                    // 居中：图像内容对称填充
                    double col1 = (imgW - dispW) / 2.0;
                    double row1 = (imgH - dispH) / 2.0;
                    double col2 = col1 + dispW;
                    double row2 = row1 + dispH;

                    HOperatorSet.SetPart(hWindowControl1.HalconWindow, row1, col1, row2, col2);
                    HOperatorSet.DispObj(image, hWindowControl1.HalconWindow);
                }
                catch (Exception ex) { AppendLog($"❌ 显示异常: {ex.Message}"); }
                finally { try { image.Dispose(); } catch { } }
            });
        }

        private void OnStateChanged(VisionController.State state)
        {
            SafeInvoke(() =>
            {
                if (lblState == null) return;
                switch (state)
                {
                    case VisionController.State.Idle: lblState.Text = "🟢 空闲"; lblState.BackColor = Color.LightGreen; break;
                    case VisionController.State.Busy: lblState.Text = "🟡 处理中..."; lblState.BackColor = Color.Gold; break;
                    case VisionController.State.Error: lblState.Text = "🔴 错误"; lblState.BackColor = Color.LightCoral; break;
                }
            });
        }

        // ==================== 按钮事件 ====================
        private void BtnConnectCamera_Click(object sender, EventArgs e)
        {
            if (_controller == null || cmbCamera.SelectedItem == null) return;
            int idx = 0;
            if (cmbCamera.SelectedItem.ToString().StartsWith("相机"))
                int.TryParse(cmbCamera.SelectedItem.ToString().Replace("相机", ""), out idx);
            if (_controller.ConnectCamera(idx)) AppendLog($"✅ 相机连接成功");
            else AppendLog($"❌ 相机连接失败");
            UpdateUI();
            UpdateCameraInfo();
        }

        private void BtnDisconnectCamera_Click(object sender, EventArgs e)
        {
            _controller?.DisconnectCamera();
            AppendLog("🔌 相机已断开");
            UpdateUI();
            UpdateCameraInfo();
        }

        private void BtnRefreshCamera_Click(object sender, EventArgs e) { RefreshCameraList(); }

        private void RefreshCameraList()
        {
            if (_controller == null) return;
            SafeInvoke(() =>
            {
                cmbCamera.Items.Clear();
                cmbCamera.Items.AddRange(_controller.GetCameraList());
                if (cmbCamera.Items.Count > 0) cmbCamera.SelectedIndex = 0;
            });
        }

        private async void BtnManualTrigger_Click(object sender, EventArgs e)
        {
            if (_controller == null || !_controller.IsCameraConnected) return;
            btnManualTrigger.Enabled = false;
            AppendLog("📸 手动触发拍照...");
            await _controller.ManualTriggerAsync();
            btnManualTrigger.Enabled = true;
            UpdateUI();
        }

        private async void BtnRepeatabilityTest_Click(object sender, EventArgs e)
        {
            if (_controller == null || !_controller.IsCameraConnected)
            {
                MessageBox.Show("请先连接相机！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (MessageBox.Show("将连续测量 50 次，期间请保持工件与相机完全静止。是否继续？",
                "重复性测试", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            btnRepeatabilityTest.Enabled = false;
            btnManualTrigger.Enabled = false;

            try
            {
                string report = await _controller.RepeatabilityTestAsync(50);
                AppendLog(report);
                MessageBox.Show(report, "重复性测试报告", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                AppendLog($"❌ 重复性测试失败: {ex.Message}");
            }
            finally
            {
                btnRepeatabilityTest.Enabled = true;
                btnManualTrigger.Enabled = _controller.IsCameraConnected;
                UpdateUI();
            }
        }

        private void BtnStartModbus_Click(object sender, EventArgs e) { _controller?.StartModbus(); UpdateUI(); }
        private void BtnStopModbus_Click(object sender, EventArgs e) { _controller?.StopModbus(); UpdateUI(); }

        private void BtnModbusConfig_Click(object sender, EventArgs e)
        {
            var config = VisionConfig.Load();
            if (config == null) return;

            int oldPort = config.ModbusPort;
            byte oldSlave = config.SlaveAddress;

            using (var dialog = new Form())
            {
                dialog.Text = "⚙ Modbus 配置";
                dialog.Size = new Size(340, 190);
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MaximizeBox = false; dialog.MinimizeBox = false;

                TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 3 };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));

                layout.Controls.Add(new Label { Text = "端口:", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, 0);
                NumericUpDown numPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = config.ModbusPort, Dock = DockStyle.Fill, Margin = new Padding(3, 8, 3, 3) };
                layout.Controls.Add(numPort, 1, 0);

                layout.Controls.Add(new Label { Text = "从站 ID:", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, 1);
                NumericUpDown numSlave = new NumericUpDown { Minimum = 1, Maximum = 247, Value = config.SlaveAddress, Dock = DockStyle.Fill, Margin = new Padding(3, 8, 3, 3) };
                layout.Controls.Add(numSlave, 1, 1);

                Button btnOK = new Button { Text = "保存", Size = new Size(80, 30), DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(92, 184, 92), ForeColor = Color.White };
                Button btnCancel = new Button { Text = "取消", Size = new Size(80, 30), DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(217, 83, 79), ForeColor = Color.White };

                FlowLayoutPanel btnPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
                btnPanel.Controls.Add(btnOK); btnPanel.Controls.Add(btnCancel);
                layout.Controls.Add(btnPanel, 0, 2); layout.SetColumnSpan(btnPanel, 2);

                dialog.Controls.Add(layout); dialog.AcceptButton = btnOK; dialog.CancelButton = btnCancel;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    int newPort = (int)numPort.Value;
                    byte newSlave = (byte)numSlave.Value;
                    if (newPort == oldPort && newSlave == oldSlave) return;

                    config.ModbusPort = newPort; config.SlaveAddress = newSlave;
                    config.Save();
                    _controller?.ReloadConfig();
                    if (_controller != null && _controller.IsModbusRunning) _controller.RestartModbus();
                    AppendLog($"✅ Modbus 配置已保存: 端口={newPort}, 从站={newSlave}");
                    UpdateUI();
                }
            }
        }

        private void InitTrayIcon()
        {
            _notifyIcon = new NotifyIcon { Icon = SystemIcons.Application, Text = "GTS 视觉服务器", Visible = true };
            _notifyIcon.DoubleClick += (s, e) => { this.Show(); this.WindowState = FormWindowState.Normal; };
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("显示主窗口", null, (s, e) => { this.Show(); this.WindowState = FormWindowState.Normal; });
            contextMenu.Items.Add("退出", null, (s, e) => { _notifyIcon.Visible = false; Application.Exit(); });
            _notifyIcon.ContextMenuStrip = contextMenu;
            this.FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; this.Hide(); _notifyIcon.ShowBalloonTip(1000, "GTS 视觉服务器", "程序已在后台运行", ToolTipIcon.Info); } };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
                _notifyIcon?.Dispose();
                _controller?.Dispose();
                _resizeEndTimer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}