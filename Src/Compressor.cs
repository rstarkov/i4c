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
using RT.Util.Collections;

namespace i4c
{
    public abstract class Compressor
    {
        public string Name;
        public string CanonicalFileName;

        /// <summary>Must initialise this to the default config</summary>
        protected RVariant[] Config = new RVariant[0];
        public string ConfigString;

        private Dictionary<string, double> _counters = new Dictionary<string, double>();
        public Dictionary<string, double> Counters;

        public List<Tuple<string, IntField>> Images = new List<Tuple<string, IntField>>();
        public Dictionary<string, RVariant[]> Dumps = new Dictionary<string, RVariant[]>();

        public virtual void Configure(params RVariant[] args)
        {
            for (int i = 0; i < args.Length && i < Config.Length; i++)
                Config[i] = args[i];
            ConfigString = Config.Select(val => (string)val).Join(",");
        }

        public abstract void Encode(IntField image, Stream output);
        public abstract IntField Decode(Stream input);

        public void AddImageGrayscale(IntField image, string caption)
        {
            AddImageGrayscale(image, image.Data.Min(), image.Data.Max(), caption);
        }

        public void AddImageGrayscale(IntField image, int min, int max, string caption)
        {
            IntField temp = image.Clone();
            temp.ArgbFromField(min, max);
            Images.Add(new Tuple<string, IntField>(caption, temp));
        }

        public void AddImageArgb(IntField image, string caption)
        {
            Images.Add(new Tuple<string, IntField>(caption, image.Clone()));
        }

        public void AddIntDump(string name, IEnumerable<int> list)
        {
            Dumps.Add(name, list.Select(val => (RVariant)val).ToArray());
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

        public void ComputeCounterTotals()
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
    }

    public abstract class CompressorFixedSizeFieldcode: Compressor
    {
        public FixedSizeForeseer Seer;
        protected int FieldcodeSymbols;

        public CompressorFixedSizeFieldcode()
        {
            Config = new RVariant[] { 7, 7, 3, 1024 };
        }

        public override void Configure(params RVariant[] args)
        {
            base.Configure(args);
            FieldcodeSymbols = (int)Config[3];
            Seer = new FixedSizeForeseer((int)Config[0], (int)Config[1], (int)Config[2], new HorzVertForeseer());
        }
    }

    public class I4cAlpha: CompressorFixedSizeFieldcode
    {
        public override void Encode(IntField image, Stream output)
        {
            // Predictive transform
            image.ArgbTo4c();
            image.PredictionEnTransformDiff(Seer, 4);
            // Convert to three fields' runlengths
            var fields = CodecUtil.FieldcodeRunlengthsEn(image, new RunLength01MaxSmartCodec(FieldcodeSymbols), this);

            // Write size
            DeltaTracker pos = new DeltaTracker();
            output.WriteUInt32Optim((uint)image.Width);
            output.WriteUInt32Optim((uint)image.Height);
            SetCounter("bytes|size", pos.Next(output.Position));
            // Write probs
            ulong[] probs = CodecUtil.GetFreqsForAllSections(fields, FieldcodeSymbols + 1);
            probs[0] = 6;
            CodecUtil.SaveFreqsCrappy(output, probs);
            SetCounter("bytes|probs", pos.Next(output.Position));
            // Write fields
            ArithmeticSectionsCodec ac = new ArithmeticSectionsCodec(probs, 6, output);
            for (int i = 0; i < fields.Count; i++)
            {
                ac.WriteSection(fields[i]);
                SetCounter("bytes|fields|"+(i+1), pos.Next(output.Position));
            }
            ac.Encode();
            SetCounter("bytes|arith-err", pos.Next(output.Position));
        }

        public override IntField Decode(Stream input)
        {
            // Read size
            int w = input.ReadUInt32Optim();
            int h = input.ReadUInt32Optim();
            // Read probabilities
            ulong[] probs = CodecUtil.LoadFreqsCrappy(input, FieldcodeSymbols + 1);
            // Read fields
            ArithmeticSectionsCodec ac = new ArithmeticSectionsCodec(probs, 6);
            ac.Decode(input);
            var fields = new List<int[]>();
            for (int i = 1; i <= 3; i++)
                fields.Add(ac.ReadSection());

            // Undo fieldcode
            IntField transformed = CodecUtil.FieldcodeRunlengthsDe(fields, w, h, new RunLength01MaxSmartCodec(FieldcodeSymbols), this);
            // Undo predictive transform
            transformed.PredictionDeTransformDiff(Seer, 4);
            transformed.ArgbFromField(0, 3);
            return transformed;
        }
    }

