using System;
using System.IO;
using System.Linq;
using RT.Util.ExtensionMethods;

namespace i4c
{
    public class XperimentBackLzw : Compressor
    {
        public override void Encode(IntField image, Stream output)
        {
            image.ArgbTo4c();
            IntField orig = image.Clone();
            //image.PredictionEnTransformXor(new HorzVertForeseer());
            //IntField backgr = CodecUtil.BackgroundFilterThin(image, 2, 2);
            IntField backgr = CodecUtil.BackgroundFilterSmall(image, 50);
            AddImageGrayscale(image, "orig");
            AddImageGrayscale(backgr, "backgr");

            for (int p = 0; p < image.Data.Length; p++)
                image.Data[p] ^= backgr.Data[p];

            AddImageGrayscale(image, "foregr");


            for (int i = 1; i <= 3; i++)
            {
                IntField field = image.Clone();
                field.Conditional(pix => pix == i);
                int[] syms = CodecUtil.LzwLinesEn(field, 4, 1);
                AddImageGrayscale(field, "field{0}-{1}syms-max{2}".Fmt(i, syms.Length, syms.Max()));
            }
        }

        public override IntField Decode(Stream input)
        {
            throw new NotImplementedException();
        }
    }
}
