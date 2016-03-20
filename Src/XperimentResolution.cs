using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace i4c
{
    public class XperimentResolution : Compressor
    {
        public override void Encode(IntField image, Stream output)
        {
            image.ArgbTo4c();
            AddImageGrayscale(image, "res0");
            List<IntField> scales = new List<IntField>();
            scales.Add(image);
            while (scales.Last().Width > 100 && scales.Last().Height > 100)
                scales.Add(scales.Last().HalfResHighestCount());

            for (int i = 1; i < scales.Count; i++)
            {
                IntField predicted = new IntField(scales[i - 1].Width, scales[i - 1].Height);
                IntField shrunk = scales[i];
                AddImageGrayscale(shrunk, "res" + i);
                for (int y = 0; y < predicted.Height; y++)
                {
                    for (int x = 0; x < predicted.Width; x++)
                    {
                        int xm2 = x & 1;
                        int ym2 = y & 1;
                        int xd2 = x >> 1;
                        int yd2 = y >> 1;
                        if (xm2 == 0 && ym2 == 0) // original
                            predicted[x, y] = shrunk[xd2, yd2];
                        else if (ym2 == 0) // between horizontal pixels
                            predicted[x, y] = shrunk[xd2, yd2];
                        else
                            predicted[x, y] = shrunk[xd2, yd2];
                    }
                }
                //AddImageGrayscale(predicted, "pred"+i);
                for (int p = 0; p < predicted.Data.Length; p++)
                    predicted.Data[p] ^= scales[i - 1].Data[p];
                //AddImageGrayscale(predicted, "diff"+i);
            }
        }

        public override IntField Decode(Stream input)
        {
            throw new NotImplementedException();
        }
    }
}
