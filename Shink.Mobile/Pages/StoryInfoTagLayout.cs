using Microsoft.Maui.Layouts;

namespace Shink.Mobile.Pages;

// Keep bubbles at their measured size, wrapping whole bubbles onto centered rows.
internal sealed class StoryInfoTagLayout : Layout
{
    protected override ILayoutManager CreateLayoutManager() => new TagLayoutManager(this);

    private sealed class TagLayoutManager(StoryInfoTagLayout layout) : LayoutManager(layout)
    {
        public override Size Measure(double widthConstraint, double heightConstraint)
        {
            var rows = MeasureRows(Math.Max(0, widthConstraint - Layout.Padding.HorizontalThickness));
            var width = rows.Count == 0 ? 0 : rows.Max(row => row.Width);
            var height = rows.Sum(row => row.Height);
            return new Size(
                ResolveConstraints(widthConstraint, Layout.Width, width + Layout.Padding.HorizontalThickness,
                    Layout.MinimumWidth, Layout.MaximumWidth),
                ResolveConstraints(heightConstraint, Layout.Height, height + Layout.Padding.VerticalThickness,
                    Layout.MinimumHeight, Layout.MaximumHeight));
        }

        public override Size ArrangeChildren(Rect bounds)
        {
            var availableWidth = Math.Max(0, bounds.Width - Layout.Padding.HorizontalThickness);
            var rows = MeasureRows(availableWidth);
            var y = bounds.Top + Layout.Padding.Top;
            foreach (var row in rows)
            {
                var x = bounds.Left + Layout.Padding.Left + Math.Max(0, (availableWidth - row.Width) / 2);
                foreach (var (child, size) in row.Children)
                {
                    child.Arrange(new Rect(x, y + (row.Height - size.Height) / 2, size.Width, size.Height));
                    x += size.Width;
                }
                y += row.Height;
            }
            return new Size(bounds.Width, y - bounds.Top + Layout.Padding.Bottom).AdjustForFill(bounds, Layout);
        }

        private List<Row> MeasureRows(double availableWidth)
        {
            var rows = new List<Row>();
            var row = new Row();
            foreach (var child in Layout)
            {
                if (child.Visibility == Visibility.Collapsed) continue;

                // Measure at the full row width so long labels also get their wrapped height.
                // IView measurements include the bubble's margin and padding.
                var size = child.Measure(availableWidth, double.PositiveInfinity);
                if (row.Children.Count > 0 && row.Width + size.Width > availableWidth)
                {
                    rows.Add(row);
                    row = new Row();
                }
                row.Children.Add((child, size));
                row.Width += size.Width;
                row.Height = Math.Max(row.Height, size.Height);
            }
            if (row.Children.Count > 0) rows.Add(row);
            return rows;
        }

        private sealed class Row
        {
            public List<(IView Child, Size Size)> Children { get; } = [];
            public double Width { get; set; }
            public double Height { get; set; }
        }
    }
}
