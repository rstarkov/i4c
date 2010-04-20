using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using RT.Util.Collections;
using RT.Util.ExtensionMethods;

namespace i4c
{
    public class I4cEcho: Compressor
    {
        private int _backgrLargeStepSize = 32;
        private int _backgrLargeAreaThresh = 3000;

        public override void Encode(IntField image, Stream output)
        {
            /// Split background from foreground. Anything with low "rectangularity" goes into foreground
            /// - measure the number of "turns" (changes from horz to vert edge) against area.
            /// Background:
            /// - Predictive xform with small radius
            /// - Modified runlengths
            /// - Context-aware arithmetic encoding
            /// Foreground:
            /// - Order colors by how common they are, call them c1,c2,c3
            /// - Flatten all colors into 0 and 1
            /// - Find all rects covering all c2-pixels without covering any c1-pixels.
            /// - Find all rects covering all c3-pixels without covering any c1 or c2 pixels.
            /// - Predictive xform on 0/1 image stopping at vertical letter boundaries. If larger image not in dict, use a smaller one and add all sizes to dict.
            /// - Modified runlengths on the 0/1 xformed
            /// - Context-aware arithmetic encoding
            /// - Rects: optimize by changing 1 pixel rects to pixels; then feed optim ints through arithmetic
            /// Modified runlengths:
            /// - As before, encode only runs of 0's.
            /// - Every run longer than thresh encoded as thresh + a number in a separate optim stream
            /// - Optim stream is fed through arithmetic
            ///
            /// Alternative color encoding: a stream of 1's, 2's, 3's, fed through runlength

            image.ArgbTo4c();
            AddImageGrayscale(image, 0, 3, "00-original");
            IntField backgr, foregr;
            backgroundSplit(image, out backgr, out foregr);

            // Backgr
            backgr.PredictionEnTransformXor(new FixedSizeForeseer(5, 3, 2, new HorzVertForeseer()));
            AddImageGrayscale(backgr, 0, 3, "20-bkg-xformed");


            saveColors(foregr);

            // save as much as possible using rects, the rest just dumping as-is

            // Foregr
            IntField foremap = foregr.Clone();
            foremap.Map(pix => pix == 0 ? 0 : 1);
            AddImageGrayscale(foremap, 0, 1, "30-fore-map");
            foremap.PredictionEnTransformXor(new FixedSizeForeseer(7, 7, 3, new HorzVertForeseer()));
            AddImageGrayscale(foremap, 0, 1, "31-fore-map-xformed");

            saveForeground(foremap.Data);
        }

        private void saveColors(IntField foregr)
        {
            var clr = foregr.Data.Where(pix => pix > 0).ToArray();
            RT.Util.ObsoleteTuple.Tuple<int, int>[] cc = new RT.Util.ObsoleteTuple.Tuple<int, int>[3];
            cc[0] = new RT.Util.ObsoleteTuple.Tuple<int, int>(clr.Where(pix => pix == 1).Count(), 1);
            cc[1] = new RT.Util.ObsoleteTuple.Tuple<int, int>(clr.Where(pix => pix == 2).Count(), 2);
            cc[2] = new RT.Util.ObsoleteTuple.Tuple<int, int>(clr.Where(pix => pix == 3).Count(), 3);
            cc = cc.OrderBy(tup => -tup.E1).ToArray();
            int c1 = cc[0].E2;
            int c2 = cc[1].E2;
            int c3 = cc[2].E2;
            IntField cover = foregr.Clone();
            cover.Map(pix => pix == c1 ? 1 : pix == c2 ? 5 : 0);
            AddImageGrayscale(cover, 0, 5, "25-cover-2s");
            cover = foregr.Clone();
            cover.Map(pix => pix == c1 ? 1 : pix == c2 ? 1 : pix == c3 ? 5 : 0);
            AddImageGrayscale(cover, 0, 5, "26-cover-3s");
            AddIntDump("colors", clr);

            List<int>[] runs = new List<int>[4];
            runs[1] = new List<int>();
            runs[2] = new List<int>();
            runs[3] = new List<int>();
            int p = 0;
            while (p < clr.Length)
            {
                for (int c = 1; c <= 3; c++)
                {
                    int pbefore = p;
                    while (p < clr.Length && clr[p] == c)
                        p++;
                    runs[c].Add(p - pbefore);
                }
            }
            runs[1].Add(0);
            runs[2].Add(0);
            runs[3].Add(0);
            AddIntDump("color-runs-1", runs[1]);
            AddIntDump("color-runs-2", runs[2]);
            AddIntDump("color-runs-3", runs[3]);

            List<int>[] leftovers = new List<int>[4];
            leftovers[1] = mrleCutOff(runs[1], 31);
            leftovers[2] = mrleCutOff(runs[2], 31);
            leftovers[3] = mrleCutOff(runs[3], 31);

            ulong[][] probs1 = mrleGetProbs(runs[1].ToArray(), 31);
            ulong[][] probs2 = mrleGetProbs(runs[2].ToArray(), 31);
            ulong[][] probs3 = mrleGetProbs(runs[3].ToArray(), 31);

            SetCounter("bytes|colors|probs", -1);

            SetCounter("bytes|colors|shortruns|1", mrleEncodeShort(runs[1].ToArray(), probs1).Length);
            SetCounter("bytes|colors|shortruns|2", mrleEncodeShort(runs[2].ToArray(), probs2).Length);
            SetCounter("bytes|colors|shortruns|3", mrleEncodeShort(runs[3].ToArray(), probs3).Length);

            SetCounter("bytes|colors|longruns|1", mrleEncodeLong(leftovers[1].ToArray()).Length);
            SetCounter("bytes|colors|longruns|2", mrleEncodeLong(leftovers[2].ToArray()).Length);
            SetCounter("bytes|colors|longruns|3", mrleEncodeLong(leftovers[3].ToArray()).Length);
        }

