using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RT.Util.ExtensionMethods;

namespace PredictionTuner
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Loading...");
            var examples = new List<Example>();
            using (var file = new StreamReader(@"..\Debug\statistics.bin"))
                while (true)
                {
                    if (examples.Count % 10000 == 0) Console.Title = $"{examples.Count:#,0} ({examples.Count / 4719681.0 * 100:0.00}%)";
                    var lineStr = file.ReadLine();
                    if (lineStr == null)
                        break;
                    var line = lineStr.Split(' ');
                    var example = new Example();
                    int cur = 0;
                    example.Actual = ParseExpected('A', line[cur++]);
                    example.Predicted = ParseExpected('P', line[cur++]);
                    example.Statistics = new List<Statistic>();
                    while (cur < line.Length - 1)
                    {
                        //if (line[cur][0] == 'E')
                        //    break;
                        var stat = new Statistic();
                        stat.Area = ParseExpected('R', line[cur++]);
                        while (true)
                        {
                            var c = line[cur][0];
                            if (c == 'R' || c == 'E')
                                break;
                            switch (c)
                            {
                                case 'a':
                                    stat.Count0 = ushort.Parse(line[cur++].Substring(1)); break;
                                case 'b':
                                    stat.Count1 = ushort.Parse(line[cur++].Substring(1)); break;
                                case 'c':
                                    stat.Count2 = ushort.Parse(line[cur++].Substring(1)); break;
                                case 'd':
                                    stat.Count3 = ushort.Parse(line[cur++].Substring(1)); break;
                                default:
                                    throw new Exception();
                            }
                        }
                        example.Statistics.Add(stat);
                    }
                    if (line[cur] != "E")
                        throw new Exception();
                    examples.Add(example);
                }
            Console.WriteLine("Loaded.");

            // Count impossible to predict
            {
                int totallyImpossible = 0, ratherImprobable = 0;
                for (int i = 0; i < examples.Count; i++)
                {
                    if (!examples[i].Statistics.Any(s => s.CountFor(examples[i].Actual) > 0))
                        totallyImpossible++;
                    if (!examples[i].Statistics.Any(s => s.CountFor(examples[i].Actual) == s.MaxCount()))
                        ratherImprobable++;
                }
                Console.WriteLine($"Totally impossible: {totallyImpossible:#,0} of {examples.Count:#,0} ({totallyImpossible / (double) examples.Count * 100:0.00}%)");
                Console.WriteLine($"Rather improbable: {ratherImprobable:#,0} of {examples.Count:#,0} ({ratherImprobable / (double) examples.Count * 100:0.00}%)");
            }

            // Naive prediction (use largest count from largest area available) (2.16%)
            {
                int naiveErrors = 0;
                for (int i = 0; i < examples.Count; i++)
                {
                    if (i % 10000 == 0) Console.Title = $"{i:#,0} ({i / (double) examples.Count * 100:0.00}%)";
                    int predict = examples[i].Statistics.Count == 0 ? 0 : examples[i].Statistics[0].MaxCountFor();
                    //if (predict != examples[i].Predicted)
                    //    throw new Exception();
                    if (predict != examples[i].Actual)
                        naiveErrors++;
                }
                Console.WriteLine($"Naive: {naiveErrors:#,0} of {examples.Count:#,0} (errors: {naiveErrors / (double) examples.Count * 100:0.00}%)");
            }

            // Naive prediction 2 (use largest count from all stats available) (4.60%)
#if false
            {
                int naiveErrors = 0;
                for (int i = 0; i < examples.Count; i++)
                {
                    if (i % 10000 == 0) Console.Title = $"{i:#,0} ({i / (double) examples.Count * 100:0.00}%)";
                    int predict = examples[i].Statistics.Count == 0 ? -1 : examples[i].Statistics.MaxElement(s => s.MaxCount()).MaxCountFor();
                    if (predict != examples[i].Actual)
                        naiveErrors++;
                }
                Console.WriteLine($"Naive 2: {naiveErrors:#,0} of {examples.Count:#,0} (errors: {naiveErrors / (double) examples.Count * 100:0.00}%)");
            }
#endif

            // Naive prediction 3 (count sum) (3.57%)
#if false
            {
                int naiveErrors = 0;
                for (int i = 0; i < examples.Count; i++)
                {
                    if (i % 10000 == 0) Console.Title = $"{i:#,0} ({i / (double) examples.Count * 100:0.00}%)";
                    int c0 = 0, c1 = 0, c2 = 0, c3 = 0;
                    foreach (var stat in examples[i].Statistics)
                    {
                        c0 += stat.Count0;
                        c1 += stat.Count1;
                        c2 += stat.Count2;
                        c3 += stat.Count3;
                    }
                    int predict = -1;
                    if (c0 >= c1 && c0 >= c2 && c0 >= c3)
                        predict = 0;
                    else if (c1 >= c2 && c1 >= c3)
                        predict = 1;
                    else if (c2 >= c3)
                        predict = 2;
                    else
                        predict = 3;
                    if (predict != examples[i].Actual)
                        naiveErrors++;
                }
                Console.WriteLine($"Naive 3: {naiveErrors:#,0} of {examples.Count:#,0} (errors: {naiveErrors / (double) examples.Count * 100:0.00}%)");
            }
