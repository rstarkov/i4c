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

namespace i4c
{
    [SecurityPermission(SecurityAction.Demand, Flags = SecurityPermissionFlag.UnmanagedCode)]
    static class Program
    {
        public static Dictionary<string, Type> Compressors = new Dictionary<string, Type>()
        {
            { "alpha", typeof(I4cAlpha) },
            { "bravo", typeof(I4cBravo) },
            { "charlie", typeof(I4cCharlie) },
            { "cec", typeof(TimwiCec) },
        };

        public static bool IsBenchmark = false;

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
                CompressBenchmark(args[1], args.Skip(2).ToArray());
            }
            else
            {
                if (args.Length < 2)
                {
                    DlgMessage.ShowError("Must specify at least the algorithm name and the file name");
                    return;
                }
                CompressSingle(args[0], args[1], args.Skip(2).ToArray());
            }
        }

        private static void CompressSingle(string algName, string fileName, string[] algArgs)
        {
            Compressor compr = GetCompressor(algName);
            if (compr == null)
                return;

            WaitFormShow("processing...");
            compr.Process(fileName, algArgs);
            WaitFormHide();
        }

        private static void CompressBenchmark(string algName, string[] algArgs)
        {
            IsBenchmark = true;
            WaitFormShow("benchmarking...");

            Dictionary<string, Compressor> compressors = new Dictionary<string, Compressor>();
            foreach (var file in Directory.GetFiles(".", "*.png"))
                compressors.Add(file, GetCompressor(algName));

            // Queue all jobs...
            foreach (var file in compressors.Keys)
            {
                Func<Compressor, string, string[], WaitCallback> makeCallback = (compr, fname, arg) => (dummy2 => compr.Process(fname, arg));
                ThreadPool.QueueUserWorkItem(makeCallback(compressors[file], file, algArgs));
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
            File.WriteAllText(PathUtil.Combine(PathUtil.AppPath, "i4c-output", "benchmark.{0},{1}.txt".Fmt(algName, compressors.Values.First().Config)), table.GetText(0, 99999, 3, false));

            WaitFormHide();
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
