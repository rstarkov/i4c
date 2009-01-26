using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RT.Util.Controls;
using RT.Util.Dialogs;
using System.Threading;
using RT.Util.Collections;

namespace i4c
{
    public partial class MainForm: Form
    {
        public static MainForm TheInstance;
        public static Queue<Tuple<IntField, string>> Images = new Queue<Tuple<IntField, string>>();

        public class Settings
        {
            public Dictionary<string, string> LastArgs = new Dictionary<string, string>();
        }

        public MainForm()
        {
            TheInstance = this;
            InitializeComponent();
            tabsMain.TabPages.Clear();

            foreach (var compr in Program.Compressors.Keys)
                miCompr.DropDownItems.Add("&" + compr, null, new EventHandler(Compressor_Click));
        }

        private void Compressor_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem mi = (ToolStripMenuItem)sender;
            Compressor compr = Program.GetCompressor(mi.Text.Replace("&", ""));
            var input = InputBox.GetLine("Please enter the arguments for this compressor:", "");
            if (input == null)
                return;
            var args = input.Split(' ');
            Func<Compressor, string, string[], WaitCallback> makeCallback = (_compr, _fname, _args) => (dummy => _compr.Process(_fname, _args));
            ThreadPool.QueueUserWorkItem(makeCallback(compr, args[0], args.Skip(1).ToArray()));
        }

        public static void AddImageTab(Bitmap image, string caption)
        {
            if (TheInstance == null) // we're running in command line mode
                return;

            lock (Images)
            {
                Images.Enqueue(new Tuple<IntField, string>(new IntField(image), caption));
            }
        }

        void panel_PaintBuffer(object sender, PaintEventArgs e)
        {
            DoubleBufferedPanel panel = (DoubleBufferedPanel)sender;
            Bitmap image = (Bitmap)panel.Tag;
            e.Graphics.Clear(Color.DarkBlue);
            int x = panel.Width/2 - image.Width/2;
            int y = panel.Height/2 - image.Height/2;
            e.Graphics.DrawImageUnscaled(image, x < 0 ? 0 : x, y < 0 ? 0 : y);
        }

        private void timerStatus_Tick(object sender, EventArgs e)
        {
            int worker, dummy;
            ThreadPool.GetAvailableThreads(out worker, out dummy);
            if (worker < Environment.ProcessorCount)
            {
                lblStatus.Text = "Busy";
                lblStatus.BackColor = Color.DarkRed;
                lblStatus.ForeColor = Color.White;
            }
            else
            {
                lblStatus.Text = "Ready";
                lblStatus.BackColor = SystemColors.Control;
                lblStatus.ForeColor = SystemColors.WindowText;
            }

            lock (Images)
            {
                while (Images.Count > 0)
                {
                    var data = Images.Dequeue();

                    TabPage page = new TabPage();
                    page.Text = (TheInstance.tabsMain.TabCount + 1) + (data.E2==null ? "" : (": " + data.E2));
                    TheInstance.tabsMain.TabPages.Add(page);
                    TheInstance.tabsMain.Visible = true;

                    DoubleBufferedPanel panel = new DoubleBufferedPanel();
                    panel.Dock = DockStyle.Fill;
                    panel.Tag = data.E1.ArgbToBitmap();
                    panel.PaintBuffer += new PaintEventHandler(TheInstance.panel_PaintBuffer);
                    page.Controls.Add(panel);

                    panel.Refresh();
                    TheInstance.tabsMain.SelectedTab = page;
                }
            }
        }
    }
}
