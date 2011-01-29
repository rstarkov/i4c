using System;
using System.Collections.Generic;
using RT.Util.Collections;

namespace i4c
{
    public abstract class SymbolCodec
    {
        public abstract int[] Encode(int[] data);
        public abstract int[] Decode(int[] data);
        public abstract int MaxSymbol { get; }
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

        public override int MaxSymbol
        {
            get { throw new InvalidOperationException("This encoder does not have an upper limit on the symbols it produces."); }
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

        public override int MaxSymbol
        {
            get { throw new NotImplementedException(); }
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

        public override int MaxSymbol
        {
            get { throw new NotImplementedException(); }
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

    public class RunLength01LongShortCodec: SymbolCodec
    {
        private int[] _syms;
        private int[] _symBase;
        private int _symLonger;
        private int _symMuchLonger;
        private int _symMax;

        public int Counter_Longers;
        public int Counter_MuchLongers;

        public RunLength01LongShortCodec(int zeroSyms, int oneSyms)
        {
            _syms = new int[2];
            _syms[0] = zeroSyms;
            _syms[1] = oneSyms;
            _symBase = new int[2];
            _symBase[0] = 0;
            _symBase[1] = zeroSyms;
            _symLonger = zeroSyms + oneSyms;
            _symMuchLonger = _symLonger + 1;
            _symMax = _symMuchLonger;
        }

        public override int MaxSymbol
        {
            get { return _symMax; }
        }

        public override int[] Encode(int[] data)
        {
            List<int> result = new List<int>();
            int pos = 0;
            int sym = 0;
            int runCount = 0;

            Counter_Longers = 0;
            Counter_MuchLongers = 0;

            while (true)
            {
                // Output count and flip symbol if necessary
                if (pos == data.Length || data[pos] != sym)
                {
                    // 1. slightly longer: <longer>, <0..longer-1>
                    //    much longer: <longer>, [<longer>, <0..longer-1>]*, <0..longer-1>
                    // 2. if slightly longer: <longer>, <0..longer-1>, <0..longer-1>
                    //    much longer: [<longer>, <0..longer-1>]*, <0..longer-1>
                    // 3. slightly longer: <longer>, <0..max>
                    //    much longer: [<muchlonger>, <0..max>]*, <0..max>
                    if (runCount < _syms[sym])
                    {
                        // normal
                        if (sym != 1 || runCount != 1)
                            result.Add(_symBase[sym] + runCount);
                    }
                    else if (runCount - _syms[sym] <= _symMax)
                    {
                        // slightly longer
                        Counter_Longers++;
                        result.Add(_symLonger);
                        result.Add(runCount - _syms[sym]);
                    }
                    else
                    {
                        // much longer
                        while (runCount > _symMax)
                        {
                            Counter_MuchLongers++;
                            result.Add(_symMuchLonger);
                            int count = Math.Min(runCount / _symMax - 1, _symMax);
                            result.Add(count);
                            runCount -= (count+1) * _symMax;
                        }
                        result.Add(runCount); // remainder
                    }

                    sym = 1 - sym;
                    runCount = 0;
                }

                // Detect end of stream
                if (pos == data.Length)
                    break;

                // Process current symbol
                if (data[pos] == sym)
                    runCount++;
                else
                    throw new Exception("This never happens?");
                pos++;
            }

            return result.ToArray();
        }

        public override int[] Decode(int[] data)
        {
            throw new NotImplementedException();
            //List<int> result = new List<int>();

            //int pos = 0;
            //while (pos < data.Length)
            //{
            //    if (pos > 0)
            //        result.Add(1);

            //    int count = 0;
            //    while (data[pos] == _maxSym)
            //    {
            //        pos++;
            //        count += (data[pos] + 1) * _maxSym;
            //        pos++;
            //    }
            //    count += data[pos];
            //    for (int i = 0; i < count; i++)
            //        result.Add(0);

            //    pos++;
            //}

            //return result.ToArray();
        }
    }

    public class RunLength01SplitCodec: SymbolCodec
    {
        public RunLength01SplitCodec()
        {
        }

        public override int MaxSymbol
        {
            get { throw new InvalidOperationException("This encoder does not have an upper limit on the symbols it produces."); }
        }

        public override int[] Encode(int[] data)
        {
            throw new Exception("Call the other Encode method for this class.");
        }

        public override int[] Decode(int[] data)
        {
            throw new Exception("Call the other Decode method for this class.");
        }

        public Tuple<List<int>, List<int>> EncodeSplit(int[] data)
        {
            List<int> runs0 = new List<int>();
            List<int> runs1 = new List<int>();
            int pos = 0;
            int runStart = 0;

            while (pos < data.Length)
            {
                #region old implementation
                //// Output count
                //if (pos == data.Length || data[pos] != sym)
                //    runs[sym].Add(runCount);

                //// Detect end of stream
                //if (pos == data.Length)
                //    break;

                //// Process current symbol
                //if (data[pos] == sym)
                //    runCount++;
                //else
                //{
                //    runCount = 1;
                //    sym = 1 - sym;
                //}
                //pos++;
                #endregion
                // new implementation

                // Zeroes
                runStart = pos;
                while (pos < data.Length && data[pos] == 0)
                    pos++;
                runs0.Add(pos - runStart);

                // Ones
                runStart = pos;
                while (pos < data.Length && data[pos] == 1)
                    pos++;
                runs1.Add(pos - runStart);
            }

            return new Tuple<List<int>, List<int>>(runs0, runs1);
        }

    }

}
