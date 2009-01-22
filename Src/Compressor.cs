using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using RT.Util;
using RT.Util.ExtensionMethods;
using System.Collections.Generic;
using System.Text;
using System;

namespace i4c
{
    public abstract class Compressor
    {
        public string Name;
        public string Config;

        private Dictionary<string, double> _counters = new Dictionary<string, double>();
        public Dictionary<string, double> Counters;

        public string CanonicalFileName;

        public abstract void Configure(params string[] args);
        public abstract void Encode(IntField image, Stream output);
        public abstract IntField Decode(Stream input);

        public void Process(string filename, params string[] args)
        {
            Configure(args);

            string basename = Path.GetFileNameWithoutExtension(filename);
            CanonicalFileName = basename;

            if (filename.ToLower().EndsWith(".i4c"))
            {
                if (basename.Contains("."))
                    CanonicalFileName = basename.Substring(0, basename.LastIndexOf("."));

                FileStream input = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
                IntField f = Decode(input);
                f.ArgbFromField(0, 3);
                string outputfilename = PathUtil.Combine(DumpDir, "decoded.png");
                f.ArgbToBitmap().Save(outputfilename, ImageFormat.Png);
                File.Copy(outputfilename, filename + ".png", true);
            }
            else
            {
                IntField f = new IntField(0, 0);
                f.ArgbLoadFromFile(filename);
                f.ArgbTo4c();
                string outputfilename = PathUtil.Combine(DumpDir, "encoded.i4c");
                Directory.CreateDirectory(Path.GetDirectoryName(outputfilename));
                FileStream output = File.Open(outputfilename, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
                Encode(f, output);
                output.Close();
                File.Copy(outputfilename, "{0}.{1},{2}.i4c".Fmt(basename, Name, Config), true);

                computeCounterTotals();
                saveCounters(PathUtil.Combine(DumpDir, "counters.txt"));
            }
        }

        public virtual string DumpDir
        {
            get
            {
                if (Program.IsBenchmark)
                    return PathUtil.Combine(PathUtil.AppPath, "i4c-output", "benchmark.{0},{1}".Fmt(Name, Config), CanonicalFileName);
                else
                    return PathUtil.Combine(PathUtil.AppPath, "i4c-output", "{0}.{1},{2}".Fmt(CanonicalFileName, Name, Config));
            }
        }

        public void AddImage(IntField image, string caption)
        {
            AddImage(image, image.Data.Min(), image.Data.Max(), caption);
        }

        public void AddImage(IntField image, int min, int max, string caption)
        {
            IntField temp = image.Clone();
            temp.ArgbFromField(min, max);
            AddImage(temp.ArgbToBitmap(), caption);
        }

        public void AddImage(Bitmap image, string caption)
        {
            MainForm.AddImageTab(image, caption);
            Directory.CreateDirectory(DumpDir);
            image.Save(PathUtil.Combine(DumpDir, "img-{0}.png".Fmt(caption)), ImageFormat.Png);
        }

        public void SetCounter(string name, double value)
        {
            if (_counters.ContainsKey(name))
                _counters[name] = value;
            else
                _counters.Add(name, value);
        }

        public void IncCounter(string name, double value)
        {
            if (_counters.ContainsKey(name))
                _counters[name] += value;
            else
                _counters.Add(name, value);
        }

        private void computeCounterTotals()
        {
            Counters = new Dictionary<string, double>();
            foreach (var key in _counters.Keys)
            {
                // Add the value
                Counters.Add(key, _counters[key]);

                // Add the totals
                string[] parts = key.Split('|');
                string curname = "";
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    curname += (curname == "" ? "" : "|") + parts[i];

                    if (Counters.ContainsKey(curname))
                        Counters[curname] += _counters[key];
                    else
                        Counters[curname] = _counters[key];
                }
            }
        }

        private void saveCounters(string filename)
        {
            StringBuilder SB = new StringBuilder();
            foreach (var key in Counters.Keys.Order())
            {
                SB.Append(' ', 4 * key.ToCharArray().Count(c => c == '|'));
                SB.Append(key.Split('|').Last());
                SB.Append(": ");
                SB.Append(Math.Round(Counters[key], 3));
                SB.AppendLine();
            }
            File.WriteAllText(filename, SB.ToString());
        }

    }

    public abstract class CompressorFixedSizeFieldcode: Compressor
    {
        public FixedSizeForeseer Seer;
        protected int FieldcodeSymbols;

        public override void Configure(params string[] args)
        {
            int w = args.Length > 0 ? int.Parse(args[0]) : 7;
            int h = args.Length > 1 ? int.Parse(args[1]) : 7;
            int x = args.Length > 2 ? int.Parse(args[2]) : 3;
            FieldcodeSymbols = args.Length > 3 ? int.Parse(args[3]) : 1024;
            Seer = new FixedSizeForeseer(w, h, x, new HorzVertForeseer());
            Config = "{0},{1},{2},{3}".Fmt(w, h, x, FieldcodeSymbols);
        }
    }

    public class I4cAlpha: CompressorFixedSizeFieldcode
    {
        public override void Encode(IntField image, Stream output)
        {
            image.PredictionEnTransformDiff(Seer, 4);
            Fieldcode fc = new Fieldcode(this, FieldcodeSymbols);
            byte[] bytes = fc.EnFieldcode(image);
            output.Write(bytes, 0, bytes.Length);
        }

        public override IntField Decode(Stream input)
        {
            Fieldcode fc = new Fieldcode(this, FieldcodeSymbols);
            IntField f = fc.DeFieldcode(input.ReadAllBytes());
            f.PredictionDeTransformDiff(Seer, 4);
            return f;
        }
    }

    public class I4cBravo: CompressorFixedSizeFieldcode
    {
        public override void Encode(IntField image, Stream output)
        {
            image.PredictionEnTransformXor(Seer);
            Fieldcode fc = new Fieldcode(this, FieldcodeSymbols);
            byte[] bytes = fc.EnFieldcode(image);
            output.Write(bytes, 0, bytes.Length);
        }

        public override IntField Decode(Stream input)
        {
            Fieldcode fc = new Fieldcode(this, FieldcodeSymbols);
            IntField f = fc.DeFieldcode(input.ReadAllBytes());
            f.PredictionDeTransformXor(Seer);
            return f;
        }
    }
}
