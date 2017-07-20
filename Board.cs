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

        private const int CELL_WIDTH = 32;
        private const int CELL_HEIGHT = 32;
        private const int DIVIDER_SIZE = 2;

        private int[,] mCells = new int[N, N];
        private int mPending = N * N;
        private DataGridView mGridView = new DataGridView();

        private struct Coordinates
        {
            public int row;
            public int col;
        };

        // By Row, By Column, By Block
        private const int NUM_PATTERNS = N + N + N;
        private Coordinates[,] mPatterns = new Coordinates[NUM_PATTERNS, N];
        private String[] mPatternLabels = new String[NUM_PATTERNS];

        private class PotentialCells : IComparable 
        {
            public int CompareTo(object otherObj)
            {
                PotentialCells other = (PotentialCells)otherObj;

                if(cellMask < other.cellMask) { return(-1); }
                if(cellMask > other.cellMask) { return(1); }

                return(0);
            }

            public int number;
            public int cellMask;
            public int cellCount;
        };

        private PotentialCells[] mPotentialCells = new PotentialCells[N];
        
        static int BitCount(int bits)
        {
           // Works for at most 14-bit values
           return((int)(((uint)(bits) * 0x200040008001UL & 0x111111111111111UL) % 0xf));
        }

        public Board()
        {
            InitializeComponent();
            Application.Idle += HandleApplicationIdle;
            int i;

            for(i = 0; i < N; ++i)
            {
                mPotentialCells[i] = new PotentialCells();
            }

            // Build LUT for each type of scan: row, col, block
            i = 0;

            // By Row
            for(int row = 0; row < N; ++row)
            {
                mPatternLabels[i] = "row " + row;
                for(int col = 0; col < N; ++col)
                {
                    mPatterns[i, col].row = row;
                    mPatterns[i, col].col = col;
                }
                ++i;
            }

            // By Column
            for(int col = 0; col < N; ++col)
            {
                mPatternLabels[i] = "col " + col;
                for(int row = 0; row < N; ++row)
                {
                    mPatterns[i, row].row = row;
                    mPatterns[i, row].col = col;
                }
                ++i;
            }

            // By Block
            for(int br = 0; br < B; ++br)
            {
                int rowBegin = br * B;
                int rowEnd = rowBegin + B;

                for(int bc = 0; bc < B; ++bc)
                {
                    int colBegin = bc * B;
                    int colEnd = colBegin + B;
                    int j = 0;

                    mPatternLabels[i] = "block " + br + "," + bc;

                    for(int row = rowBegin; row < rowEnd; ++row)
                    {
                        for(int col = colBegin; col < colEnd; ++col)
                        {
                            mPatterns[i, j].row = row;
                            mPatterns[i, j].col = col;
                            ++j;
                        }
                    }

                    ++i;
                }
            }
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
            for(int v = 1, bit = 1; v <= N; ++v, bit <<= 1)
            {
                for(int pattern = 0; pattern < NUM_PATTERNS; ++pattern)
                {
                    int count = 0;
                    int isoRow = 0; 
                    int isoCol = 0; 

                    for(int cell = 0; cell < N; ++cell)
                    {
                        int row = mPatterns[pattern, cell].row;
                        int col = mPatterns[pattern, cell].col;

                        int cellValue = mCells[row, col]; 

                        if((cellValue & bit) != 0)
                        {
                            if((cellValue & CONFIRMED) != 0)
                            {
                                count = N;
                                break;
                            }

                            ++count;

                            if(count > 1)
                            {
                                break;    
                            }

                            isoRow = row;
                            isoCol = col;
                        }
                    }

                    if(count == 1)
                    {
                        Debug.Print("Isolated [" + isoRow + "," + isoCol + "] as " + v + " by " + mPatternLabels[pattern]);
                        mGridView.Rows[isoRow].Cells[isoCol].Value = v;
                        return(true);
                    }
                }
            }
            
            return(false);
        }

        private void MaskCell(ref bool masked, int row, int col, int bit)
        {
            int cellValue = mCells[row, col]; 

            if((cellValue & bit) == 0)
            {
                return;
            }

            Debug.Assert((cellValue & CONFIRMED) == 0);
            cellValue &= ~bit;
            Debug.Assert((cellValue & MASK) != 0);
            mCells[row, col] = cellValue;
            masked = true;
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
                MaskCell(ref masked, row, col, bit);
            }

            for(int col = omitEnd; col < N; ++col)
            {
                MaskCell(ref masked, row, col, bit);
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
                MaskCell(ref masked, row, col, bit);
            }

            for(int row = omitEnd; row < N; ++row)
            {
                MaskCell(ref masked, row, col, bit);
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

        private void ResetPotentialCells()
        {
            for(int i = 0; i < N; ++i)
            {
                PotentialCells pc = mPotentialCells[i];
                pc.number = i;
                pc.cellMask = 0;
                pc.cellCount = 0;
            }
        }

        // cellNumber can be row, col or block index
        // Transpose bits (possible values in cell)
        // to cellMask (possible cells with value)
        private void UpdatePotentialCells(int bits, int cellNumber)
        {
            if((bits & CONFIRMED) != 0)
            {
                return;
            }

            for(int number = 0, bit = 1; number < N; ++number, bit <<= 1) 
            {
                if((bits & bit) != 0)
                {
                    PotentialCells pc = mPotentialCells[number];

                    pc.cellMask |= (1 << cellNumber);
                    ++pc.cellCount;
                }
            }
        }
        
        private int FindPotentialCellSet(int begin, out int bitMask)
        {
            bitMask = 0;

            int i;
             
            for(i = begin; i < N;)
            {
                PotentialCells pc = mPotentialCells[i];

                if(pc.cellCount == 0)
                {
                    ++i;
                    continue;
                }

                int end = i + pc.cellCount;

                if(end >= N)
                {
                    break;
                }

                int j;
                for(j = i + 1; j < end; ++j)
                {
                    if(mPotentialCells[j].cellMask != pc.cellMask)
                    {
                        break;
                    }
                }

                if(j < end)
                {
                    i = j;
                    continue;
                }

                for(j = i; j < end; ++j)
                {
                    bitMask |= (1 << mPotentialCells[j].number);
                }

                bitMask = ~bitMask;

                return(i);
            }

            return(N);
        }

        private bool FindGroupedExclusions()
        {
            for(int pattern = 0; pattern < NUM_PATTERNS; ++pattern)
            {
                ResetPotentialCells();

                for(int cell = 0; cell < N; ++cell)
                {
                    int row = mPatterns[pattern, cell].row;
                    int col = mPatterns[pattern, cell].col;
                    UpdatePotentialCells(mCells[row, col], cell);
                }

                Array.Sort(mPotentialCells);

                int bitMask;

                for(int i = FindPotentialCellSet(0, out bitMask); i < N; i = FindPotentialCellSet(i + mPotentialCells[i].cellCount, out bitMask))
                {
                    int cellMask = mPotentialCells[i].cellMask;
                    bool excluded = false;
                    int bit = 1;
                    
                    // Do any of the cells referenced in the cellMask have bits not in the bitMask?
                    for(int cell = 0; (cell < N) && (cellMask != 0); ++cell, bit <<= 1)
                    {
                        if((cellMask & bit) != 0)
                        {
                            cellMask &= ~bit;
                        }
                        else
                        {
                            continue;
                        }

                        int row = mPatterns[pattern, cell].row;
                        int col = mPatterns[pattern, cell].col;

                        Debug.Assert((mCells[row, col] & CONFIRMED) == 0);

                        if((mCells[row, col] & bitMask) != 0)
                        {
                            mCells[row, col] &= ~bitMask;
                            excluded = true;
                        }
                    }

                    if(excluded)
                    {
                        List<int> values = new List<int>();
                        int v;

                        for(v = 0, bit = 1; v < N; ++v, bit <<= 1)
                        {
                            if((bitMask & bit) == 0)
                            {
                                values.Add(v + 1);
                            }
                        }
            
                        String group = String.Join(", ", values.ToArray());
                        Debug.Print("Excluded group " + group + " in " + mPatternLabels[pattern]);
                        return(true);
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
                FindCollinear() ||
                FindGroupedExclusions()
            )
            {
                if(mPending == 0)
                {
                    Application.Idle -= HandleApplicationIdle;
                }

                mGridView.Invalidate();
            }
        }

        // Note: value is 1-based here
        private void OnValueChanged(int r, int c, int value)
        {
            Debug.Assert(mPending > 0);
            Debug.Assert((value > 0) && (value <= N));

            int bit = 1 << (value - 1);
            int cellValue = mCells[r, c]; 
            Debug.Assert((cellValue & (bit | CONFIRMED)) == bit);

            mCells[r, c] = MASK; // Temp to avoid DebugAssert

            --mPending;

            // Mask out bit in row, column and block
            int mask = ~bit;

            for(int row = 0; row < N; ++row)
            {
                mCells[row, c] &= mask;
                Debug.Assert((mCells[row, c] & MASK) != 0);
            }

            for(int col = 0; col < N; ++col)
            {
                mCells[r, col] &= mask;
                Debug.Assert((mCells[r, col] & MASK) != 0);
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
                    Debug.Assert((mCells[row, col] & MASK) != 0);
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
                int bit = 1 << (v - 1);
                int cellValue = mCells[e.RowIndex, e.ColumnIndex];

                if((cellValue & bit) != 0)
                {
                    return;
                }
            }

            e.Cancel = true;
        }

        private void OnValueChanged(object sender, DataGridViewCellEventArgs e)
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
            
            OnValueChanged(r, c, v);
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
            this.ClientSize = new Size((N * CELL_WIDTH) + (2 * DIVIDER_SIZE), (N * CELL_HEIGHT) + (2 * DIVIDER_SIZE));

            this.Controls.Add(mGridView);

            mGridView.ColumnCount = N;
            mGridView.RowCount = N + 1; // AllowUserToAddRows = false seems to require one more

            mGridView.AllowUserToResizeColumns = false;
            mGridView.AllowUserToOrderColumns = false;

            mGridView.RowHeadersVisible = false;
            mGridView.ColumnHeadersVisible = false;
                        
            mGridView.AllowUserToAddRows = false;
            mGridView.AllowUserToDeleteRows = false;
            mGridView.AllowUserToResizeRows = false;

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
            mGridView.CellValueChanged += new DataGridViewCellEventHandler(OnValueChanged);

            mGridView.Columns[B - 1].DividerWidth = 2;
            mGridView.Columns[(2 * B) - 1].DividerWidth = 2;
            mGridView.Rows[B - 1].DividerHeight = 2;
            mGridView.Rows[(2 * B) - 1].DividerHeight = 2;

            for(int row = 0; row < N; ++row)
            {
                for(int col = 0; col < N; ++col)
                {
                    mGridView.Rows[row].Cells[col].Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                }
            }

            mGridView.DefaultCellStyle.Font = new Font(mGridView.Font.Name, 14);

            char[] sample = "6  71 3  1 54 3 8 37    1 49163  2   876 5913  31    67      31 6 9314   31 7    ".ToCharArray();
            Debug.Assert(sample.Length == N * N);

            int i = 0;
            for(int row = 0; row < N; ++row)
            {
                for(int col = 0; col < N; ++col, ++i)
                {
                    if(sample[i] != ' ')
                    {
                        // mGridView.Rows[row].Cells[col].Value = sample[i];
                    }
                }
            }
        }
    }
}