#endif

            // Largest difference from second largest (2.11%)
#if false
            {
                int ld2lErrors = 0;
                for (int i = 0; i < examples.Count; i++)
                {
                    if (i % 10000 == 0) Console.Title = $"{i:#,0} ({i / (double) examples.Count * 100:0.00}%)";
                    int predict = examples[i].Statistics.Count == 0 ? -1 : examples[i].Statistics.MaxElement(s => (s.MaxCount() - s.SecondMaxCount()) / (double) s.MaxCount()).MaxCountFor();
                    if (predict != examples[i].Actual)
                        ld2lErrors++;
                }
                Console.WriteLine($"Largest diff from 2nd: {ld2lErrors:#,0} of {examples.Count:#,0} (errors: {ld2lErrors / (double) examples.Count * 100:0.00}%)");
            }
#endif

            // Most maxes (2.22%)
#if false
            {
                int mostMaxesErrors = 0;
                for (int i = 0; i < examples.Count; i++)
                {
                    if (i % 10000 == 0) Console.Title = $"{i:#,0} ({i / (double) examples.Count * 100:0.00}%)";
                    int[] maxCounts = new[] { 0, 0, 0, 0 };
                    foreach (var stat in examples[i].Statistics)
                        maxCounts[stat.MaxCountFor()]++;
                    int predict = maxCounts.Select((c, val) => new { c, val }).MaxElement(x => x.c).val;
                    if (predict != examples[i].Actual)
                        mostMaxesErrors++;
                }
                Console.WriteLine($"Most maxes: {mostMaxesErrors:#,0} of {examples.Count:#,0} (errors: {mostMaxesErrors / (double) examples.Count * 100:0.00}%)");
            }
#endif

            // Weighted (1.96%)
            {
                int weightedErrors = 0;
                for (int i = 0; i < examples.Count; i++)
                {
                    if (i % 10000 == 0) Console.Title = $"{i:#,0} ({i / (double) examples.Count * 100:0.00}%)";
                    double conf0 = 0, conf1 = 0, conf2 = 0, conf3 = 0;

                    foreach (var stat in examples[i].Statistics)
                    {
                        double total = stat.Count0 + stat.Count1 + stat.Count2 + stat.Count3;
                        if (total == 0)
                            continue;
                        double wt = Math.Sqrt(stat.Area);
                        conf0 += stat.Count0 / total * wt;
                        conf1 += stat.Count1 / total * wt;
                        conf2 += stat.Count2 / total * wt;
                        conf3 += stat.Count3 / total * wt;
                    }

                    int predict;
                    if (conf0 == 0 && conf1 == 0 && conf2 == 0 && conf3 == 0)
                        predict = 0;
                    else if (conf0 >= conf1 && conf0 >= conf2 && conf0 >= conf3)
                        predict = 0;
                    else if (conf1 >= conf2 && conf1 >= conf3)
                        predict = 1;
                    else if (conf2 >= conf3)
                        predict = 2;
                    else
                        predict = 3;

                    if (predict != examples[i].Predicted)
                        throw new Exception();

                    if (predict != examples[i].Actual)
                        weightedErrors++;
                }
                Console.WriteLine($"Weighted: {weightedErrors:#,0} of {examples.Count:#,0} (errors: {weightedErrors / (double) examples.Count * 100:0.00}%)");
            }


            Console.WriteLine("DONE");
            Console.ReadLine();
        }

        private static ushort ParseExpected(char expected, string part)
        {
            if (part[0] != expected)
                throw new Exception();
            return ushort.Parse(part.Substring(1));
        }
    }

    struct Example
    {
        public int Actual;
        public int Predicted;
        public List<Statistic> Statistics;
    }

    struct Statistic
    {
        public ushort Area;
        public ushort Count0, Count1, Count2, Count3;

        public int CountFor(int val)
        {
            if (val == 0) return Count0;
            else if (val == 1) return Count1;
            else if (val == 2) return Count2;
            else if (val == 3) return Count3;
            throw new Exception();
        }

        public IEnumerable<Tuple<byte, ushort>> Ordered()
        {
            return new[] { Tuple.Create((byte) 0, Count0), Tuple.Create((byte) 1, Count1), Tuple.Create((byte) 2, Count2), Tuple.Create((byte) 3, Count3) }.OrderByDescending(x => x.Item2);
        }

        public int MaxCountFor()
        {
            return Ordered().First().Item1;
            //if (Count0 >= Count1 && Count0 >= Count2 && Count0 >= Count3)
            //    return 0;
            //if (Count1 >= Count2 && Count1 >= Count3)
            //    return 1;
            //if (Count2 >= Count3)
            //    return 2;
            //return 3;
        }

        internal ushort MaxCount()
        {
            return Ordered().First().Item2;
        }

        internal ushort SecondMaxCount()
        {
            return Ordered().Skip(1).First().Item2;
        }
    }
}
