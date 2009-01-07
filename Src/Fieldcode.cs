﻿using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using RT.Util.ExtensionMethods;
using RT.Util.Streams;

namespace i4c
{
    public class Fieldcode
    {
        public byte[] EnFieldcode(IntField transformed, int symbols, string fieldNames)
        {
            var fields = new List<int[]>();
            IntField temp;
            ulong[] probs = new ulong[symbols + 2];
            RunLength01MaxSmartCodec zc = new RunLength01MaxSmartCodec(symbols);
            for (int i = 1; i <= 3; i++)
            {
                temp = transformed.Clone();
                temp.Map(x => x == i ? 1 : 0);
                var field = zc.Encode(temp.Data);
                field = new LzwCodec(symbols).Encode(field);
                CodecUtil.Shift(field, 1);
                fields.Add(field);
                var prob = CodecUtil.CountValues(field);
                if (probs.Length < prob.Length)
                    Array.Resize(ref probs, prob.Length);
                for (int a = 1; a < prob.Length; a++)
                    probs[a] += prob[a];

                if (fieldNames != null)
                {
                    temp.ArgbFromField(0, 1);
                    temp.ArgbToBitmap().Save(fieldNames.Fmt(i), ImageFormat.Png);
                    CodecUtil.Shift(field, -1);
                    File.WriteAllLines(fieldNames.Fmt(i), field.Select(num => num.ToString()).ToArray());
                    CodecUtil.Shift(field, 1);
                }
            }
            probs[0] = 6;

            MemoryStream ms = new MemoryStream();
            BinaryWriterPlus bwp = new BinaryWriterPlus(ms);
            bwp.WriteUInt32Optim((uint)transformed.Width);
            bwp.WriteUInt32Optim((uint)transformed.Height);
            for (int p = 0; p < probs.Length; p++)
                bwp.WriteUInt64Optim(probs[p]);

            ArithmeticSectionsCodec ac = new ArithmeticSectionsCodec(probs, 6, ms);
            foreach (var field in fields)
                ac.WriteSection(field);
            ac.Encode();

            return ms.ToArray();
        }

        public IntField DeFieldcode(byte[] bytes, int symbols)
        {
            MemoryStream ms = new MemoryStream(bytes);
            BinaryReaderPlus brp = new BinaryReaderPlus(ms);
            int w = brp.ReadUInt32Optim();
            int h = brp.ReadUInt32Optim();
            IntField transformed = new IntField(w, h);
            ulong[] probs = new ulong[symbols + 2];
            for (int p = 0; p < probs.Length; p++)
                probs[p] = brp.ReadUInt64Optim();

            ArithmeticSectionsCodec ac = new ArithmeticSectionsCodec(probs, 6);
            ac.Decode(ms);
            for (int i = 1; i <= 3; i++)
            {
                int[] fieldi = ac.ReadSection();
                CodecUtil.Shift(fieldi, -1);

                RunLength01MaxSmartCodec zc = new RunLength01MaxSmartCodec(symbols);
                fieldi = zc.Decode(fieldi);

                for (int p = 0; p < transformed.Width*transformed.Height; p++)
                    if (fieldi[p] == 1)
                        transformed.Data[p] = i;
            }

            return transformed;
        }

        //byte[] ReducedProbsEn(ulong[] probs, int exactStart, int combineCount, int exactEnd)
        //{
        //    MemoryStream ms = new MemoryStream();
        //    BinaryWriterPlus bwp = new BinaryWriterPlus(ms);
        //    int endBoundary = probs.Length - exactEnd;
        //    int p = 0;
        //    for (; p < exactStart; p++)
        //        bwp.WriteUInt64Optim(probs[p]);
        //    while (true)
        //    {
        //        // Update

        //        if (p >= endBoundary)
        //            break;
        //    }
        //    for (p += combineCount; p < endBoundary; p += combineCount)
        //    {
        //    }
        //    for (p = endBoundary; p < probs.Length; p++)
        //        bwp.WriteUInt64Optim(probs[p]);
        //}

        //ulong[] FieldcodeMakeProbs(int width, int height)
        //{
        //    ulong[] result = new ulong[width*height];
        //    // all occurrences are multiplied by 1000 to represent symbols that occur less than once per stream
        //    result[0] = 6 * 1000; // fieldcode separator
        //    for (int runlen = 1; runlen < result.Length; runlen++)
        //    {
        //        if (runlen < width)
        //            result[runlen] = (int)(3172 * Math.Pow(runlen, -1.47));
        //        else
        //            result[runlen] = (runlen - 
        //    }
        //}


    }
}