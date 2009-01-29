using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Permissions;
using System.Threading;
using System.Windows.Forms;
using RT.Util;
using RT.Util.Dialogs;
using RT.Util.ExtensionMethods;
using RT.Util.Text;
using RT.Util.Collections;
using System.Drawing.Imaging;

namespace i4c
{
    [SecurityPermission(SecurityAction.Demand, Flags = SecurityPermissionFlag.UnmanagedCode)]
    static class Program
    {
        /// <summary>
        /// Define any new compressors in here and they will be accessible by the name
        /// via the GUI menu or the command line.
        /// </summary>
        public static Dictionary<string, Type> Compressors = new Dictionary<string, Type>()
        {
            { "alpha", typeof(I4cAlpha) },
            { "bravo", typeof(I4cBravo) },
            { "cec", typeof(TimwiCec) },
            { "charlie", typeof(I4cCharlie) },
            { "delta", typeof(I4cDelta) },
        };

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            int worker, dummy;
            ThreadPool.SetMinThreads(Environment.ProcessorCount, 10);
            ThreadPool.SetMaxThreads(Environment.ProcessorCount, 10);
            ThreadPool.GetAvailableThreads(out worker, out dummy);
            System.Diagnostics.Debug.Assert(worker == Environment.ProcessorCount);


            if (args.Length == 0)
            {
                MainForm form = new MainForm();
                Application.Run(form);
            }
            else if (args[0] == "?")
            {
                DlgMessage.ShowInfo("Usage:\n  i4c.exe - run interactive GUI.\n  i4c.exe <algorithm> <filename> [<arg1> <arg2> ...] - compress/decompress single file.\n  i4c.exe benchmark <algorithm> [<arg1> <arg2> ...] - benchmark on all files in cur dir.\n\nAvailable algorithms:\n\n" + "".Join(Compressors.Keys.Select(compr => "* " + compr + "\n")));
            }
            else if (args[0] == "benchmark")
            {
                if (args.Length == 1)
                {
                    DlgMessage.ShowError("Must also specify the name of the algorithm to benchmark");
                    return;
                }
                command_Benchmark(args[1], args.Skip(2).Select(val => (RVariant)val).ToArray());
            }
            else
            {
                if (args.Length < 2)
                {
                    DlgMessage.ShowError("Must specify at least the algorithm name and the file name");
                    return;
                }
                command_Single(args[1], args[0], args.Skip(2).Select(val => (RVariant)val).ToArray());
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
                Compressor compr = (Compressor)Compressors[name].GetConstructor(new Type[] { }).Invoke(new object[] { });
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
                string destDir = PathUtil.Combine(PathUtil.AppPath, "i4c-output", "benchmark.{0},{1}".Fmt(algName, compr.ConfigString), compr.CanonicalFileName);
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

            // Write stats totals to a file a text file
            TextTable table = new TextTable(true, TextTable.Alignment.Right);
            table.SetAlignment(0, TextTable.Alignment.Left);
            table[0, 1] = "TOTAL";
            int colnum = 2;
            int rownum = 2;
            foreach (var str in compressors.Values.Select(val => val.CanonicalFileName).Order())
                table[0, colnum++] = str;
            int indent_prev = 0;
            foreach (var key in totals.Keys.Order())
            {
                int indent = 4 * key.ToCharArray().Count(c => c == '|');
                if (indent < indent_prev)
                    rownum++;
                indent_prev = indent;
                table[rownum, 0] = " ".Repeat(indent) + key.Split('|').Last();
                table[rownum, 1] = Math.Round(totals[key], 3).ToString("#,0");
                colnum = 2;
                foreach (var compr in compressors.Values.OrderBy(c => c.CanonicalFileName))
                    if (compr.Counters.ContainsKey(key))
                        table[rownum, colnum++] = Math.Round(compr.Counters[key], 3).ToString("#,0");
                    else
                        table[rownum, colnum++] = "N/A";
                rownum++;
            }
            File.WriteAllText(PathUtil.Combine(PathUtil.AppPath, "i4c-output", "benchmark.{0},{1}.txt".Fmt(algName, compressors.Values.First().ConfigString)), table.GetText(0, 99999, 3, false));

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
                var destDir = PathUtil.Combine(PathUtil.AppPath, "i4c-output", "decode.{0}.{1},{2}".Fmt(compr.CanonicalFileName, compr.Name, compr.ConfigString));
                var destFile = "{0}.{1},{2}.png".Fmt(compr.CanonicalFileName, compr.Name, compr.ConfigString);
                DecompressFile(compr, filename, destDir, destFile);
                File.Copy(PathUtil.Combine(destDir, destFile), PathUtil.Combine(PathUtil.AppPath, destFile));
            }
            else
            {
                var destDir = PathUtil.Combine(PathUtil.AppPath, "i4c-output", "encode.{0}.{1},{2}".Fmt(compr.CanonicalFileName, compr.Name, compr.ConfigString));
                var destFile = "{0}.{1},{2}.i4c".Fmt(compr.CanonicalFileName, compr.Name, compr.ConfigString);
                CompressFile(compr, filename, destDir, destFile);
                File.Copy(PathUtil.Combine(destDir, destFile), PathUtil.Combine(PathUtil.AppPath, destFile));
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
            FileStream output = File.Open(PathUtil.Combine(destDir, destFile), FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            compr.Encode(image, output);
            output.Close();

            SaveComprImages(compr, destDir);
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
            f.ArgbToBitmap().Save(PathUtil.Combine(destDir, destFile), ImageFormat.Png);
            SaveComprImages(compr, destDir);
            SaveComprCounters(compr, destDir);
        }

        private static void SaveComprImages(Compressor compr, string destDir)
        {
            foreach (var imgtuple in compr.Images)
            {
                imgtuple.E2.ArgbToBitmap().Save(PathUtil.Combine(destDir, "image-{0}.png".Fmt(imgtuple.E1)), ImageFormat.Png);
                MainForm.AddImageTab(imgtuple.E2, imgtuple.E1);
            }

        }

        private static void SaveComprCounters(Compressor compr, string destDir)
        {
            TextTable table = new TextTable(true, TextTable.Alignment.Right);
            table.SetAlignment(0, TextTable.Alignment.Left);
            compr.ComputeCounterTotals();

            int rownum = 0;
            int indent_prev = 0;
            foreach (var key in compr.Counters.Keys.Order())
            {
                int indent = 4 * key.ToCharArray().Count(c => c == '|');
                if (indent < indent_prev)
                    rownum++;
                indent_prev = indent;
                table[rownum, 0] = " ".Repeat(indent) + key.Split('|').Last();
                table[rownum, 1] = Math.Round(compr.Counters[key], 3).ToString("#,0");
                rownum++;
            }

            File.WriteAllText(PathUtil.Combine(destDir, "counters.txt"), table.GetText(0, 99999, 3, false));
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
