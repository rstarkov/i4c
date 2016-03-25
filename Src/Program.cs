using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Permissions;
using System.Threading;
using System.Windows.Forms;
using RT.KitchenSink;
using RT.KitchenSink.Collections;
using RT.Util;
using RT.Util.Dialogs;
using RT.Util.ExtensionMethods;
using RT.Util.Text;

namespace i4c
{
    [SecurityPermission(SecurityAction.Demand, Flags = SecurityPermissionFlag.UnmanagedCode)]
    static class Program
    {
        public static Settings Settings;

        /// <summary>
        /// Define any new compressors in here and they will be accessible by the name
        /// via the GUI menu or the command line.
        /// </summary>
        public static Dictionary<string, Type> Compressors = new Dictionary<string, Type>()
        {
            { "alpha", typeof(I4cAlpha) },
            { "bravo", typeof(I4cBravo) },
            { "delta", typeof(I4cDelta) },
            { "echo", typeof(I4cEcho) },
            { "foxtrot", typeof(I4cFoxtrot) },
            { "timwi-xor", typeof(TimwiCecXor) },
            { "timwi-cec", typeof(TimwiCecPredictive) },
            { "xp-rects", typeof(XperimentRects) },
            { "xp-pdiff", typeof(XperimentPdiff) },
            { "xp-split", typeof(XperimentSplit) },
            { "xp-backlzw", typeof(XperimentBackLzw) },
            { "xp-resolutions", typeof(XperimentResolution) },
        };

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            //CodecTests.TestRandom();
            PatternHashTable.SelfTest();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            int worker, dummy;
            ThreadPool.SetMinThreads(Environment.ProcessorCount, 10);
            ThreadPool.SetMaxThreads(Environment.ProcessorCount, 10);
            ThreadPool.GetAvailableThreads(out worker, out dummy);
            System.Diagnostics.Debug.Assert(worker == Environment.ProcessorCount);


            if (args.Length == 0)
            {
                SettingsUtil.LoadSettings(out Settings);
                MainForm form = new MainForm();
                Application.Run(form);
                Settings.Save();
            }
            else if (args[0] == "?")
            {
                DlgMessage.ShowInfo("Usage:\n  i4c.exe - run interactive GUI.\n  i4c.exe <algorithm> <filename> [<arg1> <arg2> ...] - compress/decompress single file.\n  i4c.exe benchmark <algorithm> [<arg1> <arg2> ...] - benchmark on all files in cur dir.\n\nAvailable algorithms:\n\n" + Compressors.Keys.Select(compr => "* " + compr + "\n").JoinString());
            }
            else if (args[0] == "benchmark")
            {
                if (args.Length == 1)
                {
                    DlgMessage.ShowError("Must also specify the name of the algorithm to benchmark");
                    return;
                }
                command_Benchmark(args[1], args.Skip(2).Select(val => (RVariant) val).ToArray());
            }
            else
            {
                if (args.Length < 2)
                {
                    DlgMessage.ShowError("Must specify at least the algorithm name and the file name");
                    return;
                }
                command_Single(args[1], args[0], args.Skip(2).Select(val => (RVariant) val).ToArray());
            }
        }

        /// <summary>
        /// Creates a new compressor of a specified name. Use to obtain an actual compressor
        /// instance given an algorithm name.
        /// </summary>
        public static Compressor GetCompressor(string name)
        {
            if (Compressors.ContainsKey(name))
            {
                Compressor compr = (Compressor) Compressors[name].GetConstructor(new Type[] { }).Invoke(new object[] { });
                compr.Name = name;
                return compr;
            }
            else
            {
                DlgMessage.ShowError("Compressor named {0} is not defined".Fmt(name));
                Environment.Exit(1);
                return null;
            }
        }

        /// <summary>
        /// Processes the "compress/decompress single file" command-line command.
        /// </summary>
        private static void command_Single(string algName, string filename, RVariant[] algArgs)
        {
            Compressor compr = GetCompressor(algName);
            if (compr == null)
                return;

            WaitFormShow("processing...");

            compr.Configure(algArgs);
            CompressDecompressSingle(compr, filename);

            WaitFormHide();
        }

