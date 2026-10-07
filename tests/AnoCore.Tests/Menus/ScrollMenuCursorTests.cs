using AnoCore.Runtime.Menus;

namespace AnoCore.Tests.Menus;

[TestClass]
public sealed class ScrollMenuCursorTests
{
    [TestMethod]
    public void Empty_cursor_has_no_selection()
    {
        var cursor = new ScrollMenuCursor(0);

        Assert.AreEqual(-1, cursor.SelectedIndex);
        Assert.IsFalse(cursor.MoveNext());
        Assert.IsFalse(cursor.MovePrevious());
    }

    [TestMethod]
    public void Next_scrolls_window_and_wraps()
    {
        var cursor = new ScrollMenuCursor(optionCount: 8, visibleRows: 3);

        cursor.MoveNext();
        cursor.MoveNext();
        cursor.MoveNext();

        Assert.AreEqual(3, cursor.SelectedIndex);
        Assert.AreEqual(1, cursor.StartIndex);
        Assert.AreEqual(4, cursor.EndIndexExclusive);

        for (var index = 0; index < 5; index++)
            cursor.MoveNext();

        Assert.AreEqual(0, cursor.SelectedIndex);
        Assert.AreEqual(0, cursor.StartIndex);
    }

    [TestMethod]
    public void Previous_wraps_to_last_option_and_keeps_it_visible()
    {
        var cursor = new ScrollMenuCursor(optionCount: 8, visibleRows: 3);

        cursor.MovePrevious();

        Assert.AreEqual(7, cursor.SelectedIndex);
        Assert.AreEqual(5, cursor.StartIndex);
        Assert.AreEqual(8, cursor.EndIndexExclusive);
    }
}
