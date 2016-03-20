using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RT.KitchenSink.Collections;
using RT.Util.ExtensionMethods;

namespace i4c
{
    public abstract class Compressor
    {
        public string Name;
        public string CanonicalFileName;

        /// <summary>Must initialise this to the default config</summary>
        protected RVariant[] Config = new RVariant[0];
        public string ConfigString;

        private Dictionary<string, double> _counters = new Dictionary<string, double>();
        public Dictionary<string, double> Counters;

        public List<Tuple<string, IntField>> Images = new List<Tuple<string, IntField>>();
        public Dictionary<string, RVariant[]> Dumps = new Dictionary<string, RVariant[]>();

        public virtual void Configure(params RVariant[] args)
        {
            for (int i = 0; i < args.Length && i < Config.Length; i++)
                Config[i] = args[i];
            ConfigString = Config.Select(val => (string) val).JoinString(",");
        }

        public abstract void Encode(IntField image, Stream output);
        public abstract IntField Decode(Stream input);

        public void AddImageGrayscale(IntField image, string caption)
        {
            AddImageGrayscale(image, image.Data.Min(), image.Data.Max(), caption);
        }

        public void AddImageGrayscale(IntField image, int min, int max, string caption)
        {
            IntField temp = image.Clone();
            temp.ArgbFromField(min, max);
            Images.Add(new Tuple<string, IntField>(caption, temp));
        }

        public void AddImageArgb(IntField image, string caption)
        {
            Images.Add(new Tuple<string, IntField>(caption, image.Clone()));
        }

        public void AddIntDump(string name, IEnumerable<int> list)
        {
            Dumps.Add(name, list.Select(val => (RVariant) val).ToArray());
        }

        public void SetCounter(string name, double value)
        {
            if (_counters.ContainsKey(name))
                _counters[name] = value;
            else
                _counters.Add(name, value);
        }

        public void IncCounter(string name, double value)
        {
            if (_counters.ContainsKey(name))
                _counters[name] += value;
            else
                _counters.Add(name, value);
        }

        public void ComputeCounterTotals()
        {
            Counters = new Dictionary<string, double>();
            foreach (var key in _counters.Keys)
            {
                // Add the value
                Counters.Add(key, _counters[key]);

                // Add the totals
                string[] parts = key.Split('|');
                string curname = "";
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    curname += (curname == "" ? "" : "|") + parts[i];

                    if (Counters.ContainsKey(curname))
                        Counters[curname] += _counters[key];
                    else
                        Counters[curname] = _counters[key];
                }
            }
        }
    }

    public abstract class CompressorFixedSizeFieldcode : Compressor
    {
        public FixedSizeForeseer Seer;
        protected int FieldcodeSymbols;

        public CompressorFixedSizeFieldcode()
        {
            Config = new RVariant[] { 7, 7, 3, 1024 };
        }

        public override void Configure(params RVariant[] args)
        {
            base.Configure(args);
            FieldcodeSymbols = (int) Config[3];
            Seer = new FixedSizeForeseer((int) Config[0], (int) Config[1], (int) Config[2], new HorzVertForeseer());
        }
    }
}
