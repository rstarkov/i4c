using System;
using System.IO;
using RT.KitchenSink.Collections;
using RT.Util.ExtensionMethods;
using RT.Util.Streams;

namespace i4c
{
    public class TimwiCec : Compressor
    {
        public static ulong[] kindProbsProbs = new ulong[32] { 1599, 370, 174, 137, 76, 59, 41, 36, 31, 18, 18, 21, 15, 10, 9, 5, 12, 6, 7, 8, 2, 6, 2, 4, 4, 1, 3, 7, 5, 1, 2, 128 };
        public static ulong[] runLProbsProbs = new ulong[32] { 594, 274, 294, 231, 176, 134, 84, 67, 54, 47, 54, 31, 29, 29, 13, 18, 18, 21, 16, 11, 14, 19, 6, 8, 14, 14, 8, 9, 3, 7, 7, 512 };

        int blocksizeX, blocksizeY;

        public TimwiCec()
        {
            Config = new RVariant[] { 8, 8 };
        }

        public override void Configure(params RVariant[] args)
        {
            base.Configure(args);
            blocksizeX = (int) Config[0];
            blocksizeY = (int) Config[1];
        }

        public override void Encode(IntField source, Stream output)
        {
            int bw = source.Width;
            int bh = source.Height;
            int bwbh = bw * bh;
            int blocksX = (bw + blocksizeX - 1) / blocksizeX;
            int blocksY = (bh + blocksizeY - 1) / blocksizeY;
            int blocks = blocksX * blocksY;

            source.ArgbTo4c();
            uint[] pixels = source.Data.Select(px => (uint) px).ToArray();

            var newPixels = new uint[pixels.Length];
            int[] kinds = new int[blocks];
            ulong[] blockHigh = new ulong[blocks];
            ulong[] blockLow = new ulong[blocks];
            var predictor = new Efficient128bitHashTable<ulong[]>();
            ulong context1 = 0, context2 = 0;

            // Predictive transform
            for (int i = 0; i < bwbh; i++)
            {
                int x = i % bw;
                uint p = pixels[i];
                bool fallback = false;

                if (i < 7 * bw || x < 3)
                    fallback = true;
                else
                {
                    if (x == 3)
                    {
                        context1 = 0;
                        context2 = 0;
                        for (int yy = -7 * bw; yy <= 0; yy += bw)
                            for (int xx = -3; (xx <= 4) && (xx + yy < 0); xx++)
                            {
                                context1 = (context1 << 2) | ((context2 & 0xC000000000000000) >> 62);
                                context2 = (context2 << 2) | pixels[i + xx + yy];
                            }
                    }
                    else
                    {
                        context1 = ((context1 << 2) & 0x3FFF3FFF3FFF3C) | ((context2 & 0xC000000000000000) >> 62);
                        context2 = ((context2 << 2) & 0xFF3FFF3FFF3FFF3C) | (ulong) pixels[i - 1];
                        if (x + 4 < bw)
                        {
                            context1 |= ((ulong) pixels[i - 7 * bw + 4] << 38) | ((ulong) pixels[i - 6 * bw + 4] << 22) | ((ulong) pixels[i - 5 * bw + 4] << 6);
                            context2 |= ((ulong) pixels[i - 4 * bw + 4] << 54) | ((ulong) pixels[i - 3 * bw + 4] << 38) | ((ulong) pixels[i - 2 * bw + 4] << 22) | ((ulong) pixels[i - 1 * bw + 4] << 6);
                        }
                    }
                    ulong[] prev = predictor.Get(context1, context2);
                    if (prev == null)
                    {
                        prev = new ulong[4];
                        predictor.Add(context1, context2, prev);
                        fallback = true;
                    }
                    else
                    {
                        ulong p0 = prev[0], p1 = prev[1], p2 = prev[2], p3 = prev[3];
                        p = p ^ ((p0 >= p1 && p0 >= p2 && p0 >= p3) ? (byte) 0 :
                            (p1 >= p2 && p1 >= p3) ? (byte) 1 :
                            (p2 >= p3) ? (byte) 2 : (byte) 3);
                    }
                    prev[pixels[i]]++;
                }

                if (fallback)
                {
                    // Fall back to old XOR transform
                    if (x > 0 && i >= bw)
                    {
                        if (pixels[i - 1] == pixels[i - bw - 1])
                            p = p ^ pixels[i - bw];
                        else
                            p = p ^ pixels[i - 1];
                    }
                    else if (x > 0)
                        p = p ^ pixels[i - 1];
                    else if (i >= bw)
                        p = p ^ pixels[i - bw];
                }

                if (p != 0)
                {
                    newPixels[i] = p;
                    int blockIndex = ((i / bw) / blocksizeY) * blocksX + x / blocksizeX;
                    kinds[blockIndex] |= 1 << (int) (p - 1);
                    int indexInBlock = ((i / bw) % blocksizeY) * blocksizeX + x % blocksizeX;
                    if (indexInBlock < 32)
                        blockLow[blockIndex] |= (ulong) p << (2 * indexInBlock);
                    else
                        blockHigh[blockIndex] |= (ulong) p << (2 * (indexInBlock - 32));
                }
            }

            // Start writing to the file
            long pos = 0;

            output.WriteUInt32Optim((uint) bw);
            output.WriteUInt32Optim((uint) bh);

            SetCounter("bytes|size", output.Position - pos);
            pos = output.Position;

            // Some slight optimisations
            for (int i = 0; i < blocks; i++)
            {
                var k = kinds[i];

                // If a kind-5 block is sandwiched between two kind-7s, it is worth setting to 7 to improve the run-length encoding
                if (k == 5 && i > 0 && i < blocks - 1 && kinds[i - 1] == 7 && kinds[i + 1] == 7)
                    k = kinds[i] = 7;

                // Kinds 3 and 6 are the rarest. It is more common for a block to have its top or bottom half all black.
                // Therefore, redefine 3 and 6 to mean top and bottom half all black.
                // A block that has kind 5 should also be changed to 3 or 6 if it has one half all black, because
                // having kind 5 reduces the block's entropy by 1/3, but having one half all black by 1/2.
                if (k == 3 || k > 4)
                    kinds[i] = blockLow[i] == 0 ? 3 : blockHigh[i] == 0 ? 6 : k == 5 ? 5 : 7;
            }

            // Encode the kinds as three planes of optim-encoded run lengths
            long pos2 = 0;
            var ms = new MemoryStream();
            bool curRunData = false;
            uint curRunLength = 0;
            for (int c = 0; c < 3; c++)
            {
                for (int i = 0; i < blocks; i++)
                {
                    if (curRunData ^ ((kinds[i] & (1 << c)) != 0))
                    {
                        ms.WriteUInt32Optim(curRunLength);
                        curRunData = !curRunData;
                        curRunLength = 1;
                    }
                    else
                        curRunLength++;
                }
                SetCounter("kinds-raw|" + c, ms.Position - pos2);
                pos2 = ms.Position;
            }
            ms.WriteUInt32Optim(curRunLength);
            ms.Close();
            var kindsArr = ms.ToArray();
            SetCounter("kinds-raw|error", kindsArr.Length - pos2);

            // Save the frequencies of each byte in the Optim-encoded run lengths
            ulong[] kFreq = new ulong[256];
            for (int i = 0; i < kindsArr.Length; i++)
                kFreq[kindsArr[i]]++;
            CodecUtil.SaveFreqs(output, kFreq, kindProbsProbs, "kindProbsProbs");

            SetCounter("bytes|kinds|probs", output.Position - pos);
            pos = output.Position;

            // Save the run lengths themselves
            MemoryStream kMaster = new MemoryStream();
            var kAcw = new ArithmeticCodingWriter(kMaster, kFreq);
            foreach (var symb in kindsArr)
                kAcw.WriteSymbol(symb);
            kAcw.Close(false);
            var kArr = kMaster.ToArray();
            output.WriteUInt32Optim((uint) kindsArr.Length);
            output.WriteUInt32Optim((uint) kArr.Length);
            output.Write(kArr, 0, kArr.Length);
            //Console.WriteLine("krl " + kArr.Length);

            SetCounter("bytes|kinds|data", output.Position - pos);
            pos = output.Position;

            /*
            Bitmap bXor = PixelsToBitmap(newPixels, bw, bh);
            bXor.Save(target + "-xor.png");
            Bitmap bBlocks = KindsToBitmap(kinds, blocksizeX, blocksizeY, bw, bh);
            Graphics g = Graphics.FromImage(bXor);
            GraphicsUtil.DrawImageAlpha(g, bBlocks, new Rectangle(0, 0, bw, bh), 0.5f);
            bXor.Save(target + "-xor-blocks.png");
            /**/

            // Create the sequence of symbols that represents the run lengths for the actual pixels
            ms = new MemoryStream();

            int j = 0;
            ulong curLength = 0;
            var kind = kinds[0];
            while (j < 3 * bwbh)
            {
                var c = j / bwbh;
                var i = j % bwbh;
                var x = i % bw;
                if (x % blocksizeX == 0)
                    kind = kinds[((i / bw) / blocksizeY) * blocksX + x / blocksizeX];
                j++;
                if (kind == 3)
                {
                    if (((i / bw) % blocksizeY) * blocksizeX + x % blocksizeX < 32) continue;
                    kind = 7;
                }
                else if (kind == 6)
                {
                    if (((i / bw) % blocksizeY) * blocksizeX + x % blocksizeX > 31) continue;
                    kind = 7;
                }
                else if (((kind % 8) & (1 << c)) == 0)
                    continue;

                if (newPixels[i] == c + 1)
                {
                    ms.WriteUInt64Optim(curLength);
                    curLength = 0;
                }
                else if (newPixels[i] == 0 || newPixels[i] > c + 1)
                    curLength++;
            }
            if (curLength > 0)
                ms.WriteUInt64Optim(curLength);

            ms.Close();
            var runLengthsArr = ms.ToArray();

            // Save the frequencies of each byte in the Optim-encoded run lengths
            ulong[] freq = new ulong[256];
            for (int i = 0; i < runLengthsArr.Length; i++)
                freq[runLengthsArr[i]]++;
            CodecUtil.SaveFreqs(output, freq, runLProbsProbs, "runLProbsProbs");

            SetCounter("bytes|runs|probs", output.Position - pos);
            pos = output.Position;

            // Save the run lengths themselves
            MemoryStream master = new MemoryStream();
            master.WriteUInt32Optim((uint) runLengthsArr.Length);
            var acw = new ArithmeticCodingWriter(master, freq);
            foreach (var symb in runLengthsArr)
                acw.WriteSymbol(symb);
            acw.Close(false);
            var arr = master.ToArray();
            output.Write(arr, 0, arr.Length);
            //Console.WriteLine("rl " + arr.Length);

            SetCounter("bytes|runs|data", output.Position - pos);
            pos = output.Position;

            output.Close();

        }

