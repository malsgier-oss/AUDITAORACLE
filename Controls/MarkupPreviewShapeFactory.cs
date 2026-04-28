using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json;
using WorkAudit.Domain;
using WBrush = System.Windows.Media.Brush;
using WBrushes = System.Windows.Media.Brushes;
using WColor = System.Windows.Media.Color;

namespace WorkAudit.Controls;

/// <summary>Builds WPF elements for persisted <see cref="MarkupRegion"/> preview (image or PDF canvas).</summary>
internal static class MarkupPreviewShapeFactory
{
    public const string InkStrokeColor = "#CC3399FF";
    public const string TextForegroundColor = "#FFE8E8E8";

    public static UIElement Create(MarkupRegion m, double cw, double ch)
    {
        var x = m.NormX * cw;
        var y = m.NormY * ch;
        var w = m.NormW * cw;
        var h = m.NormH * ch;

        try
        {
            if (m.Kind == MarkupKind.Highlight)
            {
                var fill = (WBrush)new BrushConverter().ConvertFromString(m.Color)!;
                var r = new System.Windows.Shapes.Rectangle
                {
                    Width = Math.Max(1, w),
                    Height = Math.Max(1, h),
                    Fill = fill,
                    Stroke = WBrushes.Transparent,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(r, x);
                Canvas.SetTop(r, y);
                return r;
            }

            if (m.Kind == MarkupKind.Text)
            {
                var tb = new TextBlock
                {
                    Text = m.Label ?? "",
                    Foreground = TryBrush(m.Color) ?? new SolidColorBrush(WColor.FromRgb(0xE8, 0xE8, 0xE8)),
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = Math.Max(48, w)
                };
                Canvas.SetLeft(tb, x);
                Canvas.SetTop(tb, y);
                return tb;
            }

            if (m.Kind == MarkupKind.Markup)
            {
                var poly = new System.Windows.Shapes.Polyline
                {
                    Stroke = TryBrush(m.Color) ?? new SolidColorBrush(WColor.FromRgb(0x33, 0x99, 0xFF)),
                    StrokeThickness = 2.5,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    IsHitTestVisible = false
                };
                var pts = DeserializeInkPoints(m.Label);
                foreach (var p in pts)
                    poly.Points.Add(new System.Windows.Point(p[0] * cw, p[1] * ch));
                return poly;
            }

            // Rectangle (and legacy unknown kinds drawn as outline)
            WBrush stroke;
            try { stroke = (WBrush)new BrushConverter().ConvertFromString(m.Color)!; }
            catch { stroke = new SolidColorBrush(WColor.FromRgb(0xFF, 0x44, 0x44)); }
            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = Math.Max(1, w),
                Height = Math.Max(1, h),
                Fill = WBrushes.Transparent,
                Stroke = stroke,
                StrokeThickness = 2,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            return rect;
        }
        catch
        {
            var r = new System.Windows.Shapes.Rectangle { Width = Math.Max(1, w), Height = Math.Max(1, h), Fill = WBrushes.Yellow, IsHitTestVisible = false };
            Canvas.SetLeft(r, x);
            Canvas.SetTop(r, y);
            return r;
        }
    }

    private static WBrush? TryBrush(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return null;
        try { return (WBrush)new BrushConverter().ConvertFromString(color)!; }
        catch { return null; }
    }

    private static List<double[]> DeserializeInkPoints(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return new List<double[]>();
        try
        {
            var list = JsonConvert.DeserializeObject<List<double[]>>(label);
            return list ?? new List<double[]>();
        }
        catch
        {
            return new List<double[]>();
        }
    }

    /// <summary>Serialize canvas-space points to normalized JSON for <see cref="MarkupKind.Markup"/>.</summary>
    public static string SerializeInkPoints(IEnumerable<System.Windows.Point> canvasPoints, double cw, double ch)
    {
        if (cw <= 0 || ch <= 0) return "[]";
        var arr = canvasPoints
            .Select(p => new[] { Math.Clamp(p.X / cw, 0, 1), Math.Clamp(p.Y / ch, 0, 1) })
            .ToList();
        return JsonConvert.SerializeObject(arr);
    }
}
