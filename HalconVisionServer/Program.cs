using HalconVisionServer.Forms;
using HalconVisionServer.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HalconVisionServer
{
    internal static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppLogger.Initialize("VisionLogs");
            AppLogger.Info("GTS 视觉服务器 v1.0 启动");

            var form = new HalconVisionServerForm(); // 这里要对应窗体类的名字
            Application.Run(form);
        }
    }
}
