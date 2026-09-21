using System;
using System.Numerics;

namespace NoireLib.UI;

internal static class TourDim
{
    public const int MaxHoles = 2;

    public const int MaxBands = 25;

    public static int Bands(Vector4[] destination, Vector2 viewMin, Vector2 viewMax, Vector4[] holes, int holeCount)
    {
        holeCount = Math.Clamp(holeCount, 0, Math.Min(MaxHoles, holes.Length));

        Span<float> columns = stackalloc float[(MaxHoles * 2) + 2];
        Span<float> rows = stackalloc float[(MaxHoles * 2) + 2];

        var columnCount = Edges(columns, viewMin.X, viewMax.X, holes, holeCount, horizontal: true);
        var rowCount = Edges(rows, viewMin.Y, viewMax.Y, holes, holeCount, horizontal: false);

        var count = 0;

        for (var row = 0; row < rowCount - 1; row++)
        {
            var top = rows[row];
            var bottom = rows[row + 1];

            if (bottom <= top)
                continue;

            var runStart = float.NaN;

            for (var column = 0; column < columnCount - 1; column++)
            {
                var left = columns[column];
                var right = columns[column + 1];
                var lit = right > left && IsInside(holes, holeCount, (left + right) * 0.5f, (top + bottom) * 0.5f);

                if (!lit && right > left)
                {
                    if (float.IsNaN(runStart))
                        runStart = left;

                    continue;
                }

                if (float.IsNaN(runStart))
                    continue;

                count = Append(destination, count, runStart, top, left, bottom);
                runStart = float.NaN;
            }

            if (!float.IsNaN(runStart))
                count = Append(destination, count, runStart, top, columns[columnCount - 1], bottom);
        }

        return count;
    }

    private static int Edges(Span<float> destination, float min, float max, Vector4[] holes, int holeCount, bool horizontal)
    {
        var count = 0;
        count = Insert(destination, count, min);
        count = Insert(destination, count, max);

        for (var index = 0; index < holeCount; index++)
        {
            var hole = holes[index];
            var low = horizontal ? hole.X : hole.Y;
            var high = horizontal ? hole.Z : hole.W;

            if (low > min && low < max)
                count = Insert(destination, count, low);

            if (high > min && high < max)
                count = Insert(destination, count, high);
        }

        return count;
    }

    private static int Insert(Span<float> destination, int count, float value)
    {
        // Bands sharing an edge must share the same number, or a hairline shows through.
        value = MathF.Round(value);

        var position = 0;

        while (position < count && destination[position] < value)
            position++;

        if (position < count && destination[position] == value)
            return count;

        for (var index = count; index > position; index--)
            destination[index] = destination[index - 1];

        destination[position] = value;
        return count + 1;
    }

    private static bool IsInside(Vector4[] holes, int holeCount, float x, float y)
    {
        for (var index = 0; index < holeCount; index++)
        {
            var hole = holes[index];

            if (x > hole.X && x < hole.Z && y > hole.Y && y < hole.W)
                return true;
        }

        return false;
    }

    private static int Append(Vector4[] destination, int count, float left, float top, float right, float bottom)
    {
        if (count >= destination.Length || right <= left || bottom <= top)
            return count;

        destination[count] = new Vector4(left, top, right, bottom);
        return count + 1;
    }
}