        public override IntField Decode(Stream input)
        {
            throw new NotImplementedException();
        }
    }

    public class Efficient128bitHashTable<T>
    {
        private class element
        {
            public ulong _low, _high;
            public T _value;
        };

        private element[][] _buckets;
        private uint _capacity;

        public Efficient128bitHashTable()
        {
            _capacity = 213307;    // some largish prime number
            _buckets = new element[_capacity][];
        }

        public uint hash(ulong low, ulong high)
        {
            ulong uhash = ((ulong.MaxValue % _capacity) + 1) * (high % _capacity);
            return (uint) (((uhash % _capacity) + (low % _capacity)) % _capacity);
        }

        public void Add(ulong low, ulong high, T value)
        {
            uint hsh = hash(low, high);
            element[] e;
            if (_buckets[hsh] == null)
                _buckets[hsh] = e = new element[1];
            else
            {
                e = new element[_buckets[hsh].Length + 1];
                Array.Copy(_buckets[hsh], 0, e, 1, _buckets[hsh].Length);
                _buckets[hsh] = e;
            }
            e[0] = new element { _high = high, _low = low, _value = value };
            /*
            if (e.Length > 6)
            {
                Console.WriteLine("bad collision! Hash: " + hsh);
                foreach (var el in e)
                    Console.WriteLine("- {0} {1}".Fmt(el._low.ToString("X16"), el._high.ToString("X16")));
            }
            */
        }

        public T Get(ulong low, ulong high)
        {
            uint hsh = hash(low, high);
            element[] e = _buckets[hsh];
            if (e == null) return default(T);
            foreach (var f in e)
                if (f._low == low && f._high == high)
                    return f._value;
            return default(T);
        }

        public int Count()
        {
            int r = 0;
            foreach (var e in _buckets)
                if (e != null)
                    r += e.Length;
            return r;
        }
    }
}
