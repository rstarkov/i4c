using System.Collections.Generic;
using RT.Util;
using RT.Util.Collections;
using RT.Util.ExtensionMethods;
using System;

namespace i4c
{
    public class RunLengthCodec
    {
        private int _symMax;
        private int _symDataMax;
        private int _rlStages;
        private int[] _symRle;

        private int[][] _symSpec;
        private Dictionary<int, Tuple<int, int>> _symSpecInv;
        private int[] _maxRunLength;
        private int _maxRunLengthPossible;

        public RunLengthCodec(int symMax, int symDataMax, int rlStages, params int[] symRle)
        {
            _symMax = symMax;
            _symDataMax = symDataMax;
            _rlStages = rlStages;
            _symRle = symRle;

            // Allocate special symbols
            int freeSym = _symDataMax + 1;
            _symSpec = new int[_rlStages][];
            _symSpecInv = new Dictionary<int, Tuple<int, int>>();
            for (int stage = 0; stage < _rlStages; stage++)
            {
                _symSpec[stage] = new int[_symRle.Length];
                for (int symbol = 0; symbol < _symRle.Length; symbol++)
                {
                    _symSpec[stage][symbol] = freeSym;
                    _symSpecInv.Add(freeSym, new Tuple<int, int>(stage, symbol));
                    freeSym++;
                }
            }
            // Validate
            if (freeSym - 1 > _symMax)
                throw new RTException("Maximum symbol {0} is exceeded after allocating {1}*{2} special symbols.".Fmt(_symMax, _rlStages, _symRle.Length));

            // Run length boundaries
            _maxRunLength = new int[_rlStages];
            int prod = 1;
            for (int stage = 0; stage < _rlStages; stage++)
            {
                prod = prod * (_symMax + 1);
                _maxRunLength[stage] = (stage == 0 ? 0 : _maxRunLength[stage-1]) + prod;
            }
            _maxRunLengthPossible = _maxRunLength[_rlStages - 1];
        }

        public int[] Encode(int[] data)
        {
            List<int> result = new List<int>();

            int pos = 0;
            bool runActive = false;
            int runSymbol = 0, runSymbolIndex = 0, runCount = 0;
            while (true)
            {
                // Check if the active run must end here
                if (runActive && (pos == data.Length || data[pos] != runSymbol || runCount == _maxRunLengthPossible))
                {
                    int stage = 0;
                    while (runCount > _maxRunLength[stage])
                        stage++;
                    result.Add(_symSpec[stage][runSymbolIndex]);

                    runCount = runCount - 1 - (stage == 0 ? 0 : _maxRunLength[stage - 1]);
                    while (stage >= 0)
                    {
                        result.Add(runCount % (_symMax + 1));
                        runCount = runCount / (_symMax + 1);
                        stage--;
                    }

                    runActive = false;
                }

                // Have we processed all the data yet?
                if (pos == data.Length)
                    break;

                if (runActive)
                {
                    // If run is still active here then it definitely may continue with the current symbol
                    runCount++;
                    pos++;
                }
                else
                {
                    // Determine whether to start a run or not
                    if (pos+2 < data.Length && data[pos] == data[pos+1] && data[pos] == data[pos+2])
                    {
                        runSymbolIndex = -1;
                        for (int i = 0; i < _symRle.Length; i++)
                        {
                            if (_symRle[i] == data[pos])
                            {
                                runSymbolIndex = i;
                                break;
                            }
                        }

                        if (runSymbolIndex >= 0)
                        {
                            runActive = true;
                            runCount = 3;
                            runSymbol = data[pos];
                            pos += 3;
                        }
                    }

                    // If the run hasn't started then just output the symbol
                    if (!runActive)
                    {
                        result.Add(data[pos]);
                        pos++;
                    }
                }
            }

            return result.ToArray();
        }

        public int[] Decode(int[] data)
        {
            List<int> result = new List<int>();

            int pos = 0;
            while (pos < data.Length)
            {
                if (data[pos] <= _symDataMax)
                {
                    result.Add(data[pos]);
                }
                else
                {
                    int stage = _symSpecInv[data[pos]].Item1;
                    int symbol = _symSpecInv[data[pos]].Item2;
                    // Decode the number of repetitions
                    int count = 0;
                    int mul = 1;
                    for (; stage >= 0; stage--)
                    {
                        pos++;
                        count += (data[pos] + 1) * mul;
                        mul *= _symMax + 1;
                    }
                    // Add to the output
                    for (int i = 0; i < count; i++)
                        result.Add(symbol);
                }
                pos++;
            }

            return result.ToArray();
        }
    }
}
