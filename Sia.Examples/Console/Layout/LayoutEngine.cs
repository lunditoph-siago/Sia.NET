#if !BROWSER
namespace Sia_Examples.Console.Layout;

internal static class LayoutEngine
{
    public static Rect[] Split(Rect area, Direction direction, params Constraint[] constraints)
    {
        var total = direction == Direction.Horizontal ? area.Width : area.Height;
        var sizes = Resolve(total, constraints);

        var result = new Rect[constraints.Length];
        var offset = 0;
        for (var i = 0; i < constraints.Length; i++) {
            result[i] = direction == Direction.Horizontal
                ? new Rect(area.X + offset, area.Y, sizes[i], area.Height)
                : new Rect(area.X, area.Y + offset, area.Width, sizes[i]);
            offset += sizes[i];
        }
        return result;
    }

    private static int[] Resolve(int total, Constraint[] constraints)
    {
        var sizes = new int[constraints.Length];
        var remaining = Math.Max(total, 0);
        var fillTotalWeight = 0;
        var fillCount = 0;

        for (var i = 0; i < constraints.Length; i++) {
            var constraint = constraints[i];
            switch (constraint.Kind) {
                case ConstraintKind.Length:
                    sizes[i] = Math.Clamp(constraint.Value, 0, remaining);
                    remaining -= sizes[i];
                    break;
                case ConstraintKind.Percentage:
                    sizes[i] = Math.Clamp(total * constraint.Value / 100, 0, remaining);
                    remaining -= sizes[i];
                    break;
                case ConstraintKind.Fill:
                    fillTotalWeight += Math.Max(constraint.Value, 1);
                    fillCount++;
                    break;
            }
        }

        if (fillCount > 0 && remaining > 0) {
            var distributed = 0;
            var seen = 0;
            for (var i = 0; i < constraints.Length; i++) {
                if (constraints[i].Kind != ConstraintKind.Fill) {
                    continue;
                }
                seen++;
                var weight = Math.Max(constraints[i].Value, 1);
                var share = seen == fillCount
                    ? remaining - distributed
                    : remaining * weight / fillTotalWeight;
                sizes[i] = share;
                distributed += share;
            }
            remaining -= distributed;
        }

        if (remaining > 0 && constraints.Length > 0) {
            sizes[^1] += remaining;
        }

        return sizes;
    }
}
#endif