        /// <summary>
        /// Processes the "benchmark algorithm on all files" command-line command.
        /// </summary>
        private static void command_Benchmark(string algName, RVariant[] algArgs)
        {
            WaitFormShow("benchmarking...");

            Dictionary<string, Compressor> compressors = new Dictionary<string, Compressor>();
            foreach (var file in Directory.GetFiles(".", "*.png"))
                compressors.Add(file, GetCompressor(algName));

            // Queue all jobs...
            foreach (var file in compressors.Keys)
            {
                Compressor compr = compressors[file];
                compr.Configure(algArgs);
                compr.CanonicalFileName = Path.GetFileNameWithoutExtension(file);
                string sourcePath = file;
                string destDir = PathUtil.AppPathCombine("i4c-output", "benchmark.{0},{1}".Fmt(algName, compr.ConfigString), compr.CanonicalFileName);
                string destFile = "{0}.{1},{2}.i4c".Fmt(compr.CanonicalFileName, algName, compr.ConfigString);

                Func<Compressor, string, string, string, WaitCallback> makeCallback =
                    (v1, v2, v3, v4) => (dummy2 => CompressFile(v1, v2, v3, v4));
                ThreadPool.QueueUserWorkItem(makeCallback(compr, sourcePath, destDir, destFile));
            }
            // ...and wait until they're finished.
            int worker = 0, dummy;
            while (worker < Environment.ProcessorCount)
            {
                Thread.Sleep(200);
                ThreadPool.GetAvailableThreads(out worker, out dummy);
            }

            // Compute stats totals
            Dictionary<string, double> totals = new Dictionary<string, double>();
            foreach (var file in compressors.Keys)
            {
                var counters = compressors[file].Counters;
                foreach (var key in counters.Keys)
                {
                    if (totals.ContainsKey(key))
                        totals[key] += counters[key];
                    else
                        totals.Add(key, counters[key]);
                }
            }

            // Write stats totals to a text file
            TextTable table = new TextTable { ColumnSpacing = 3, MaxWidth = int.MaxValue, DefaultAlignment = HorizontalTextAlignment.Right };
            table.SetCell(1, 0, "TOTAL");
            int colnum = 2;
            int rownum = 2;
            foreach (var str in compressors.Values.Select(val => val.CanonicalFileName).Order())
                table.SetCell(colnum++, 0, str);
            int indent_prev = 0;
            foreach (var key in totals.Keys.Order())
            {
                int indent = 4 * key.ToCharArray().Count(c => c == '|');
                if (indent < indent_prev)
                    rownum++;
                indent_prev = indent;
                table.SetCell(0, rownum, new string(' ', indent) + key.Split('|').Last(), alignment: HorizontalTextAlignment.Left);
                table.SetCell(1, rownum, Math.Round(totals[key], 3).ToString("#,0"));
                colnum = 2;
                foreach (var compr in compressors.Values.OrderBy(c => c.CanonicalFileName))
                    if (compr.Counters.ContainsKey(key))
                        table.SetCell(colnum++, rownum, Math.Round(compr.Counters[key], 3).ToString("#,0"));
                    else
                        table.SetCell(colnum++, rownum, "N/A");
                rownum++;
            }
            File.WriteAllText(PathUtil.AppPathCombine("i4c-output", "benchmark.{0},{1}.txt".Fmt(algName, compressors.Values.First().ConfigString)), table.ToString());

            WaitFormHide();
        }

        /// <summary>
        /// Compresses/decompresses a single file, saving all outputs where appropriate. Places a copy
        /// of the resulting file in the application directory.
        /// </summary>
        public static void CompressDecompressSingle(Compressor compr, string filename)
        {
            string basename = Path.GetFileNameWithoutExtension(filename);
            compr.CanonicalFileName = basename;

            if (filename.ToLower().EndsWith(".i4c"))
            {
                if (basename.Contains("."))
                    compr.CanonicalFileName = basename.Substring(0, basename.LastIndexOf("."));
                var destDir = PathUtil.AppPathCombine("i4c-output", "decode.{0}.{1},{2}".Fmt(compr.CanonicalFileName, compr.Name, compr.ConfigString));
                var destFile = "{0}.{1},{2}.png".Fmt(compr.CanonicalFileName, compr.Name, compr.ConfigString);
                DecompressFile(compr, filename, destDir, destFile);
                File.Copy(Path.Combine(destDir, destFile), PathUtil.AppPathCombine(destFile), true);
            }
            else
            {
                var destDir = PathUtil.AppPathCombine("i4c-output", "encode.{0}.{1},{2}".Fmt(compr.CanonicalFileName, compr.Name, compr.ConfigString));
                var destFile = "{0}.{1},{2}.i4c".Fmt(compr.CanonicalFileName, compr.Name, compr.ConfigString);
                CompressFile(compr, filename, destDir, destFile);
                File.Copy(Path.Combine(destDir, destFile), PathUtil.AppPathCombine(destFile), true);
            }
        }

