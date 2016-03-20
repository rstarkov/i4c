using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using RT.KitchenSink.Collections;
using RT.Util.ExtensionMethods;

namespace i4c
{
    public class XperimentPdiff : Compressor
    {
        protected Foreseer Seer;

        public XperimentPdiff()
        {
            Config = new RVariant[] { 7, 7, 3 };
        }

        public override void Configure(params RVariant[] args)
        {
            base.Configure(args);
            Seer = new FixedSizeForeseer((int) Config[0], (int) Config[1], (int) Config[2], new HorzVertForeseer());
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
                    xps.Add(CodecUtil.GetNextProbsGivenGreater(xs, given - 1));
                    yps.Add(CodecUtil.GetNextProbsGivenGreater(ys, given - 1));
                }
                AddIntDump("xp-{0}".Fmt(given), xps.Last().Select(var => (int) var));
                AddIntDump("yp-{0}".Fmt(given), yps.Last().Select(var => (int) var));
                xpts.Add(xps.Last().Aggregate((tot, val) => tot + val));
                ypts.Add(yps.Last().Aggregate((tot, val) => tot + val));
            }

            List<ulong[]> cps = new List<ulong[]>();
            cps.Add(new ulong[4] { 0, 1, 1, 1 });
            for (int given = 1; given <= 3; given++)
            {
                cps.Add(CodecUtil.GetNextProbsGiven(cs, given));
                AddIntDump("cp-{0}".Fmt(given), cps.Last().Select(var => (int) var));
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
            while (dist < (image.Width / 2 + image.Height / 2 + 2))
            {
                for (int x = dist, y = 0; x > 0; x--, y++)
                    if (image.GetWrapped(cx + x, cy + y) != 0)
                        return new Point(x, y);
                for (int x = 0, y = dist; y > 0; x--, y--)
                    if (image.GetWrapped(cx + x, cy + y) != 0)
                        return new Point(x, y);
                for (int x = -dist, y = 0; x < 0; x++, y--)
                    if (image.GetWrapped(cx + x, cy + y) != 0)
                        return new Point(x, y);
                for (int x = 0, y = -dist; y < 0; x++, y++)
                    if (image.GetWrapped(cx + x, cy + y) != 0)
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
}
