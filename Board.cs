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
        private const int B = 3; // Block Size
        private const int N = 9; // Board Size
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

        // Cells that have been confirmed to have only one value
        private bool FindConfirmed()
        {
            for(int row = 0; row < N; ++row)
            {
                for(int col = 0; col < N; ++col)
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
                            Debug.Print("Confirmed [" + row + "," + col + "] as " + (v + 1));
                            mGridView.Rows[row].Cells[col].Value = (v + 1);
                            return(true);
                        }
                    }

                    Debug.Fail("Bit not found");
                }
            }

            return(false);
        }

        // Finds a value that is isolated by row, column or block
        private bool FindIsolated()
        {

            return(false);
        }

        // Excludes a value from all elements in a row except for the given block (returns if anything was masked)
        private bool MaskRow(int row, int value, int omitBlock)
        {
            int omitBegin = omitBlock * B;
            int omitEnd = omitBegin + B;
            int bit = 1 << (value - 1);
            bool masked = false;

            for(int col = 0; col < omitBegin; ++col)
            {
                int cellValue = mCells[row, col]; 

                if((cellValue & bit) != 0)
                {
                    Debug.Assert((cellValue & CONFIRMED) == 0);
                    masked = true;
                    mCells[row, col] = cellValue ^ bit;
                }
            }

            for(int col = omitEnd; col < N; ++col)
            {
                int cellValue = mCells[row, col];

                if((cellValue & bit) != 0)
                {
                    Debug.Assert((cellValue & CONFIRMED) == 0);
                    masked = true;
                    mCells[row, col] = cellValue ^ bit;
                }
            }

            return(masked);
        }

        // Excludes a value from all elements in a col except for the given block (returns if anything was masked)
        private bool MaskCol(int col, int value, int omitBlock)
        {
            int omitBegin = omitBlock * B;
            int omitEnd = omitBegin + B;
            int bit = 1 << (value - 1);
            bool masked = false;

            for(int row = 0; row < omitBegin; ++row)
            {
                int cellValue = mCells[row, col]; 

                if((cellValue & bit) != 0)
                {
                    Debug.Assert((cellValue & CONFIRMED) == 0);
                    masked = true;
                    mCells[row, col] = cellValue ^ bit;
                }
            }

            for(int row = omitEnd; row < N; ++row)
            {
                int cellValue = mCells[row, col];

                if((cellValue & bit) != 0)
                {
                    Debug.Assert((cellValue & CONFIRMED) == 0);
                    masked = true;
                    mCells[row, col] = cellValue ^ bit;
                }
            }

            return(masked);
        }

        // Finds a value that is isolated to a single row or column in 3x3 block
        private bool FindCollinear()
        {
            int[] countPerBlock = new int[N];
            int[,] countPerRow = new int[B,N]; // [row,value]
            int[,] countPerCol = new int[B,N]; // [col,value]

            for(int br = 0; br < B; ++br)
            {
                int rowBegin = br * B;

                for(int bc = 0; bc < B; ++bc)
                {
                    int colBegin = bc * B;

                    Array.Clear(countPerBlock, 0, countPerBlock.Length);
                    Array.Clear(countPerRow, 0, countPerRow.Length);
                    Array.Clear(countPerCol, 0, countPerCol.Length);

                    for(int row = 0; row < B; ++row)
                    {
                        for(int col = 0; col < B; ++col)
                        {
                            int bits = mCells[rowBegin + row, colBegin + col];

                            if((bits & CONFIRMED) != 0)
                            {
                                continue;
                            }
                    
                            for(int v = 0, bit = 1; v < N; ++v, bit <<= 1)
                            {
                                if((bits & bit) != 0)
                                {
                                    ++countPerBlock[v];
                                    ++countPerRow[row, v];
                                    ++countPerCol[col, v];
                                }
                            }
                        }
                    }

                    for(int v = 0; v < N; ++v)
                    {
                        int c = countPerBlock[v];

                        if((c < 2) || (c > B))
                        {
                            continue;
                        }

                        for(int i = 0; i < B; ++i)
                        {
                            if(countPerRow[i, v] == c)
                            {
                                if(MaskRow(rowBegin + i, v + 1, bc))
                                {
                                    Debug.Print("Masked row " + (rowBegin + i) + " with " + (v + 1) + " for block " + bc);
                                    return(true);
                                }
                            }
                            else if(countPerCol[i, v] == c)
                            {
                                if(MaskCol(colBegin + i, v + 1, br))
                                {
                                    Debug.Print("Masked col " + (colBegin + i) + " with " + (v + 1) + " for block " + br);
                                    return(true);
                                }
                            }
                        }
                    }
                }
            }

            return(false);
        }

        private void HandleApplicationIdle(object sender, EventArgs e)
        {
            Debug.Assert(mPending > 0);

            if
            (
                FindConfirmed() ||
                FindIsolated() ||
                FindCollinear()
            )
            {
                if(mPending == 0)
                {
                    Application.Idle -= HandleApplicationIdle;
                }

                mGridView.Invalidate();
            }
        }

        private void SetValue(int r, int c, int v)
        {
            Debug.Assert(mPending > 0);

            int bit = 1 << (v - 1);
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

            int rowBegin = r - (r % B);
            int rowEnd = rowBegin + B;

            int colBegin = c - (c % B);
            int colEnd = colBegin + B;

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

            int v;
            bool parsed = int.TryParse(value, out v);
            if(parsed && (v >= 1) && (v <= N))
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

            int v;
            bool parsed = int.TryParse(value, out v);
            Debug.Assert(parsed && (v >= 1) && (v <= N));
            
            SetValue(r, c, v);
        }
        
        void ShowCellToolTip(object sender, DataGridViewCellFormattingEventArgs e)
        {
            DataGridViewCell cell = mGridView.Rows[e.RowIndex].Cells[e.ColumnIndex];
            int bits = mCells[e.RowIndex, e.ColumnIndex];
            
            if((bits & CONFIRMED) != 0)
            {
                cell.ToolTipText = "";
                return;
            }

            List<int> values = new List<int>();
            
            for(int v = 0, bit = 1; v < N; ++v, bit <<= 1)
            {
                if((bits & bit) != 0)
                {
                    values.Add(v + 1);
                }
            }
            
            cell.ToolTipText = String.Join(", ", values.ToArray());
        }

        private void Board_Load(object sender, EventArgs e)
        {
            this.ClientSize = new Size(N * CELL_WIDTH, N * CELL_HEIGHT);

            this.Controls.Add(mGridView);

            mGridView.ColumnCount = N;
            mGridView.RowCount = N;

            mGridView.AllowUserToResizeColumns = false;
            mGridView.AllowUserToResizeRows = false;

            mGridView.RowHeadersVisible = false;
            mGridView.ColumnHeadersVisible = false;

            mGridView.MultiSelect = false;
            mGridView.Dock = DockStyle.Fill;
            mGridView.ScrollBars = ScrollBars.None;

            mGridView.CellFormatting += ShowCellToolTip;

            foreach(DataGridViewRow row in mGridView.Rows)
            {
                row.Height = CELL_HEIGHT;
            }

            foreach(DataGridViewColumn col in mGridView.Columns)
            {
                col.Width = CELL_WIDTH;
            }

            for(int row = 0; row < N; ++row)
            {
                for(int col = 0; col < N; ++col)
                {
                    mCells[row,col] = MASK;
                }
            }

            mPending = N * N;

            mGridView.CellValidating += new DataGridViewCellValidatingEventHandler(CellValidating);
            mGridView.CellValueChanged += new DataGridViewCellEventHandler(CellValueChanged);

            // Sample puzzle copied from http://www.sudoku.com/ 
            string[] sample = { "7", "5", "", "4", "6", "2", "", "9", "1", "1", "", "", "", "", "", "5", "2", "4", "4", "9", "2", "", "5", "1", "7", "", "", "", "2", "7", "", "", "", "", "", "", "", "", "4", "", "", "", "6", "", "", "", "", "", "", "", "", "2", "4", "", "", "", "9", "7", "1", "", "4", "6", "2", "6", "7", "1", "", "", "", "", "", "8", "2", "4", "", "6", "9", "8", "", "7", "3" };

            int i = 0;
            for(int row = 0; row < N; ++row)
            {
                for(int col = 0; col < N; ++col, ++i)
                {
                    DataGridViewCell cell = mGridView.Rows[row].Cells[col];
                    cell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    cell.Value = sample[i];
                }
            }
        }
    }
}