        /// <summary>
        /// Compresses a single file using a given compressor, saving all debug or
        /// visualisation outputs to the specified directory.
        /// </summary>
        public static void CompressFile(Compressor compr, string sourcePath, string destDir, string destFile)
        {
            IntField image = new IntField(0, 0);
            image.ArgbLoadFromFile(sourcePath);

            Directory.CreateDirectory(destDir);
            compr.AddImageArgb(image, "orig");
            using (var output = File.Open(Path.Combine(destDir, destFile), FileMode.Create, FileAccess.Write, FileShare.Read))
            compr.Encode(image, output);

            SaveComprImages(compr, destDir);
            SaveComprDumps(compr, destDir);
            SaveComprCounters(compr, destDir);
        }

        /// <summary>
        /// Decompresses a single file using a given compressor, saving all debug or
        /// visualisation outputs to the specified directory.
        /// </summary>
        public static void DecompressFile(Compressor compr, string sourcePath, string destDir, string destFile)
        {
            FileStream input = File.Open(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            IntField f = compr.Decode(input);
            input.Close();

            Directory.CreateDirectory(destDir);
            f.ArgbToBitmap().Save(Path.Combine(destDir, destFile), ImageFormat.Png);
            SaveComprImages(compr, destDir);
            SaveComprDumps(compr, destDir);
            SaveComprCounters(compr, destDir);
        }

        private static void SaveComprImages(Compressor compr, string destDir)
        {
            foreach (var imgtuple in compr.Images)
            {
                imgtuple.Item2.ArgbToBitmap().Save(Path.Combine(destDir, "image-{0}.png".Fmt(imgtuple.Item1)), ImageFormat.Png);
                MainForm.AddImageTab(imgtuple.Item2, imgtuple.Item1);
            }

        }

        private static void SaveComprDumps(Compressor compr, string destDir)
        {
            foreach (var dumpname in compr.Dumps.Keys)
            {
                CsvTable table = new CsvTable();
                var dumpdata = compr.Dumps[dumpname];
                for (int i = 0; i < dumpdata.Length; i++)
                    table[i, 0] = dumpdata[i];
                table.SaveToFile(Path.Combine(destDir, "dump-{0}.csv".Fmt(dumpname)));
            }
        }

        private static void SaveComprCounters(Compressor compr, string destDir)
        {
            TextTable table = new TextTable { DefaultAlignment = HorizontalTextAlignment.Right, ColumnSpacing = 3, MaxWidth = int.MaxValue };
            compr.ComputeCounterTotals();

            int rownum = 0;
            int indent_prev = 0;
            foreach (var key in compr.Counters.Keys.Order())
            {
                int indent = 4 * key.ToCharArray().Count(c => c == '|');
                if (indent < indent_prev)
                    rownum++;
                indent_prev = indent;
                table.SetCell(0, rownum, new string(' ', indent) + key.Split('|').Last(), alignment: HorizontalTextAlignment.Left);
                table.SetCell(1, rownum, Math.Round(compr.Counters[key], 3).ToString("#,0"));
                rownum++;
            }

            File.WriteAllText(Path.Combine(destDir, "counters.txt"), table.ToString());
        }

        #region WaitForm

        static Form _waitform;

        static void WaitFormShow(string caption)
        {
            _waitform = new Form();
            _waitform.Text = "i4c: " + caption;
            _waitform.Height = 70;
            _waitform.ShowIcon = false;
            _waitform.MinimizeBox = false;
            _waitform.MaximizeBox = false;
            _waitform.Show();
        }

        static void WaitFormHide()
        {
            _waitform.Close();
            _waitform = null;
        }

        #endregion
    }

}
