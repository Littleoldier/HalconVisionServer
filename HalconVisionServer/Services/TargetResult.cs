using System;

namespace HalconVisionServer.Services
{
    /// <summary>
    /// 单个目标的检测结果
    /// 用于多目标检测场景下，向 Modbus 从站回传每个目标的数据
    /// </summary>
    public class TargetResult
    {
        /// <summary>
        /// 目标序号（从 1 开始）
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// 直径（毫米）
        /// </summary>
        public double DiameterMm { get; set; }

        /// <summary>
        /// 物理 X 坐标（毫米，手眼标定后）
        /// </summary>
        public double PhysicalX { get; set; }

        /// <summary>
        /// 物理 Y 坐标（毫米，手眼标定后）
        /// </summary>
        public double PhysicalY { get; set; }

        /// <summary>
        /// 像素坐标 X（列）
        /// </summary>
        public double PixelX { get; set; }

        /// <summary>
        /// 像素坐标 Y（行）
        /// </summary>
        public double PixelY { get; set; }

        /// <summary>
        /// 半径（像素）
        /// </summary>
        public double RadiusPx { get; set; }

        /// <summary>
        /// 单个目标是否合格（直径在公差范围内）
        /// </summary>
        public bool IsPass { get; set; }

        /// <summary>
        /// 缺陷数（可选，默认 0）
        /// </summary>
        public int DefectCount { get; set; } = 0;
    }
}