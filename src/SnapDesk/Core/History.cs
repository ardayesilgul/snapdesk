namespace SnapDesk.Core;

/// <summary>
/// Sınırlı derinlikte geri/ileri al yığını. Yeni bir değişiklik ileri yığınını
/// temizler; kapasite aşılırsa en eski kayıt düşer.
/// </summary>
public sealed class History<T> where T : class
{
    private readonly List<T> _undo = new();
    private readonly List<T> _redo = new();

    public History(int capacity = 40)
    {
        Capacity = Math.Max(1, capacity);
    }

    public int Capacity { get; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public int UndoCount => _undo.Count;

    public int RedoCount => _redo.Count;

    /// <summary>Yeni durumu kaydeder ve ileri yığınını temizler.</summary>
    public void Push(T snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _undo.Add(snapshot);
        if (_undo.Count > Capacity)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
    }

    /// <summary>Bir adım geri döner; mevcut durum ileri yığınına taşınır.</summary>
    public T? Undo(T current)
    {
        if (_undo.Count == 0)
        {
            return null;
        }

        _redo.Add(current);
        var snapshot = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        return snapshot;
    }

    /// <summary>Bir adım ileri döner; mevcut durum geri yığınına taşınır.</summary>
    public T? Redo(T current)
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        _undo.Add(current);
        var snapshot = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        return snapshot;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
