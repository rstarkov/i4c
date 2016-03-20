using System.Collections.Generic;
using System.IO;
using RT.Util.ExtensionMethods;

namespace i4c
{
    public class I4cBravo : CompressorFixedSizeFieldcode
    {
        public override void Encode(IntField image, Stream output)
        {
            // Predictive transform
            image.ArgbTo4c();
            image.PredictionEnTransformXor(Seer);

            // Convert to three fields' runlengths
            var fields = CodecUtil.FieldcodeRunlengthsEn(image, new RunLength01MaxSmartCodec(FieldcodeSymbols), this);

            // Write size
            DeltaTracker pos = new DeltaTracker();
            output.WriteUInt32Optim((uint) image.Width);
            output.WriteUInt32Optim((uint) image.Height);
            SetCounter("bytes|size", pos.Next(output.Position));

            // Write probs
            ulong[] probs = CodecUtil.GetFreqsForAllSections(fields, FieldcodeSymbols + 1);
            probs[0] = 6;
            CodecUtil.SaveFreqsCrappy(output, probs);
            SetCounter("bytes|probs", pos.Next(output.Position));

            // Write fields
            ArithmeticSectionsCodec ac = new ArithmeticSectionsCodec(probs, 6, output);
            for (int i = 0; i < fields.Count; i++)
            {
                ac.WriteSection(fields[i]);
                SetCounter("bytes|fields|" + (i + 1), pos.Next(output.Position));
            }
            ac.Encode();
            SetCounter("bytes|arith-err", pos.Next(output.Position));
        }

        public override IntField Decode(Stream input)
        {
            // Read size
            int w = (int) input.ReadUInt32Optim();
            int h = (int) input.ReadUInt32Optim();
            // Read probabilities
            ulong[] probs = CodecUtil.LoadFreqsCrappy(input, FieldcodeSymbols + 1);
            // Read fields
            ArithmeticSectionsCodec ac = new ArithmeticSectionsCodec(probs, 6);
            ac.Decode(input);
            var fields = new List<int[]>();
            for (int i = 1; i <= 3; i++)
                fields.Add(ac.ReadSection());

            // Undo fieldcode
            IntField transformed = CodecUtil.FieldcodeRunlengthsDe(fields, w, h, new RunLength01MaxSmartCodec(FieldcodeSymbols), this);
            // Undo predictive transform
            transformed.PredictionDeTransformXor(Seer);
            transformed.ArgbFromField(0, 3);
            return transformed;
        }
    }
}
