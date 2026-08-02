using System;
using System.Runtime.CompilerServices;
using NodaTime;

namespace ClickHouse.Client.Types;

public static class DateTimeConversions
{
    // بهینه‌سازی ۱: حذف ساخت DateTimeOffset اضافی
    public static readonly DateTime DateTimeEpochStart = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

#if NET6_0_OR_GREATER
    public static readonly DateOnly DateOnlyEpochStart = new(1970, 1, 1);
#endif

    // بهینه‌سازی ۲: محاسبه روزها با استفاده از ریاضیات اعداد صحیح (Integer Math)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ToUnixTimeDays(this DateTimeOffset dto)
    {
        // تقسیم بر ۸۶۴۰۰ (تعداد ثانیه‌های یک روز) بسیار سریع‌تر از کم کردن DateTime و محاسبه TotalDays است
        return (int)(dto.ToUnixTimeSeconds() / 86400);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DateTime FromUnixTimeDays(int days) => DateTimeEpochStart.AddDays(days);
}

internal abstract class AbstractDateTimeType : ParameterizedType
{
    private DateTimeZone timeZone;

    // ✅ کش‌شده + مقداردهی اولیه با UTC (حفظ رفتار وقتی setter هرگز صدا زده نمی‌شود)
    private DateTimeZone timeZoneOrUtc = DateTimeZone.Utc;

    public DateTimeOffset CoerceToDateTimeOffset(object value)
    {
        return value switch
        {
#if NET6_0_OR_GREATER
            DateOnly date => new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero),
#endif
            DateTimeOffset v => v,
            DateTime dt => timeZoneOrUtc.AtLeniently(LocalDateTime.FromDateTime(dt)).ToDateTimeOffset(),
            OffsetDateTime o => o.ToDateTimeOffset(),
            ZonedDateTime z => z.ToDateTimeOffset(),
            Instant i => ToDateTimeOffset(i),
            _ => throw new NotSupportedException()
        };
    }

    public override Type FrameworkType => typeof(DateTime);

    public DateTimeZone TimeZone
    {
        get => timeZone;
        set
        {
            timeZone = value;
            timeZoneOrUtc = value ?? DateTimeZone.Utc;   // ✅ محاسبه یک‌باره در زمان set
        }
    }

    // ✅ خواندن مستقیم از فیلد — بدون get_TimeZone، بدون null-check
    public DateTimeZone TimeZoneOrUtc => timeZoneOrUtc;

    public override string ToString() => timeZone == null ? $"{Name}" : $"{Name}({timeZone.Id})";

    private DateTimeOffset ToDateTimeOffset(Instant instant) => instant.InZone(timeZoneOrUtc).ToDateTimeOffset();

    // بهینه‌سازی ۵: Short-circuit کردن برای حالت UTC
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DateTime ToDateTime(Instant instant)
    {
        var zone = timeZoneOrUtc;

        // ✅ مسیر سریع برای UTC (رایج‌ترین حالت در ClickHouse)
        // DateTimeZone.Utc یک singleton است، پس این یک reference comparison سریع است
        if (zone == DateTimeZone.Utc)
            return instant.ToDateTimeUtc();

        var zonedDateTime = instant.InZone(zone);
        return zonedDateTime.Offset.Ticks == 0
            ? zonedDateTime.ToDateTimeUtc()
            : zonedDateTime.ToDateTimeUnspecified();
    }
}
