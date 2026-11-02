namespace PSiptv.Core;

public enum TvNavigationDirection { Left, Right, Up, Down, PageUp, PageDown }

public static class TvChannelNavigation
{
    // -1 lets the caller move focus outside the channel grid at its edges.
    public static int Target(int index, int count, int columns, TvNavigationDirection direction, int pageRows = 1)
    {
        if (index < 0 || index >= count || columns < 1) return -1;
        var target = direction switch
        {
            TvNavigationDirection.Left when index % columns > 0 => index - 1,
            TvNavigationDirection.Right when index % columns < columns - 1 && index + 1 < count => index + 1,
            TvNavigationDirection.Up when index >= columns => index - columns,
            TvNavigationDirection.Down when index / columns < (count - 1) / columns => Math.Min(count - 1, index + columns),
            TvNavigationDirection.PageUp => Math.Max(0, index - columns * Math.Max(1, pageRows)),
            TvNavigationDirection.PageDown => Math.Min(count - 1, index + columns * Math.Max(1, pageRows)),
            _ => -1
        };
        return target == index ? -1 : target;
    }
}
