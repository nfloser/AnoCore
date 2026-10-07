namespace AnoCore.Runtime.Menus;

public sealed class ScrollMenuCursor
{
    public ScrollMenuCursor(int optionCount, int visibleRows = 6)
    {
        if (optionCount < 0)
            throw new ArgumentOutOfRangeException(nameof(optionCount));
        if (visibleRows <= 0)
            throw new ArgumentOutOfRangeException(nameof(visibleRows));

        OptionCount = optionCount;
        VisibleRows = visibleRows;
        SelectedIndex = optionCount == 0 ? -1 : 0;
    }

    public int OptionCount { get; }

    public int VisibleRows { get; }

    public int SelectedIndex { get; private set; }

    public int StartIndex { get; private set; }

    public int EndIndexExclusive => Math.Min(OptionCount, StartIndex + VisibleRows);

    public bool MovePrevious(int count = 1)
    {
        if (OptionCount == 0 || count <= 0 || SelectedIndex <= 0)
            return false;

        SelectedIndex = Math.Max(0, SelectedIndex - count);
        KeepVisible();
        return true;
    }

    public bool MoveNext(int count = 1)
    {
        if (OptionCount == 0 || count <= 0 || SelectedIndex >= OptionCount - 1)
            return false;

        SelectedIndex = Math.Min(OptionCount - 1, SelectedIndex + count);
        KeepVisible();
        return true;
    }

    private void KeepVisible()
    {
        if (SelectedIndex < StartIndex)
            StartIndex = SelectedIndex;
        else if (SelectedIndex >= StartIndex + VisibleRows)
            StartIndex = SelectedIndex - VisibleRows + 1;

        var maxStart = Math.Max(0, OptionCount - VisibleRows);
        StartIndex = Math.Clamp(StartIndex, 0, maxStart);
    }
}
