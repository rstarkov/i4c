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
        protected RVariant[] Config;
        public string ConfigString;

        private Dictionary<string, double> _counters = new Dictionary<string, double>();
        public Dictionary<string, double> Counters;

        public List<Tuple<string, IntField>> Images = new List<Tuple<string, IntField>>();

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

    public class I4cCharlie: Compressor
    {
        public FixedSizeForeseer Seer;
        protected int FieldcodeSymbols;
        protected int ReduceBlocksize;

        public I4cCharlie()
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
            AddImageArgb(visual, "crux");

            var probs = CodecUtil.CountValues(symbols);
            output.WriteUInt32Optim((uint)probs.Length);
            for (int p = 0; p < probs.Length; p++)
                output.WriteUInt64Optim(probs[p]);
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

    public class I4cDelta: CompressorFixedSizeFieldcode
    {
        public override void Encode(IntField image, Stream output)
        {
            // Predictive transform
            image.ArgbTo4c();
            image.PredictionEnTransformXor(Seer);

            // Convert to three fields' runlengths
            var fields = CodecUtil.FieldcodeRunlengthsEn2(image, new RunLength01MaxSmartCodec(FieldcodeSymbols), this);

            // Write size
            DeltaTracker pos = new DeltaTracker();
            output.WriteUInt32Optim((uint)image.Width);
            output.WriteUInt32Optim((uint)image.Height);
            SetCounter("bytes|size", pos.Next(output.Position));

            // Write probs
            ulong[] probs = CodecUtil.CountValues(fields, FieldcodeSymbols);
            CodecUtil.SaveFreqs(output, probs, TimwiCec.runLProbsProbs, "");
            SetCounter("bytes|probs", pos.Next(output.Position));

            // Write fields
            ArithmeticCodingWriter acw = new ArithmeticCodingWriter(output, probs);
            output.WriteUInt32Optim((uint)fields.Length);
            foreach (var sym in fields)
                acw.WriteSymbol(sym);
            acw.Close(false);
            SetCounter("bytes|fields", pos.Next(output.Position));
        }

        public override IntField Decode(Stream input)
        {
            // Read size
            int w = input.ReadUInt32Optim();
            int h = input.ReadUInt32Optim();
            // Read probabilities
            ulong[] probs = CodecUtil.LoadFreqsCrappy(input, FieldcodeSymbols + 1);
            // Read fields
            ArithmeticSectionsCodec ac = new ArithmeticSectionsCodec(probs, 3);
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

}