    public class I4cBravo: CompressorFixedSizeFieldcode
    {
        public override void Encode(IntField image, Stream output)
        {
            // Predictive transform
            image.ArgbTo4c();
            image.PredictionEnTransformXor(Seer);

            // Convert to three fields' runlengths
            var fields = CodecUtil.FieldcodeRunlengthsEn(image, new RunLength01MaxSmartCodec(FieldcodeSymbols), this);

            // Write size
            DeltaTracker pos = new DeltaTracker();
            output.WriteUInt32Optim((uint)image.Width);
            output.WriteUInt32Optim((uint)image.Height);
            SetCounter("bytes|size", pos.Next(output.Position));

            // Write probs
            ulong[] probs = CodecUtil.GetFreqsForAllSections(fields, FieldcodeSymbols + 1);
            probs[0] = 6;
            CodecUtil.SaveFreqsCrappy(output, probs);
            SetCounter("bytes|probs", pos.Next(output.Position));

            // Write fields
            ArithmeticSectionsCodec ac = new ArithmeticSectionsCodec(probs, 6, output);
            for (int i = 0; i < fields.Count; i++)
            {
                ac.WriteSection(fields[i]);
                SetCounter("bytes|fields|"+(i+1), pos.Next(output.Position));
            }
            ac.Encode();
            SetCounter("bytes|arith-err", pos.Next(output.Position));
        }

        public override IntField Decode(Stream input)
        {
            // Read size
            int w = input.ReadUInt32Optim();
            int h = input.ReadUInt32Optim();
            // Read probabilities
            ulong[] probs = CodecUtil.LoadFreqsCrappy(input, FieldcodeSymbols + 1);
            // Read fields
            ArithmeticSectionsCodec ac = new ArithmeticSectionsCodec(probs, 6);
            ac.Decode(input);
            var fields = new List<int[]>();
            for (int i = 1; i <= 3; i++)
                fields.Add(ac.ReadSection());

            // Undo fieldcode
            IntField transformed = CodecUtil.FieldcodeRunlengthsDe(fields, w, h, new RunLength01MaxSmartCodec(FieldcodeSymbols), this);
            // Undo predictive transform
            transformed.PredictionDeTransformXor(Seer);
            transformed.ArgbFromField(0, 3);
            return transformed;
        }
    }

    public class I4cDelta: Compressor
    {
        protected Foreseer Seer;
        protected SymbolCodec RLE;

        public I4cDelta()
        {
            Config = new RVariant[] { 7, 7, 3, 1000, 10 };
        }

        public override void Configure(params RVariant[] args)
        {
            base.Configure(args);
            Seer = new FixedSizeForeseer((int)Config[0], (int)Config[1], (int)Config[2], new HorzVertForeseer());
            RLE = new RunLength01LongShortCodec((int)Config[3], (int)Config[4]);
        }

        public override void Encode(IntField image, Stream output)
        {
            // Predictive transform
            image.ArgbTo4c();
            image.PredictionEnTransformXor(Seer);

            // Convert to three fields' runlengths
            var fields = CodecUtil.FieldcodeRunlengthsEn2(image, RLE, this);
            SetCounter("rle|longer", (RLE as RunLength01LongShortCodec).Counter_Longers);
            SetCounter("rle|muchlonger", (RLE as RunLength01LongShortCodec).Counter_MuchLongers);

            // Write size
            DeltaTracker pos = new DeltaTracker();
            output.WriteUInt32Optim((uint)image.Width);
            output.WriteUInt32Optim((uint)image.Height);
            SetCounter("bytes|size", pos.Next(output.Position));

            // Write probs
            ulong[] probs = CodecUtil.CountValues(fields, RLE.MaxSymbol);
            CodecUtil.SaveFreqs(output, probs, TimwiCec.runLProbsProbs, "");
            SetCounter("bytes|probs", pos.Next(output.Position));

            // Write fields
            ArithmeticWriter aw = new ArithmeticWriter(output, probs);
            output.WriteUInt32Optim((uint)fields.Length);
            foreach (var sym in fields)
                aw.WriteSymbol(sym);
            aw.Flush();
            SetCounter("bytes|fields", pos.Next(output.Position));
        }

