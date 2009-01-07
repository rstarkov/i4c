using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace i4c
{
    public partial class MainForm: Form
    {
        private Bitmap bmpDisplay = new Bitmap(8, 8);

        public MainForm()
        {
            InitializeComponent();

            //CodecTests.TestRandom();
        }

        private void miActionA1_Click(object sender, EventArgs e)
        {
            IntField f = new IntField(0, 0);
            f.ArgbLoadFromFile("scr7.png");
            f.ArgbTo4c();
            f.PredictionEnTransform(new HorzVertForeseer());
            f.PredictionDeTransform(new HorzVertForeseer());
            //f.PredictionEnTransform(new FixedSizeForeseer(7, 7, 3, new HorzVertForeseer()));
            //f.PredictionDeTransform(new FixedSizeForeseer(7, 7, 3, new HorzVertForeseer()));
            ViewImage(f);
            //Fieldcode fc = new Fieldcode();
            //byte[] bytes = fc.EnFieldcode(f, 1024, basename + ".field.{0}{1}.png");

            //File.WriteAllBytes(basename + ".i4c", bytes);

            //f.ArgbFromField(-3, 3);
            //f.ArgbToBitmap().Save(basename + ".transform.png", ImageFormat.Png);
        }

        private void miActionA2_Click(object sender, EventArgs e)
        {
        }

        IntField ViewImage(string name)
        {
            IntField f = new IntField(0, 0);
            f.ArgbLoadFromFile(name);
            f.ArgbTo4c();
            return ViewImage(f);
        }

        IntField ViewImage(IntField image)
        {
            IntField f = image.Clone();
            IntField result = f.Clone();
            f.ArgbFromField(0, 3);
            bmpDisplay = f.ArgbToBitmap();
            pnl.Refresh();
            return result;
        }

        IntField ViewPrediction(IntField image, Foreseer foreseer)
        {
            IntField f = image.Clone();
            f.Prediction(foreseer);
            IntField result = f.Clone();
            f.ArgbFromField(0, 3);
            bmpDisplay = f.ArgbToBitmap();
            pnl.Refresh();
            return result;
        }

        IntField ViewTransformed(IntField image, Foreseer foreseer)
        {
            IntField f = image.Clone();
            f.PredictionEnTransform(foreseer);
            IntField result = f.Clone();
            f.ArgbFromField(-3, 3);
            bmpDisplay = f.ArgbToBitmap();
            pnl.Refresh();
            return result;
        }

        void InfoToCsv(CsvTable tbl, int perfectTotal, List<ulong[]> probs)
        {
            tbl.AdvanceRight = true;
            int leftCol = tbl.CurCol;
            int longest = probs.Select(arr => arr.Length).Max();
            tbl.Add(perfectTotal);
            tbl.SetCursor(tbl.CurRow + 1, leftCol);
            tbl.Add("-3"); tbl.Add("-2"); tbl.Add("-1"); tbl.Add("1"); tbl.Add("2"); tbl.Add("3");
            for (int i = 0; i < longest; i++)
            {
                tbl.SetCursor(tbl.CurRow+1, leftCol);
                foreach (var prob in probs)
                {
                    if (i < prob.Length)
                        tbl.Add(prob[i]);
                    else
                        tbl.AdvanceCursor();
                }
            }
        }

        private void pnl_PaintBuffer(object sender, PaintEventArgs e)
        {
            e.Graphics.Clear(Color.Black);
            e.Graphics.DrawImageUnscaled(bmpDisplay, 0, 0);
        }

    }

}
