using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using RT.Util.ExtensionMethods;
using RT.Util.Streams;

namespace i4c
{
    public static class CodecUtil
    {
        public static void Shift(int[] data, int shift)
        {
            for (int i = 0; i < data.Length; i++)
                data[i] += shift;
        }

        public static ulong[] CountValues(int[] data)
        {
            ulong[] result = new ulong[256];
            int maxSym = -1;
            foreach (var sym in data)
            {
                if (maxSym < sym)
                    maxSym = sym;
                if (sym >= result.Length)
                    Array.Resize(ref result, Math.Max(sym+1, result.Length * 2));
                result[sym]++;
            }
            Array.Resize(ref result, maxSym + 1);
            return result;
        }

        public static ulong[] CountValues(int[] data, int maxSymbol)
        {
            ulong[] result = CountValues(data);
            Array.Resize(ref result, maxSymbol + 1);
            return result;
        }

        public static Dictionary<int, ulong> CountValuesMap(int[] data)
        {
            Dictionary<int, ulong> result = new Dictionary<int, ulong>();
            foreach (var sym in data)
            {
                if (result.ContainsKey(sym))
                    result[sym]++;
                else
                    result.Add(sym, 1);
            }
            return result;
        }

        public static List<Rectangle> FindRectsFrom(IntField image, int xfr, int yfr)
        {
            List<Rectangle> rects = new List<Rectangle>();
            if (image[xfr, yfr] == 0)
                return rects;
            int xto_max = image.Width - 1;
            int yto_max = image.Height - 1;
            for (int yto = yfr; yto <= yto_max; yto++)
            {
                for (int xto = xfr; xto <= xto_max; xto++)
                {
                    if (image[xto, yto] == 0)
                    {
                        xto_max = xto-1;
                        break;
                    }
                }
                if (xto_max < xfr)
                    break;
                rects.Add(new Rectangle(xfr, yfr, xto_max - xfr + 1, yto - yfr + 1));
            }

            List<int> todelete = new List<int>();
            for (int i = 1; i < rects.Count; i++)
                if (rects[i].Width == rects[i-1].Width)
                    todelete.Add(i-1);
            for (int i = todelete.Count-1; i >= 0; i--)
                rects.RemoveAt(todelete[i]);

            return rects;
        }

        public static List<Rectangle> FindAllRects(IntField image)
        {
            List<Rectangle> rects = new List<Rectangle>();
            // optimize: the only starts can ever be on intersections of a black-to-white vertical boundary with b-to-w horz boundary
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                    if (image[x, y] != 0)
                        rects.AddRange(FindRectsFrom(image, x, y));

            List<int> todelete = new List<int>();
            for (int i = 1; i < rects.Count; i++)
                for (int j = 0; j < i; j++)
                    if (rects[j].Contains(rects[i].Left, rects[i].Top) && rects[j].Right == rects[i].Right && rects[j].Bottom == rects[i].Bottom)
                        if (!todelete.Contains(i))
                            todelete.Add(i);
            for (int i = todelete.Count-1; i >= 0; i--)
                rects.RemoveAt(todelete[i]);

            return rects.OrderBy(rect => -rect.Width * rect.Height).ToList();
        }

        public static Rectangle ShrinkRectangle(IntField image, Rectangle rect)
        {
            int fx = rect.Left;
            int fy = rect.Top;
            int tx = Math.Min(rect.Left + rect.Width - 1, image.Width - 1);
            int ty = Math.Min(rect.Top + rect.Height - 1, image.Height - 1);
            // Top
            while (fy <= ty)
            {
                for (int x = fx; x <= tx; x++)
                    if (image[x, fy] != 0)
                        goto doneTop;
                fy++;
            }
            doneTop: ;
            // Bottom
            while (ty >= fy)
            {
                for (int x = fx; x <= tx; x++)
                    if (image[x, ty] != 0)
                        goto doneBottom;
                ty--;
            }
            doneBottom: ;
            // Left
            while (fx <= tx)
            {
                for (int y = fy; y <= ty; y++)
                    if (image[fx, y] != 0)
                        goto doneLeft;
                fx++;
            }
            doneLeft: ;
            // Right
            while (tx >= fx)
            {
                for (int y = fy; y <= ty; y++)
                    if (image[tx, y] != 0)
                        goto doneRight;
                tx--;
            }
            doneRight: ;
            return new Rectangle(fx, fy, tx - fx + 1, ty - fy + 1);
        }

