using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;

namespace Babbage
{
    public partial class Board : Form
    {
        private const int N = 9;
        private const int MASK = (1 << N) - 1;
        private const int CONFIRMED = (1 << N);

        private const int CELL_WIDTH = 25;
        private const int CELL_HEIGHT = 25;

        private int[,] mCells = new int[N, N];
        private int mPending = N * N;
        private DataGridView mGridView = new DataGridView();
        
        static int BitCount(int bits)
        {
           // Works for at most 14-bit values
           return((int)(((uint)(bits) * 0x200040008001UL & 0x111111111111111UL) % 0xf));
        }

        public Board()
        {
            InitializeComponent();
            Application.Idle += HandleApplicationIdle;
        }

        // Isolated cells that have been fully deduced
        private bool FindIsolated()
        {
            for(int row = 0; row < 9; ++row)
            {
                for(int col = 0; col < 9; ++col)
                {
                    int bits = mCells[row, col];

                    if(BitCount(bits) != 1)
                    {
                        continue;
                    }

                    for(int v = 0, bit = 1; v < N; ++v, bit <<= 1)
                    {
                        if((bits & bit) != 0)
                        {
                            mGridView.Rows[row].Cells[col].Value = (v + 1);
                            return(true);
                        }
                    }

                    Debug.Fail("Bit not found");
                }
            }

            return(false);
        }

        private void HandleApplicationIdle(object sender, EventArgs e)
        {
            Debug.Assert(mPending > 0);

            if
            (
                FindIsolated()
            )
            {
                if(mPending == 0)
                {
                    Application.Idle -= HandleApplicationIdle;
                }
            }
        }

        private void SetValue(int r, int c, int i)
        {
            Debug.Assert(mPending > 0);

            int bit = 1 << (i - 1);
            int cellValue = mCells[r, c]; 
            Debug.Assert((cellValue & bit) != 0);
            --mPending;

            // Mask out bit in row, column and block
            int mask = ~bit;

            for(int row = 0; row < N; ++row)
            {
                mCells[row, c] &= mask;
            }

            for(int col = 0; col < N; ++col)
            {
                mCells[r, col] &= mask;
            }

            int rowBegin = r - (r % 3);
            int rowEnd = rowBegin + 3;

            int colBegin = c - (c % 3);
            int colEnd = colBegin + 3;

            for(int row = rowBegin; row < rowEnd; ++row)
            {
                for(int col = colBegin; col < colEnd; ++col)
                {
                    mCells[row, col] &= mask;
                }
            }

            mCells[r, c] = bit | CONFIRMED;
        }
        
        private void CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            string value = e.FormattedValue.ToString();
            if(string.IsNullOrEmpty(value))
            {
                return;
            }

            int i;
            bool parsed = int.TryParse(value, out i);
            if(parsed && (i >= 1) && (i <= N))
            {
                return;
            }
            
            e.Cancel = true;
        }

        private void CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            int r = e.RowIndex;
            int c = e.ColumnIndex;
            string value = mGridView.Rows[r].Cells[c].Value.ToString();

            if(string.IsNullOrEmpty(value))
            {
                return;
            }

            int i;
            bool parsed = int.TryParse(value, out i);
            Debug.Assert(parsed && (i >= 1) && (i <= N));
            
            SetValue(r, c, i);
        }

        private void Board_Load(object sender, EventArgs e)
        {
            this.ClientSize = new Size(N * CELL_WIDTH, N * CELL_HEIGHT);

            this.Controls.Add(mGridView);

            mGridView.ColumnCount = 9;
            mGridView.RowCount = 9;

            mGridView.AllowUserToResizeColumns = false;
            mGridView.AllowUserToResizeRows = false;

            mGridView.RowHeadersVisible = false;
            mGridView.ColumnHeadersVisible = false;

            mGridView.MultiSelect = false;
            mGridView.Dock = DockStyle.Fill;
            mGridView.ScrollBars = ScrollBars.None;

            foreach (DataGridViewRow row in mGridView.Rows)
            {
                row.Height = CELL_HEIGHT;
            }

            foreach (DataGridViewColumn col in mGridView.Columns)
            {
                col.Width = CELL_WIDTH;
            }

            for(int row = 0; row < 9; ++row)
            {
                for(int col = 0; col < 9; ++col)
                {
                    mCells[row, col] = MASK;
                }
            }

            mPending = N * N;

            mGridView.CellValidating += new DataGridViewCellValidatingEventHandler(CellValidating);
            mGridView.CellValueChanged += new DataGridViewCellEventHandler(CellValueChanged);

            // Sample puzzle copied from http://www.sudoku.com/ 
            string[] sample = { "7", "5", "", "4", "6", "2", "", "9", "1", "1", "", "", "", "", "", "5", "2", "4", "4", "9", "2", "", "5", "1", "7", "", "", "", "2", "7", "", "", "", "", "", "", "", "", "4", "", "", "", "6", "", "", "", "", "", "", "", "", "2", "4", "", "", "", "9", "7", "1", "", "4", "6", "2", "6", "7", "1", "", "", "", "", "", "8", "2", "4", "", "6", "9", "8", "", "7", "3" };

            int i = 0;
            for(int row = 0; row < 9; ++row)
            {
                for(int col = 0; col < 9; ++col, ++i)
                {
                    DataGridViewCell cell = mGridView.Rows[row].Cells[col];
                    cell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    cell.Value = sample[i];
                }
            }
        }
    }
}
