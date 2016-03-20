using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using RT.KitchenSink.Collections;
using RT.Util.ExtensionMethods;

namespace i4c
{
    public class XperimentRects : Compressor
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
            FieldcodeSymbols = (int) Config[3];
            ReduceBlocksize = (int) Config[4];
            Seer = new FixedSizeForeseer((int) Config[0], (int) Config[1], (int) Config[2], new HorzVertForeseer());
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

                    Rectangle r = new Rectangle(rects[0].Left * ReduceBlocksize, rects[0].Top * ReduceBlocksize,
                        rects[0].Width * ReduceBlocksize, rects[0].Height * ReduceBlocksize);
                    r = CodecUtil.ShrinkRectangle(remains, r);
                    areases[i].Add(r);

                    cutmaps[i].ShadeRect(rects[0].Left, rects[0].Top, rects[0].Width, rects[0].Height, 0, 0);
                    remains.ShadeRect(r.Left, r.Top, r.Width, r.Height, 0, 0);
                }

                SetCounter("areas|" + i, areases[i].Count);

                IntField vis = fields[i].Clone();
                vis.ArgbFromField(0, 1);
                foreach (var area in areases[i])
                    vis.ShadeRect(area.Left, area.Top, area.Width, area.Height, 0xFFFF7F7F, 0x007F0000);
                for (int x = 0; x < cutmaps[i].Width; x++)
                    for (int y = 0; y < cutmaps[i].Height; y++)
                        if (cutmaps[i][x, y] > 0)
                            vis.ShadeRect(x * ReduceBlocksize, y * ReduceBlocksize, ReduceBlocksize, ReduceBlocksize,
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
                SetCounter("bytes|areas|x,y|" + i, output.Position - pos);
                pos = output.Position;
            }

            for (int i = 1; i <= 3; i++)
            {
                foreach (var area in areases[i])
                {
                    output.WriteInt32Optim(area.Width);
                    output.WriteInt32Optim(area.Height);
                }
                SetCounter("bytes|areas|w,h|" + i, output.Position - pos);
                pos = output.Position;
            }

            for (int i = 1; i <= 3; i++)
            {
                var pts = CodecUtil.GetPixelCoords(cutmaps[i], 1);
                SetCounter("leftpixels|" + i, pts.Count);
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
                    visdata.AddRange(aredata.Select(val => unchecked((int) 0xFF000000) | ((i == 1 ? 0xF00000 : i == 2 ? 0xF08000 : 0xF00080) >> (2 - val * 2))));
                    fields[i].ShadeRect(area.Left, area.Top, area.Width, area.Height, 0, 0);
                }
            }
            for (int i = 1; i <= 3; i++)
            {
                var pts = CodecUtil.GetPixelCoords(cutmaps[i], 1);
                foreach (var pt in pts)
                {
                    Rectangle rect = new Rectangle(pt.X * ReduceBlocksize, pt.Y * ReduceBlocksize, ReduceBlocksize, ReduceBlocksize);
                    int[] aredata = fields[i].GetRectData(rect);
                    data.AddRange(aredata);
                    visdata.AddRange(aredata.Select(val => unchecked((int) 0xFF000000) | ((i == 1 ? 0x00F000 : i == 2 ? 0x80F000 : 0x00F080) >> (2 - val * 2))));
                }
            }
            int[] dataA = data.ToArray();
            int[] symbols = runlen.Encode(dataA);
            SetCounter("crux-pixels", data.Count);
            SetCounter("crux-rle-symbols", symbols.Length);

            int viw = (int) (Math.Sqrt(data.Count) * 1.3);
            int vih = (int) Math.Ceiling((double) data.Count / viw);
            IntField visual = new IntField(viw, vih);
            Array.Copy(visdata.ToArray(), visual.Data, visdata.Count);
            AddImageArgb(visual, "crux");

            var probs = CodecUtil.CountValues(symbols);
            output.WriteUInt32Optim((uint) probs.Length);
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
}
