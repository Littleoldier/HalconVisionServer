using HalconVisionServer.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HalconVisionServer.Services
{
    /// <summary>
    /// 单个配方：一套完整的工件检测参数
    /// </summary>
    public class Recipe
    {
        public string Name { get; set; } = "";
        public double CalibrationFactor { get; set; } = 0.1113;
        public double StandardDiameterMm { get; set; } = 20.0;
        public double ToleranceMm { get; set; } = 0.2;
        public string OcrModelPath { get; set; } = "";   // OCR 模型路径
    }

    public class VisionConfig
    {
        // ==================== Modbus 配置 ====================
        public int ModbusPort { get; set; } = 503;
        public byte SlaveAddress { get; set; } = 1;
        public int TriggerCoilAddress { get; set; } = 100;
        public int BusyCoilAddress { get; set; } = 101;
        public int ResultCoilAddress { get; set; } = 102;
        public int ResultCodeRegister { get; set; } = 1000;
        public int FileNameRegisterStart { get; set; } = 1001;

        // ==================== 相机配置 ====================
        public int DefaultCameraIndex { get; set; } = 0;
        public int CameraWidth { get; set; } = 640;
        public int CameraHeight { get; set; } = 480;

        // ==================== 图像保存配置 ====================
        public string ImageSavePath { get; set; } = ".\\Images";
        public string ImagePrefix { get; set; } = "VISION_";
        public int CaptureTimeoutMs { get; set; } = 5000;

        // ==================== 算法参数（当前生效值） ====================
        public int ThresholdMin { get; set; } = 0;
        public int ThresholdMax { get; set; } = 80;
        public double CalibrationFactor { get; set; } = 0.1113;
        public double StandardDiameterMm { get; set; } = 20.0;
        public double ToleranceMm { get; set; } = 0.2;
        public string OcrModelPath { get; set; } = "";

        // ==================== 手眼标定矩阵（HomMat2D） ====================
        public double[] HomMat2D { get; set; } = new double[] { 0.1113, 0, -50, 0, 0.1113, -50 };

        // ==================== 配方管理 ====================
        public List<Recipe> Recipes { get; set; } = new List<Recipe>();
        public string ActiveRecipeName { get; set; } = "";

        // 运行时便捷属性（不序列化）
        [JsonIgnore]
        public Recipe? ActiveRecipe => Recipes.FirstOrDefault(r => r.Name == ActiveRecipeName);

        // ==================== 加载/保存 ====================
        private static string ConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vision_config.json");

        public static VisionConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var config = JsonSerializer.Deserialize<VisionConfig>(json);
                    if (config != null)
                    {
                        // 如果没有配方，创建默认配方
                        if (config.Recipes == null || config.Recipes.Count == 0)
                        {
                            config.Recipes = new List<Recipe>
                            {
                                new Recipe
                                {
                                    Name = "默认配方",
                                    CalibrationFactor = config.CalibrationFactor,
                                    StandardDiameterMm = config.StandardDiameterMm,
                                    ToleranceMm = config.ToleranceMm,
                                    OcrModelPath = config.OcrModelPath
                                }
                            };
                            config.ActiveRecipeName = config.Recipes[0].Name;
                        }
                        // 如果 ActiveRecipeName 为空，取第一个配方
                        if (string.IsNullOrEmpty(config.ActiveRecipeName) && config.Recipes.Count > 0)
                            config.ActiveRecipeName = config.Recipes[0].Name;
                        // 应用激活配方到便捷字段
                        config.ApplyActiveRecipe();
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                try { AppLogger.Warn($"加载配置文件失败: {ex.Message}"); } catch { }
            }

            // 创建默认配置
            var defaultConfig = new VisionConfig();
            defaultConfig.Recipes = new List<Recipe>
            {
                new Recipe
                {
                    Name = "默认配方",
                    CalibrationFactor = 0.1113,
                    StandardDiameterMm = 20.0,
                    ToleranceMm = 0.2,
                    OcrModelPath = ""
                }
            };
            defaultConfig.ActiveRecipeName = defaultConfig.Recipes[0].Name;
            defaultConfig.ApplyActiveRecipe();
            defaultConfig.Save();
            return defaultConfig;
        }

        /// <summary>
        /// 将激活配方的参数复制到当前生效字段
        /// </summary>
        public void ApplyActiveRecipe()
        {
            var recipe = ActiveRecipe;
            if (recipe == null) return;
            CalibrationFactor = recipe.CalibrationFactor;
            StandardDiameterMm = recipe.StandardDiameterMm;
            ToleranceMm = recipe.ToleranceMm;
            OcrModelPath = recipe.OcrModelPath;
        }

        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                try { AppLogger.Error($"保存配置文件失败: {ex.Message}"); } catch { }
            }
        }
    }
}