        public static List<Point> GetPixelCoords(IntField image, int color)
        {
            var indices = image.Data.Select((pix, ntx) => pix == color ? ntx : -1).Where(val => val >= 0);
            var result = new List<Point>();
            foreach (var index in indices)
                result.Add(new Point(index % image.Width, index / image.Width));
            return result;
        }

        public static void SaveFreqsCrappy(Stream saveTo, ulong[] freqs)
        {
            foreach (var f in freqs)
                saveTo.WriteUInt64Optim(f);
        }

        public static ulong[] LoadFreqsCrappy(Stream input, int maxSymbol)
        {
            ulong[] probs = new ulong[maxSymbol + 1];
            for (int p = 0; p < probs.Length; p++)
                probs[p] = input.ReadUInt64Optim();
            return probs;
        }

        /// <summary>
        /// USAGE WARNING: this modifies "freqs", and the /modified/ version is the one
        /// that is actually saved. Make sure to use the modified version in arithmetic codec.
        /// </summary>
        public static void SaveFreqs(Stream saveTo, ulong[] freqs, ulong[] probsProbs, string id)
        {
            int count = freqs.Length;
            MemoryStream master = new MemoryStream();

            MemoryStream ms = new MemoryStream();
            var aw = new ArithmeticWriter(ms, probsProbs);
            for (int i = 0; i < count; i++)
            {
                aw.WriteSymbol(freqs[i] >= 31 ? 31 : (int)freqs[i]);
            }
            aw.Flush();
            var arr = ms.ToArray();
            master.WriteUInt32Optim((uint)arr.Length);
            master.Write(arr, 0, arr.Length);
            for (int i = 0; i < count; i++)
                if (freqs[i] >= 31)
                {
                    freqs[i] -= freqs[i] % 31;
                    master.WriteUInt64Optim(freqs[i] / 31 - 1);
                }

            master.Close();
            arr = master.ToArray();
            saveTo.Write(arr, 0, arr.Length);
            //counters.IncSafe("freq " + id, (ulong) arr.Length);
            //Console.Write("freq " + arr.Length + "; ");
        }

        public static ulong[] LoadFreqs(Stream readFrom, ulong[] probsProbs, int numFreqs)
        {
            var len = readFrom.ReadUInt32Optim();
            byte[] arr = new byte[len];
            readFrom.Read(arr, 0, len);
            var acr = new ArithmeticCodingReader(new MemoryStream(arr), probsProbs);
            var freqs = new ulong[numFreqs];
            for (int i = 0; i < numFreqs; i++)
                freqs[i] = (ulong)acr.ReadSymbol();
            acr.Close();
            for (int i = 0; i < numFreqs; i++)
                if (freqs[i] == 31)
                    freqs[i] = (readFrom.ReadUInt64Optim() + 1) * 31;
            return freqs;
        }

        public static ulong[] GetFreqsForAllSections(IEnumerable<int[]> sections, int maxSymbol)
        {
            var probs = new ulong[maxSymbol + 1];
            foreach (var section in sections)
                foreach (var value in section)
                    probs[value]++;
            return probs;
        }

        public static List<int[]> FieldcodeRunlengthsEn(IntField image, SymbolCodec runlengthCodec, Compressor compr)
        {
            compr.AddImageGrayscale(image, 0, 3, "xformed");
            var fields = new List<int[]>();
            for (int i = 1; i <= 3; i++)
            {
                IntField temp = image.Clone();
                temp.Map(x => x == i ? 1 : 0);
                var field = runlengthCodec.Encode(temp.Data);
                CodecUtil.Shift(field, 1);
                compr.SetCounter("symbols|field-"+i, field.Length);
                fields.Add(field);
                compr.AddImageGrayscale(temp, 0, 1, "field" + i);
            }
            return fields;
        }