        private void saveForeground(int[] data)
        {
            RunLength01Codec rle = new RunLength01Codec();
            var runs = rle.Encode(data);
            AddIntDump("foregr-runs", runs);
            var runsL = runs.ToList();
            var leftover = mrleCutOff(runsL, 40);
            var probs = mrleGetProbs(runsL.ToArray(), 40);
            SetCounter("bytes|foremap|probs", -1);
            var bytesShort = mrleEncodeShort(runsL.ToArray(), probs);
            SetCounter("bytes|foremap|short", bytesShort.Length);
            SetCounter("bytes|foremap|long", mrleEncodeLong(leftover.ToArray()).Length);
        }

        private List<int> mrleCutOff(List<int> data, int cutoff)
        {
            List<int> leftover = new List<int>();
            for (int p = 0; p < data.Count; p++)
                if (data[p] >= cutoff)
                {
                    leftover.Add(data[p] - cutoff);
                    data[p] = cutoff;
                }
            return leftover;
        }

        private ulong[][] mrleGetProbs(int[] data, int cutoff)
        {
            ulong[][] probs = new ulong[cutoff+1][];
            for (int i = 0; i <= cutoff; i++)
                probs[i] = new ulong[cutoff+1];
            int prev = 0;
            for (int p = 0; p < data.Length; p++)
            {
                probs[prev][data[p]]++;
                prev = data[p];
            }
            return probs;
        }

        private byte[] mrleEncodeShort(int[] data, ulong[][] probs)
        {
            MemoryStream ms = new MemoryStream();
            ArithmeticWriter aw = new ArithmeticWriter(ms, null);
            ulong[] tots = new ulong[probs.Length];
            for (int i = 0; i < tots.Length; i++)
                tots[i] = (ulong)probs[i].Sum(val => (long)val);

            int prev = 0;
            for (int p = 0; p < data.Length; p++)
            {
                aw.Probs = probs[prev];
                aw.TotalProb = tots[prev];
                aw.WriteSymbol(data[p]);
                prev = data[p];
            }
            aw.Flush();
            return ms.ToArray();
        }

        private byte[] mrleEncodeLong(int[] data)
        {
            MemoryStream ms = new MemoryStream();
            for (int p = 0; p < data.Length; p++)
                ms.WriteUInt32Optim((uint)data[p]);
            return ms.ToArray();
        }

