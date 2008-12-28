using System;
using System.Collections.Generic;
using System.IO;
using RT.Util.Streams;

namespace i4c
{
    public class ArithmeticCodec
    {
        private ulong[] _probs;
        private int _symEnd;

        public ArithmeticCodec(ulong[] probabilities)
        {
            _probs = new ulong[probabilities.Length + 1];
            _symEnd = probabilities.Length;
            Array.Copy(probabilities, _probs, probabilities.Length);
            _probs[_symEnd] = 1;
        }

        public byte[] Encode(int[] data)
        {
            MemoryStream ms = new MemoryStream();
            ArithmeticCodingWriter acw = new ArithmeticCodingWriter(ms, _probs);
            foreach (var sym in data)
                acw.WriteSymbol(sym);
            acw.WriteSymbol(_symEnd);
            acw.Close(false);
            return ms.ToArray();
        }

        public int[] Decode(byte[] data)
        {
            List<int> result = new List<int>();
            MemoryStream ms = new MemoryStream(data);
            ArithmeticCodingReader acr = new ArithmeticCodingReader(ms, _probs);
            while (true)
            {
                int sym = acr.ReadSymbol();
                if (sym == _symEnd)
                    break;
                else
                    result.Add(sym);
            }
            return result.ToArray();
        }
    }

    public class ArithmeticSectionsCodec
    {
        private ulong[] _probs;
        private int _symEnd;
        private MemoryStream _memStream;
        private ArithmeticCodingWriter _arithWriter;
        private ArithmeticCodingReader _arithReader;

        public ArithmeticSectionsCodec(ulong[] probabilities, ulong estSections)
        {
            _probs = new ulong[probabilities.Length + 1];
            Array.Copy(probabilities, _probs, probabilities.Length);
            _symEnd = probabilities.Length;
            _probs[_symEnd] = estSections;
        }

        public byte[] Encode()
        {
            if (_arithReader != null)
                throw new InvalidOperationException("Cannot mix reads and writes in ArithmeticSectionsCodec");

            _arithWriter.Close(false);
            return _memStream.ToArray();
        }

        public void Decode(byte[] data)
        {
            if (_arithWriter != null)
                throw new InvalidOperationException("Cannot mix reads and writes in ArithmeticSectionsCodec");

            if (_arithReader == null)
            {
                _memStream = new MemoryStream(data);
                _arithReader = new ArithmeticCodingReader(_memStream, _probs);
            }
        }

        public void WriteSection(int[] data)
        {
            if (_arithReader != null)
                throw new InvalidOperationException("Cannot mix reads and writes in ArithmeticSectionsCodec");

            if (_arithWriter == null)
            {
                _memStream = new MemoryStream();
                _arithWriter = new ArithmeticCodingWriter(_memStream, _probs);
            }

            foreach (var sym in data)
                _arithWriter.WriteSymbol(sym);
            _arithWriter.WriteSymbol(_symEnd);
        }

        public int[] ReadSection()
        {
            if (_arithWriter != null)
                throw new InvalidOperationException("Cannot mix reads and writes in ArithmeticSectionsCodec");

            List<int> result = new List<int>();
            while (true)
            {
                int sym = _arithReader.ReadSymbol();
                if (sym == _symEnd)
                    break;
                else
                    result.Add(sym);
            }

            return result.ToArray();
        }
    }
}
