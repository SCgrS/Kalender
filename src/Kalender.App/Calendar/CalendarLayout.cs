using System.Drawing;

namespace Kalender.Calendar;

/// <summary>Takvim ızgarasındaki tek bir gün hücresi.</summary>
internal sealed class DayCell
{
    public required DateOnly Date { get; init; }

    /// <summary>0-6 arası sütun; 0 Pazartesi.</summary>
    public required int Column { get; init; }

    /// <summary>0-5 arası hafta satırı.</summary>
    public required int Row { get; init; }

    /// <summary>Hücrenin pencere koordinatlarındaki dikdörtgeni.</summary>
    public required RectangleF Bounds { get; init; }

    /// <summary>Gün rakamının ortalandığı nokta.</summary>
    public required PointF Center { get; init; }

    /// <summary>Gösterilen aya mı ait, yoksa önceki/sonraki aydan mı taşmış?</summary>
    public required bool InCurrentMonth { get; init; }

    /// <summary>Pazar günü mü?</summary>
    public bool IsSunday => Date.DayOfWeek == DayOfWeek.Sunday;

    /// <summary>Cumartesi günü mü?</summary>
    public bool IsSaturday => Date.DayOfWeek == DayOfWeek.Saturday;
}

/// <summary>
/// Takvimin geometrisi. Hem çizim hem de fare konumundan gün bulma
/// (hit-test) aynı nesneyi kullanır; böylece ikisi asla ayrışmaz.
/// İleride takvim alanına özel sağ tık menüsü eklendiğinde de aynı
/// hesap kullanılacak.
/// </summary>
internal sealed class CalendarLayout
{
    /// <summary>Ölçek 1.0 iken bir sütunun genişliği (piksel).</summary>
    public const float BaseColumnWidth = 100f;

    /// <summary>Ölçek 1.0 iken bir satırın yüksekliği (piksel).</summary>
    public const float BaseRowHeight = 88f;

    public const int Columns = 7;
    public const int WeekRows = 6;

    /// <summary>Başlık + gün adları + 6 hafta = 8 satırlık dikey ritim.</summary>
    private const int TotalRows = 2 + WeekRows;

    private CalendarLayout(DateOnly monthStart, float scale, PointF origin,
        IReadOnlyList<DayCell> cells, RectangleF titleBounds, RectangleF[] weekdayCells)
    {
        MonthStart = monthStart;
        Scale = scale;
        Origin = origin;
        Cells = cells;
        TitleBounds = titleBounds;
        WeekdayCells = weekdayCells;
        Bounds = new RectangleF(origin, MeasureSize(scale));
    }

    public DateOnly MonthStart { get; }

    /// <summary>DPI ve kullanıcı boyut yüzdesi birleştirilmiş toplam ölçek.</summary>
    public float Scale { get; }

    /// <summary>Bloğun sol üst köşesi (pencere koordinatı).</summary>
    public PointF Origin { get; }

    /// <summary>Bloğun tamamını kapsayan dikdörtgen.</summary>
    public RectangleF Bounds { get; }

    /// <summary>Ay adının yazıldığı satır.</summary>
    public RectangleF TitleBounds { get; }

    /// <summary>Gün adı baş harflerinin yazıldığı 7 hücre.</summary>
    public RectangleF[] WeekdayCells { get; }

    /// <summary>42 gün hücresi; sırayla soldan sağa, yukarıdan aşağıya.</summary>
    public IReadOnlyList<DayCell> Cells { get; }

    public float ColumnWidth => BaseColumnWidth * Scale;

    public float RowHeight => BaseRowHeight * Scale;

    /// <summary>Verilen ölçekte takvim bloğunun kapladığı alan.</summary>
    public static SizeF MeasureSize(float scale)
        => new(BaseColumnWidth * Columns * scale, BaseRowHeight * TotalRows * scale);

    public static CalendarLayout Create(DateOnly anyDayOfMonth, float scale, PointF origin)
    {
        var monthStart = new DateOnly(anyDayOfMonth.Year, anyDayOfMonth.Month, 1);
        float colW = BaseColumnWidth * scale;
        float rowH = BaseRowHeight * scale;

        var title = new RectangleF(origin.X, origin.Y, colW * Columns, rowH);

        var weekdays = new RectangleF[Columns];
        for (int c = 0; c < Columns; c++)
        {
            weekdays[c] = new RectangleF(origin.X + (c * colW), origin.Y + rowH, colW, rowH);
        }

        // Izgara, ayın 1'inden önceki (ya da 1'ine denk gelen) Pazartesi'den başlar.
        int lead = TurkishNames.WeekIndex(monthStart.DayOfWeek);
        DateOnly first = monthStart.AddDays(-lead);

        var cells = new List<DayCell>(Columns * WeekRows);
        for (int r = 0; r < WeekRows; r++)
        {
            for (int c = 0; c < Columns; c++)
            {
                DateOnly date = first.AddDays((r * Columns) + c);
                var bounds = new RectangleF(
                    origin.X + (c * colW),
                    origin.Y + ((r + 2) * rowH),
                    colW,
                    rowH);

                cells.Add(new DayCell
                {
                    Date = date,
                    Column = c,
                    Row = r,
                    Bounds = bounds,
                    Center = new PointF(bounds.X + (bounds.Width / 2f), bounds.Y + (bounds.Height / 2f)),
                    InCurrentMonth = date.Month == monthStart.Month && date.Year == monthStart.Year,
                });
            }
        }

        return new CalendarLayout(monthStart, scale, origin, cells, title, weekdays);
    }

    /// <summary>
    /// Pencere koordinatındaki bir noktanın hangi güne denk geldiğini söyler.
    /// Nokta takvim ızgarasının dışındaysa null döner.
    /// </summary>
    public DayCell? HitTest(PointF point)
    {
        float gridTop = Origin.Y + (2 * RowHeight);
        if (point.X < Origin.X || point.X >= Origin.X + (Columns * ColumnWidth))
        {
            return null;
        }

        if (point.Y < gridTop || point.Y >= gridTop + (WeekRows * RowHeight))
        {
            return null;
        }

        int col = (int)((point.X - Origin.X) / ColumnWidth);
        int row = (int)((point.Y - gridTop) / RowHeight);

        col = Math.Clamp(col, 0, Columns - 1);
        row = Math.Clamp(row, 0, WeekRows - 1);

        return Cells[(row * Columns) + col];
    }
}
