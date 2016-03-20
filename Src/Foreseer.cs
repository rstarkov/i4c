using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RT.Util.ExtensionMethods;

namespace i4c
{
    public abstract class Foreseer
    {
        public virtual void Initialize(IntField image)
        {
        }

        public abstract int Foresee(IntField image, int x, int y, int p);

        public virtual void Learn(IntField image, int x, int y, int p, int actual)
        {
        }
    }

    public class NullForeseer : Foreseer
    {
        public override int Foresee(IntField image, int x, int y, int p)
        {
            return 0;
        }
    }

    public class MostFrequentForeseer : Foreseer
    {
        private int _size;

        public MostFrequentForeseer(int size)
        {
            _size = size;
        }

        public static int MostFrequent(IntField image, int ptX, int ptY, int lookbackW, int lookbackH)
        {
            Dictionary<int, int> counts = new Dictionary<int, int>();
            for (int y = ptY - lookbackH + 1; y <= ptY; y++)
                for (int x = ptX - lookbackW + 1; x <= ptX; x++)
                    if (y != ptY && x != ptX && y >= 0 && x >= 0)
                    {
                        int sym = image[x, y];
                        if (counts.ContainsKey(sym))
                            counts[sym]++;
                        else
                            counts.Add(sym, 1);
                    }
            if (counts.Count == 0)
                return 0;
            int maxcount = counts.Values.Max();
            return counts.Where(kvp => kvp.Value == maxcount).First().Key;
        }

        public override int Foresee(IntField image, int x, int y, int p)
        {
            return MostFrequent(image, x, y, _size, _size);
        }
    }

    public class BackgroundForeseer : Foreseer
    {
        private int _horzLookback = 6;
        private int _vertLookback = 6;
        private int _horzThickness = 4;
        private int _vertThickness = 4;
        private int _freqThickness = 12;

        private enum pattern
        {
            Solid,
            CleanVert,
            CleanHorz,
            Dirty,
        }

        public override void Initialize(IntField image)
        {
            image.OutOfBoundsMode = OutOfBoundsMode.SubstColor;
            image.OutOfBoundsColor = 0;
        }

        private pattern findPattern(IntField image, int fx, int fy, int tw, int th)
        {
            int c = image[fx, fy];
            int h = image[fx + 1, fy];
            int v = image[fx, fy + 1];

            bool certainlyNotCH = c != h;
            bool certainlyNotCV = c != v;
            bool certainlyNotSolid = c != h || c != v;

            for (int y = fy + 1; y < fy + th; y++)
                for (int x = fx + 1; x < fx + tw; x++)
                {
                    c = image[x, y];
                    h = image[x - 1, y];
                    v = image[x, y - 1];

                    if (!certainlyNotSolid && (c != h || c != v))
                        certainlyNotSolid = true;
                    if (!certainlyNotCH && (c != h))
                        certainlyNotCH = true;
                    if (!certainlyNotCV && (c != v))
                        certainlyNotCV = true;

                    if (certainlyNotCH && certainlyNotCV && certainlyNotSolid)
                        return pattern.Dirty;
                }

            if (!certainlyNotSolid) return pattern.Solid;
            else if (!certainlyNotCV) return pattern.CleanVert;
            else return pattern.CleanHorz;
        }

        public override int Foresee(IntField image, int x, int y, int p)
        {
            pattern horzProbe = findPattern(image, x - _horzLookback, y - _horzThickness + 1, _horzLookback, _horzThickness);
            pattern vertProbe = findPattern(image, x - _vertThickness + 1, y - _vertLookback, _vertThickness, _vertLookback);

            if (horzProbe == pattern.Dirty && vertProbe == pattern.Dirty)
                return MostFrequentForeseer.MostFrequent(image, x, y, _freqThickness, _freqThickness);

            bool haveH = horzProbe == pattern.CleanHorz || vertProbe == pattern.CleanHorz;
            bool haveV = horzProbe == pattern.CleanVert || vertProbe == pattern.CleanVert;

            if (haveH && !haveV)
                return MostFrequentForeseer.MostFrequent(image, x, y, _horzLookback, _horzThickness);
            if (haveV && !haveH)
                return MostFrequentForeseer.MostFrequent(image, x, y, _vertThickness, _vertLookback);

            if (horzProbe == pattern.Solid)
                return MostFrequentForeseer.MostFrequent(image, x, y, _horzLookback, _horzThickness);
            else
                return MostFrequentForeseer.MostFrequent(image, x, y, _vertThickness, _vertLookback);
        }

    }

