using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using RT.KitchenSink.Collections;
using RT.Util.Controls;
using RT.Util.Dialogs;
using RT.Util.Forms;
using RT.Util.ExtensionMethods;

namespace i4c
{
    public partial class MainForm : ManagedForm
    {
        public static MainForm TheInstance;
        public static Queue<Tuple<string, IntField>> Images = new Queue<Tuple<string, IntField>>();

        public MainForm()
            : base(Program.Settings.MainFormSettings)
        {
            TheInstance = this;
            InitializeComponent();
            tabsMain.TabPages.Clear();

            foreach (var compr in Program.Compressors.Keys)
                miCompr.DropDownItems.Add("&" + compr, null, new EventHandler(Compressor_Click));
        }

        private void Compressor_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem mi = (ToolStripMenuItem) sender;
            var compressorName = mi.Text.Replace("&", "");
            Compressor compr = Program.GetCompressor(compressorName);
            var input = InputBox.GetLine("Please enter the arguments for this compressor:", Program.Settings.LastArgs.Get(compressorName, ""));
            if (input == null)
                return;
            Program.Settings.LastArgs[compressorName] = input;
            Program.Settings.Save();
            var args = input.Split(' ');
            compr.Configure(args.Skip(1).Select(val => (RVariant) val).ToArray());
            ThreadPool.QueueUserWorkItem(dummy => Program.CompressDecompressSingle(compr, args[0]));
        }

        public static void AddImageTab(IntField argb, string caption)
        {
            if (TheInstance == null) // we're running in command line mode
                return;

            lock (Images)
            {
                Images.Enqueue(new Tuple<string, IntField>(caption, argb));
            }
        }

        void panel_PaintBuffer(object sender, PaintEventArgs e)
        {
            DoubleBufferedPanel panel = (DoubleBufferedPanel) sender;
            Bitmap image = (Bitmap) panel.Tag;
            e.Graphics.Clear(Color.DarkBlue);
            int x = panel.Width / 2 - image.Width / 2;
            int y = panel.Height / 2 - image.Height / 2;
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
                    page.Text = (TheInstance.tabsMain.TabCount + 1) + (data.Item1 == null ? "" : (": " + data.Item1));
                    TheInstance.tabsMain.TabPages.Add(page);
                    TheInstance.tabsMain.Visible = true;

                    DoubleBufferedPanel panel = new DoubleBufferedPanel();
                    panel.Dock = DockStyle.Fill;
                    panel.Tag = data.Item2.ArgbToBitmap();
                    panel.PaintBuffer += new PaintEventHandler(TheInstance.panel_PaintBuffer);
                    page.Controls.Add(panel);

                    panel.Refresh();
                    TheInstance.tabsMain.SelectedTab = page;
                }
            }
        }
    }
}
