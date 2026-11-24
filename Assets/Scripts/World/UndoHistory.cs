namespace SimpleBox.World
{
    /// <summary>
    /// Fixed-size ring of created-object ids for one owner (GMod-style undo: removes the last thing you created).
    /// Push overwrites the oldest entry when full. No allocation after construction.
    /// </summary>
    public sealed class UndoHistory
    {
        private readonly int[] _ids;
        private int _top;   // index of the next free slot
        private int _count;

        public UndoHistory(int capacity)
        {
            _ids = new int[capacity < 1 ? 1 : capacity];
        }

        public int Count => _count;

        public void Push(int id)
        {
            _ids[_top] = id;
            _top = (_top + 1) % _ids.Length;
            if (_count < _ids.Length) _count++;
        }

        public bool TryPop(out int id)
        {
            if (_count == 0)
            {
                id = 0;
                return false;
            }
            _top = (_top - 1 + _ids.Length) % _ids.Length;
            id = _ids[_top];
            _ids[_top] = 0;
            _count--;
            return true;
        }

        public void Clear()
        {
            for (int i = 0; i < _ids.Length; i++) _ids[i] = 0;
            _top = 0;
            _count = 0;
        }
    }
}
