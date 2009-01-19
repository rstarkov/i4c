using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using RT.Util;
using RT.Util.Controls;
using RT.Util.Dialogs;
using RT.Util.ExtensionMethods;
using RT.Util.XmlClassify;

namespace i4c
{
    public partial class MainForm: Form
    {
        public List<Task> Tasks = new List<Task>()
        {
            new CodecI4cAlpha("alpha"),
        };

        public static MainForm TheInstance;

        public class Settings
        {
            public Dictionary<string, string> LastArgs = new Dictionary<string, string>();
        }

        public MainForm()
        {
            TheInstance = this;
            InitializeComponent();
            tabsMain.TabPages.Clear();

            foreach (var task in Tasks)
                miTask.DropDownItems.Add(task.Name, null, new EventHandler(Task_Click));
            miTask.DropDownItems.Remove(miTaskExit);
            miTask.DropDownItems.Add(miTaskExit);

            LoadSettings();
        }

        void SaveSettings()
        {
            var st = new Settings();
            foreach (var task in Tasks)
                st.LastArgs.Add(task.Name, task.LastArgs);
            XmlClassify.SaveObjectToXmlFile(st, PathUtil.AppPath + "i4c-settings.xml");
        }

        void LoadSettings()
        {
            Settings st;
            if (File.Exists(PathUtil.AppPath + "i4c-settings.xml"))
                st = XmlClassify.LoadObjectFromXmlFile<Settings>(PathUtil.AppPath + "i4c-settings.xml");
            else
                st = new Settings();

            foreach (var task in Tasks)
                if (st.LastArgs.ContainsKey(task.Name))
                    task.LastArgs = st.LastArgs[task.Name];
        }

        private void Task_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem mi = (ToolStripMenuItem)sender;
            Task task = Tasks.Find(t => t.Name == mi.Text);
            var input = InputBox.GetLine("Please enter the arguments for this task:", task.LastArgs);
            if (input == null)
                return;
            task.LastArgs = input;
            SaveSettings();
            task.Process(task.LastArgs.Split(' '));
        }

        private void miTaskExit_Click(object sender, EventArgs e)
        {
            Close();
        }

        public static void AddImage(IntField image, string caption)
        {
            AddImage(image, image.Data.Min(), image.Data.Max(), caption);
        }

        public static void AddImage(IntField image, int min, int max, string caption)
        {
            IntField temp = image.Clone();
            temp.ArgbFromField(min, max);
            AddImage(temp.ArgbToBitmap(), caption);
        }

        public static void AddImage(Bitmap image, string caption)
        {
            TabPage page = new TabPage();
            page.Text = (TheInstance.tabsMain.TabCount + 1) + (caption==null ? "" : (": " + caption));
            TheInstance.tabsMain.TabPages.Add(page);

            DoubleBufferedPanel panel = new DoubleBufferedPanel();
            panel.Dock = DockStyle.Fill;
            panel.Tag = image;
            panel.PaintBuffer += new PaintEventHandler(TheInstance.panel_PaintBuffer);
            page.Controls.Add(panel);

            panel.Refresh();
            TheInstance.tabsMain.SelectedTab = page;
        }

        void panel_PaintBuffer(object sender, PaintEventArgs e)
        {
            DoubleBufferedPanel panel = (DoubleBufferedPanel)sender;
            Bitmap image = (Bitmap)panel.Tag;
            e.Graphics.Clear(Color.DarkBlue);
            int x = panel.Width/2 - image.Width/2;
            int y = panel.Height/2 - image.Height / 2;
            e.Graphics.DrawImageUnscaled(image, x < 0 ? 0 : x, y < 0 ? 0 : y);
        }

    }

    public abstract class Task
    {
        public string Name;
        public string LastArgs = "";
        public abstract void Process(params string[] args);
    }

    public abstract class CodecBase: Task
    {
        public override void Process(params string[] args)
        {
            string filename = args[0];
            string basename = Path.GetFileNameWithoutExtension(filename);
            Configure(args.Skip(1).ToArray());
            if (filename.ToLower().EndsWith(".png"))
            {
                IntField f = new IntField(0, 0);
                f.ArgbLoadFromFile(filename);
                f.ArgbTo4c();
                FileStream output = File.Open(basename + "." + Suffix + ".i4c", FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
                Encode(f, output);
                output.Close();
            }
            else
            {
                FileStream input = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
                IntField f = Decode(input);
                f.ArgbFromField(0, 3);
                f.ArgbToBitmap().Save(Path.GetFileNameWithoutExtension(basename) + ".decoded.png", ImageFormat.Png);
            }
        }

        public abstract string Suffix { get; }
        public abstract void Configure(params string[] args);
        public abstract void Encode(IntField image, Stream output);
        public abstract IntField Decode(Stream input);
    }

    public class CodecI4cAlpha: CodecBase
    {
        public Foreseer Seer;
        private string _config;

        public CodecI4cAlpha(string name)
        {
            Name = name;
        }

        public override string Suffix
        {
            get { return Name + "-" + _config; }
        }

        public override void Configure(params string[] args)
        {
            int w = args.Length > 0 ? int.Parse(args[0]) : 7;
            int h = args.Length > 1 ? int.Parse(args[1]) : 7;
            int x = args.Length > 2 ? int.Parse(args[2]) : 3;
            Seer = new FixedSizeForeseer(w, h, x, new HorzVertForeseer());
            _config = w + "" + h + x;
        }

        public override void Encode(IntField image, Stream output)
        {
            image.PredictionEnTransformDiff(Seer, 4);
            Fieldcode fc = new Fieldcode();
            byte[] bytes = fc.EnFieldcode(image, 1024);
            output.Write(bytes, 0, bytes.Length);
        }

        public override IntField Decode(Stream input)
        {
            Fieldcode fc = new Fieldcode();
            IntField f = fc.DeFieldcode(input.ReadAllBytes(), 1024);
            f.PredictionDeTransformDiff(Seer, 4);
            return f;
        }
    }
}
