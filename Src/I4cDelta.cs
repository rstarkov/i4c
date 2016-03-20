using System.IO;
using RT.KitchenSink.Collections;
using RT.Util.ExtensionMethods;
using RT.Util.Streams;

namespace i4c
{
    public class I4cDelta : Compressor
    {
        protected Foreseer Seer;
        protected SymbolCodec RLE;

        public I4cDelta()
        {
            Config = new RVariant[] { 7, 7, 3, 1000, 10 };
        }

        public override void Configure(params RVariant[] args)
        {
            base.Configure(args);
            Seer = new FixedSizeForeseer((int) Config[0], (int) Config[1], (int) Config[2], new HorzVertForeseer());
            RLE = new RunLength01LongShortCodec((int) Config[3], (int) Config[4]);
        }

        public override void Encode(IntField image, Stream output)
        {
            // Predictive transform
            image.ArgbTo4c();
            image.PredictionEnTransformXor(Seer);

            // Convert to three fields' runlengths
            var fields = CodecUtil.FieldcodeRunlengthsEn2(image, RLE, this);
            SetCounter("rle|longer", (RLE as RunLength01LongShortCodec).Counter_Longers);
            SetCounter("rle|muchlonger", (RLE as RunLength01LongShortCodec).Counter_MuchLongers);

            // Write size
            DeltaTracker pos = new DeltaTracker();
            output.WriteUInt32Optim((uint) image.Width);
            output.WriteUInt32Optim((uint) image.Height);
            SetCounter("bytes|size", pos.Next(output.Position));

            // Write probs
            ulong[] probs = CodecUtil.CountValues(fields, RLE.MaxSymbol);
            CodecUtil.SaveFreqs(output, probs, TimwiCec.runLProbsProbs, "");
            SetCounter("bytes|probs", pos.Next(output.Position));

            // Write fields
            ArithmeticWriter aw = new ArithmeticWriter(output, probs);
            output.WriteUInt32Optim((uint) fields.Length);
            foreach (var sym in fields)
                aw.WriteSymbol(sym);
            aw.Flush();
            SetCounter("bytes|fields", pos.Next(output.Position));
        }

        public override IntField Decode(Stream input)
        {
            // Read size
            int w = (int) input.ReadUInt32Optim();
            int h = (int) input.ReadUInt32Optim();
            // Read probabilities
            ulong[] probs = CodecUtil.LoadFreqs(input, TimwiCec.runLProbsProbs, RLE.MaxSymbol + 1);
            // Read fields
            int len = (int) input.ReadUInt32Optim();
            ArithmeticCodingReader acr = new ArithmeticCodingReader(input, probs);
            int[] fields = new int[len];
            for (int p = 0; p < len; p++)
                fields[p] = acr.ReadSymbol();

            // Undo fieldcode
            IntField transformed = CodecUtil.FieldcodeRunlengthsDe2(fields, w, h, RLE, this);
            // Undo predictive transform
            transformed.PredictionDeTransformXor(Seer);
            transformed.ArgbFromField(0, 3);
            return transformed;
        }
    }
}
