# 🎯 HalconVisionServer (GTS 视觉服务器)

![C#](https://img.shields.io/badge/C%23-.NET%20Framework%20%7C%20.NET%206+-512BD4?style=flat-square&logo=c-sharp)
![WinForms](https://img.shields.io/badge/UI-WinForms-0078D7?style=flat-square&logo=windows)
![HALCON](https://img.shields.io/badge/Vision-HALCON-005A9C?style=flat-square)
![OpenCV](https://img.shields.io/badge/Camera-OpenCvSharp-5C3EE8?style=flat-square&logo=opencv)
![Modbus](https://img.shields.io/badge/Protocol-Modbus%20TCP-orange?style=flat-square)

基于 **C# WinForms + HALCON + OpenCvSharp** 开发的工业级 AOI（自动光学检测）视觉服务端应用。主要用于工业产线上的工件尺寸测量、缺陷判定，并通过 **Modbus TCP** 与 PLC 进行高效的自动化通信。

---

## ✨ 核心功能

- **📷 实时图像采集与预览**：基于 OpenCvSharp 驱动工业相机，后台采集线程与 UI 渲染解耦，加入帧率节流与宽高比自适应，确保预览流畅不卡顿。
- **🔍 HALCON 工业视觉算法**：
  - Canny 亚像素边缘提取。
  - 基于轮廓长度、圆度（Circularity）和边缘点数密度的多重鲁棒筛选。
  - 鲁棒圆拟合 (`FitCircleContourXld`) 计算高精度圆心与直径。
  - 支持手眼标定矩阵（HomMat2D），将像素坐标转换为物理坐标（mm）。
- **🛡️ 多帧中位数测量机制**：单次触发连续采集 5 帧，取中位数作为最终结果，极大降低了工业现场震动或光照波动带来的误差。
- **🔌 Modbus TCP 从站集成**：
  - 实时轮询接收 PLC 的触发信号（线圈）。
  - 高速回传检测结果（OK/NG）、直径、物理 X/Y 坐标、缺陷数。
  - 支持多目标（Multi-Target）场景下的结果批量回传。
- **📊 重复性测试 (GR&R)**：支持一键连续测量 50 次，自动生成包含极差、标准差（精确到微米级）的统计报告，辅助评估视觉系统稳定性。
- **📝 高性能异步日志**：基于 `System.Threading.Channels` 实现的无锁非阻塞日志组件，支持按天/按大小自动滚动切分（Rollover）与过期清理。

---

## 🛠️ 技术栈

*   **开发语言**：C# (.NET Framework 4.7.2+ / .NET 6+)
*   **UI 框架**：Windows Forms (WinForms)
*   **视觉底层**：MVTec HALCON
*   **相机驱动**：OpenCvSharp4
*   **通信协议**：NModbus (Modbus TCP)
*   **日志系统**：自定义 `AppLogger` (基于 Channel)

---

## ⚠️ 环境要求

> [!WARNING]  
> **关于 HALCON 依赖**：由于 MVTec HALCON 为商业授权软件，本仓库**不包含**任何 HALCON 的二进制文件（如 `halcondotnet.dll` 等）及授权文件（`license.dat`）。

1.  **HALCON**：需自行安装 MVTec HALCON 并配置环境变量（推荐 HALCON 20.11 及以上版本）。
2.  **Visual Studio**：推荐 VS 2019 或 VS 2022，需安装 ".NET 桌面开发" 工作负载。
3.  **NuGet 包**：项目依赖以下 NuGet 包，克隆后请在 VS 中执行“还原 NuGet 包”：
    *   `OpenCvSharp4`
    *   `OpenCvSharp4.Extensions`
    *   `OpenCvSharp4.runtime.win`
    *   `NModbus`

---

## 🚀 快速开始

### 1. 克隆仓库
```bash
git clone https://github.com/Littleoldier/HalconVisionServer.git
```

### 2. 打开解决方案
使用 Visual Studio 打开项目根目录下的 `HalconVisionServer.sln`。

### 3. 添加 HALCON 引用
在你的本地项目中，手动添加对你本地 HALCON 安装目录下的 `halcondotnet.dll` 的引用。

### 4. 编译并运行
按 `F5` 启动项目。程序启动后会自动加载 `vision_config.json`（如无则创建默认配置），并启动 Modbus TCP 监听（默认端口 503）。

---

## 📂 目录结构

```text
HalconVisionServer/
├── Forms/                  # 主窗体 UI (HalconVisionServerForm)
├── Services/               # 核心业务逻辑
│   ├── CameraService.cs    # 相机采集、事件分发 (OpenCvSharp)
│   ├── ModbusSlaveService.cs# Modbus TCP 服务端及寄存器读写
│   ├── VisionController.cs # 核心视觉算法闭环与状态机
│   └── VisionConfig.cs     # 参数配置与持久化 (JSON)
├── Utils/                  # 通用工具
│   └── AppLogger.cs        # 异步文件日志组件
└── Program.cs              # 程序入口
```

---

## ⚙️ 配置与 Modbus 寄存器说明

项目根目录下的 `vision_config.json` 用于保存运行参数，核心字段如下：

| 字段名 | 说明 | 默认值 |
| :--- | :--- | :--- |
| `ModbusPort` | Modbus TCP 监听端口 | `503` |
| `CalibrationFactor` | 像素当量（毫米/像素） | `0.1139` |
| `StandardDiameterMm` | 工件标准直径 (mm) | `20.0` |
| `ToleranceMm` | 公差范围 (±mm) | `0.5` |
| `HomMat2D` | 2x3 手眼标定矩阵 (像素转物理坐标) | `[0.1139, 0, -50, 0, 0.1139, -50]` |

### 默认 Modbus 地址映射

<details>
<summary>👉 点击展开查看 Modbus 地址表</summary>

**线圈 (Coils)**

*   `100` (读/写): PLC 触发拍照信号（上升沿有效）。
*   `101` (读): 视觉系统 Busy 状态。
*   `102` (读): 最终检测结果 (True=OK, False=NG)。

**保持寄存器 (Holding Registers)**

*   `1000` (读): 错误码。
*   `1003` (读): 测量直径 (乘100，例: 2050 表示 20.50 mm)。
*   `1004` (读): 缺陷数量。
*   `1005` (读): 物理 X 坐标 (乘100，支持负数)。
*   `1006` (读): 物理 Y 坐标 (乘100，支持负数)。

</details>

---

## 📄 免责声明与 License

> **注意：**
> - 本项目仅供学习、交流和技术演示使用。
> - 使用本代码进行商业生产前，请确保您已获得 **MVTec HALCON** 的合法商业授权。
> 
> 本项目基于 [MIT License](LICENSE) 开源。