        public static IntField FieldcodeRunlengthsDe(List<int[]> fields, int width, int height, SymbolCodec runlengthCodec, Compressor compr)
        {
            IntField image = new IntField(width, height);
            for (int i = 1; i <= 3; i++)
            {
                CodecUtil.Shift(fields[i-1], -1);
                var f = runlengthCodec.Decode(fields[i-1]);

                for (int p = 0; p < width*height; p++)
                    if (f[p] == 1)
                        image.Data[p] = i;

                // Visualise
                IntField img = new IntField(width, height);
                img.Data = f;
                compr.AddImageGrayscale(img, 0, 1, "field" + i);
            }
            compr.AddImageGrayscale(image, 0, 3, "xformed");
            return image;
        }

        public static int[] FieldcodeRunlengthsEn2(IntField image, SymbolCodec runlengthCodec, Compressor compr)
        {
            compr.AddImageGrayscale(image, 0, 3, "xformed");
            List<int> data = new List<int>();
            for (int i = 1; i <= 3; i++)
            {
                IntField temp = image.Clone();
                temp.Map(x => x == i ? 1 : 0);
                data.AddRange(temp.Data);
                compr.AddImageGrayscale(temp, 0, 1, "field" + i);
            }
            var runs = runlengthCodec.Encode(data.ToArray());
            compr.SetCounter("runs", runs.Length);
            return runs;
        }

        public static IntField FieldcodeRunlengthsDe2(int[] runs, int width, int height, SymbolCodec runlengthCodec, Compressor compr)
        {
            IntField image = new IntField(width, height);
            var data = runlengthCodec.Decode(runs);
            for (int i = 1; i <= 3; i++)
            {
                int offs = width*height * (i-1);
                for (int p = 0; p < width*height; p++)
                    if (data[offs + p] == 1)
                        image.Data[p] = i;

                // Visualise
                IntField img = new IntField(width, height);
                Array.Copy(data, offs, img.Data, 0, width*height);
                compr.AddImageGrayscale(img, 0, 1, "field" + i);
            }
            compr.AddImageGrayscale(image, 0, 3, "xformed");
            return image;
        }

        public static IntField BackgroundFilterThin(IntField image, int w1passes, int w2passes)
        {
            image = image.Clone();

            bool changed = true;
            int counter = 0;
            while (changed)
            {
                changed = false;
                for (int y = 0; y < image.Height; y++)
                    for (int x = 1; x < image.Width-1; x++)
                        if (image[x-1, y] == image[x+1, y] && image[x, y] != image[x-1, y])
                        {
                            image[x, y] = image[x-1, y];
                            changed = true;
                        }

                for (int x = 0; x < image.Width; x++)
                    for (int y = 1; y < image.Height-1; y++)
                        if (image[x, y-1] == image[x, y+1] && image[x, y] != image[x, y-1])
                        {
                            image[x, y] = image[x, y-1];
                            changed = true;
                        }

                counter++;
                if (counter >= w1passes)
                    break;
            }

            changed = true;
            counter = 0;
            while (changed)
            {
                changed = false;
                for (int y = 0; y < image.Height; y++)
                    for (int x = 2; x < image.Width-1; x++)
                    {
                        if (image[x-2, y] == image[x+1, y] && image[x, y] != image[x-2, y])
                        {
                            image[x, y] = image[x-2, y];
                            changed = true;
                        }
                        if (image[x-2, y] == image[x+1, y] && image[x-1, y] != image[x-2, y])
                        {
                            image[x-1, y] = image[x-2, y];
                            changed = true;
                        }
                    }

                for (int x = 0; x < image.Width; x++)
                    for (int y = 2; y < image.Height-1; y++)
                        if (image[x, y-2] == image[x, y+1] && image[x, y-1] != image[x, y-2])
                        {
                            image[x, y-1] = image[x, y-2];
                            changed = true;
                        }

                counter++;
                if (counter >= w2passes)
                    break;
            }

            return image;
        }

