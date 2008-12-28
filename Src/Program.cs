using System;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace i4c
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (false)
            {
                Application.Run(new MainForm());
            }
            else
            {
                string filename = args[0];
                string basename = Path.GetFileNameWithoutExtension(filename);
                if (filename.ToLower().EndsWith(".png"))
                {
                    IntField f = new IntField(0, 0);
                    f.ArgbLoadFromFile(filename);
                    f.ArgbTo4c();
                    f.PredictionEnTransform(new FixedSizeForeseer(7, 7, 3, new HorzVertForeseer()));
                    Fieldcode fc = new Fieldcode();
                    byte[] bytes = fc.EnFieldcode(f, 1024, basename + ".field.{0}{1}.png");

                    File.WriteAllBytes(basename + ".i4c", bytes);

                    f.ArgbFromField(-3, 3);
                    f.ArgbToBitmap().Save(basename + ".transform.png", ImageFormat.Png);
                }
                else
                {
                    var bytes = File.ReadAllBytes(filename);
                    IntField f = new IntField(1024, 768);
                    Fieldcode fc = new Fieldcode();
                    fc.DeFieldcode(bytes, f, 1024);
                    f.PredictionDeTransform(new FixedSizeForeseer(7, 7, 3, new HorzVertForeseer()));

                    f.ArgbFromField(0, 3);
                    f.ArgbToBitmap().Save(basename + ".decoded.png", ImageFormat.Png);
                }
            }
        }
    }
}
