using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using RT.Util;
using RT.Util.ExtensionMethods;
using RT.Util.Streams;
using RT.Util.Text;

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
            TextTable table = new TextTable(true, TextTable.Alignment.Right);
            table.SetAlignment(0, TextTable.Alignment.Left);

            int rownum = 0;
            int indent_prev = 0;
            foreach (var key in Counters.Keys.Order())
            {
                int indent = 4 * key.ToCharArray().Count(c => c == '|');
                if (indent < indent_prev)
                    rownum++;
                indent_prev = indent;
                table[rownum, 0] = " ".Repeat(indent) + key.Split('|').Last();
                table[rownum, 1] = Math.Round(Counters[key], 3).ToString("#,0");
                rownum++;
            }
            File.WriteAllText(filename, table.GetText(0, 99999, 3, false));
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

    public class I4cCharlie: Compressor
    {
        public FixedSizeForeseer Seer;
        protected int FieldcodeSymbols;
        protected int ReduceBlocksize;

        public override void Configure(params string[] args)
        {
            int w = args.Length > 0 ? int.Parse(args[0]) : 7;
            int h = args.Length > 1 ? int.Parse(args[1]) : 7;
            int x = args.Length > 2 ? int.Parse(args[2]) : 3;
            FieldcodeSymbols = args.Length > 3 ? int.Parse(args[3]) : 1024;
            ReduceBlocksize = args.Length > 4 ? int.Parse(args[4]) : 32;
            Seer = new FixedSizeForeseer(w, h, x, new HorzVertForeseer());
            Config = "{0},{1},{2},{3},{4}".Fmt(w, h, x, FieldcodeSymbols, ReduceBlocksize);
        }

        public override void Encode(IntField image, Stream output)
        {
            image.PredictionEnTransformXor(Seer);
            IntField[] fields = new IntField[4];
            IntField[] cutmaps = new IntField[4];
            List<Rectangle>[] areases = new List<Rectangle>[4];
            for (int i = 1; i <= 3; i++)
            {
                fields[i] = image.Clone();
                fields[i].Map(x => x == i ? 1 : 0);
                cutmaps[i] = fields[i].ReduceKeepingPixels(ReduceBlocksize);

                areases[i] = new List<Rectangle>();
                IntField remains = fields[i].Clone();
                while (true)
                {
                    List<Rectangle> rects = CodecUtil.FindAllRects(cutmaps[i]);
                    if (rects.Count == 0 || rects[0].Width * rects[0].Height <= 1)
                        break;

                    Rectangle r = new Rectangle(rects[0].Left*ReduceBlocksize, rects[0].Top*ReduceBlocksize,
                        rects[0].Width*ReduceBlocksize, rects[0].Height*ReduceBlocksize);
                    r = CodecUtil.ShrinkRectangle(remains, r);
                    areases[i].Add(r);

                    cutmaps[i].ShadeRect(rects[0].Left, rects[0].Top, rects[0].Width, rects[0].Height, 0, 0);
                    remains.ShadeRect(r.Left, r.Top, r.Width, r.Height, 0, 0);
                }

                SetCounter("areas|"+i, areases[i].Count);

                IntField vis = fields[i].Clone();
                vis.ArgbFromField(0, 1);
                foreach (var area in areases[i])
                    vis.ShadeRect(area.Left, area.Top, area.Width, area.Height, 0xFFFF7F7F, 0x007F0000);
                for (int x = 0; x < cutmaps[i].Width; x++)
                    for (int y = 0; y < cutmaps[i].Height; y++)
                        if (cutmaps[i][x, y] > 0)
                            vis.ShadeRect(x*ReduceBlocksize, y*ReduceBlocksize, ReduceBlocksize, ReduceBlocksize,
                                0xFF7FFF7F, 0x00007F00);
                AddImage(vis.ArgbToBitmap(), "vis" + i);
            }

            // now have: fields, covered in part by areas and in part by cutmap (only remaining cutmap left at this stage)

            BinaryWriterPlus bwp = new BinaryWriterPlus(output);
            long pos = 0;
            bwp.WriteInt32Optim(image.Width);
            bwp.WriteInt32Optim(image.Height);
            SetCounter("bytes|size", output.Position - pos);
            pos = output.Position;

            for (int i = 1; i <= 3; i++)
                bwp.WriteInt32Optim(areases[i].Count);
            SetCounter("bytes|areas|count", output.Position - pos);
            pos = output.Position;

            for (int i = 1; i <= 3; i++)
            {
                foreach (var area in areases[i])
                {
                    bwp.WriteInt32Optim(area.Left);
                    bwp.WriteInt32Optim(area.Top);
                }
                SetCounter("bytes|areas|x,y|"+i, output.Position - pos);
                pos = output.Position;
            }

            for (int i = 1; i <= 3; i++)
            {
                foreach (var area in areases[i])
                {
                    bwp.WriteInt32Optim(area.Width);
                    bwp.WriteInt32Optim(area.Height);
                }
                SetCounter("bytes|areas|w,h|"+i, output.Position - pos);
                pos = output.Position;
            }

            for (int i = 1; i <= 3; i++)
            {
                var pts = CodecUtil.GetPixelCoords(cutmaps[i], 1);
                SetCounter("leftpixels|"+i, pts.Count);
                bwp.WriteInt32Optim(pts.Count);
                foreach (var pt in pts)
                {
                    bwp.WriteInt32Optim(pt.X);
                    bwp.WriteInt32Optim(pt.Y);
                }
            }

            RunLength01MaxSmartCodec runlen = new RunLength01MaxSmartCodec(FieldcodeSymbols);
            List<int> data = new List<int>();
            List<int> visdata = new List<int>();
            for (int i = 1; i <= 3; i++)
            {
                foreach (var area in areases[i])
                {
                    int[] aredata = fields[i].GetRectData(area);
                    data.AddRange(aredata);
                    visdata.AddRange(aredata.Select(val => unchecked((int) 0xFF000000) | ((i == 1 ? 0xF00000 : i == 2 ? 0xF08000 : 0xF00080) >> (2-val*2))));
                    fields[i].ShadeRect(area.Left, area.Top, area.Width, area.Height, 0, 0);
                }
            }
            for (int i = 1; i <= 3; i++)
            {
                var pts = CodecUtil.GetPixelCoords(cutmaps[i], 1);
                foreach (var pt in pts)
                {
                    Rectangle rect = new Rectangle(pt.X*ReduceBlocksize, pt.Y*ReduceBlocksize, ReduceBlocksize, ReduceBlocksize);
                    int[] aredata = fields[i].GetRectData(rect);
                    data.AddRange(aredata);
                    visdata.AddRange(aredata.Select(val => unchecked((int)0xFF000000) | ((i == 1 ? 0x00F000 : i == 2 ? 0x80F000 : 0x00F080) >> (2-val*2))));
                }
            }
            int[] dataA = data.ToArray();
            int[] symbols = runlen.Encode(dataA);
            SetCounter("crux-pixels", data.Count);
            SetCounter("crux-rle-symbols", symbols.Length);

            int viw = (int)(Math.Sqrt(data.Count) * 1.3);
            int vih = (int)Math.Ceiling((double)data.Count / viw);
            IntField visual = new IntField(viw, vih);
            Array.Copy(visdata.ToArray(), visual.Data, visdata.Count);
            AddImage(visual.ArgbToBitmap(), "crux");

            var probs = CodecUtil.CountValues(symbols);
            bwp.WriteUInt32Optim((uint)probs.Length);
            for (int p = 0; p < probs.Length; p++)
                bwp.WriteUInt64Optim(probs[p]);
            SetCounter("bytes|probs", output.Position - pos);
            pos = output.Position;

            ArithmeticCodingWriter acw = new ArithmeticCodingWriter(output, probs);
            foreach (int sym in symbols)
                acw.WriteSymbol(sym);
            SetCounter("bytes|crux", output.Position - pos);
            pos = output.Position;
            acw.Close(false);

            // BETTER RLE - RUNS OF 1'S
            // ARITH THE AREAS
            // ARITH THE PROBS
            // SHRINK GREENS
        }

        public override IntField Decode(Stream input)
        {
            throw new NotImplementedException();
        }
    }
}
