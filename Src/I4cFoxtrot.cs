using System;
using System.IO;
using RT.KitchenSink.Collections;

namespace i4c
{
    public class I4cFoxtrot : TimwiCecCompressor
    {
        private Foreseer SeerH, SeerV;
        private int _mode; // 0 = only horizontal, 1 = only vertical, 2 = h then w, 3 = w then h

        public I4cFoxtrot()
        {
            Config = new RVariant[] { 8, 8, 0, 7, 7, 3, 7, 7, 3 };
        }

        public override void Configure(params RVariant[] args)
        {
            base.Configure(args);
            _mode = Config[2];
            SeerH = new VariableSizeForeseer(Config[3], Config[4], Config[5], new HorzVertForeseer());
            SeerV = new VariableSizeForeseer(Config[6], Config[7], Config[8], new HorzVertForeseer());
        }

        public override void Encode(IntField image, Stream output)
        {
            image.ArgbTo4c();
            switch (_mode)
            {
                case 0:
                    image.PredictionEnTransformXor(SeerH);
                    AddImageGrayscale(image, "xformed-h");
                    break;
                case 1:
                    image.Transpose();
                    image.PredictionEnTransformXor(SeerV);
                    image.Transpose();
                    AddImageGrayscale(image, "xformed-v");
                    break;
                case 2:
                    image.PredictionEnTransformXor(SeerH);
                    AddImageGrayscale(image, "xformed-h");
                    image.Transpose();
                    image.PredictionEnTransformXor(SeerV);
                    image.Transpose();
                    AddImageGrayscale(image, "xformed-v");
                    break;
                case 3:
                    image.Transpose();
                    image.PredictionEnTransformXor(SeerV);
                    image.Transpose();
                    AddImageGrayscale(image, "xformed-v");
                    image.PredictionEnTransformXor(SeerH);
                    AddImageGrayscale(image, "xformed-h");
                    break;
                default:
                    throw new Exception();
            }

            base.Encode(image, output);
        }

        public override IntField Decode(Stream input)
        {
            throw new NotImplementedException();
            //// Decode CEC
            //// Undo predictive transform
            //transformed.PredictionDeTransformXor(Seer);
            //transformed.ArgbFromField(0, 3);
            //return transformed;
        }
    }
}
