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
            Config = new RVariant[] { 8, 8, 0, 13, 13 };
        }

        public override void Configure(params RVariant[] args)
        {
            base.Configure(args);
            _mode = Config[2];
            SeerH = new HashForeseer(Config[3], Config[4], new HorzVertForeseer());
            SeerV = new HashForeseer(Config[3], Config[4], new HorzVertForeseer());
        }

        public override void Encode(IntField image, Stream output)
        {
            image.ArgbTo4c();
            image.OutOfBoundsMode = OutOfBoundsMode.SubstColor;
            image.OutOfBoundsColor = 0;
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
