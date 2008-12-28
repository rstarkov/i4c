using System;
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
            List<int[]> fields = new List<int[]>();
            IntField temp;
            ulong[] probs = new ulong[symbols + 2];
            RunLength01MaxSmartCodec zc = new RunLength01MaxSmartCodec(symbols);
            for (int i = -3; i <= 3; i++)
            {
                if (i == 0)
                    continue;
                temp = transformed.Clone();
                temp.Map(x => x == i ? 1 : 0);
                var field = zc.Encode(temp.Data);
                CodecUtil.Shift(field, 1);
                fields.Add(field);
                var prob = CodecUtil.CountValues(field);
                for (int a = 1; a < prob.Length; a++)
                    probs[a] += prob[a];

                if (fieldNames != null)
                {
                    temp.ArgbFromField(0, 1);
                    temp.ArgbToBitmap().Save(fieldNames.Fmt(Math.Abs(i), i > 0 ? "+" : "-"), ImageFormat.Png);
                }
            }
            probs[0] = 6;

            //byte[] b_probs = ReducedProbsEn(probs, 32, 3, 16);
            MemoryStream ms = new MemoryStream();
            BinaryWriterPlus bwp = new BinaryWriterPlus(ms);
            for (int p = 0; p < probs.Length; p++)
                bwp.WriteUInt64Optim(probs[p]);
            bwp.Close();
            byte[] b_probs = ms.ToArray();

            ms = new MemoryStream();
            ArithmeticCodingWriter acw = new ArithmeticCodingWriter(ms, probs);
            foreach (var field in fields)
            {
                foreach (var sym in field)
                    acw.WriteSymbol(sym);
                acw.WriteSymbol(0);
            }
            acw.Close(false);
            byte[] b_data = ms.ToArray();

            return b_probs.Concat(b_data).ToArray();
        }

        public void DeFieldcode(byte[] bytes, IntField transformed, int symbols)
        {
            MemoryStream ms = new MemoryStream(bytes);
            BinaryReaderPlus brp = new BinaryReaderPlus(ms);
            ulong[] probs = new ulong[symbols + 2];
            for (int p = 0; p < probs.Length; p++)
                probs[p] = brp.ReadUInt64Optim();

            ArithmeticCodingReader acr = new ArithmeticCodingReader(ms, probs);
            for (int i = -3; i <= 3; i++)
            {
                if (i == 0)
                    continue;

                List<int> field = new List<int>();
                while (true)
                {
                    int sym = acr.ReadSymbol();
                    if (sym == 0)
                        break;
                    else
                        field.Add(sym);
                }

                int[] fieldi = field.ToArray();
                CodecUtil.Shift(fieldi, -1);

                RunLength01MaxSmartCodec zc = new RunLength01MaxSmartCodec(symbols);
                fieldi = zc.Decode(fieldi);

                for (int p = 0; p < transformed.Width*transformed.Height; p++)
                    if (fieldi[p] == 1)
                        transformed.Data[p] = i;
            }
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