        public static IntField BackgroundFilterSmall(IntField image, int areaThresh)
        {
            image = image.Clone();
            IntField processed = image.Clone();
            // 0..3 - original unprocessed colors
            // -1   - accepted as background
            // -2   - trial fill / kept for later elimination
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                    if (processed[x, y] >= 0)
                    {
                        // Do trial fill
                        processed.Floodfill(x, y, -2);
                        // Accept as background?
                        if (processed.Counter_Floodfill_TotalPixels > areaThresh)
                            processed.Floodfill(x, y, -1);
                    }

            for (int p = 0; p < image.Data.Length; p++)
                if (processed.Data[p] == -2)
                    image.Data[p] = -2;

            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                    if (processed[x, y] == -2)
                    {
                        if (x > 0 && processed[x-1, y] == -1)
                        {
                            image.Floodfill(x, y, image[x-1, y]);
                            processed.Floodfill(x, y, -3);
                        }
                        else if (y > 0 && processed[x, y-1] == -1)
                        {
                            image.Floodfill(x, y, image[x, y-1]);
                            processed.Floodfill(x, y, -3);
                        }
                        else if (x < image.Width-1 && processed[x+1, y] == -1)
                        {
                            image.Floodfill(x, y, image[x+1, y]);
                            processed.Floodfill(x, y, -3);
                        }
                        else if (y < image.Height-1 && processed[x, y+1] == -1)
                        {
                            image.Floodfill(x, y, image[x, y+1]);
                            processed.Floodfill(x, y, -3);
                        }
                    }

            return image;
        }

        public static int[] BitImageToLineSymbols(IntField image, int lineheight)
        {
            int[] result = new int[image.Width * ((image.Height + lineheight - 1) / lineheight)];
            int p = 0;
            for (int ly = 0; ly < image.Height; ly+=lineheight)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    int val = 0;
                    for (int y = ly; y < ly+lineheight && y < image.Height; y++)
                        val = (val << 1) | (image[x, y] == 0 ? 0 : 1);
                    result[p++] = val;
                }
            }
            return result;
        }

        public static int[] LzwLinesEn(IntField bitfield, int lineheight, int backadd)
        {
            int[] symbols = BitImageToLineSymbols(bitfield, lineheight);
            LzwCodec lzw = new LzwCodec(1 << lineheight);
            return lzw.Encode(symbols);
        }

        public static int[] InterleaveNegatives(int[] data)
        {
            int[] res = (int[])data.Clone();
            for (int i = 0; i < res.Length; i++)
                res[i] = (res[i] <= 0) ? (-2*res[i]) : (2*res[i] - 1);
            return res;
        }

        public static ulong[] GetNextProbsGiven(int[] vals, int given)
        {
            ulong[] result = new ulong[16];
            int prev = 0;
            for (int i = 1; i < vals.Length; i++)
            {
                if (prev == given)
                {
                    if (vals[i] >= result.Length)
                        Array.Resize(ref result, (vals[i]+1) * 3 / 2);
                    result[vals[i]]++;
                }
                prev = vals[i];
            }
            return result;
        }

        public static ulong[] GetNextProbsGivenGreater(int[] vals, int given)
        {
            ulong[] result = new ulong[16];
            int prev = 0;
            for (int i = 1; i < vals.Length; i++)
            {
                if (prev > given)
                {
                    if (vals[i] >= result.Length)
                        Array.Resize(ref result, (vals[i]+1) * 3 / 2);
                    result[vals[i]]++;
                }
                prev = vals[i];
            }
            return result;
        }
    }

    public class DeltaTracker
    {
        long _previous;

        public DeltaTracker()
        {
            _previous = 0;
        }

        public DeltaTracker(long initial)
        {
            _previous = initial;
        }

        public int Next(int newvalue)
        {
            int result = newvalue - (int)_previous;
            _previous = newvalue;
            return result;
        }

        public long Next(long newvalue)
        {
            long result = newvalue - _previous;
            _previous = newvalue;
            return result;
        }
    }
}