        private void backgroundSplit(IntField image, out IntField backgr, out IntField foregr)
        {
            IntField largeBackgrMap;

            // First pass: all large areas are always backgrounds
            {
                IntField vis_hitpoints = image.Clone();
                largeBackgrMap = image.Clone();
                int x = 0, y = 0, xfr = 0;
                double yd = 0;
                double ystep = _backgrLargeStepSize * 0.866 / ((double)image.Width / _backgrLargeStepSize);
                int curfill = -2;
                while (true)
                {
                    y = (int)yd;
                    if (y >= image.Height)
                        break;

                    vis_hitpoints[x, y] = 10;
                    if (largeBackgrMap[x, y] >= 0)
                    {
                        largeBackgrMap.Floodfill(x, y, curfill);
                        curfill--;
                        if (largeBackgrMap.Counter_Floodfill_TotalPixels > _backgrLargeAreaThresh)
                            largeBackgrMap.Floodfill(x, y, -1);
                    }

                    yd += ystep;
                    x += _backgrLargeStepSize;
                    if (x >= image.Width)
                    {
                        xfr += _backgrLargeStepSize/3;
                        if (xfr > _backgrLargeStepSize)
                            xfr -= _backgrLargeStepSize;
                        x = xfr;
                    }
                }

                largeBackgrMap.Map(pix => pix == -1 ? 1 : 0);
                AddImageGrayscale(vis_hitpoints, "10-bkg-large-hitpoints");
                AddImageGrayscale(largeBackgrMap, "11-bkg-large-map");
            }

            // TODO: also background if perfectly rectangular and larger than 4 area

            // Second pass - background continuation
            {
                // Prepare image
                backgr = image.Clone();
                for (int p = 0; p < image.Data.Length; p++)
                    if (largeBackgrMap.Data[p] == 0)
                        backgr.Data[p] = -1;
                AddImageGrayscale(backgr, -1, 3, "12-bkg-cont-before");

                // Horizontal continuation
                for (int y = 0; y < backgr.Height; y++)
                {
                    int lastbg = -1;
                    int lastfgstart = -1;  // -1 means not in a foreground run
                    int x = 0;
                    while (true)
                    {
                        if (lastfgstart != -1 && (x == backgr.Width || backgr[x, y] != -1))
                        {   // in a fg run and either end of image or end of run (no longer foreground)
                            if (lastbg == -1 || x == backgr.Width || lastbg == backgr[x, y])
                            {
                                int clr = x == backgr.Width ? lastbg : backgr[x, y];
                                for (int fx = lastfgstart; fx < x; fx++)
                                    backgr[fx, y] = clr;
                            }
                            lastfgstart = -1;
                        }
                        if (x == backgr.Width)
                            break;
                        if (lastfgstart == -1 && backgr[x, y] == -1)
                        {
                            lastfgstart = x;
                            lastbg = x == 0 ? -1 : backgr[x-1, y];
                        }
                        x++;
                    }
                }
                AddImageGrayscale(backgr, -1, 3, "13-bkg-cont-horz");

                // Vertical continuation
                for (int x = 0; x < backgr.Width; x++)
                {
                    int lastbg = -1;
                    int lastfgstart = -1;  // -1 means not in a foreground run
                    int y = 0;
                    while (true)
                    {
                        if (lastfgstart != -1 && (y == backgr.Height || backgr[x, y] != -1))
                        {   // in a fg run and either end of image or end of run (no longer foreground)
                            if (lastbg == -1 || y == backgr.Height || lastbg == backgr[x, y])
                            {
                                int clr = y == backgr.Height ? lastbg : backgr[x, y];
                                for (int fy = lastfgstart; fy < y; fy++)
                                    backgr[x, fy] = clr;
                            }
                            lastfgstart = -1;
                        }
                        if (y == backgr.Height)
                            break;
                        if (lastfgstart == -1 && backgr[x, y] == -1)
                        {
                            lastfgstart = y;
                            lastbg = y == 0 ? -1 : backgr[x, y-1];
                        }
                        y++;
                    }
                }
                AddImageGrayscale(backgr, -1, 3, "14-bkg-cont-vert");

                // Continuation of leftover areas
                int lastc = -1;
                for (int p = 0; p < backgr.Data.Length; p++)
                {
                    if (backgr.Data[p] == -1 && lastc != -1)
                        backgr.Floodfill(p % backgr.Width, p / backgr.Width, lastc);
                    else if (p > 0 && backgr.Data[p-1] == -1 && backgr.Data[p] != -1)
                        backgr.Floodfill((p-1) % backgr.Width, (p-1) / backgr.Width, backgr.Data[p]);
                    if (backgr.Data[p] != -1)
                        lastc = backgr.Data[p];
                }

                // TODO: remove anything that's highly non-rect

                AddImageGrayscale(backgr, 0, 3, "15-final-bkg");
            }

            // Final pass - extract foreground
            {
                foregr = image.Clone();
                for (int p = 0; p < image.Data.Length; p++)
                    foregr.Data[p] ^= backgr.Data[p];
                AddImageGrayscale(foregr, 0, 3, "16-final-fgr");
            }
        }

        public override IntField Decode(Stream input)
        {
            throw new NotImplementedException();
        }
    }

}
