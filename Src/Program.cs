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
            //CodecTests.TestLzw();
            //args = new string[] { "scr6lzw.png" };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 0)
            {
                Application.Run(new MainForm());
            }
            else
            {
                string filename = args[0];
                string basename = Path.GetFileNameWithoutExtension(filename);
                Foreseer seer = new FixedSizeForeseer(7, 7, 3, new HorzVertForeseer());
                if (filename.ToLower().EndsWith(".png"))
                {
                    IntField f = new IntField(0, 0);
                    f.ArgbLoadFromFile(filename);
                    f.ArgbTo4c();
                    f.PredictionEnTransform(seer);
                    Fieldcode fc = new Fieldcode();
                    byte[] bytes = fc.EnFieldcode(f, 1024, basename + ".field.{0}.png");

                    File.WriteAllBytes(basename + ".i4c", bytes);

                    f.ArgbFromField(0, 3);
                    f.ArgbToBitmap().Save(basename + ".transform.png", ImageFormat.Png);
                }
                else
                {
                    var bytes = File.ReadAllBytes(filename);
                    Fieldcode fc = new Fieldcode();
                    IntField f = fc.DeFieldcode(bytes, 1024);
                    f.PredictionDeTransform(seer);

                    f.ArgbFromField(0, 3);
                    f.ArgbToBitmap().Save(basename + ".decoded.png", ImageFormat.Png);
                }
            }
        }
    }
}
