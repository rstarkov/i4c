using System;
using System.IO;
using RT.KitchenSink.Collections;
using RT.Util.ExtensionMethods;

namespace i4c
{
    public class XperimentSplit : Compressor
    {
        protected Foreseer Seer;

        public XperimentSplit()
        {
            Config = new RVariant[] { 7, 7, 3 };
        }

        public override void Configure(params RVariant[] args)
        {
            base.Configure(args);
            Seer = new FixedSizeForeseer((int) Config[0], (int) Config[1], (int) Config[2], new HorzVertForeseer());
        }

        public override void Encode(IntField image, Stream output)
        {
            // Predictive transform
            image.ArgbTo4c();
            image.PredictionEnTransformXor(Seer);

            // Convert to three fields' runlengths
            for (int i = 1; i <= 3; i++)
            {
                IntField temp = image.Clone();
                temp.Map(x => x == i ? 1 : 0);
                AddImageGrayscale(temp, 0, 1, "field" + i);
                var runs = new RunLength01SplitCodec().EncodeSplit(temp.Data);
                SetCounter("runs|field{0}|0s".Fmt(i), runs.Item1.Count);
                SetCounter("runs|field{0}|1s".Fmt(i), runs.Item2.Count);
                AddIntDump("field{0}-0s".Fmt(i), runs.Item1);
                AddIntDump("field{0}-1s".Fmt(i), runs.Item2);
            }
        }

        public override IntField Decode(Stream input)
        {
            throw new NotImplementedException();
        }
    }
}
