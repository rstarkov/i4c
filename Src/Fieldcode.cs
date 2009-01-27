using System.Collections.Generic;
using System.IO;
using RT.Util.ExtensionMethods;

namespace i4c
{
    public class Fieldcode
    {
        Compressor _compr;
        int _symbols;

        public Fieldcode(Compressor compr, int symbols)
        {
            _compr = compr;
            _symbols = symbols;
        }

        public byte[] EnFieldcode(IntField transformed)
        {
            _compr.AddImage(transformed, 0, 3, "enfield-xformed");
            var fields = new List<int[]>();
            IntField temp;
            ulong[] probs = new ulong[_symbols + 2];
            RunLength01MaxSmartCodec zc = new RunLength01MaxSmartCodec(_symbols);
            for (int i = 1; i <= 3; i++)
            {
                temp = transformed.Clone();
                temp.Map(x => x == i ? 1 : 0);
                var field = zc.Encode(temp.Data);
                CodecUtil.Shift(field, 1);
                _compr.SetCounter("symbols|field-"+i, field.Length);
                fields.Add(field);
                var prob = CodecUtil.CountValues(field);
                for (int a = 1; a < prob.Length; a++)
                    probs[a] += prob[a];
                _compr.AddImage(temp, 0, 1, "enfield-f" + i);
            }
            probs[0] = 6;

            long pos = 0;
            MemoryStream ms = new MemoryStream();
            ms.WriteUInt32Optim((uint)transformed.Width);
            ms.WriteUInt32Optim((uint)transformed.Height);
            _compr.SetCounter("bytes|size", ms.Position - pos);
            pos = ms.Position;

            for (int p = 0; p < probs.Length; p++)
                ms.WriteUInt64Optim(probs[p]);
            _compr.SetCounter("bytes|probs", ms.Position - pos);
            pos = ms.Position;

            ArithmeticSectionsCodec ac = new ArithmeticSectionsCodec(probs, 6, ms);
            for (int i = 0; i < fields.Count; i++)
            {
                ac.WriteSection(fields[i]);
                _compr.SetCounter("bytes|fields|"+(i+1), ms.Position - pos);
                pos = ms.Position;
            }
            ac.Encode();

            var arr = ms.ToArray();
            _compr.SetCounter("bytes|arith-err", arr.Length - pos);

            return arr;
        }

        public IntField DeFieldcode(byte[] bytes)
        {
            MemoryStream ms = new MemoryStream(bytes);
            int w = ms.ReadUInt32Optim();
            int h = ms.ReadUInt32Optim();
            IntField transformed = new IntField(w, h);
            ulong[] probs = new ulong[_symbols + 2];
            for (int p = 0; p < probs.Length; p++)
                probs[p] = ms.ReadUInt64Optim();

            ArithmeticSectionsCodec ac = new ArithmeticSectionsCodec(probs, 6);
            ac.Decode(ms);
            for (int i = 1; i <= 3; i++)
            {
                int[] fieldi = ac.ReadSection();
                CodecUtil.Shift(fieldi, -1);

                RunLength01MaxSmartCodec zc = new RunLength01MaxSmartCodec(_symbols);
                fieldi = zc.Decode(fieldi);

                for (int p = 0; p < transformed.Width*transformed.Height; p++)
                    if (fieldi[p] == 1)
                        transformed.Data[p] = i;

                IntField img = new IntField(transformed.Width, transformed.Height);
                img.Data = fieldi;
                _compr.AddImage(img, 0, 1, "defield-f" + i);
            }

            _compr.AddImage(transformed, 0, 3, "defield-xformed");

            return transformed;
        }
    }
}
