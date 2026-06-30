using System;
using System.Collections.Generic;
using Travora.Domain.Entities;

namespace Travora.Infrastructure.Helpers;

public static class TimezoneHelper
{
    public static DateTime ConvertUtcToAirportLocal(Airport? airport, DateTime utcDateTime)
    {
        if (airport != null && !string.IsNullOrEmpty(airport.Timezone))
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(airport.Timezone);
                return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, tz);
            }
            catch
            {
                // Fallback to static GMT
            }
        }

        if (airport != null && !string.IsNullOrEmpty(airport.GMT) && double.TryParse(airport.GMT, out double gmtOffset))
        {
            return utcDateTime.AddHours(gmtOffset);
        }

        return utcDateTime; // Fallback to UTC
    }

    public static DateTime ConvertAirportLocalToUtc(Airport? airport, DateTime localDateTime)
    {
        var localUnspecified = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);

        if (airport != null && !string.IsNullOrEmpty(airport.Timezone))
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(airport.Timezone);
                return TimeZoneInfo.ConvertTimeToUtc(localUnspecified, tz);
            }
            catch
            {
                // Fallback to static GMT
            }
        }

        if (airport != null && !string.IsNullOrEmpty(airport.GMT) && double.TryParse(airport.GMT, out double gmtOffset))
        {
            return localDateTime.AddHours(-gmtOffset);
        }

        return localDateTime; // Fallback to UTC
    }

    public static (DateTime StartUtc, DateTime EndUtc) GetSlotUtcTimes(Airport? airport, DateTime dateUtc, string slotStr)
    {
        var localDate = ConvertUtcToAirportLocal(airport, dateUtc);

        var slots = new List<string>
        {
            "00:00-02:00", "02:00-04:00", "04:00-06:00", "06:00-08:00",
            "08:00-10:00", "10:00-12:00", "12:00-14:00", "14:00-16:00",
            "16:00-18:00", "18:00-20:00", "20:00-22:00", "22:00-24:00"
        };

        foreach (var slot in slots)
        {
            var parts = slot.Split('-');
            var start = TimeSpan.Parse(parts[0]);
            var end = parts[1] == "24:00" ? TimeSpan.FromHours(24) : TimeSpan.Parse(parts[1]);

            var localStartDt = localDate.Date.Add(start);
            var localEndDt = localDate.Date.Add(end);

            var utcStart = ConvertAirportLocalToUtc(airport, localStartDt);
            var utcEnd = ConvertAirportLocalToUtc(airport, localEndDt);

            var formattedUtcSlot = $"{utcStart:HH:mm}-{utcEnd:HH:mm}";
            if (utcEnd.TimeOfDay == TimeSpan.Zero && utcEnd.Date > utcStart.Date)
            {
                formattedUtcSlot = $"{utcStart:HH:mm}-24:00";
            }

            if (formattedUtcSlot == slotStr)
            {
                return (utcStart, utcEnd);
            }
        }

        // Fallback in case of mismatch or custom slot string formats
        try
        {
            var fallbackParts = slotStr.Split('-');
            var fStart = TimeSpan.Parse(fallbackParts[0]);
            var fEnd = fallbackParts[1] == "24:00" ? TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)) : TimeSpan.Parse(fallbackParts[1]);
            return (dateUtc.Date + fStart, dateUtc.Date + fEnd);
        }
        catch
        {
            return (dateUtc.Date, dateUtc.Date.AddHours(2));
        }
    }
}
