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
    public void Next_scrolls_window_without_wrapping()
    {
        var cursor = new ScrollMenuCursor(optionCount: 8, visibleRows: 3);

        cursor.MoveNext();
        cursor.MoveNext();
        cursor.MoveNext();

        Assert.AreEqual(3, cursor.SelectedIndex);
        Assert.AreEqual(1, cursor.StartIndex);
        Assert.AreEqual(4, cursor.EndIndexExclusive);

        Assert.IsTrue(cursor.MoveNext(10));
        Assert.AreEqual(7, cursor.SelectedIndex);
        Assert.AreEqual(5, cursor.StartIndex);
        Assert.IsFalse(cursor.MoveNext());
    }

    [TestMethod]
    public void Previous_stops_at_first_option()
    {
        var cursor = new ScrollMenuCursor(optionCount: 8, visibleRows: 3);

        cursor.MoveNext(7);
        Assert.IsTrue(cursor.MovePrevious(10));

        Assert.AreEqual(0, cursor.SelectedIndex);
        Assert.AreEqual(0, cursor.StartIndex);
        Assert.IsFalse(cursor.MovePrevious());
    }

    [TestMethod]
    public void Page_navigation_keeps_selected_row_visible()
    {
        var cursor = new ScrollMenuCursor(optionCount: 20, visibleRows: 6);

        cursor.MoveNext(6);

        Assert.AreEqual(6, cursor.SelectedIndex);
        Assert.AreEqual(1, cursor.StartIndex);

        cursor.MoveNext(6);

        Assert.AreEqual(12, cursor.SelectedIndex);
        Assert.AreEqual(7, cursor.StartIndex);

        cursor.MovePrevious(6);

        Assert.AreEqual(6, cursor.SelectedIndex);
        Assert.AreEqual(6, cursor.StartIndex);
    }
}
