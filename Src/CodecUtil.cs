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

        public static void SaveFreqs(Stream saveTo, ulong[] freqs, ulong[] probsProbs, string id)
        {
            int count = freqs.Length;
            MemoryStream master = new MemoryStream();

            MemoryStream ms = new MemoryStream();
            var acw = new ArithmeticCodingWriter(ms, probsProbs);
            for (int i = 0; i < count; i++)
            {
                acw.WriteSymbol(freqs[i] >= 31 ? 31 : (int)freqs[i]);
            }
            acw.Close(false);
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
            compr.AddImage(image, 0, 3, "xformed");
            var fields = new List<int[]>();
            for (int i = 1; i <= 3; i++)
            {
                IntField temp = image.Clone();
                temp.Map(x => x == i ? 1 : 0);
                var field = runlengthCodec.Encode(temp.Data);
                CodecUtil.Shift(field, 1);
                compr.SetCounter("symbols|field-"+i, field.Length);
                fields.Add(field);
                compr.AddImage(temp, 0, 1, "field" + i);
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
                compr.AddImage(img, 0, 1, "field" + i);
            }
            compr.AddImage(image, 0, 3, "xformed");
            return image;
        }

        public static int[] FieldcodeRunlengthsEn2(IntField image, SymbolCodec runlengthCodec, Compressor compr)
        {
            compr.AddImage(image, 0, 3, "xformed");
            List<int> data = new List<int>();
            for (int i = 1; i <= 3; i++)
            {
                IntField temp = image.Clone();
                temp.Map(x => x == i ? 1 : 0);
                data.AddRange(temp.Data);
                compr.AddImage(temp, 0, 1, "field" + i);
            }
            var runs = runlengthCodec.Encode(data.ToArray());
            compr.SetCounter("runs", runs.Length);
            return runs;
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