        public override IntField Decode(Stream input)
        {
            // Read size
            int w = input.ReadUInt32Optim();
            int h = input.ReadUInt32Optim();
            // Read probabilities
            ulong[] probs = CodecUtil.LoadFreqs(input, TimwiCec.runLProbsProbs, RLE.MaxSymbol+1);
            // Read fields
            int len = input.ReadUInt32Optim();
            ArithmeticCodingReader acr = new ArithmeticCodingReader(input, probs);
            int[] fields = new int[len];
            for (int p = 0; p < len; p++)
                fields[p] = acr.ReadSymbol();

            // Undo fieldcode
            IntField transformed = CodecUtil.FieldcodeRunlengthsDe2(fields, w, h, RLE, this);
            // Undo predictive transform
            transformed.PredictionDeTransformXor(Seer);
            transformed.ArgbFromField(0, 3);
            return transformed;
        }
    }

    public class XperimentRects: Compressor
    {
        public FixedSizeForeseer Seer;
        protected int FieldcodeSymbols;
        protected int ReduceBlocksize;

        public XperimentRects()
        {
            Config = new RVariant[] { 7, 7, 3, 1024, 48 };
        }

        public override void Configure(params RVariant[] args)
        {
            base.Configure(args);
            FieldcodeSymbols = (int)Config[3];
            ReduceBlocksize = (int)Config[4];
            Seer = new FixedSizeForeseer((int)Config[0], (int)Config[1], (int)Config[2], new HorzVertForeseer());
        }

        public override void Encode(IntField image, Stream output)
        {
            image.ArgbTo4c();
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
                AddImageArgb(vis, "vis" + i);
            }

            // now have: fields, covered in part by areas and in part by cutmap (only remaining cutmap left at this stage)

            long pos = 0;
            output.WriteInt32Optim(image.Width);
            output.WriteInt32Optim(image.Height);
            SetCounter("bytes|size", output.Position - pos);
            pos = output.Position;

            for (int i = 1; i <= 3; i++)
                output.WriteInt32Optim(areases[i].Count);
            SetCounter("bytes|areas|count", output.Position - pos);
            pos = output.Position;

            for (int i = 1; i <= 3; i++)
            {
                foreach (var area in areases[i])
                {
                    output.WriteInt32Optim(area.Left);
                    output.WriteInt32Optim(area.Top);
                }
                SetCounter("bytes|areas|x,y|"+i, output.Position - pos);
                pos = output.Position;
            }

            for (int i = 1; i <= 3; i++)
            {
                foreach (var area in areases[i])
                {
                    output.WriteInt32Optim(area.Width);
                    output.WriteInt32Optim(area.Height);
                }
                SetCounter("bytes|areas|w,h|"+i, output.Position - pos);
                pos = output.Position;
            }

