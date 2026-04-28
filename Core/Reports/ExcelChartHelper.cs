using System.IO;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Wpf;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Renders chart images for Excel export using OxyPlot.
/// ClosedXML does not support native charts; we embed chart as PNG image.
/// </summary>
public static class ExcelChartHelper
{
    /// <summary>Creates a bar chart PNG for the given (name, count) data. Returns PNG bytes or null.</summary>
    public static byte[]? CreateBarChartPng(List<(string Name, int Count)> data, string title, int width = 500, int height = 300)
    {
        if (data == null || data.Count == 0) return null;

        var toShow = data.Take(20).ToList();
        var plot = new PlotModel { Title = title, TitleFontSize = 12 };
        plot.Axes.Add(new CategoryAxis
        {
            Position = AxisPosition.Left,
            ItemsSource = toShow.Select(d => d.Name.Length > 25 ? d.Name[..22] + "..." : d.Name).ToArray(),
            FontSize = 9
        });
        plot.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Minimum = 0,
            Title = "Count",
            FontSize = 9
        });
        plot.Series.Add(new BarSeries
        {
            ItemsSource = toShow.Select(d => new BarItem(d.Count)).ToArray(),
            FillColor = OxyColor.FromRgb(14, 99, 156),
            StrokeColor = OxyColor.FromRgb(10, 70, 110),
            StrokeThickness = 1
        });

        using var stream = new MemoryStream();
        var exporter = new PngExporter { Width = width, Height = height };
        exporter.Export(plot, stream);
        return stream.ToArray();
    }
}
