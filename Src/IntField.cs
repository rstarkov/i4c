using System;
using System.Drawing;
using System.Drawing.Imaging;
using RT.Util;
using RT.Util.Drawing;

namespace i4c
{
    public enum OutOfBoundsMode
    {
        ThrowException,
        SubstColor,
    }

    public class IntField
    {
        public int Width;
        public int Height;
        public int[] Data;

        public OutOfBoundsMode OutOfBoundsMode = OutOfBoundsMode.ThrowException;
        public int OutOfBoundsColor = 0;

        public IntField(int width, int height)
        {
            Width = width;
            Height = height;
            Data = new int[Width*Height];
        }

        public int this[int x, int y]
        {
            get
            {
                if (x < 0 || x >= Width || y < 0 || y >= Height)
                {
                    if (OutOfBoundsMode == OutOfBoundsMode.SubstColor)
                        return OutOfBoundsColor;
                    else
                        throw new RTException("Cannot access pixel outside the image boundary");
                }
                else
                    return Data[y * Width + x];
            }

            set
            {
                if (x < 0 || x >= Width || y < 0 || y >= Height)
                {
                    if (OutOfBoundsMode == OutOfBoundsMode.ThrowException)
                        throw new RTException("Cannot access pixel outside the image boundary");
                }
                else
                    Data[y * Width + x] = value;
            }
        }

        public IntField Clone()
        {
            IntField result = new IntField(Width, Height);
            Array.Copy(Data, result.Data, Data.Length);
            return result;
        }

        public void ArgbLoadFromFile(string filename)
        {
            Bitmap bmp = new Bitmap(filename);
            BytesBitmap bb = new BytesBitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb);
            Graphics gr = Graphics.FromImage(bb.Bitmap);
            gr.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height);

            Width = bmp.Width;
            Height = bmp.Height;
            Data = new int[Width*Height];

            for (int p = 0, bp = 0; p < Width*Height; p++, bp += 4)
                //                alpha                       red                      green              blue
                Data[p] = (bb.Bits[bp + 3] << 24) | (bb.Bits[bp + 2] << 16) | (bb.Bits[bp + 1] << 8) | bb.Bits[bp];
        }

        public Bitmap ArgbToBitmap()
        {
            BytesBitmap bb = new BytesBitmap(Width, Height, PixelFormat.Format32bppArgb);
            for (int p = 0, bp = 0; p < Width*Height; p++)
            {
                int data = Data[p];
                bb.Bits[bp++] = (byte)data;          // blue
                bb.Bits[bp++] = (byte)(data >> 8);   // green
                bb.Bits[bp++] = (byte)(data >> 16);  // red
                bb.Bits[bp++] = (byte)(data >> 24);  // alpha
            }
            return bb.Bitmap;
        }

        public void ArgbSetAlpha(byte alpha)
        {
            int alpha_int = alpha << 24;
            for (int p = 0; p < Width*Height; p++)
                Data[p] = Data[p] & 0xFFFFFF | alpha_int;
        }

        public void ArgbTo4c()
        {
            for (int p = 0; p < Width*Height; p++)
            {
                // grayscale ranging 0 .. 765
                int grayscale = (Data[p] & 0xFF) + ((Data[p] >> 8) & 0xFF) + ((Data[p] >> 16) & 0xFF);
                if (grayscale < 192)
                    Data[p] = 0;
                else if (grayscale < 384)
                    Data[p] = 1;
                else if (grayscale < 576)
                    Data[p] = 2;
                else
                    Data[p] = 3;
            }
        }

        public void ArgbFromField(int min, int max)
        {
            for (int p = 0; p < Width*Height; p++)
            {
                int val = (255 * (Data[p] - min)) / (max - min);
                Data[p] = (255 << 24) | (val << 16) | (val << 8) | val;
            }
        }

        public void Prediction(Foreseer foreseer)
        {
            IntField orig = this.Clone();
            foreseer.Initialize(orig);

            int p = 0;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++, p++)
                {
                    Data[p] = foreseer.Foresee(orig, x, y, p);
                    foreseer.Learn(orig, x, y, p, orig.Data[p]);
                }
        }

        public void PredictionEnTransform(Foreseer foreseer)
        {
            IntField orig = this.Clone();
            foreseer.Initialize(orig);

            int p = 0;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++, p++)
                {
                    Data[p] = orig.Data[p] - foreseer.Foresee(orig, x, y, p);
                    if (Data[p] < 0)
                        Data[p] += 4;
                    foreseer.Learn(orig, x, y, p, orig.Data[p]);
                }
        }

        public void PredictionDeTransform(Foreseer foreseer)
        {
            IntField diff = this.Clone();
            foreseer.Initialize(this);

            int p = 0;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++, p++)
                {
                    Data[p] = (diff.Data[p] + foreseer.Foresee(this, x, y, p)) % 4;
                    foreseer.Learn(this, x, y, p, this.Data[p]);
                }
        }

        public void Conditional(Func<int, bool> condition)
        {
            for (int p = 0; p < Width*Height; p++)
                Data[p] = condition(Data[p]) ? 1 : 0;
        }

        public void Map(Func<int, int> map)
        {
            for (int p = 0; p < Width*Height; p++)
                Data[p] = map(Data[p]);
        }

        public IntField Extract(int fx, int fy, int width, int height)
        {
            IntField result = new IntField(width, height);
            for (int y = fy; y < fy + height; y++)
                Array.Copy(Data, fx + y*Width, result.Data, (y-fy)*width, width);
            return result;
        }

    }

}
