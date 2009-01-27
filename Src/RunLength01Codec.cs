using System;
using System.Collections.Generic;

namespace i4c
{
    public abstract class SymbolCodec
    {
        public abstract int[] Encode(int[] data);
        public abstract int[] Decode(int[] data);
    }

    public class RunLength01Codec: SymbolCodec
    {
        public RunLength01Codec()
        {
            // number of symbols = number of 1's + 1
            // 0 0 0 1 0 0 0: 3 3
            // 0 0 0: 3
            // 0 0 0 1 0 0 0 1: 3 3 0
            // 1 0 0 0 1 0 0 0: 0 3 3
            // 
            // 1 1 1: 0 0 0 0
            // 1 1: 0 0 0
            // 1: 0 0
            // : 0
            // 
            // 0 0 0: 3
            // 0 0: 2
            // 0: 1
            // : 0
            // 
            // Output count every time a 1 or EOS is encountered
        }

        public override int[] Encode(int[] data)
        {
            List<int> result = new List<int>();
            int pos = 0;
            int runCount = 0;

            while (true)
            {
                // Output count
                if (pos == data.Length || data[pos] == 1)
                    result.Add(runCount);

                // Detect end of stream
                if (pos == data.Length)
                    break;

                // Process current symbol
                if (data[pos] == 0)
                    runCount++;
                else
                    runCount = 0;
                pos++;
            }

            return result.ToArray();
        }

        public override int[] Decode(int[] data)
        {
            List<int> result = new List<int>();

            for (int pos = 0; pos < data.Length; pos++)
            {
                if (pos > 0)
                    result.Add(1);

                for (int i = 0; i < data[pos]; i++)
                    result.Add(0);
            }

            return result.ToArray();
        }
    }

    public class RunLength01MaxCodec: SymbolCodec
    {
        private int _maxrun;

        public RunLength01MaxCodec(int maxrun)
        {
            /// All symbols generated are less than or equal to maxrun.
            _maxrun = maxrun;
        }

        public override int[] Encode(int[] data)
        {
            List<int> result = new List<int>();
            int pos = 0;
            int runCount = 0;

            while (true)
            {
                // Output count
                if (pos == data.Length || data[pos] == 1)
                {
                    while (runCount >= _maxrun)
                    {
                        result.Add(_maxrun);
                        runCount -= _maxrun;
                    }
                    result.Add(runCount); // even if 0
                }

                // Detect end of stream
                if (pos == data.Length)
                    break;

                // Process current symbol
                if (data[pos] == 0)
                    runCount++;
                else
                    runCount = 0;
                pos++;
            }

            return result.ToArray();
        }

        public override int[] Decode(int[] data)
        {
            List<int> result = new List<int>();

            int pos = 0;
            while (pos < data.Length)
            {
                if (pos > 0)
                    result.Add(1);

                int count = 0;
                while (data[pos] == _maxrun)
                {
                    count += _maxrun;
                    pos++;
                }
                for (int i = 0; i < count + data[pos]; i++)
                    result.Add(0);

                pos++;
            }

            return result.ToArray();
        }
    }

    public class RunLength01MaxSmartCodec: SymbolCodec
    {
        private int _maxSym;

        public RunLength01MaxSmartCodec(int maxSym)
        {
            /// All symbols generated are less than or equal to maxSym.
            _maxSym = maxSym;
        }

        public override int[] Encode(int[] data)
        {
            List<int> result = new List<int>();
            int pos = 0;
            int runCount = 0;

            while (true)
            {
                // Output count
                if (pos == data.Length || data[pos] == 1)
                {
                    while (runCount >= _maxSym)
                    {
                        result.Add(_maxSym);
                        int count = Math.Min(runCount / _maxSym - 1, _maxSym);
                        result.Add(count);
                        runCount -= (count+1) * _maxSym;
                    }
                    result.Add(runCount); // even if 0
                }

                // Detect end of stream
                if (pos == data.Length)
                    break;

                // Process current symbol
                if (data[pos] == 0)
                    runCount++;
                else
                    runCount = 0;
                pos++;
            }

            return result.ToArray();
        }

        public override int[] Decode(int[] data)
        {
            List<int> result = new List<int>();

            int pos = 0;
            while (pos < data.Length)
            {
                if (pos > 0)
                    result.Add(1);

                int count = 0;
                while (data[pos] == _maxSym)
                {
                    pos++;
                    count += (data[pos] + 1) * _maxSym;
                    pos++;
                }
                count += data[pos];
                for (int i = 0; i < count; i++)
                    result.Add(0);

                pos++;
            }

            return result.ToArray();
        }
    }
}
