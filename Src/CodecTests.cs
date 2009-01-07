using System;
using System.Collections.Generic;
using NUnit.Framework;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace i4c
{
    public static class CodecTests
    {
        public static void All()
        {
            TestEncode();
            TestDecode();
            TestRandom();
        }

        public static void TestLzw()
        {
            LzwCodec lzw = new LzwCodec(5);
            int[] res = lzw.Encode(new int[] { 0, 1, 0, 1, 0, 1, 0, 1, 2, 3, 4, 2, 3, 4, 2, 3, 4 });
        }

        public static void TestEncode()
        {
            RunLengthCodec enc = new RunLengthCodec(4, 2, 2, 0);
            // 0, 1, 2 - data symbols
            // 3, 4 - run of 0, stage 0 and 1
            Assert.IsTrue(enc.Encode(new int[] { 0, 1, 2, 2, 2, 0, 0, 0, 0, 0, 0, 0 })
                .EqualItems(new int[] { 0, 1, 2, 2, 2, 4, 1, 0 }));

            enc = new RunLengthCodec(9, 2, 3, 0);
            // 0, 1 - data
            // 2, 3, 4 - run of 0, stage 0, 1, 2
            Assert.IsTrue(enc.Encode(new int[] { 0, 1, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })
                .EqualItems(new int[] { 0, 1, 1, 0, 1, 4, 5, 0 }));

            enc = new RunLengthCodec(5, 2, 3, 0);
            // 0, 1 - data
            // 2, 3, 4 - run of 0, stage 0, 1, 2
            Assert.IsTrue(enc.Encode(new int[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })
                .EqualItems(new int[] { 4, 3, 5 }));
        }

        public static void TestDecode()
        {
            RunLengthCodec dec = new RunLengthCodec(4, 2, 2, 0);
            // 0, 1, 2 - data symbols
            // 3, 4 - run of 0, stage 0 and 1
            Assert.IsTrue(dec.Decode(new int[] { 0, 0, 0, 1, 1, 2, 2, 2 })
                .EqualItems(new int[] { 0, 0, 0, 1, 1, 2, 2, 2 }));
            Assert.IsTrue(dec.Decode(new int[] { 3, 2 })
                .EqualItems(new int[] { 0, 0, 0 }));
            Assert.IsTrue(dec.Decode(new int[] { 3, 0, 3, 2, 1, 3, 4, 2, 3, 0, 4, 0, 0 })
                .EqualItems(new int[] { 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0 }));
        }

        public static void TestRandom()
        {
            for (int iter = 0; iter < 10000; iter++)
            {
                int symDataMax = Ut.RndInt(1, 20);
                int rlStages = Ut.RndInt(1, 4);
                int symRles = Ut.RndInt(1, Math.Min(symDataMax, 3));
                int symMax = Ut.RndInt(symDataMax + rlStages*symRles, symDataMax + rlStages*symRles + 10);
                int[] symRle = new int[symRles];
                for (int i = 0; i < symRles; i++)
                    symRle[i] = i;

                int dataLen;
                if (Ut.RndDouble() < 0.5)
                    dataLen = Ut.RndInt(0, 10);
                else
                    dataLen = Ut.RndInt(0, 50000);

                List<int> data = new List<int>();
                while (dataLen > 0)
                {
                    if (Ut.RndDouble() > 0.03)
                    {
                        data.Add(Ut.RndInt(0, symDataMax+1));
                        dataLen--;
                    }
                    else
                    {
                        int runLen = Ut.RndInt(1, dataLen);
                        int runSym = symRle[Ut.RndInt(0, symRle.Length)];
                        for (int i = 0; i < runLen; i++)
                            data.Add(runSym);
                        dataLen -= runLen;
                    }
                }

                TestData(data.ToArray(), symMax, symDataMax, rlStages, symRle);
            }
        }

        public static void TestData(int[] data, int symMax, int symDataMax, int rlStages, params int[] symRle)
        {
            int[] trip;

            //RunLengthCodec enc = new RunLengthCodec(symMax, symDataMax, rlStages, symRle);
            //RunLengthCodec dec = new RunLengthCodec(symMax, symDataMax, rlStages, symRle);
            //trip = dec.Decode(enc.Encode(data));
            //Assert.IsTrue(data.EqualItems(trip));

            //ulong[] probs = CodecUtil.CountValues(data);
            //ArithmeticCodec enca = new ArithmeticCodec(probs);
            //ArithmeticCodec deca = new ArithmeticCodec(probs);
            //trip = deca.Decode(enca.Encode(data));
            //Assert.IsTrue(data.EqualItems(trip));

            if (symDataMax == 1)
            {
                //RunLength01Codec encz = new RunLength01Codec();
                //RunLength01Codec decz = new RunLength01Codec();
                //trip = decz.Decode(encz.Encode(data));
                //Assert.IsTrue(data.EqualItems(trip));

                //RunLength01MaxCodec enczm = new RunLength01MaxCodec(15);
                //RunLength01MaxCodec deczm = new RunLength01MaxCodec(15);
                //trip = deczm.Decode(enczm.Encode(data));
                //Assert.IsTrue(data.EqualItems(trip));

                RunLength01MaxSmartCodec enczs = new RunLength01MaxSmartCodec(15);
                RunLength01MaxSmartCodec deczs = new RunLength01MaxSmartCodec(15);
                trip = deczs.Decode(enczs.Encode(data));
                Assert.IsTrue(data.EqualItems(trip));
            }
        }
    }
}