    public class HorzVertForeseer : Foreseer
    {
        public override int Foresee(IntField image, int x, int y, int p)
        {
            int l = x == 0 ? 0 : image.Data[p - 1];
            int t = y == 0 ? 0 : image.Data[p - image.Width];
            int tl = (x == 0 || y == 0) ? 0 : image.Data[p - image.Width - 1];

            if (l == tl && t == tl)
                return tl; // solid fill
            else if (l == tl)
                return t;  // vertical line
            else if (t == tl)
                return l;  // horizontal line
            else if (t == l)
                return tl; // 50% diffusion fill
            else
                return tl;  // favour diagonals from top-left to bottom-right
        }
    }

    public class VertForeseer : Foreseer
    {
        public override int Foresee(IntField image, int x, int y, int p)
        {
            int l = x == 0 ? 0 : image.Data[p - 1];
            int t = y == 0 ? 0 : image.Data[p - image.Width];
            int tl = (x == 0 || y == 0) ? 0 : image.Data[p - image.Width - 1];

            if (l == tl && t == tl)
                return tl; // solid fill
            else if (t == l && t != tl)
                return tl; // 50% diffusion fill
            else
                return t;  // favour same as top
        }
    }

    public class FixedSizeMonoForeseer : Foreseer
    {
        private int _width;
        private int _height;
        private int _xpos;
        private Foreseer _fallback;

        private class outcomes
        {
            public int MostFrequent = 0;
            public int Other = 0;
        }

        private Dictionary<string, outcomes> _history = new Dictionary<string, outcomes>();

        public FixedSizeMonoForeseer(int width, int height, int xpos, Foreseer fallback)
        {
            _width = width;
            _height = height;
            _xpos = xpos;
            _fallback = fallback;
        }

        private int _curMostFreqSymbol;
        private int _curSecondFreqSymbol;
        private string _curArea;

        public override void Initialize(IntField image)
        {
            _history.Clear();
            _fallback.Initialize(image);
        }

        public override int Foresee(IntField image, int x, int y, int p)
        {
            _curArea = ExtractArea(image, x, y, out _curMostFreqSymbol, out _curSecondFreqSymbol);

            if (_curArea == null)
                return _fallback.Foresee(image, x, y, p);

            if (_history.ContainsKey(_curArea))
                return _history[_curArea].MostFrequent > _history[_curArea].Other ? _curMostFreqSymbol : _curSecondFreqSymbol;
            else
                return _fallback.Foresee(image, x, y, p);
        }

        public override void Learn(IntField image, int x, int y, int p, int actual)
        {
            if (_curArea == null)
            {
                _fallback.Learn(image, x, y, p, actual);
                return;
            }

            if (!_history.ContainsKey(_curArea))
                _history.Add(_curArea, new outcomes());

            if (actual == _curMostFreqSymbol)
                _history[_curArea].MostFrequent++;
            else
                _history[_curArea].Other++;
        }

