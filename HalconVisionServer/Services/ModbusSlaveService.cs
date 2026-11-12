using HalconVisionServer.Utils;
using NModbus;
using NModbus.Data;
using NModbus.Device;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace HalconVisionServer.Services
{
    public class ModbusLogEntry
    {
        public DateTime Time { get; set; }
        public string Type { get; set; } = "";
        public string Value { get; set; } = "";
    }

    public class ModbusSlaveService : IDisposable
    {
        private readonly object _lock = new();
        private readonly ModbusConfig _config;
        private readonly VisionConfig _visionConfig;

        private TcpListener? _listener;
        private IModbusSlaveNetwork? _slaveNetwork;
        private IModbusSlave? _slave;
        private IPointSource<bool>? _coils;
        private IPointSource<ushort>? _registers;
        private CancellationTokenSource? _cts;
        private Task? _listenTask;
        private Task? _pollingTask;
        private bool _isRunning;
        private bool _lastTriggerValue = false;

        // ============================================================
        // 寄存器地址定义
        // ============================================================
        private const ushort REG_SINGLE_DIAMETER = 1003;
        private const ushort REG_SINGLE_DEFECT = 1004;
        private const ushort REG_SINGLE_X = 1005;
        private const ushort REG_SINGLE_Y = 1006;

        private const ushort REG_TARGET_COUNT = 1007;
        private const ushort REG_TARGET_BASE = 1010;
        private const int REGS_PER_TARGET = 4;
        private const int MAX_TARGETS = 20;

        public event Action<int, bool>? CoilWriteReceived;
        public event EventHandler<bool>? ConnectionStateChanged;
        public event Action<ModbusLogEntry>? LogMessage;

        public bool IsRunning => _isRunning;
        public int Port => _config.Port;

        public ModbusSlaveService(ModbusConfig config, VisionConfig visionConfig)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _visionConfig = visionConfig ?? throw new ArgumentNullException(nameof(visionConfig));
        }

        private void Log(string msg, LogLevel level = LogLevel.Info)
        {
            AppLogger.Log(msg, level);
            LogMessage?.Invoke(new ModbusLogEntry { Time = DateTime.Now, Type = "系统", Value = msg });
        }

        // ============================================================
        // 启动 Modbus TCP 从站（NModbus 3.0.83 / NModbus.* 命名空间）
        // ============================================================
        public bool Start()
        {
            lock (_lock)
            {
                if (_isRunning) return true;
                try
                {
                    _listener = new TcpListener(IPAddress.Any, _config.Port);
                    _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    _listener.Start();

                    var factory = new ModbusFactory();

                    // ⭐ DataStore 是所有客户端共享的同一份
                    var dataStore = new DefaultSlaveDataStore();
                    _coils = dataStore.CoilDiscretes;
                    _registers = dataStore.HoldingRegisters;

                    // ---- 初始化线圈 ----
                    _coils.WritePoints((ushort)_visionConfig.TriggerCoilAddress, new bool[] { false });
                    _coils.WritePoints((ushort)_visionConfig.BusyCoilAddress, new bool[] { false });
                    _coils.WritePoints((ushort)_visionConfig.ResultCoilAddress, new bool[] { true });

                    // ---- 初始化单目标寄存器 ----
                    _registers.WritePoints((ushort)_visionConfig.ResultCodeRegister, new ushort[] { 0 });
                    _registers.WritePoints((ushort)_visionConfig.FileNameRegisterStart, new ushort[] { 0 });
                    _registers.WritePoints((ushort)(_visionConfig.FileNameRegisterStart + 1), new ushort[] { 0 });
                    _registers.WritePoints(REG_SINGLE_DIAMETER, new ushort[] { 0 });
                    _registers.WritePoints(REG_SINGLE_DEFECT, new ushort[] { 0 });
                    _registers.WritePoints(REG_SINGLE_X, new ushort[] { 0 });
                    _registers.WritePoints(REG_SINGLE_Y, new ushort[] { 0 });

                    // ---- 初始化多目标寄存器 ----
                    _registers.WritePoints(REG_TARGET_COUNT, new ushort[] { 0 });
                    for (int i = 0; i < MAX_TARGETS * REGS_PER_TARGET; i++)
                        _registers.WritePoints((ushort)(REG_TARGET_BASE + i), new ushort[] { 0 });

                    // ---- 创建 Slave + SlaveNetwork ----
                    _slave = factory.CreateSlave(_config.SlaveAddress, dataStore);
                    _slaveNetwork = factory.CreateSlaveNetwork(_listener);
                    _slaveNetwork.AddSlave(_slave);

                    _cts = new CancellationTokenSource();

                    // ⭐ 启动监听（异步，不阻塞）
                    _listenTask = _slaveNetwork.ListenAsync(_cts.Token);

                    // 触发轮询
                    _pollingTask = Task.Run(() => PollingLoop(_cts.Token), _cts.Token);

                    _isRunning = true;
                    ConnectionStateChanged?.Invoke(this, true);
                    Log($"✅ Modbus Slave 启动成功，端口: {_config.Port}");
                    return true;
                }
                catch (Exception ex)
                {
                    Log($"❌ Modbus 启动失败: {ex.Message}", LogLevel.Error);
                    _isRunning = false;
                    return false;
                }
            }
        }

        // ============================================================
        // 轮询触发线圈
        // ============================================================
        private async Task PollingLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _isRunning)
            {
                try
                {
                    await Task.Delay(50, ct);
                    if (_coils == null) continue;

                    var values = _coils.ReadPoints((ushort)_visionConfig.TriggerCoilAddress, 1);
                    if (values == null || values.Length == 0) continue;

                    bool currentValue = values[0];
                    if (currentValue && !_lastTriggerValue)
                    {
                        Log($"📨 检测到触发信号");
                        _coils.WritePoints((ushort)_visionConfig.TriggerCoilAddress, new bool[] { false });
                        CoilWriteReceived?.Invoke(_visionConfig.TriggerCoilAddress, true);
                    }
                    _lastTriggerValue = currentValue;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Log($"⚠️ 轮询异常: {ex.Message}", LogLevel.Warn); }
            }
        }

        // ============================================================
        // 停止
        // ============================================================
        public void Stop()
        {
            lock (_lock)
            {
                if (!_isRunning) return;
                try { _cts?.Cancel(); } catch { }
                try { (_slaveNetwork as IDisposable)?.Dispose(); } catch { }
                try { _listener?.Stop(); } catch { }

                _slaveNetwork = null;
                _slave = null;
                _coils = null;
                _registers = null;
                _listener = null;
                _isRunning = false;

                ConnectionStateChanged?.Invoke(this, false);
                Log("⏹ Modbus Slave 已停止");
            }
        }

        // ============================================================
        // 忙状态
        // ============================================================
        public void UpdateBusyState(bool isBusy)
        {
            if (_coils != null)
                _coils.WritePoints((ushort)_visionConfig.BusyCoilAddress, new bool[] { isBusy });
        }

        // ============================================================
        // 单目标回传
        // ============================================================
        public void UpdateStatus(bool success, int errorCode, string fileName,
            double diameterMm = 0, int defectCount = 0,
            double centerX = 0, double centerY = 0)
        {
            if (_coils == null || _registers == null) return;

            try
            {
                _coils.WritePoints((ushort)_visionConfig.ResultCoilAddress, new bool[] { success });
                _registers.WritePoints((ushort)_visionConfig.ResultCodeRegister, new ushort[] { (ushort)errorCode });

                var bytes = System.Text.Encoding.ASCII.GetBytes(fileName ?? "");
                _registers.WritePoints((ushort)_visionConfig.FileNameRegisterStart,
                    new ushort[] { (ushort)(bytes.Length > 0 ? bytes[0] : 0) });
                _registers.WritePoints((ushort)(_visionConfig.FileNameRegisterStart + 1),
                    new ushort[] { (ushort)(bytes.Length > 1 ? bytes[1] : 0) });

                _registers.WritePoints(REG_SINGLE_DIAMETER,
                    new ushort[] { (ushort)Math.Round(diameterMm * 100, MidpointRounding.AwayFromZero) });
                _registers.WritePoints(REG_SINGLE_DEFECT,
                    new ushort[] { (ushort)defectCount });
                _registers.WritePoints(REG_SINGLE_X,
                    new ushort[] { (ushort)(short)Math.Round(centerX * 100, MidpointRounding.AwayFromZero) });
                _registers.WritePoints(REG_SINGLE_Y,
                    new ushort[] { (ushort)(short)Math.Round(centerY * 100, MidpointRounding.AwayFromZero) });
            }
            catch (Exception ex)
            {
                Log($"❌ UpdateStatus 写入失败: {ex.Message}", LogLevel.Error);
            }
        }

        // ============================================================
        // 多目标回传
        // ============================================================
        public void UpdateMultiTargetStatus(bool overallPass, int targetCount, List<TargetResult> targets)
        {
            if (_coils == null || _registers == null) return;

            try
            {
                _coils.WritePoints((ushort)_visionConfig.ResultCoilAddress, new bool[] { overallPass });
                _registers.WritePoints((ushort)_visionConfig.ResultCodeRegister,
                    new ushort[] { (ushort)(overallPass ? 0 : 3) });

                if (targets.Count > 0)
                {
                    var first = targets[0];
                    _registers.WritePoints(REG_SINGLE_DIAMETER,
                        new ushort[] { (ushort)Math.Round(first.DiameterMm * 100, MidpointRounding.AwayFromZero) });
                    _registers.WritePoints(REG_SINGLE_DEFECT,
                        new ushort[] { (ushort)first.DefectCount });
                    _registers.WritePoints(REG_SINGLE_X,
                        new ushort[] { (ushort)(short)Math.Round(first.PhysicalX * 100, MidpointRounding.AwayFromZero) });
                    _registers.WritePoints(REG_SINGLE_Y,
                        new ushort[] { (ushort)(short)Math.Round(first.PhysicalY * 100, MidpointRounding.AwayFromZero) });
                }

                _registers.WritePoints(REG_TARGET_COUNT, new ushort[] { (ushort)targetCount });

                int count = Math.Min(targets.Count, MAX_TARGETS);
                for (int i = 0; i < count; i++)
                {
                    var t = targets[i];
                    ushort baseAddr = (ushort)(REG_TARGET_BASE + i * REGS_PER_TARGET);

                    _registers.WritePoints(baseAddr,
                        new ushort[] { (ushort)Math.Round(t.DiameterMm * 100, MidpointRounding.AwayFromZero) });
                    _registers.WritePoints((ushort)(baseAddr + 1),
                        new ushort[] { (ushort)(short)Math.Round(t.PhysicalX * 100, MidpointRounding.AwayFromZero) });
                    _registers.WritePoints((ushort)(baseAddr + 2),
                        new ushort[] { (ushort)(short)Math.Round(t.PhysicalY * 100, MidpointRounding.AwayFromZero) });
                    _registers.WritePoints((ushort)(baseAddr + 3),
                        new ushort[] { (ushort)(t.IsPass ? 1 : 0) });
                }
            }
            catch (Exception ex)
            {
                Log($"❌ UpdateMultiTargetStatus 写入失败: {ex.Message}", LogLevel.Error);
            }
        }

        // ============================================================
        // 释放资源
        // ============================================================
        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}