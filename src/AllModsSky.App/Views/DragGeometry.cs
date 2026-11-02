namespace AllModsSky.App.Views;

/// <summary>
/// 拖拽插入位置的几何计算。
///
/// 单独拎出来是因为这块以前靠命中测试算：光标落进行间那 4px 空隙、或者跑到列表顶部
/// 留白里时，<c>InputHitTest</c> 命不中任何行，算出来是 NaN，线就闪一下或者干脆没了。
/// 现在只跟坐标比，不碰视觉树，顺带能脱离 UI 做断言。
///
/// 提示线和实际落点共用 <see cref="Slot"/> 这一个结果，避免「线画在这儿、东西掉到别处」。
/// </summary>
internal static class DragGeometry
{
    /// <summary>一行在列表坐标系里的上下边界。</summary>
    public readonly record struct RowSpan(double Top, double Bottom)
    {
        public double Middle => (Top + Bottom) / 2;
    }

    /// <summary>插入位置：第 <paramref name="Index"/> 行之前（After = false）或之后（After = true）。</summary>
    public readonly record struct DropSlot(int Index, bool After);

    /// <summary>提示线离行的距离，落在行间距里，不压住行的边框。</summary>
    public const double Offset = 2;

    /// <summary>
    /// 按光标的 Y 找插入位置。<paramref name="rows"/> 需按从上到下排列。
    /// 光标在首行之上、末行之下、行间空隙里都能给出位置，不会返回空。
    /// </summary>
    public static DropSlot Slot(IReadOnlyList<RowSpan> rows, double y)
    {
        if (rows.Count == 0) return new DropSlot(0, false);

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];

            // 落在这行上边界之上，说明在它前面（也就是上一行之后的空隙里）
            if (y < row.Top) return new DropSlot(i, false);

            if (y <= row.Bottom) return new DropSlot(i, y > row.Middle);
        }

        return new DropSlot(rows.Count - 1, true);
    }

    /// <summary>插入位置对应的提示线 Y。返回 NaN 表示没有行可依附（列表为空）。</summary>
    public static double MarkerY(IReadOnlyList<RowSpan> rows, DropSlot slot)
    {
        if (rows.Count == 0) return double.NaN;

        var index = Math.Clamp(slot.Index, 0, rows.Count - 1);
        return slot.After ? rows[index].Bottom + Offset : rows[index].Top - Offset;
    }

    /// <summary>
    /// 把提示线夹进可见范围：第一行上方算出来是负值，画到控件外面会被裁掉，
    /// 看着就像「拖到最顶上线条消失了」。NaN 原样返回。
    /// </summary>
    public static double ClampToView(double markerY, double height)
    {
        if (double.IsNaN(markerY)) return double.NaN;
        return Math.Clamp(markerY, 1, Math.Max(1, height - 1));
    }
}
