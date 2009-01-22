using System;
using System.Linq;
using System.Windows.Forms;
using RT.Util.Dialogs;
using RT.Util.ExtensionMethods;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Security.Permissions;
using System.Text;
using RT.Util.Text;
using RT.Util;

namespace i4c
{
    [SecurityPermission(SecurityAction.Demand, Flags = SecurityPermissionFlag.UnmanagedCode)]
    static class Program
    {
        public static Dictionary<string, Type> Compressors = new Dictionary<string, Type>()
        {
            { "alpha", typeof(I4cAlpha) },
            { "bravo", typeof(I4cBravo) },
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

            if (args.Length == 0)
            {
                MainForm form = new MainForm();
                Application.Run(form);
            }
            else if (args[0] == "?")
            {
                DlgMessage.ShowInfo("Available tasks:\n\n" + "".Join(Compressors.Keys.Select(compr => "* " + compr + "\n")));
            }
            else if (args[0] == "benchmark")
            {
                if (args.Length == 1)
                {
                    DlgMessage.ShowError("Must also specify the name of the algorithm to benchmark");
                    return;
                }
                IsBenchmark = true;

                Dictionary<string, Compressor> compressors = new Dictionary<string, Compressor>();
                foreach (var file in Directory.GetFiles(".", "*.png"))
                    compressors.Add(file, GetCompressor(args[1]));

                WaitFormShow("benchmarking...");
                int threads = Environment.ProcessorCount;
                int worker, dummy;
                ThreadPool.SetMinThreads(threads, 10);
                ThreadPool.SetMaxThreads(threads, 10);
                ThreadPool.GetAvailableThreads(out worker, out dummy);
                System.Diagnostics.Debug.Assert(worker == threads);
                var compr_args = args.Skip(2).ToArray();
                foreach (var file in compressors.Keys)
                {
                    Func<Compressor, string, string[], WaitCallback> makeCallback = (compr, fname, arg) => (dummy2 => compr.Process(fname, arg));
                    ThreadPool.QueueUserWorkItem(makeCallback(compressors[file], file, compr_args));
                }
                worker = 0;
                while (worker < threads)
                {
                    Thread.Sleep(200);
                    ThreadPool.GetAvailableThreads(out worker, out dummy);
                }

                // Record stats
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
                File.WriteAllText(PathUtil.Combine(PathUtil.AppPath, "i4c-output", "bench-totals.txt"), table.GetText(0, 99999, 3, false));

                WaitFormHide();
            }
            else
            {
                Compressor compr = GetCompressor(args[0]);
                if (compr == null)
                    return;

                WaitFormShow("processing...");
                compr.Process(args[1], args.Skip(2).ToArray());
                WaitFormHide();
            }
        }

        #region WaitForm

        static Form _waitform;

        static void WaitFormShow(string caption)
        {
            _waitform = new Form();
            _waitform.Text = "i4c: " + caption;
            _waitform.Height = 70;
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