            for (int i = 1; i <= 3; i++)
            {
                var pts = CodecUtil.GetPixelCoords(cutmaps[i], 1);
                SetCounter("leftpixels|"+i, pts.Count);
                output.WriteInt32Optim(pts.Count);
                foreach (var pt in pts)
                {
                    output.WriteInt32Optim(pt.X);
                    output.WriteInt32Optim(pt.Y);
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
                    visdata.AddRange(aredata.Select(val => unchecked((int)0xFF000000) | ((i == 1 ? 0xF00000 : i == 2 ? 0xF08000 : 0xF00080) >> (2-val*2))));
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
            AddImageArgb(visual, "crux");

            var probs = CodecUtil.CountValues(symbols);
            output.WriteUInt32Optim((uint)probs.Length);
            for (int p = 0; p < probs.Length; p++)
                output.WriteUInt64Optim(probs[p]);
            SetCounter("bytes|probs", output.Position - pos);
            pos = output.Position;

            ArithmeticWriter aw = new ArithmeticWriter(output, probs);
            foreach (int sym in symbols)
                aw.WriteSymbol(sym);
            SetCounter("bytes|crux", output.Position - pos);
            pos = output.Position;
            aw.Flush();

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

    public class XperimentPdiff: Compressor
    {
        protected Foreseer Seer;

        public XperimentPdiff()
        {
            Config = new RVariant[] { 7, 7, 3 };
        }

        public override void Configure(params RVariant[] args)
        {
            base.Configure(args);
            Seer = new FixedSizeForeseer((int)Config[0], (int)Config[1], (int)Config[2], new HorzVertForeseer());
        }

        public override void Encode(IntField image, Stream output)
        {
            // Predictive transform
            image.ArgbTo4c();
            image.PredictionEnTransformXor(Seer);

            int px = 0, py = 0;
            List<int> dxs = new List<int>();
            List<int> dys = new List<int>();
            List<int> clr = new List<int>();
            List<Point> jumps = new List<Point>();

            IntField vis = new IntField(image.Width, image.Height);
            int vis_ctr = 0;
            while (true)
            {
                Point pt = FindNextPixel(image, px, py);
                px += pt.X;
                py += pt.Y;
                int c = image.GetWrapped(px, py);
                if (c == 0)
                    break;
                if (Math.Abs(pt.X) > 5 || Math.Abs(pt.Y) > 5)
                {
                    jumps.Add(pt);
                }
                else
                {
                    dxs.Add(pt.X);
                    dys.Add(pt.Y);
                }
                clr.Add(c);
                image.SetWrapped(px, py, 0);
                if (vis_ctr % 1000 == 0)
                    AddImageGrayscale(image, "progress.{0:00000}".Fmt(vis_ctr));
                vis.SetWrapped(px, py, ++vis_ctr);
            }

            SetCounter("jumps", jumps.Count);

            AddIntDump("xs", dxs);
            AddIntDump("ys", dys);
            AddIntDump("cs", clr);

            AddImageGrayscale(vis, "seq-global");
            vis.Data = vis.Data.Select(val => val % 512).ToArray();
            AddImageGrayscale(vis, "seq-local");

            var xs = CodecUtil.InterleaveNegatives(dxs.ToArray());
            var ys = CodecUtil.InterleaveNegatives(dxs.ToArray());
            var cs = clr.ToArray();

            List<ulong[]> xps = new List<ulong[]>();
            List<ulong[]> yps = new List<ulong[]>();
            List<ulong> xpts = new List<ulong>();
            List<ulong> ypts = new List<ulong>();
            for (int given = 0; given <= 10; given++)
            {
                if (given < 10)
                {
                    xps.Add(CodecUtil.GetNextProbsGiven(xs, given));
                    yps.Add(CodecUtil.GetNextProbsGiven(ys, given));
                }
                else
                {
                    xps.Add(CodecUtil.GetNextProbsGivenGreater(xs, given-1));
                    yps.Add(CodecUtil.GetNextProbsGivenGreater(ys, given-1));
                }
                AddIntDump("xp-{0}".Fmt(given), xps.Last().Select(var => (int)var));
                AddIntDump("yp-{0}".Fmt(given), yps.Last().Select(var => (int)var));
                xpts.Add(xps.Last().Aggregate((tot, val) => tot+val));
                ypts.Add(yps.Last().Aggregate((tot, val) => tot+val));
            }

            List<ulong[]> cps = new List<ulong[]>();
            cps.Add(new ulong[4] { 0, 1, 1, 1 });
            for (int given = 1; given <= 3; given++)
            {
                cps.Add(CodecUtil.GetNextProbsGiven(cs, given));
                AddIntDump("cp-{0}".Fmt(given), cps.Last().Select(var => (int)var));
            }
            ulong[] cpts = new ulong[4];
            for (int i = 0; i < cps.Count; i++)
                cpts[i] = cps[i].Aggregate((tot, val) => tot + val);

            ArithmeticWriter aw = new ArithmeticWriter(output, null);
            int prev;

            //prev = 0;
            //for (int i = 0; i < cs.Length; i++)
            //{
            //    aw.Probs = cps[prev];
            //    aw.TotalProb = cpts[prev];
            //    aw.WriteSymbol(cs[i]);
            //    prev = cs[i];
            //}
            //aw.Flush();

            // For comparison: normal arithmetic 5846, shifting probs 3270
            //ulong[] probs = CodecUtil.CountValues(cs);
            //AddIntDump("cp-overall", probs.Select(val => (int)val));
            //ArithmeticWriter aw = new ArithmeticWriter(output, probs);
            //for (int i = 0; i < cs.Length; i++)
            //    aw.WriteSymbol(cs[i]);
            //aw.Flush();

            prev = 0;
            for (int i = 0; i < xs.Length; i++)
            {
                if (prev > 10) prev = 10;
                aw.Probs = xps[prev];
                aw.TotalProb = xpts[prev];
                aw.WriteSymbol(xs[i]);
                prev = xs[i];
            }
            aw.Flush();

            //prev = 0;
            //for (int i = 0; i < ys.Length; i++)
            //{
            //    if (prev > 10) prev = 10;
            //    aw.Probs = yps[prev];
            //    aw.TotalProb = ypts[prev];
            //    aw.WriteSymbol(ys[i]);
            //    prev = ys[i];
            //}
            //aw.Flush();
        }

        public Point FindNextPixel(IntField image, int cx, int cy)
        {
            int dist = 0;
            while (dist < (image.Width/2 + image.Height/2 + 2))
            {
                for (int x = dist, y = 0; x > 0; x--, y++)
                    if (image.GetWrapped(cx+x, cy+y) != 0)
                        return new Point(x, y);
                for (int x = 0, y = dist; y > 0; x--, y--)
                    if (image.GetWrapped(cx+x, cy+y) != 0)
                        return new Point(x, y);
                for (int x = -dist, y = 0; x < 0; x++, y--)
                    if (image.GetWrapped(cx+x, cy+y) != 0)
                        return new Point(x, y);
                for (int x = 0, y = -dist; y < 0; x++, y++)
                    if (image.GetWrapped(cx+x, cy+y) != 0)
                        return new Point(x, y);
                dist++;
            }
            return new Point(0, 0);
        }

        public override IntField Decode(Stream input)
        {
            throw new NotImplementedException();
        }
    }

    public class XperimentSplit: Compressor
    {
        protected Foreseer Seer;

        public XperimentSplit()
        {
            Config = new RVariant[] { 7, 7, 3 };
        }

        public override void Configure(params RVariant[] args)
        {
            base.Configure(args);
            Seer = new FixedSizeForeseer((int)Config[0], (int)Config[1], (int)Config[2], new HorzVertForeseer());
        }

        public override void Encode(IntField image, Stream output)
        {
            // Predictive transform
            image.ArgbTo4c();
            image.PredictionEnTransformXor(Seer);

            // Convert to three fields' runlengths
            for (int i = 1; i <= 3; i++)
            {
                IntField temp = image.Clone();
                temp.Map(x => x == i ? 1 : 0);
                AddImageGrayscale(temp, 0, 1, "field" + i);
                var runs = new RunLength01SplitCodec().EncodeSplit(temp.Data);
                SetCounter("runs|field{0}|0s".Fmt(i), runs.E1.Count);
                SetCounter("runs|field{0}|1s".Fmt(i), runs.E2.Count);
                AddIntDump("field{0}-0s".Fmt(i), runs.E1);
                AddIntDump("field{0}-1s".Fmt(i), runs.E2);
            }
        }

        public override IntField Decode(Stream input)
        {
            throw new NotImplementedException();
        }
    }

    public class XperimentBackLzw: Compressor
    {
        public override void Encode(IntField image, Stream output)
        {
            image.ArgbTo4c();
            IntField orig = image.Clone();
            //image.PredictionEnTransformXor(new HorzVertForeseer());
            IntField backgr = CodecUtil.BackgroundFilterThin(image, 2, 2);
            AddImageGrayscale(image, "orig");
            AddImageGrayscale(backgr, "backgr");

            for (int p = 0; p < image.Data.Length; p++)
                image.Data[p] ^= backgr.Data[p];

            AddImageGrayscale(image, "foregr");


            for (int i = 1; i <= 3; i++)
            {
                IntField field = image.Clone();
                field.Conditional(pix => pix == i);
                int[] syms = CodecUtil.LzwLinesEn(field, 4, 1);
                AddImageGrayscale(field, "field{0}-{1}syms-max{2}".Fmt(i, syms.Length, syms.Max()));
            }
        }

        public override IntField Decode(Stream input)
        {
            throw new NotImplementedException();
        }
    }

    public class XperimentResolution: Compressor
    {
        public override void Encode(IntField image, Stream output)
        {
            image.ArgbTo4c();
            AddImageGrayscale(image, "res0");
            List<IntField> scales = new List<IntField>();
            scales.Add(image);
            while (scales.Last().Width > 100 && scales.Last().Height > 100)
                scales.Add(scales.Last().HalfResHighestCount());

            for (int i = 1; i < scales.Count; i++)
            {
                IntField predicted = new IntField(scales[i-1].Width, scales[i-1].Height);
                IntField shrunk = scales[i];
                AddImageGrayscale(shrunk, "res"+i);
                for (int y = 0; y < predicted.Height; y++)
                {
                    for (int x = 0; x < predicted.Width; x++)
                    {
                        int xm2 = x & 1;
                        int ym2 = y & 1;
                        int xd2 = x >> 1;
                        int yd2 = y >> 1;
                        if (xm2 == 0 && ym2 == 0) // original
                            predicted[x, y] = shrunk[xd2, yd2];
                        else if (ym2 == 0) // between horizontal pixels
                            predicted[x, y] = shrunk[xd2, yd2];
                        else
                            predicted[x, y] = shrunk[xd2, yd2];
                    }
                }
                //AddImageGrayscale(predicted, "pred"+i);
                for (int p = 0; p < predicted.Data.Length; p++)
                    predicted.Data[p] ^= scales[i-1].Data[p];
                //AddImageGrayscale(predicted, "diff"+i);
            }
        }

        public override IntField Decode(Stream input)
        {
            throw new NotImplementedException();
        }
    }
}
