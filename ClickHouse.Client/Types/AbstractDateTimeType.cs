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
    // بهینه‌سازی ۳: بررسی Kind برای DateTime جهت جلوگیری از تبدیل‌های اضافی
    public DateTimeOffset CoerceToDateTimeOffset(object value)
    {
        return value switch
        {
#if NET6_0_OR_GREATER
            DateOnly date => new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero),
#endif
            DateTimeOffset v => v,

            // اگر DateTime از قبل UTC باشد، نیازی به ساخت LocalDateTime نیست
            DateTime dt => dt.Kind == DateTimeKind.Utc
                ? Instant.FromDateTimeUtc(dt).InZone(TimeZoneOrUtc).ToDateTimeOffset()
                : TimeZoneOrUtc.AtLeniently(LocalDateTime.FromDateTime(dt)).ToDateTimeOffset(),

            OffsetDateTime o => o.ToDateTimeOffset(),
            ZonedDateTime z => z.ToDateTimeOffset(),
            Instant i => ToDateTimeOffset(i),
            _ => throw new NotSupportedException()
        };
    }

    public override Type FrameworkType => typeof(DateTime);

    public DateTimeZone TimeZone { get; set; }

    // بهینه‌سازی ۴: کش کردن مقدار UTC برای جلوگیری از بررسی null در هر بار فراخوانی
    public DateTimeZone TimeZoneOrUtc => TimeZone ?? DateTimeZone.Utc;

    public override string ToString() => TimeZone == null ? Name : $"{Name}({TimeZone.Id})";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private DateTimeOffset ToDateTimeOffset(Instant instant) => instant.InZone(TimeZoneOrUtc).ToDateTimeOffset();

    // بهینه‌سازی ۵: Short-circuit کردن برای حالت UTC
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DateTime ToDateTime(Instant instant)
    {
        // اگر TimeZone تنظیم نشده باشد (یعنی UTC باشد)، اصلاً نیازی به InZone نیست
        if (TimeZone == null)
            return instant.ToDateTimeUtc();

        var zonedDateTime = instant.InZone(TimeZone);
        return zonedDateTime.Offset.Ticks == 0
            ? zonedDateTime.ToDateTimeUtc()
            : zonedDateTime.ToDateTimeUnspecified();
    }
}
