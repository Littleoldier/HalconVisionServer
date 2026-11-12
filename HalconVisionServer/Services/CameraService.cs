using OpenCvSharp;
using OpenCvSharp.Extensions;
using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HalconVisionServer.Services
{
    /// <summary>
    /// 图像事件参数
    ///
    /// ⚠️ 所有权约定（重要）：
    ///   - <see cref="Image"/> 由「发布方」创建并负责深拷贝一份；
    ///   - 「订阅方」接收后拥有该拷贝的所有权，必须在用完后调用 Dispose()。
    ///   - 若发布时无人订阅（handler == null），发布方自行释放，避免泄漏。
    ///
    /// 该约定避免了跨线程共享同一个 HObject 导致的崩溃。
    /// </summary>
    public class CameraImageEventArgs : EventArgs
    {
        public HObject Image { get; }
        public DateTime Timestamp { get; }
        public bool IsTriggered { get; }
        public long FrameId { get; }

        public CameraImageEventArgs(HObject image, bool isTriggered = false, long frameId = 0)
        {
            Image = image;
            Timestamp = DateTime.Now;
            IsTriggered = isTriggered;
            FrameId = frameId;
        }
    }

    public interface ICameraService : IDisposable
    {
        event EventHandler<CameraImageEventArgs>? ImageCaptured;
        event EventHandler<bool>? ConnectionStateChanged;

        bool IsConnected { get; }
        bool IsGrabbing { get; }
        long FrameCounter { get; }

        List<string> GetCameraList();
        Task<bool> ConnectAsync(string? cameraName = null);
        void Disconnect();

        // ⭐ TriggerAsync 直接返回 HObject，不再通过事件广播
        //   调用方负责 Dispose 返回值
        Task<HObject> TriggerAsync();

        void StartGrabbing();
        void StopGrabbing();

        void EnableAutoExposure();
        void SetExposure(double value);
        void LockExposure(double? exposureValue = null);
        void EnableAutoFocus();
        void LockFocus(double? focusValue = null);

        double GetExposure();
        double GetFocus();
        double GetGain();
        (int Width, int Height, double Fps) GetCameraInfo();
    }

    public class RealCameraService : ICameraService
    {
        private VideoCapture? _capture;
        private CancellationTokenSource? _cts;
        private Task? _grabTask;
        private volatile bool _isConnected;
        private volatile bool _isGrabbing;

        private readonly object _captureLock = new object();
        private HObject? _latestFrame = null;
        private readonly object _frameLock = new object();

        private long _frameCounter = 0;
        public long FrameCounter => Interlocked.Read(ref _frameCounter);

        public event EventHandler<CameraImageEventArgs>? ImageCaptured;
        public event EventHandler<bool>? ConnectionStateChanged;

        public bool IsConnected => _isConnected;
        public bool IsGrabbing => _isGrabbing;

        public List<string> GetCameraList() => new List<string> { "相机0", "相机1", "相机2" };

        // ============================================================
        // 连接
        // ============================================================
        public Task<bool> ConnectAsync(string? cameraName = null)
        {
            if (_isConnected) return Task.FromResult(true);

            int index = 0;
            if (!string.IsNullOrEmpty(cameraName))
            {
                var digits = new string(cameraName.Where(char.IsDigit).ToArray());
                int.TryParse(digits, out index);
            }

            try
            {
                _capture = new VideoCapture(index);
                if (!_capture.IsOpened())
                {
                    _capture.Dispose();
                    _capture = null;
                    Utils.AppLogger.Error($"❌ OpenCvSharp 无法打开相机 (索引 {index})", "Camera");
                    return Task.FromResult(false);
                }

                int w = (int)_capture.Get(VideoCaptureProperties.FrameWidth);
                int h = (int)_capture.Get(VideoCaptureProperties.FrameHeight);
                double fps = _capture.Get(VideoCaptureProperties.Fps);
                Utils.AppLogger.Info($"📷 相机打开成功: 索引={index}, 分辨率={w}x{h}, FPS={fps:F1}", "Camera");

                // 保持自动曝光/对焦
                try
                {
                    _capture.Set(VideoCaptureProperties.AutoExposure, 1);
                    _capture.Set(VideoCaptureProperties.AutoFocus, 1);
                    Utils.AppLogger.Info($"📷 自动曝光/对焦已启用（由相机自适应）", "Camera");
                }
                catch (Exception ex)
                {
                    Utils.AppLogger.Warn($"⚠️ 设置自动曝光/对焦失败（相机可能不支持）: {ex.Message}", "Camera");
                }

                _isConnected = true;
                ConnectionStateChanged?.Invoke(this, true);
                Utils.AppLogger.Info($"✅ OpenCvSharp 相机已连接 (索引 {index})", "Camera");

                StartGrabbing();
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Utils.AppLogger.Error($"❌ 相机连接异常: {ex.Message}", "Camera");
                return Task.FromResult(false);
            }
        }

        // ============================================================
        // 曝光 / 对焦控制
        // ============================================================
        public void EnableAutoExposure()
        {
            try
            {
                if (_capture != null && _capture.IsOpened())
                {
                    _capture.Set(VideoCaptureProperties.AutoExposure, 1);
                    Utils.AppLogger.Info($"📷 自动曝光已启用", "Camera");
                }
            }
            catch (Exception ex) { Utils.AppLogger.Warn($"⚠️ 启用自动曝光失败: {ex.Message}", "Camera"); }
        }

        public void SetExposure(double value)
        {
            try
            {
                if (_capture != null && _capture.IsOpened())
                {
                    _capture.Set(VideoCaptureProperties.AutoExposure, 0);
                    _capture.Set(VideoCaptureProperties.Exposure, value);
                    Utils.AppLogger.Info($"📷 曝光已设置为: {value}", "Camera");
                }
            }
            catch (Exception ex) { Utils.AppLogger.Warn($"⚠️ 设置曝光失败: {ex.Message}", "Camera"); }
        }

        public void LockExposure(double? exposureValue = null)
        {
            try
            {
                if (_capture != null && _capture.IsOpened())
                {
                    double val = exposureValue ?? _capture.Get(VideoCaptureProperties.Exposure);
                    _capture.Set(VideoCaptureProperties.AutoExposure, 0);
                    _capture.Set(VideoCaptureProperties.Exposure, val);
                    Utils.AppLogger.Info($"📷 曝光已锁定: {val}", "Camera");
                }
            }
            catch (Exception ex) { Utils.AppLogger.Warn($"⚠️ 锁定曝光失败: {ex.Message}", "Camera"); }
        }

        public void EnableAutoFocus()
        {
            try
            {
                if (_capture != null && _capture.IsOpened())
                {
                    _capture.Set(VideoCaptureProperties.AutoFocus, 1);
                    Utils.AppLogger.Info($"📷 自动对焦已启用", "Camera");
                }
            }
            catch (Exception ex) { Utils.AppLogger.Warn($"⚠️ 启用自动对焦失败: {ex.Message}", "Camera"); }
        }

        public void LockFocus(double? focusValue = null)
        {
            try
            {
                if (_capture != null && _capture.IsOpened())
                {
                    double val = focusValue ?? _capture.Get(VideoCaptureProperties.Focus);
                    _capture.Set(VideoCaptureProperties.AutoFocus, 0);
                    _capture.Set(VideoCaptureProperties.Focus, val);
                    Utils.AppLogger.Info($"📷 对焦已锁定: {val}", "Camera");
                }
            }
            catch (Exception ex) { Utils.AppLogger.Warn($"⚠️ 锁定对焦失败: {ex.Message}", "Camera"); }
        }

        // ============================================================
        // 诊断
        // ============================================================
        public double GetExposure()
        {
            try { return _capture?.Get(VideoCaptureProperties.Exposure) ?? 0; } catch { return 0; }
        }

        public double GetFocus()
        {
            try { return _capture?.Get(VideoCaptureProperties.Focus) ?? 0; } catch { return 0; }
        }

        public double GetGain()
        {
            try { return _capture?.Get(VideoCaptureProperties.Gain) ?? 0; } catch { return 0; }
        }

        public (int Width, int Height, double Fps) GetCameraInfo()
        {
            try
            {
                if (_capture == null) return (0, 0, 0);
                int w = (int)_capture.Get(VideoCaptureProperties.FrameWidth);
                int h = (int)_capture.Get(VideoCaptureProperties.FrameHeight);
                double fps = _capture.Get(VideoCaptureProperties.Fps);
                return (w, h, fps);
            }
            catch { return (0, 0, 0); }
        }

        // ============================================================
        // 断开
        // ============================================================
        public void Disconnect()
        {
            StopGrabbing();
            _capture?.Dispose();
            _capture = null;
            _isConnected = false;

            lock (_frameLock)
            {
                _latestFrame?.Dispose();
                _latestFrame = null;
            }

            ConnectionStateChanged?.Invoke(this, false);
        }

        // ============================================================
        // ⭐ 触发拍照：直接返回 HObject，不再通过事件广播
        // 谁调用谁持有所有权，谁负责 Dispose
        // ============================================================
        public Task<HObject> TriggerAsync()
        {
            return Task.Run(() =>
            {
                if (!_isConnected || _capture == null) throw new Exception("相机未连接");

                long startFrameId = Interlocked.Read(ref _frameCounter);
                var sw = System.Diagnostics.Stopwatch.StartNew();

                // 等待 GrabLoop 更新至少一帧（超时 1000ms）
                while (Interlocked.Read(ref _frameCounter) <= startFrameId
                       && sw.ElapsedMilliseconds < 1000
                       && _isConnected)
                {
                    Thread.Sleep(5);
                }

                if (!_isConnected) throw new Exception("相机已断开");

                long currentFrameId = Interlocked.Read(ref _frameCounter);
                if (currentFrameId <= startFrameId)
                {
                    Utils.AppLogger.Warn($"⚠️ TriggerAsync 等待新帧超时 ({sw.ElapsedMilliseconds}ms)，使用缓存帧", "Camera");
                }

                HObject? captured = null;
                lock (_frameLock)
                {
                    if (_latestFrame != null && _latestFrame.IsInitialized())
                    {
                        // ⭐ 深拷贝：返回给调用方，调用方负责 Dispose
                        HOperatorSet.CopyImage(_latestFrame, out captured);
                    }
                }

                if (captured == null)
                {
                    lock (_captureLock)
                    {
                        if (_capture != null && _capture.IsOpened())
                        {
                            using var mat = new Mat();
                            _capture.Read(mat);
                            if (!mat.Empty())
                            {
                                captured = ConvertMatToHObject(mat);
                            }
                        }
                    }
                }

                if (captured == null) throw new Exception("拍照返回空图像");
                return captured;
            });
        }

        // ============================================================
        // 采集循环（后台帧通过事件转发给 UI）
        // ============================================================
        public void StartGrabbing()
        {
            if (_isGrabbing || !_isConnected || _capture == null) return;
            _isGrabbing = true;
            _cts = new CancellationTokenSource();
            _grabTask = Task.Run(() => GrabLoop(_cts.Token));
        }

        public void StopGrabbing()
        {
            _isGrabbing = false;
            _cts?.Cancel();
            try { _grabTask?.Wait(500); } catch { }
            _cts?.Dispose();
            _cts = null;
        }

        private void GrabLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _isConnected && _capture != null)
            {
                try
                {
                    using (var mat = new Mat())
                    {
                        lock (_captureLock)
                        {
                            if (_capture == null) break;
                            _capture.Read(mat);
                        }

                        if (!mat.Empty())
                        {
                            // ⭐ 本方法创建，本方法负责释放（try/finally 兜底）
                            HObject hImage = ConvertMatToHObject(mat);
                            try
                            {
                                // ---- 1. 更新缓存（深拷贝，供 TriggerAsync 使用）----
                                lock (_frameLock)
                                {
                                    _latestFrame?.Dispose();
                                    HOperatorSet.CopyImage(hImage, out _latestFrame);
                                }

                                Interlocked.Increment(ref _frameCounter);

                                // ---- 2. 采样日志 ----
                                if (_frameCounter % 30 == 0)
                                {
                                    try
                                    {
                                        Scalar mean = Cv2.Mean(mat);
                                        Utils.AppLogger.Debug(
                                            $"📷 采集: {mat.Width}x{mat.Height}, 平均BGR=({mean.Val0:F0},{mean.Val1:F0},{mean.Val2:F0}), FrameId={_frameCounter}",
                                            "Camera");
                                    }
                                    catch { }
                                }

                                // ---- 3. 为事件深拷贝一份（跨线程传递必须 CopyImage）----
                                //    所有权约定：有订阅者 → 转移给订阅者；无订阅者 → 本方法释放
                                HObject? hImageForEvent = null;
                                try
                                {
                                    HOperatorSet.CopyImage(hImage, out hImageForEvent);

                                    var handler = ImageCaptured;
                                    if (handler != null)
                                    {
                                        handler.Invoke(this, new CameraImageEventArgs(
                                            hImageForEvent, isTriggered: false, frameId: _frameCounter));
                                        hImageForEvent = null; // ✅ 所有权已转移，不再由本方法释放
                                    }
                                }
                                finally
                                {
                                    hImageForEvent?.Dispose(); // 无订阅者时释放，避免泄漏
                                }
                            }
                            finally
                            {
                                // ⭐ 原图用完即释放，绝不跨线程
                                hImage.Dispose();
                            }
                        }
                        else
                        {
                            Utils.AppLogger.Warn("⚠️ GrabLoop 读到空帧（相机可能未就绪）", "Camera");
                            Thread.Sleep(50);
                        }
                    }
                    Thread.Sleep(15);
                }
                catch (Exception ex)
                {
                    Utils.AppLogger.Warn($"⚠️ 采集异常: {ex.Message}", "Camera");
                    Thread.Sleep(100);
                }
            }
        }

        /// <summary>
        /// 将 OpenCV Mat 转换为 HALCON HObject
        ///
        /// ⚠️ 关键点：
        ///   HALCON 的 GenImageInterleaved 生成图像时【不深拷贝】像素数据，
        ///   它只是引用 PixelPointer 指向的内存。因此必须：
        ///     1. 在 handle 仍然 pinned（有效）时立即调用 CopyImage 深拷贝一份；
        ///     2. 之后才能 handle.Free() 释放外部内存。
        ///   否则一旦 handle 被释放，返回的 HObject 内部指针就悬空了。
        /// </summary>
        private HObject ConvertMatToHObject(Mat mat)
        {
            using (Bitmap bmp = BitmapConverter.ToBitmap(mat))
            {
                BitmapData bmpData = bmp.LockBits(
                    new Rectangle(0, 0, bmp.Width, bmp.Height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format24bppRgb);

                int width = bmp.Width;
                int height = bmp.Height;
                int stride = bmpData.Stride;

                byte[] pixelData = new byte[width * height * 3];

                for (int y = 0; y < height; y++)
                {
                    IntPtr sourcePtr = IntPtr.Add(bmpData.Scan0, y * stride);
                    Marshal.Copy(sourcePtr, pixelData, y * width * 3, width * 3);
                }

                GCHandle handle = GCHandle.Alloc(pixelData, GCHandleType.Pinned);
                try
                {
                    // HALCON 的 GenImageInterleaved C# 包装：out HObject + 12 个输入参数
                    // （不含 CopyData，不要多写一个参数）
                    HOperatorSet.GenImageInterleaved(
                        out HObject hImageRef,           // out
                        handle.AddrOfPinnedObject(),     // PixelPointer
                        "bgr",                           // ColorFormat
                        width,                           // OriginalWidth
                        height,                          // OriginalHeight
                        0,                               // Alignment
                        "byte",                          // Type
                        width,                           // ImageWidth
                        height,                          // ImageHeight
                        0,                               // StartRow
                        0,                               // StartColumn
                        8,                               // BitsPerChannel
                        0);                              // BitOrder

                    // ⭐ 关键修复：handle 仍然 pinned 时，立即深拷贝到 HALCON 自有内存
                    //    这样后面 handle.Free() 释放外部 buffer 后，hImageCopy 依然安全
                    HOperatorSet.CopyImage(hImageRef, out HObject hImageCopy);
                    hImageRef.Dispose();   // 中间对象不再需要

                    return hImageCopy;
                }
                finally
                {
                    handle.Free();
                    bmp.UnlockBits(bmpData);
                }
            }
        }

        public void Dispose() => Disconnect();
    }
}