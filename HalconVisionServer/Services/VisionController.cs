using HalconVisionServer.Utils;
using HalconDotNet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HalconVisionServer.Services
{
    public class VisionController : IDisposable
    {
        public enum State { Idle, Busy, Error }

        // ============================================================
        // 私有字段
        // ============================================================
        private readonly VisionConfig _config;
        private readonly ModbusSlaveService _modbus;
        private ICameraService? _camera;
        private State _currentState = State.Idle;
        private int _processCount = 0;
        private int _failCount = 0;
        private string _lastImagePath = "";
        private readonly object _lock = new();

        // 实时预览节流
        private DateTime _lastPreviewTime = DateTime.MinValue;
        private const int PREVIEW_THROTTLE_MS = 66;

        // ============================================================
        // 事件
        // ============================================================
        public event Action<State>? StateChanged;
        public event Action<HObject>? ImageCaptured;
        public event Action<string>? LogMessage;
        public event Action<ModbusLogEntry>? ModbusLog;

        // ============================================================
        // 公共属性
        // ============================================================
        public State CurrentState => _currentState;
        public int ProcessCount => _processCount;
        public int FailCount => _failCount;
        public string LastImagePath => _lastImagePath;
        public bool IsCameraConnected => _camera?.IsConnected ?? false;
        public bool IsModbusRunning => _modbus.IsRunning;
        public int Port => _modbus?.Port ?? 0;
        public byte SlaveAddress => _config.SlaveAddress;

        // ============================================================
        // 算法参数
        // ============================================================
        private const int MEASUREMENT_FRAMES = 7;        // 每次测量采集 7 帧中位数
        private const int MAX_CAPTURE_RETRIES = 3;       // 单帧拍照最多重试 3 次
        private const int MAX_DETECT_RETRIES = 3;        // 未检测到目标时最多重拍 3 次
        private const int RETRY_DELAY_MS = 50;           // 重试间隔

        // ---- 边缘筛选参数 ----
        private const double MIN_CIRCULARITY = 0.65;     // 圆度下限
        private const int MIN_EDGE_POINTS = 50;          // 边缘点数下限
        private const double RADIUS_MIN_PX = 83;         // 半径下限
        private const double RADIUS_MAX_PX = 98;         // 半径上限
        private const int MIN_CONTOUR_LENGTH = 100;      // 轮廓最小长度

        // ---- 拟合残差检查参数 ----
        private const double MAX_MEAN_RESIDUAL_PX = 1.2; // 平均残差上限（像素）
        private const double MAX_STD_RESIDUAL_PX = 0.8;  // 残差标准差上限（像素）

        // ============================================================
        // 构造
        // ============================================================
        public VisionController(VisionConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));

            var modbusConfig = new ModbusConfig
            {
                Protocol = ModbusProtocol.Tcp,
                Port = _config.ModbusPort,
                SlaveAddress = _config.SlaveAddress,
                TimeoutMs = 3000
            };

            _modbus = new ModbusSlaveService(modbusConfig, _config);
            _modbus.CoilWriteReceived += OnModbusCoilWrite;
            _modbus.LogMessage += (entry) => ModbusLog?.Invoke(entry);
            _modbus.ConnectionStateChanged += OnModbusConnectionChanged;

            _modbus.Start();

            Log($"✅ 视觉控制器初始化完成，Modbus端口: {_config.ModbusPort}");
        }

        // ============================================================
        // Modbus 事件
        // ============================================================
        private void OnModbusCoilWrite(int address, bool value)
        {
            if (address == _config.TriggerCoilAddress && value && _currentState == State.Idle)
            {
                Log($"📨 收到 Modbus 触发指令");
                _ = ExecuteCaptureAsync("Modbus触发");
            }
        }

        private void OnModbusConnectionChanged(object? sender, bool connected)
        {
            ModbusLog?.Invoke(new ModbusLogEntry
            {
                Time = DateTime.Now,
                Type = "连接",
                Value = connected ? "已连接" : "已断开"
            });
            Log($"🌐 Modbus 状态: {(connected ? "已连接" : "已断开")}");
        }

        // ============================================================
        // 相机控制
        // ============================================================
        public bool ConnectCamera(int index)
        {
            try
            {
                _camera?.Dispose();
                _camera = new RealCameraService();

                var task = _camera.ConnectAsync($"相机{index}");
                task.Wait(5000);

                if (!_camera.IsConnected)
                {
                    _camera = null;
                    SetState(State.Error);
                    Log($"❌ 相机连接失败");
                    return false;
                }

                _camera.ImageCaptured += OnImageCaptured;
                SetState(State.Idle);
                Log($"✅ 相机已连接 (索引 {index})");
                return true;
            }
            catch (Exception ex)
            {
                Log($"❌ 相机连接异常: {ex.Message}");
                SetState(State.Error);
                return false;
            }
        }

        public void DisconnectCamera()
        {
            if (_camera != null)
            {
                _camera.ImageCaptured -= OnImageCaptured;
                _camera.Dispose();
                _camera = null;
                Log("🔌 相机已断开");
                SetState(State.Idle);
            }
        }

        public string[] GetCameraList()
        {
            try
            {
                using var tempCam = new RealCameraService();
                return tempCam.GetCameraList().ToArray();
            }
            catch
            {
                return new[] { "未检测到相机" };
            }
        }

        // ============================================================
        // 曝光 / 对焦 透传
        // ============================================================
        public void EnableAutoExposure()
        {
            if (_camera is RealCameraService real) real.EnableAutoExposure();
        }

        public void SetExposure(double value)
        {
            if (_camera is RealCameraService real) real.SetExposure(value);
        }

        public void LockExposure(double? value = null)
        {
            if (_camera is RealCameraService real) real.LockExposure(value);
        }

        public void EnableAutoFocus()
        {
            if (_camera is RealCameraService real) real.EnableAutoFocus();
        }

        public void LockFocus(double? value = null)
        {
            if (_camera is RealCameraService real) real.LockFocus(value);
        }

        public (double Exposure, double Focus, double Gain, int Width, int Height, double Fps) GetCameraDiagnostics()
        {
            if (_camera is RealCameraService real)
            {
                var (w, h, fps) = real.GetCameraInfo();
                return (real.GetExposure(), real.GetFocus(), real.GetGain(), w, h, fps);
            }
            return (0, 0, 0, 0, 0, 0);
        }

        // ============================================================
        // Modbus 控制
        // ============================================================
        public void StartModbus()
        {
            if (!_modbus.IsRunning)
            {
                _modbus.Start();
                Log($"🌐 Modbus 服务已启动");
            }
        }

        public void StopModbus()
        {
            _modbus.Stop();
            Log("🌐 Modbus 服务已停止");
        }

        public void RestartModbus()
        {
            _modbus.Stop();
            Thread.Sleep(500);
            _modbus.Start();
            Log($"🌐 Modbus 服务已重启");
        }

        // ============================================================
        // 配置重载
        // ============================================================
        public void ReloadConfig()
        {
            var newConfig = VisionConfig.Load();
            if (newConfig == null) return;

            _config.ThresholdMin = newConfig.ThresholdMin;
            _config.ThresholdMax = newConfig.ThresholdMax;
            _config.CalibrationFactor = newConfig.CalibrationFactor;
            _config.StandardDiameterMm = newConfig.StandardDiameterMm;
            _config.ToleranceMm = newConfig.ToleranceMm;
            _config.HomMat2D = newConfig.HomMat2D;

            Log($"🔄 算法参数与手眼标定矩阵已重新加载");
        }

        // ============================================================
        // 手动触发
        // ============================================================
        public async Task<bool> ManualTriggerAsync()
        {
            Log("🖐️ 手动触发拍照...");
            return await ExecuteCaptureAsync("手动触发");
        }

        // ============================================================
        // 帧测量结果
        // ============================================================
        private class FrameMeasurementResult
        {
            public bool Success { get; set; }
            public double Diameter { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public HObject? DisplayImage { get; set; }
            public int Attempts { get; set; }
        }

        // ============================================================
        // 单帧拍照 + 测量 + 自动重试
        // ============================================================
        private async Task<FrameMeasurementResult> CaptureAndMeasureWithRetryAsync(
            int frameIndex,
            bool needDisplayImage)
        {
            var result = new FrameMeasurementResult();

            for (int retry = 0; retry < MAX_CAPTURE_RETRIES; retry++)
            {
                result.Attempts = retry + 1;

                HObject? image = null;
                try
                {
                    image = await CaptureFrameAsync();
                }
                catch (Exception ex)
                {
                    Log($"   ⚠️ 帧{frameIndex} 第 {retry + 1}/{MAX_CAPTURE_RETRIES} 次拍照异常: {ex.Message}", LogLevel.Warn);
                    await Task.Delay(RETRY_DELAY_MS);
                    continue;
                }

                if (image == null || !image.IsInitialized())
                {
                    Log($"   ⚠️ 帧{frameIndex} 第 {retry + 1}/{MAX_CAPTURE_RETRIES} 次拍照返回空图，准备重拍...", LogLevel.Warn);
                    image?.Dispose();
                    await Task.Delay(RETRY_DELAY_MS);
                    continue;
                }

                if (needDisplayImage && result.DisplayImage == null)
                {
                    try
                    {
                        HOperatorSet.CopyImage(image, out HObject tempDisplay);
                        result.DisplayImage = tempDisplay;
                    }
                    catch { }
                }

                double d = SingleMeasurement(image, out double px, out double py, frameIndex);
                image.Dispose();

                if (d > 0)
                {
                    result.Success = true;
                    result.Diameter = d;
                    result.X = px;
                    result.Y = py;

                    if (retry > 0)
                        Log($"   ✅ 帧{frameIndex} 第 {retry + 1} 次尝试成功: {d:F4} mm", LogLevel.Info);

                    return result;
                }

                Log($"   ⚠️ 帧{frameIndex} 第 {retry + 1}/{MAX_CAPTURE_RETRIES} 次测量失败，准备重拍...", LogLevel.Warn);
                await Task.Delay(RETRY_DELAY_MS);
            }

            Log($"   ❌ 帧{frameIndex} 连续 {MAX_CAPTURE_RETRIES} 次拍照/测量均失败，放弃该帧", LogLevel.Error);
            return result;
        }

        // ============================================================
        // 采集一帧（trigger 帧）
        // ============================================================
        private async Task<HObject?> CaptureFrameAsync()
        {
            if (_camera == null) return null;

            try
            {
                return await _camera.TriggerAsync();
            }
            catch (Exception ex)
            {
                Log($"⚠️ TriggerAsync 异常: {ex.Message}", LogLevel.Warn);
                return null;
            }
        }

        // ============================================================
        // 单次测量（用于重复性测试）
        // ============================================================
        public async Task<double> MeasureOnceAsync()
        {
            try
            {
                if (_camera == null || !_camera.IsConnected) return -1;

                var results = new List<double>();
                double lastX = 0, lastY = 0;
                int successFrames = 0;
                int failFrames = 0;
                int totalAttempts = 0;

                for (int frame = 0; frame < MEASUREMENT_FRAMES; frame++)
                {
                    var fr = await CaptureAndMeasureWithRetryAsync(
                        frameIndex: frame + 1,
                        needDisplayImage: false);

                    totalAttempts += fr.Attempts;

                    if (fr.DisplayImage != null)
                    {
                        fr.DisplayImage.Dispose();
                        fr.DisplayImage = null;
                    }

                    if (fr.Success)
                    {
                        results.Add(fr.Diameter);
                        lastX = fr.X;
                        lastY = fr.Y;
                        successFrames++;
                    }
                    else
                    {
                        failFrames++;
                    }

                    await Task.Delay(20);
                }

                if (results.Count == 0) return -1;

                results.Sort();
                double median = results[results.Count / 2];

                Log($"   ✅ 本次测量: {median:F4} mm (成功 {successFrames}/{MEASUREMENT_FRAMES} 帧，共尝试 {totalAttempts} 次)",
                    LogLevel.Info);

                return median;
            }
            catch (Exception ex)
            {
                Log($"⚠️ 单次测量异常: {ex.Message}", LogLevel.Warn);
                return -1;
            }
        }

        // ============================================================
        // 重复性测试
        // ============================================================
        public async Task<string> RepeatabilityTestAsync(int count = 50)
        {
            var diameters = new List<double>();
            Log($"▶️ 开始重复性测试（连续 {count} 次，每次 {MEASUREMENT_FRAMES} 帧中位数，单帧最多重试 {MAX_CAPTURE_RETRIES} 次）...");

            for (int i = 0; i < count; i++)
            {
                double d = await MeasureOnceAsync();
                if (d > 0)
                {
                    diameters.Add(d);
                    Log($"   [{i + 1}/{count}] 直径: {d:F4} mm");
                }
                else
                {
                    Log($"   [{i + 1}/{count}] ⚠️ 测量失败（已尝试 {MEASUREMENT_FRAMES}×{MAX_CAPTURE_RETRIES} 次）", LogLevel.Warn);
                }
            }

            if (diameters.Count == 0)
                return "⚠️ 重复性测试失败：未获取到有效数据";

            double max = diameters.Max();
            double min = diameters.Min();
            double avg = diameters.Average();
            double stdDev = Math.Sqrt(diameters.Select(v => Math.Pow(v - avg, 2)).Average());

            var report = new System.Text.StringBuilder();
            report.AppendLine("========== 📊 重复性测试报告 ==========");
            report.AppendLine($"   有效测量次数: {diameters.Count} / {count}");
            report.AppendLine($"   单次采帧数量: {MEASUREMENT_FRAMES} 帧");
            report.AppendLine($"   单帧最大重试: {MAX_CAPTURE_RETRIES} 次");
            report.AppendLine($"   最大值 (Max):  {max:F4} mm");
            report.AppendLine($"   最小值 (Min):  {min:F4} mm");
            report.AppendLine($"   平均值 (Avg):  {avg:F4} mm");
            report.AppendLine($"   极差 (Max-Min): {(max - min) * 1000:F1} 微米");
            report.AppendLine($"   标准差 (StdDev): {stdDev * 1000:F1} 微米");

            if ((max - min) * 1000 < 20)
                report.AppendLine("   🏆 结论：重复性极佳，达到工业级稳定标准！");
            else if ((max - min) * 1000 < 40)
                report.AppendLine("   ✅ 结论：重复性良好，满足常规产线要求。");
            else
                report.AppendLine("   ⚠️ 结论：波动较大，建议优化打光或算法参数。");
            report.AppendLine("========================================");

            return report.ToString();
        }

        // ============================================================
        // 核心：工业视觉检测闭环（未检测到目标时自动重拍）
        // ============================================================
        private async Task<bool> ExecuteCaptureAsync(string source)
        {
            lock (_lock)
            {
                if (_currentState == State.Busy) return false;
                SetState(State.Busy);
            }

            try
            {
                if (_camera == null || !_camera.IsConnected)
                {
                    SetState(State.Error);
                    _modbus.UpdateStatus(false, 2, "");
                    return false;
                }

                List<TargetResult>? targets = null;
                HObject? displayImage = null;

                // ⭐⭐⭐ 未检测到目标时最多重拍 MAX_DETECT_RETRIES 次 ⭐⭐⭐
                for (int attempt = 0; attempt < MAX_DETECT_RETRIES; attempt++)
                {
                    HObject? mainImage = null;

                    // 采集主检测帧（含帧级重试）
                    for (int tryCapture = 0; tryCapture < MAX_CAPTURE_RETRIES; tryCapture++)
                    {
                        try
                        {
                            mainImage = await CaptureFrameAsync();
                            if (mainImage != null && mainImage.IsInitialized())
                                break;
                        }
                        catch (Exception ex)
                        {
                            Log($"   ⚠️ 主检测帧采集失败 (尝试 {tryCapture + 1}/{MAX_CAPTURE_RETRIES}): {ex.Message}", LogLevel.Warn);
                        }

                        mainImage?.Dispose();
                        mainImage = null;
                        await Task.Delay(RETRY_DELAY_MS);
                    }

                    if (mainImage == null)
                    {
                        Log($"   ❌ 第 {attempt + 1}/{MAX_DETECT_RETRIES} 次检测：主检测帧采集失败", LogLevel.Warn);
                        continue;
                    }

                    // 多目标检测
                    targets = DetectMultipleTargets(mainImage, out displayImage);
                    mainImage.Dispose();

                    if (targets.Count > 0)
                    {
                        if (attempt > 0)
                            Log($"   ✅ 第 {attempt + 1} 次检测成功，检测到 {targets.Count} 个目标", LogLevel.Info);
                        break;
                    }

                    // 未检测到目标 → 清理并重拍
                    Log($"   ⚠️ 第 {attempt + 1}/{MAX_DETECT_RETRIES} 次未检测到目标，准备重拍...", LogLevel.Warn);
                    displayImage?.Dispose();
                    displayImage = null;
                    targets = null;

                    await Task.Delay(100);
                }

                // ====================================================
                // 全部重试都失败 → 返回 NG（业务结果，不抛异常）
                // ====================================================
                if (targets == null || targets.Count == 0)
                {
                    Log($"⚠️ {MAX_DETECT_RETRIES} 次检测均未发现目标（业务 NG）", LogLevel.Warn);
                    _modbus.UpdateMultiTargetStatus(false, 0, new List<TargetResult>());

                    if (displayImage != null)
                    {
                        TrySaveImage(displayImage);
                        ImageCaptured?.Invoke(displayImage);
                    }

                    _processCount++;
                    SetState(State.Idle);
                    return false;
                }

                // ====================================================
                // 有目标 → 统计结果
                // ====================================================
                bool overallPass = targets.All(t => t.IsPass);
                int passCount = targets.Count(t => t.IsPass);
                int ngCount = targets.Count - passCount;

                Log($"📊 检测完成 | 目标数: {targets.Count} | 整体结果: {(overallPass ? "OK" : "NG")} (OK:{passCount} NG:{ngCount})");

                foreach (var t in targets)
                {
                    Log($"   🎯 目标[{t.Index}] {(t.IsPass ? "OK" : "NG")} | 直径: {t.DiameterMm:F2}mm | 坐标: ({t.PhysicalX:F2}, {t.PhysicalY:F2})");
                }

                _modbus.UpdateMultiTargetStatus(overallPass, targets.Count, targets);

                if (displayImage != null)
                {
                    TrySaveImage(displayImage);
                    ImageCaptured?.Invoke(displayImage);
                }

                _processCount++;
                SetState(State.Idle);
                return overallPass;
            }
            catch (Exception ex)
            {
                _failCount++;
                Log($"❌ 处理失败 [{source}]: {ex.Message}");
                _modbus.UpdateStatus(false, 1, "");
                SetState(State.Error);
                return false;
            }
        }

        // ============================================================
        // 多目标检测
        // ============================================================
        private List<TargetResult> DetectMultipleTargets(HObject image, out HObject? displayImage)
        {
            displayImage = null;
            var targets = new List<TargetResult>();

            HObject? ho_GrayImage = null;
            HObject? ho_SmoothedImage = null;
            HObject? ho_Edges = null;
            HObject? ho_EdgesLen = null;
            HObject? ho_EdgesCirc = null;

            try
            {
                HOperatorSet.Rgb1ToGray(image, out ho_GrayImage);
                HOperatorSet.GaussFilter(ho_GrayImage, out ho_SmoothedImage, 7);
                HOperatorSet.EdgesSubPix(ho_SmoothedImage, out ho_Edges, "canny", 1.0, 20, 40);

                HOperatorSet.SelectShapeXld(ho_Edges, out ho_EdgesLen, "contlength", "and", MIN_CONTOUR_LENGTH, 99999);
                HOperatorSet.SelectShapeXld(ho_EdgesLen, out ho_EdgesCirc, "circularity", "and", MIN_CIRCULARITY, 1.0);

                HOperatorSet.CountObj(ho_EdgesCirc, out HTuple hv_Count);
                if (hv_Count.I == 0)
                {
                    Log($"   ⚠️ 圆度筛选后无候选轮廓 (circularity≥{MIN_CIRCULARITY})", LogLevel.Debug);
                    TryCopyDisplay(image, out displayImage);
                    return targets;
                }

                for (int i = 1; i <= hv_Count.I; i++)
                {
                    HObject? ho_One = null;
                    try
                    {
                        HOperatorSet.SelectObj(ho_EdgesCirc, out ho_One, i);

                        HOperatorSet.GetContourXld(ho_One, out HTuple hv_Rows, out HTuple hv_Cols);
                        if (hv_Rows.Length < MIN_EDGE_POINTS)
                        {
                            Log($"   ↳ 轮廓[{i}] 边缘点数不足 ({hv_Rows.Length} < {MIN_EDGE_POINTS})，跳过", LogLevel.Debug);
                            continue;
                        }

                        HOperatorSet.FitCircleContourXld(ho_One, "ahuber", -1, 2, 0, 3, 2,
                            out HTuple hv_Row, out HTuple hv_Column, out HTuple hv_Radius,
                            out HTuple hv_StartPhi, out HTuple hv_EndPhi, out HTuple hv_PointOrder);

                        if (hv_Radius.D < RADIUS_MIN_PX || hv_Radius.D > RADIUS_MAX_PX)
                        {
                            Log($"   ↳ 轮廓[{i}] 半径异常 ({hv_Radius.D:F1}px, 期望 {RADIUS_MIN_PX}~{RADIUS_MAX_PX})，跳过", LogLevel.Debug);
                            continue;
                        }

                        // ---- 拟合残差检查：手算每个点到圆心的距离 ----
                        if (!CheckCircleResidual(ho_One, hv_Row, hv_Column, hv_Radius,
                                                 out double meanDist, out double stdDist, out string failReason))
                        {
                            Log($"   ↳ 轮廓[{i}] {failReason}，跳过", LogLevel.Debug);
                            continue;
                        }

                        double diameterMm = hv_Radius.D * 2 * _config.CalibrationFactor;

                        double physicalX = hv_Column.D;
                        double physicalY = hv_Row.D;
                        try
                        {
                            HTuple homMat2D = new HTuple(_config.HomMat2D);
                            HOperatorSet.AffineTransPoint2d(homMat2D, hv_Column.D, hv_Row.D,
                                out HTuple px, out HTuple py);
                            physicalX = px.D;
                            physicalY = py.D;
                        }
                        catch { }

                        bool sizeOk = (diameterMm >= _config.StandardDiameterMm - _config.ToleranceMm) &&
                                      (diameterMm <= _config.StandardDiameterMm + _config.ToleranceMm);

                        targets.Add(new TargetResult
                        {
                            Index = targets.Count + 1,
                            DiameterMm = diameterMm,
                            PhysicalX = physicalX,
                            PhysicalY = physicalY,
                            PixelX = hv_Column.D,
                            PixelY = hv_Row.D,
                            RadiusPx = hv_Radius.D,
                            IsPass = sizeOk,
                            DefectCount = 0
                        });
                    }
                    catch (Exception ex)
                    {
                        Log($"   ↳ 轮廓[{i}] 处理异常: {ex.Message}", LogLevel.Debug);
                    }
                    finally
                    {
                        ho_One?.Dispose();
                    }
                }

                TryCopyDisplay(image, out displayImage);
                return targets;
            }
            finally
            {
                ho_GrayImage?.Dispose();
                ho_SmoothedImage?.Dispose();
                ho_Edges?.Dispose();
                ho_EdgesLen?.Dispose();
                ho_EdgesCirc?.Dispose();
            }
        }

        // ============================================================
        // 单次测量（取最长轮廓 + 拟合残差双重校验）
        //
        // 三级过滤策略：
        //   L1. 圆度 ≥ 0.65         → 过滤掉高光弧等"扁平"轮廓
        //   L2. 半径 ∈ [83, 98]px   → 过滤掉大小异常的轮廓
        //   L3. 拟合残差 ≤ 1.2px    → 过滤掉点分布不均匀的"伪圆"
        //       且残差std ≤ 0.8px  → 保证边缘点高度贴合
        // ============================================================
        private double SingleMeasurement(HObject image, out double physicalX, out double physicalY, int frameNo = 0)
        {
            physicalX = 0;
            physicalY = 0;

            HObject? ho_GrayImage = null;
            HObject? ho_SmoothedImage = null;
            HObject? ho_Edges = null;
            HObject? ho_EdgesLen = null;
            HObject? ho_EdgesCirc = null;
            HObject? ho_BestEdge = null;

            string tag = frameNo > 0 ? $"帧{frameNo}" : "";

            try
            {
                // ---- 1. 灰度化 + 高斯平滑 ----
                HOperatorSet.Rgb1ToGray(image, out ho_GrayImage);
                HOperatorSet.GaussFilter(ho_GrayImage, out ho_SmoothedImage, 7);

                // ---- 2. 亚像素边缘提取 ----
                HOperatorSet.EdgesSubPix(ho_SmoothedImage, out ho_Edges, "canny", 1.0, 20, 40);

                // ---- 3. 长度筛选 ----
                HOperatorSet.SelectShapeXld(ho_Edges, out ho_EdgesLen, "contlength", "and", MIN_CONTOUR_LENGTH, 99999);
                HOperatorSet.CountObj(ho_EdgesLen, out HTuple n1);
                if (n1.I == 0)
                {
                    Log($"      ↳ {tag} 长度筛选后无边缘 (contlength≥{MIN_CONTOUR_LENGTH})", LogLevel.Debug);
                    return 0;
                }

                // ---- 4. 圆度筛选（L1）----
                HOperatorSet.SelectShapeXld(ho_EdgesLen, out ho_EdgesCirc, "circularity", "and", MIN_CIRCULARITY, 1.0);
                HOperatorSet.CountObj(ho_EdgesCirc, out HTuple n2);
                if (n2.I == 0)
                {
                    Log($"      ↳ {tag} 圆度筛选后无边缘 (circularity≥{MIN_CIRCULARITY})", LogLevel.Debug);
                    return 0;
                }

                // ---- 5. 取最长轮廓 ----
                HOperatorSet.LengthXld(ho_EdgesCirc, out HTuple hv_Lengths);
                HOperatorSet.TupleMax(hv_Lengths, out HTuple hv_MaxLen);
                HOperatorSet.TupleFind(hv_Lengths, hv_MaxLen, out HTuple hv_Indices);
                HOperatorSet.SelectObj(ho_EdgesCirc, out ho_BestEdge, hv_Indices + 1);

                // ---- 6. 边缘点数检查 ----
                HOperatorSet.GetContourXld(ho_BestEdge, out HTuple hv_Rows, out HTuple hv_Cols);
                if (hv_Rows.Length < MIN_EDGE_POINTS)
                {
                    Log($"      ↳ {tag} 边缘点数不足 ({hv_Rows.Length} < {MIN_EDGE_POINTS})", LogLevel.Debug);
                    return 0;
                }

                // ---- 7. 圆拟合 ----
                HOperatorSet.FitCircleContourXld(ho_BestEdge, "ahuber", -1, 2, 0, 3, 2,
                    out HTuple hv_Row, out HTuple hv_Column, out HTuple hv_Radius,
                    out HTuple hv_StartPhi, out HTuple hv_EndPhi, out HTuple hv_PointOrder);

                // ---- 8. 半径范围检查（L2）----
                if (hv_Radius.D < RADIUS_MIN_PX || hv_Radius.D > RADIUS_MAX_PX)
                {
                    Log($"      ↳ {tag} 半径异常 ({hv_Radius.D:F1}px, 期望 {RADIUS_MIN_PX}~{RADIUS_MAX_PX})", LogLevel.Debug);
                    return 0;
                }

                // ================================================================
                // 9. 拟合残差检查（L3 - 核心优化）
                //    手算每个轮廓点到圆心的距离，减去半径得到残差，
                //    然后检查残差的均值和标准差
                // ================================================================
                if (!CheckCircleResidual(ho_BestEdge, hv_Row, hv_Column, hv_Radius,
                                         out double meanDist, out double stdDist, out string failReason))
                {
                    Log($"      ↳ {tag} {failReason}", LogLevel.Debug);
                    return 0;
                }

                // ---- 10. 计算直径 ----
                double diameterMm = hv_Radius.D * 2 * _config.CalibrationFactor;

                // ---- 11. 坐标变换 ----
                try
                {
                    HTuple homMat2D = new HTuple(_config.HomMat2D);
                    HOperatorSet.AffineTransPoint2d(homMat2D, hv_Column.D, hv_Row.D, out HTuple px, out HTuple py);
                    physicalX = px.D;
                    physicalY = py.D;
                }
                catch
                {
                    physicalX = hv_Column.D;
                    physicalY = hv_Row.D;
                }

                return diameterMm;
            }
            finally
            {
                ho_GrayImage?.Dispose();
                ho_SmoothedImage?.Dispose();
                ho_Edges?.Dispose();
                ho_EdgesLen?.Dispose();
                ho_EdgesCirc?.Dispose();
                ho_BestEdge?.Dispose();
            }
        }

        // ============================================================
        // 🆕 拟合残差检查（辅助方法）
        //
        // 原理：
        //   对每个轮廓点 (r, c)，计算它到圆心的欧氏距离，
        //   然后减去拟合半径，得到"该点偏离理想圆的程度"（残差）。
        //   真实圆的残差应非常小（均值 < 1.2px，std < 0.8px）；
        //   高光弧 / 噪声轮廓的残差会明显偏大。
        // ============================================================
        private bool CheckCircleResidual(
            HObject contour,
            HTuple centerRow, HTuple centerCol, HTuple radius,
            out double meanDist, out double stdDist, out string failReason)
        {
            meanDist = 0;
            stdDist = 0;
            failReason = "";

            try
            {
                // ---- 1. 获取轮廓上所有点 ----
                HOperatorSet.GetContourXld(contour, out HTuple pointsRow, out HTuple pointsCol);
                if (pointsRow.Length == 0)
                {
                    failReason = "轮廓点为空";
                    return false;
                }

                // ---- 2. 计算每个点到圆心的距离 ----
                //   dx = col - centerCol
                //   dy = row - centerRow
                //   dist = sqrt(dx² + dy²)
                HOperatorSet.TupleSub(pointsRow, centerRow, out HTuple dy);       // Δrow
                HOperatorSet.TupleSub(pointsCol, centerCol, out HTuple dx);       // Δcol

                HOperatorSet.TupleMult(dy, dy, out HTuple dy2);                    // Δrow²
                HOperatorSet.TupleMult(dx, dx, out HTuple dx2);                    // Δcol²
                HOperatorSet.TupleAdd(dy2, dx2, out HTuple sumSq);                 // Δrow² + Δcol²
                HOperatorSet.TupleSqrt(sumSq, out HTuple distances);              // 距离数组

                // ---- 3. 残差 = 距离 - 半径（有正有负）----
                HOperatorSet.TupleSub(distances, radius, out HTuple residuals);

                // ---- 4. 残差取绝对值（我们关心偏离的幅度）----
                HOperatorSet.TupleAbs(residuals, out HTuple absResiduals);

                // ---- 5. 计算统计量 ----
                HOperatorSet.TupleMean(absResiduals, out HTuple hv_Mean);
                HOperatorSet.TupleDeviation(absResiduals, out HTuple hv_Std);

                meanDist = hv_Mean.D;
                stdDist = hv_Std.D;

                // ---- 6. 阈值判定 ----
                if (meanDist > MAX_MEAN_RESIDUAL_PX)
                {
                    failReason = $"拟合残差过大: 平均={meanDist:F2}px (阈值{MAX_MEAN_RESIDUAL_PX})";
                    return false;
                }

                if (stdDist > MAX_STD_RESIDUAL_PX)
                {
                    failReason = $"残差标准差过大: std={stdDist:F2}px (阈值{MAX_STD_RESIDUAL_PX})";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                failReason = $"残差计算异常: {ex.Message}";
                return false;
            }
        }

        // ============================================================
        // 辅助
        // ============================================================
        private void TryCopyDisplay(HObject image, out HObject? displayImage)
        {
            displayImage = null;
            try
            {
                HOperatorSet.CopyImage(image, out HObject temp);
                displayImage = temp;
            }
            catch { }
        }

        private void TrySaveImage(HObject image)
        {
            try
            {
                string fileName = $"{_config.ImagePrefix}{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
                string dir = Path.Combine(_config.ImageSavePath, DateTime.Now.ToString("yyyy-MM-dd"));
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string fullPath = Path.Combine(dir, fileName);
                HOperatorSet.WriteImage(image, "png", 0, fullPath);
                _lastImagePath = fullPath;
            }
            catch (Exception ex)
            {
                Log($"⚠️ 保存图像失败: {ex.Message}", LogLevel.Warn);
            }
        }

        // ============================================================
        // 相机帧回调：只处理后台实时帧
        // ============================================================
        private void OnImageCaptured(object? sender, CameraImageEventArgs e)
        {
            if (e.Image == null || !e.Image.IsInitialized())
            {
                e.Image?.Dispose();
                return;
            }

            // trigger 帧不由事件送达
            if (e.IsTriggered)
            {
                e.Image.Dispose();
                return;
            }

            // 后台实时帧：节流处理
            var now = DateTime.Now;
            if ((now - _lastPreviewTime).TotalMilliseconds < PREVIEW_THROTTLE_MS)
            {
                e.Image.Dispose();
                return;
            }

            _lastPreviewTime = now;
            ImageCaptured?.Invoke(e.Image);
        }

        private void SetState(State state)
        {
            if (_currentState != state)
            {
                _currentState = state;
                StateChanged?.Invoke(state);
                _modbus.UpdateBusyState(state == State.Busy);
            }
        }

        private void Log(string msg, LogLevel level = LogLevel.Info)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            LogMessage?.Invoke($"[{timestamp}] {msg}");
            AppLogger.Log(msg, level);
        }

        public void Dispose()
        {
            try { _modbus?.Dispose(); } catch { }
            try { _camera?.Dispose(); } catch { }
            GC.SuppressFinalize(this);
        }
    }
}