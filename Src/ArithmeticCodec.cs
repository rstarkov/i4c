using System;
using System.Collections.Generic;
using System.IO;
using RT.Util.Streams;

namespace i4c
{
    /// <summary>
    /// Provides a write-only stream that can compress data using Arithmetic Coding.
    /// </summary>
    /// <seealso cref="ArithmeticCodingReader"/>
    public class ArithmeticWriter
    {
        public ulong[] Probs;
        public ulong TotalProb;
        public Stream BaseStream;

        private ulong _high, _low;
        private int _underflow;
        private byte _curbyte;
        private int _curbit;

        /// <summary>
        /// Initialises an <see cref="ArithmeticWriter"/> instance given a base stream and a set of byte probabilities.
        /// </summary>
        /// <param name="basestr">The base stream to which the compressed data will be written.</param>
        /// <param name="probabilities">The probability of each byte occurring. Can be null, in which 
        /// case all bytes are assumed to have the same probability. When reading the data back using
        /// an <see cref="ArithmeticCodingReader"/>, the set of probabilities must be exactly the same.</param>
        /// <remarks>The compressed data will not be complete until the stream is closed using <see cref="Close()"/>.</remarks>
        public ArithmeticWriter(Stream basestr, ulong[] probabilities)
        {
            BaseStream = basestr;
            if (probabilities != null)
            {
                Probs = probabilities;
                UpdateTotalProb();
            }

            _high = 0xffffffff;
            _low = 0;
            _curbyte = 0;
            _curbit = 0;
            _underflow = 0;
        }

        /// <summary>
        /// Recalculates the total probability by summing up the probs array.
        /// </summary>
        public void UpdateTotalProb()
        {
            TotalProb = 0;
            for (int i = 0; i < Probs.Length; i++)
                TotalProb += Probs[i];
        }

        /// <summary>
        /// Writes a single symbol. Use this if you are not using bytes as your symbol alphabet.
        /// </summary>
        /// <param name="p">Symbol to write. Must be an integer between 0 and the length of the probabilities array passed in the constructor.</param>
        public void WriteSymbol(int sym)
        {
            if (sym >= Probs.Length)
                throw new Exception("Attempt to encode non-existent symbol");
            if (Probs[sym] == 0)
                throw new Exception("Attempt to encode a symbol with zero probability");

            ulong pos = 0;
            for (int i = 0; i < sym; i++)
                pos += Probs[i];

            // Set high and low to the new values
            ulong newlow = (_high - _low + 1) * pos / TotalProb + _low;
            _high = (_high - _low + 1) * (pos + Probs[sym]) / TotalProb + _low - 1;
            _low = newlow;

            // While most significant bits match, shift them out and output them
            while ((_high & 0x80000000) == (_low & 0x80000000))
            {
                OutputBit((_high & 0x80000000) != 0);
                while (_underflow > 0)
                {
                    OutputBit((_high & 0x80000000) == 0);
                    _underflow--;
                }
                _high = ((_high << 1) & 0xffffffff) | 1;
                _low = (_low << 1) & 0xffffffff;
            }

            // If underflow is imminent, shift it out
            while (((_low & 0x40000000) != 0) && ((_high & 0x40000000) == 0))
            {
                _underflow++;
                _high = ((_high & 0x7fffffff) << 1) | 0x80000001;
                _low = (_low << 1) & 0x7fffffff;
            }
        }

        private void OutputBit(bool bit)
        {
            if (bit) _curbyte |= (byte)(1 << _curbit);
            if (_curbit >= 7)
            {
                BaseStream.WriteByte(_curbyte);
                _curbit = 0;
                _curbyte = 0;
            }
            else
                _curbit++;
        }

        /// <summary>
        /// Flushes any bytes not yet written to the stream. Absolutely MUST be called
        /// after writing the last symbol.
        /// </summary>
        public void Flush()
        {
            OutputBit((_low & 0x40000000) != 0);
            _underflow++;
            while (_underflow > 0)
            {
                OutputBit((_low & 0x40000000) == 0);
                _underflow--;
            }
            BaseStream.WriteByte(_curbyte);

            // Reset so that further writing can continue from scratch if necessary
            _high = 0xffffffff;
            _low = 0;
            _curbyte = 0;
            _curbit = 0;
            _underflow = 0;
        }
    }

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
            ArithmeticWriter aw = new ArithmeticWriter(ms, _probs);
            foreach (var sym in data)
                aw.WriteSymbol(sym);
            aw.WriteSymbol(_symEnd);
            aw.Flush();
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
        private ArithmeticWriter _arithWriter;
        private ArithmeticCodingReader _arithReader;

        public ArithmeticSectionsCodec(ulong[] probabilities, ulong estSections)
        {
            _probs = new ulong[probabilities.Length + 1];
            Array.Copy(probabilities, _probs, probabilities.Length);
            _symEnd = probabilities.Length;
            _probs[_symEnd] = estSections;
        }

        public ArithmeticSectionsCodec(ulong[] probabilities, ulong estSections, Stream sourceStream)
            : this(probabilities, estSections)
        {
            _arithWriter = new ArithmeticWriter(sourceStream, _probs);
        }

        public byte[] Encode()
        {
            if (_arithReader != null)
                throw new InvalidOperationException("Cannot mix reads and writes in ArithmeticSectionsCodec");

            _arithWriter.Flush();
            return _memStream == null ? null : _memStream.ToArray();
        }

        public void Decode(byte[] data)
        {
            Decode(new MemoryStream(data));
        }

        public void Decode(Stream stream)
        {
            if (_arithWriter != null)
                throw new InvalidOperationException("Cannot mix reads and writes in ArithmeticSectionsCodec");

            if (_arithReader == null)
                _arithReader = new ArithmeticCodingReader(stream, _probs);
        }

        public void WriteSection(int[] data)
        {
            if (_arithReader != null)
                throw new InvalidOperationException("Cannot mix reads and writes in ArithmeticSectionsCodec");

            if (_arithWriter == null)
            {
                _memStream = new MemoryStream();
                _arithWriter = new ArithmeticWriter(_memStream, _probs);
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
