using System.Collections.Generic;
using System.IO;
using RT.Util.Collections;

namespace i4c
{
    public class CsvTable
    {
        private List<List<RVariant>> _data = new List<List<RVariant>>();

        public int CurRow = 0;
        public int CurCol = 0;
        public bool AdvanceRight = true;

        public void Add(RVariant value)
        {
            this[CurRow, CurCol] = value;
            AdvanceCursor();
        }

        public void AdvanceCursor()
        {
            if (AdvanceRight)
                CurCol++;
            else
                CurRow++;
        }

        public void SetCursor(int row, int col)
        {
            CurRow = row;
            CurCol = col;
        }

        public RVariant this[int row, int col]
        {
            get
            {
                if (row < _data.Count && col < _data[row].Count)
                    return _data[row][col];
                else
                    return new RVariant();
            }
            set
            {
                while (row >= _data.Count)
                    _data.Add(new List<RVariant>());
                while (col >= _data[row].Count)
                    _data[row].Add(new RVariant());

                _data[row][col] = value;
            }
        }

        public void SaveToFile(string name)
        {
            StreamWriter wr = new StreamWriter(name);
            foreach (var row in _data)
            {
                foreach (var cell in row)
                {
                    if (cell.Kind == RVariantKind.Stub)
                        wr.Write(",");
                    else
                        wr.Write("\"" + cell.ToString().Replace("\"", "\"\"") + "\",");
                }
                wr.WriteLine();
            }
            wr.Close();
        }
    }
}