        public string ExtractArea(IntField image, int x, int y, out int mostFreqSymbol, out int secondFreqSymbol)
        {
            mostFreqSymbol = secondFreqSymbol = int.MinValue;

            if (x < _width - 1 - _xpos || x >= image.Width - _xpos || y < _height - 1)
                return null;

            IntField area = image.Extract(x - _width + 1 + _xpos, y - _height + 1, _width, _height);
            int[] data = new int[area.Data.Length - 1 - _xpos];
            Array.Copy(area.Data, data, data.Length);

            // Find the smallest most frequent and second most frequent symbols
            var counts = CodecUtil.CountValues(data);
            ulong mostFreq = ulong.MinValue;
            ulong secondFreq = ulong.MinValue;
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] > mostFreq)
                {
                    secondFreq = mostFreq;
                    secondFreqSymbol = mostFreqSymbol;
                    mostFreq = counts[i];
                    mostFreqSymbol = i;
                }
                else if (counts[i] > secondFreq)
                {
                    secondFreq = counts[i];
                    secondFreqSymbol = i;
                }
            }

            // Convert to string
            StringBuilder SB = new StringBuilder(data.Length);
            foreach (var sym in data)
                SB.Append(sym == mostFreqSymbol ? "1" : "0");
            return SB.ToString();
        }
    }

    class ColorFrequencies
    {
        private int Freq0 = 0;
        private int Freq1 = 0;
        private int Freq2 = 0;
        private int Freq3 = 0;

        public int Predicted()
        {
            if (Freq0 >= Freq1 && Freq0 >= Freq2 && Freq0 >= Freq3)
                return 0;
            if (Freq1 >= Freq2 && Freq1 >= Freq3)
                return 1;
            if (Freq2 >= Freq3)
                return 2;
            return 3;
        }

        public void Learn(int actual)
        {
            switch (actual)
            {
                case 0: Freq0++; return;
                case 1: Freq1++; return;
                case 2: Freq2++; return;
                case 3: Freq3++; return;
                default: throw new Exception();
            }
        }
    }

    public class FixedSizeForeseer : Foreseer
    {
        private int _width;
        private int _height;
        private int _xpos;
        private Foreseer _fallback;

        private Dictionary<string, ColorFrequencies> _history = new Dictionary<string, ColorFrequencies>();

        public FixedSizeForeseer(int width, int height, int xpos, Foreseer fallback)
        {
            _width = width;
            _height = height;
            _xpos = xpos;
            _fallback = fallback;
        }

        private string _curArea;

        public override void Initialize(IntField image)
        {
            _history.Clear();
            _fallback.Initialize(image);
        }

        public override int Foresee(IntField image, int x, int y, int p)
        {
            _curArea = ExtractArea(image, x, y);

            if (_curArea == null)
                return _fallback.Foresee(image, x, y, p);

            if (_history.ContainsKey(_curArea))
                return _history[_curArea].Predicted();
            else
                return _fallback.Foresee(image, x, y, p);
        }

        public override void Learn(IntField image, int x, int y, int p, int actual)
        {
            if (_curArea == null)
            {
                _fallback.Learn(image, x, y, p, actual);
                return;
            }

            if (!_history.ContainsKey(_curArea))
                _history.Add(_curArea, new ColorFrequencies());

            _history[_curArea].Learn(actual);
        }

        public string ExtractArea(IntField image, int x, int y)
        {
            if (x < _width - 1 - _xpos || x >= image.Width - _xpos || y < _height - 1)
                return null;

            var sb = new StringBuilder(_width * _height);
            for (int cx = x - _xpos; cx < x; cx++)
                sb.Append((char) ('0' + image[cx, y]));
            for (int cy = y - 1; cy > y - _height; cy--)
            {
                sb.Append('|');
                for (int cx = x - _xpos; cx < x - _xpos + _width; cx++)
                    sb.Append((char) ('0' + image[cx, cy]));
            }
            return sb.ToString();
        }
    }

    public class VariableSizeForeseer : Foreseer
    {
        private int _width;
        private int _height;
        private int _xpos;
        private Foreseer _fallback;

        private Dictionary<string, ColorFrequencies> _history = new Dictionary<string, ColorFrequencies>();

        public VariableSizeForeseer(int width, int height, int xpos, Foreseer fallback)
        {
            _width = width;
            _height = height;
            _xpos = xpos;
            _fallback = fallback;
        }

        private List<string> _curAreas = new List<string>();

        public override void Initialize(IntField image)
        {
            _history.Clear();
            _fallback.Initialize(image);
        }

        public override int Foresee(IntField image, int x, int y, int p)
        {
            _curAreas.Clear();
            for (int h = _height; h >= 1; h--)
            {
                var area = ExtractArea(image, x, y, h);
                if (area != null)
                    _curAreas.Add(area);
            }

            foreach (var area in _curAreas)
            {
                ColorFrequencies cf;
                if (_history.TryGetValue(area, out cf))
                    return cf.Predicted();
            }

            return _fallback.Foresee(image, x, y, p);
        }

        public override void Learn(IntField image, int x, int y, int p, int actual)
        {
            _fallback.Learn(image, x, y, p, actual);

            foreach (var area in _curAreas)
            {
                ColorFrequencies cf;
                if (!_history.TryGetValue(area, out cf))
                    _history.Add(area, cf = new ColorFrequencies());

                cf.Learn(actual);
            }
        }

        public string ExtractArea(IntField image, int x, int y, int height)
        {
            if (x < _xpos || x > image.Width - _width + _xpos || y < height - 1)
                return null;

            var sb = new StringBuilder(_width * height);
            for (int cx = x - _xpos; cx < x; cx++)
                sb.Append((char) ('0' + image[cx, y]));
            for (int cy = y - 1; cy > y - height; cy--)
            {
                sb.Append('|');
                for (int cx = x - _xpos; cx < x - _xpos + _width; cx++)
                    sb.Append((char) ('0' + image[cx, cy]));
            }
            return sb.ToString();
        }
    }
}
