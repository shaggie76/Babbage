﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;

using Microsoft.Win32;

// TODO: study techniques described in http://www.sudokuwiki.org/sudoku.htm
// TODO: undo?
// TOOD: accelerators
// TOOD: deleting entry (recalculate)

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

        private string REGISTRY_KEY = "HKEY_CURRENT_USER\\Software\\Babbage";
        private string REGISTRY_BOARD = "Board";

        private int[,] mCells = new int[N, N];
        private int mPending = N * N;
        private DataGridView mGridView = new DataGridView();

        private int mRun = 0;
        private bool mDirty = false;

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
        private int[] mIsolatedCells = new int[N];

        static int BitCount(int bits)
        {
           // Works for at most 14-bit values
           return((int)(((uint)(bits) * 0x200040008001UL & 0x111111111111111UL) % 0xf));
        }

        public Board()
        {
            InitializeComponent();
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
                            Debug.Print("[" + row + "," + col + "] = " + (v + 1));
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
        private bool FindHiddenSingles()
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
                        Debug.Print("[" + isoRow + "," + isoCol + "] = " + v + " (Hidden single by " + mPatternLabels[pattern] + ")");
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

        // Finds a value that is isolated to a single row or column within a block
        private bool MaskCollinear()
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
                                    Debug.Print("Masked row " + (rowBegin + i) + " with " + (v + 1) + " from block " + bc);
                                    return(true);
                                }
                            }
                            else if(countPerCol[i, v] == c)
                            {
                                if(MaskCol(colBegin + i, v + 1, br))
                                {
                                    Debug.Print("Masked col " + (colBegin + i) + " with " + (v + 1) + " from block " + br);
                                    return(true);
                                }
                            }
                        }
                    }
                }
            }

            return(false);
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

        private void GetPotentialCells(int pattern)
        {
            for(int i = 0; i < N; ++i)
            {
                PotentialCells pc = mPotentialCells[i];
                pc.number = i;
                pc.cellMask = 0;
                pc.cellCount = 0;
            }

            for(int cell = 0; cell < N; ++cell)
            {
                int row = mPatterns[pattern, cell].row;
                int col = mPatterns[pattern, cell].col;
                UpdatePotentialCells(mCells[row, col], cell);
            }
        }
        
        private int FindNakedPair(int begin, out int bitMask)
        {
            bitMask = 0;

            int i;
             
            for(i = begin; i < N;)
            {
                PotentialCells pc = mPotentialCells[i];

                if(pc.cellCount != 2)
                {
                    ++i;
                    continue;
                }

                int end = i + pc.cellCount;

                if(end > N)
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

        private String GetGroupName(int bitMask)
        {
            List<int> values = new List<int>();
            int v;
            int bit;

            for(v = 0, bit = 1; v < N; ++v, bit <<= 1)
            {
                if((bitMask & bit) != 0)
                {
                    values.Add(v + 1);
                }
            }
            
            return("[" + String.Join(", ", values.ToArray())+ "]");
        }

        // Naked pairs only show up in exactly 2 places in a pattern
        private bool FindNakedPairs()
        {
            for(int pattern = 0; pattern < NUM_PATTERNS; ++pattern)
            {
                GetPotentialCells(pattern);
                Array.Sort(mPotentialCells);

                int bitMask;

                for(int i = FindNakedPair(0, out bitMask); i < N; i = FindNakedPair(i + mPotentialCells[i].cellCount, out bitMask))
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
                        Debug.Print("Naked pair " + GetGroupName(~bitMask) + " in " + mPatternLabels[pattern]);
                        return(true);
                    }
                }
            }

            return(false);
        }

        private bool FindNakedTriples()
        {
            for(int pattern = 0; pattern < NUM_PATTERNS; ++pattern)
            {
                GetPotentialCells(pattern);

                for(int i = 0; i < (N - 2); ++i)
                {
                    ref PotentialCells iCell = ref mPotentialCells[i];

                    if((iCell.cellCount < 2) || (iCell.cellCount > 3))
                    {
                        continue;
                    }

                    for(int j = i + 1; j < (N - 1); ++j)
                    {
                        ref PotentialCells jCell = ref mPotentialCells[j];

                        if((jCell.cellCount < 2) || (jCell.cellCount > 3))
                        {
                            continue;
                        }

                        for(int k = j + 1; k < N; ++k)
                        {
                            ref PotentialCells kCell = ref mPotentialCells[k];

                            if((kCell.cellCount < 2) || (kCell.cellCount > 3))
                            {
                                continue;
                            }

                            int cellMask = iCell.cellMask | jCell.cellMask | kCell.cellMask;

                            if(BitCount(cellMask) != 3)
                            {
                                continue;
                            }

                            // Confirmed a naked triple; but is it new?

                            int bitMask = ~((1 << iCell.number) | (1 << jCell.number) | (1 << kCell.number));

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
                                Debug.Print("Naked triple " + GetGroupName(~bitMask) + " in " + mPatternLabels[pattern]);
                                return(true);
                            }
                        }
                    }
                }
            }

            return(false);
        }

        // Finds isolated pairs/triples in a pattern
        private bool FindIsolated()
        {
            for(int pattern = 0; pattern < NUM_PATTERNS; ++pattern)
            {
                for(int cell0 = 0; (cell0 + 1) < N; ++cell0)
                {
                    int row0 = mPatterns[pattern, cell0].row;
                    int col0 = mPatterns[pattern, cell0].col;

                    int bits0 = mCells[row0, col0]; 

                    if((bits0 & CONFIRMED) != 0)
                    {
                        continue;
                    }

                    int bitCount = BitCount(bits0);
                    Debug.Assert(bitCount > 0);

                    if(cell0 + bitCount >= N)
                    {
                        continue;
                    }

                    int isolatedBits = bits0;
                    int numIsolated = 0;
                    mIsolatedCells[numIsolated++] = cell0;

                    for(int cell1 = cell0+1; cell1 < N; ++cell1)
                    {
                        int row1 = mPatterns[pattern, cell1].row;
                        int col1 = mPatterns[pattern, cell1].col;
                    
                        int bits1 = mCells[row1, col1];
                        
                        if(bits0 == bits1)
                        {
                            mIsolatedCells[numIsolated++] = cell1;
#if !(DEBUG)
                            if(numIsolated == bitCount)
                            {
                                break;
                            }
#endif
                        }
                    }
                    Debug.Assert(numIsolated <= bitCount);

                    if(numIsolated != bitCount)
                    {
                        continue;
                    }

                    bool excluded = false;
                    
                    // mask out bits from cells in cells not isolated in pattern
                    int isolatedIndex = 0;

                    for(int cell = 0; cell < N; ++cell)
                    {
                        if(cell == mIsolatedCells[isolatedIndex])
                        {
                            ++isolatedIndex;
                            continue;
                        }

                        int row = mPatterns[pattern, cell].row;
                        int col = mPatterns[pattern, cell].col;

                        int bits = mCells[row, col]; 

                        if((bits & CONFIRMED) != 0)
                        {
                            continue;
                        }

                        if((bits & isolatedBits) != 0)
                        {
                            mCells[row, col] = bits & ~isolatedBits;
                            excluded = true;
                        }
                    }

                    if(excluded)
                    {
                        var icells = new ArraySegment<int>(mIsolatedCells, 0, numIsolated);
                        String cells = String.Join(", ", icells);
                        var noun = (numIsolated == 2) ? "pair" : "group";

                        Debug.Print("Naked isolated " + noun + " " + GetGroupName(isolatedBits) + " in " + mPatternLabels[pattern] + " at cells " + cells);
                        return(true);
                    }
                }
            }
            return(false);
        }

        private static int FindIsolatedBlock(int[] blockMasks, int bit)
        {
            for(int block = 0; block < B; ++block)
            {
                if((blockMasks[block] & bit) == 0)
                {
                    continue;
                }

                for(int next = block + 1; next < B; ++next)
                {
                    if((blockMasks[next] & bit) != 0)
                    {
                        return(-1);
                    }
                }

                return(block);
            }

            return(-1);
        }

        // Excludes a value from all elements in a block except for the given row (returns if anything was masked)
        private bool MaskBlockRows(int blockRow, int blockCol, int bit, int omitRow)
        {
            int rowBegin = blockRow * B;
            int rowEnd = rowBegin + B;

            int colBegin = blockCol * B;
            int colEnd = colBegin + B;

            bool masked = false;

            for(int row = rowBegin; row < rowEnd; ++row)
            {
                if(row == omitRow)
                {
                    continue;
                }
                
                for(int col = colBegin; col < colEnd; ++col)
                {
                    MaskCell(ref masked, row, col, bit);
                }
            }

            return(masked);
        }

        private bool MaskBlockCols(int blockRow, int blockCol, int bit, int omitCol)
        {
            int rowBegin = blockRow * B;
            int rowEnd = rowBegin + B;

            int colBegin = blockCol * B;
            int colEnd = colBegin + B;

            bool masked = false;

            for(int col = colBegin; col < colEnd; ++col)
            {
                if(col == omitCol)
                {
                    continue;
                }
                
                for(int row = rowBegin; row < rowEnd; ++row)
                {
                    MaskCell(ref masked, row, col, bit);
                }
            }

            return(masked);
        }
        
        // If the only potential spots in a line are confined to a single block: mask rest of block
        private bool FindBoxLineReduction()
        {
            // For each row/colum
            //    Generate mask for each block of potentials
            //    For each bit in mask: if bit set in only one block => mask block
            
            int[] blockMasks = new int[B];

            for(int row = 0; row < N; ++row)
            {
                Array.Clear(blockMasks, 0, blockMasks.Length);

                for(int col = 0; col < N; ++col)
                {
                    int bits = mCells[row, col];

                    if((bits & CONFIRMED) != 0)
                    {
                        continue;
                    }

                    int block = (col / B);
                    blockMasks[block] |= bits;
                }

                for(int v = 0, bit = 1; v < N; ++v, bit <<= 1)
                {
                    int block = FindIsolatedBlock(blockMasks, bit);

                    if(block < 0)
                    {
                        continue;
                    }

                    // Mask v/bit from block:
                    if(MaskBlockRows(row / B, block, bit, row))
                    {
                        Debug.Print("Masked box-" + block + "-row-" + row + " reduction for " + (v + 1));
                        return(true);
                    }
                }
            }

            for(int col = 0; col < N; ++col)
            {
                Array.Clear(blockMasks, 0, blockMasks.Length);

                for(int row = 0; row < N; ++row)
                {
                    int bits = mCells[row, col];

                    if((bits & CONFIRMED) != 0)
                    {
                        continue;
                    }

                    int block = (row / B);
                    blockMasks[block] |= bits;
                }

                for(int v = 0, bit = 1; v < N; ++v, bit <<= 1)
                {
                    int block = FindIsolatedBlock(blockMasks, bit);

                    if(block < 0)
                    {
                        continue;
                    }

                    // Mask v/bit from block:
                    if(MaskBlockCols(block, col / B, bit, col))
                    {
                        Debug.Print("Masked box-" + block + "-col-" + col + " reduction for " + (v + 1));
                        return(true);
                    }
                }
            }

            return(false);
        }

        private void HandleApplicationIdle(object sender, EventArgs e)
        {
            Debug.Assert(mPending > 0);
            Debug.Assert(mRun > 0);

            int prevPending = mPending;
            bool wasDirty = mDirty;

            if
            (
                FindConfirmed() ||
                FindHiddenSingles() ||
                MaskCollinear() ||
                FindNakedPairs() ||
                FindBoxLineReduction() ||
                FindIsolated() ||
                FindNakedTriples()
            )
            {
                int found = (prevPending - mPending);
                --mRun;

                if((mRun == 0) || (mPending == 0))
                {
                    Application.Idle -= HandleApplicationIdle;
                }
                else
                {
                    mGridView.Invalidate(); // We need another idle cycle (TODO; improve)

                    //for(int row = 0; row < N; ++row)
                    //{
                    //    for(int col = 0; col < N; ++col)
                    //    {
                    //        string value = mGridView.Rows[row].Cells[col].FormattedValue.ToString();
                    //    }
                    //}
                }

                if(found > 0)
                {
                    mDirty = wasDirty;
                }
            }
            else
            {
                mRun = 0;
                Application.Idle -= HandleApplicationIdle;
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

            object cellValue = mGridView.Rows[r].Cells[c].Value;

            if(cellValue == null)
            {
                return;
            }

            string value = cellValue.ToString();

            if(string.IsNullOrEmpty(value))
            {
                return;
            }

            int v;
            bool parsed = int.TryParse(value, out v);
            Debug.Assert(parsed && (v >= 1) && (v <= N));
            
            OnValueChanged(r, c, v);
            mDirty = true;
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

        private void HandleFormClosing(object sender, CancelEventArgs e)
        {
            if(!mDirty)
            {
                return;
            }

            DialogResult result = MessageBox.Show("Do you want to save the board before quitting?", "Babbage", MessageBoxButtons.YesNoCancel);

            if(result == DialogResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if(result == DialogResult.Yes)
            {
                SaveState();
            }
        }
        
        private void Game_Save(object sender, EventArgs e)
        {
            SaveState();
            mDirty = false;
        }

        private void Game_Clear(object sender, EventArgs e)
        {
            if(MessageBox.Show("Clear the board?", "Babbage", MessageBoxButtons.OKCancel) == DialogResult.OK) 
            {
                ResetState();
            }
        }

        private void Game_Quit(object sender, EventArgs e)
        {
            Close();
        }

        private void Solver_Run(object sender, EventArgs e)
        {
            mRun = Int32.MaxValue;
            Application.Idle += HandleApplicationIdle;
        }

        private void Solver_Step(object sender, EventArgs e)
        {
            mRun = 1;
            Application.Idle += HandleApplicationIdle;
        }

        private void Board_Load(object sender, EventArgs e)
        {
            MainMenu mainMenu = new MainMenu();
            this.Menu = mainMenu;

            MenuItem game = new MenuItem("Game");
            mainMenu.MenuItems.Add(game);
                game.MenuItems.Add("&Save", new EventHandler(Game_Save));
                game.MenuItems.Add("&Clear", new EventHandler(Game_Clear));
                game.MenuItems.Add("&Quit", new EventHandler(Game_Quit));
            MenuItem solver = new MenuItem("Solver");
            mainMenu.MenuItems.Add(solver);
                solver.MenuItems.Add("&Run", new EventHandler(Solver_Run));
                solver.MenuItems.Add("&Step", new EventHandler(Solver_Step));
            // etc ...
            
            this.FormClosing += HandleFormClosing;

            int cellWidth = this.LogicalToDeviceUnits(CELL_WIDTH);
            int cellHeight = this.LogicalToDeviceUnits(CELL_HEIGHT);
            int dividerSize2 = (2 * DIVIDER_SIZE);

            this.ClientSize = new Size((N * cellWidth) + dividerSize2, (N * cellHeight) + dividerSize2);

            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;

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

            foreach(DataGridViewColumn col in mGridView.Columns)
            {
                col.Width = cellWidth;
            }

            foreach(DataGridViewRow row in mGridView.Rows)
            {
                row.Height = cellHeight;
            }

            ResetState();

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

            mGridView.DefaultCellStyle.Font = new Font(mGridView.Font.Name, this.LogicalToDeviceUnits(13));

            LoadState();
        }

        private void SaveState()
        {
            string text = "";
            for(int row = 0; row < N; ++row)
            {
                for(int col = 0; col < N; ++col)
                {
                    object cellValue = mGridView.Rows[row].Cells[col].Value;

                    if(cellValue == null)
                    {
                        continue;
                    }

                    string value = cellValue.ToString();

                    if(string.IsNullOrEmpty(value))
                    {
                        text += " ";
                    }
                    else
                    {
                        Debug.Assert(value.Length == 1);
                        text += value;
                    }
                }
            }

            Debug.Print("Saving [" + text + "]");
            Registry.SetValue(REGISTRY_KEY, REGISTRY_BOARD, text);
        }

        private void LoadState()
        {
            string text = (string)Registry.GetValue(REGISTRY_KEY, REGISTRY_BOARD, "");

            if(string.IsNullOrEmpty(text))
            {
                return;
            }

            if(text.Length != (N * N))
            {
                Debug.Print("Registry state invalid.");
                return;
            }

            // text = "6  71 3  1 54 3 8 37    1 49163  2   876 5913  31    67      31 6 9314   31 7    ";
            char[] sample = text.ToCharArray();
            Debug.Assert(sample.Length == N * N);

            int i = 0;
            for(int row = 0; row < N; ++row)
            {
                for(int col = 0; col < N; ++col, ++i)
                {
                    if(sample[i] != ' ')
                    {
                        mGridView.Rows[row].Cells[col].Value = sample[i];
                    }
                }
            }

            mDirty = false;
        }

        private void ResetState()
        {
            for(int row = 0; row < N; ++row)
            {
                for(int col = 0; col < N; ++col)
                {
                    mCells[row,col] = MASK;
                }
            }

            mPending = N * N;

            for(int row = 0; row < N; ++row)
            {
                for(int col = 0; col < N; ++col)
                {
                    mGridView.Rows[row].Cells[col].Value = "";
                }
            }
        }
    }
}
