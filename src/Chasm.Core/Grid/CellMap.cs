namespace Chasm.Core.Grid;

/// <summary>Dense per-cell storage for one Tile (fast lookups for pathfinding and drawing).</summary>
public sealed class CellMap<T>
{
    private readonly T[] _values;
    private readonly int _w;

    public CellMap(T initial = default!)
    {
        _ = IsoGrid.AllCells; // ensure bounds computed
        _w = IsoGrid.MaxQ - IsoGrid.MinQ + 1;
        int h = IsoGrid.MaxR - IsoGrid.MinR + 1;
        _values = new T[_w * h];
        Array.Fill(_values, initial);
    }

    private int Index(CellCoord c) => (c.R - IsoGrid.MinR) * _w + (c.Q - IsoGrid.MinQ);

    public static bool InStorage(CellCoord c) =>
        c.Q >= IsoGrid.MinQ && c.Q <= IsoGrid.MaxQ && c.R >= IsoGrid.MinR && c.R <= IsoGrid.MaxR;

    public T this[CellCoord c]
    {
        get => InStorage(c) ? _values[Index(c)] : default!;
        set { if (InStorage(c)) _values[Index(c)] = value; }
    }
}
