﻿// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

namespace ExFat
{
    using System;
    using System.Globalization;
    using System.Linq;

    public static class DateTimeUtility
    {
        /// <summary>
        /// Converts a time stamp to <see cref="DateTime"/>.
        /// </summary>
        /// <param name="timeStamp">The time stamp.</param>
        /// <param name="tenMs">The ten ms.</param>
        /// <returns></returns>
        public static DateTime FromTimeStamp(UInt32 timeStamp, Byte tenMs)
        {
            var twoSeconds = (int) timeStamp & 0x1F; // 0-4 - 5 bits
            var minute = (int) (timeStamp >> 5) & 0x3F; // 5-10 - 6 bits
            var hour = (int) (timeStamp >> 11) & 0x1F; // 11-15 - 5 bits
            var day = (int) (timeStamp >> 16) & 0x1F; // 16-20 - 5 bits
            var month = (int) (timeStamp >> 21) & 0x0F; // 21-24 - 4 bits
            var year = (int) (timeStamp >> 25) & 0x7F; // 25-31 - 7 bits
            var seconds = twoSeconds * 2 + tenMs / 100;
            var milliseconds = tenMs % 100 * 10;
            return new DateTime(year + 1980, month, day, hour, minute, seconds, milliseconds, DateTimeKind.Local);
        }

        /// <summary>
        /// Converts a <see cref="DateTime"/> to time stamp.
        /// </summary>
        /// <param name="dateTime">The date time.</param>
        /// <returns></returns>
        public static Tuple<UInt32, Byte> ToTimeStamp(this DateTime dateTime)
        {
            var timeStamp = (dateTime.Year - 1980) << 25
                            | dateTime.Month << 21
                            | dateTime.Day << 16
                            | dateTime.Hour << 11
                            | dateTime.Minute << 5
                            | dateTime.Second >> 1;
            var tenMs = dateTime.Millisecond / 10 + dateTime.Second % 2 * 100;
            return Tuple.Create((UInt32) timeStamp, (Byte) tenMs);
        }

        public static TimeZoneInfo FromTimeZoneOffset(Byte offset)
        {
            if (offset < 0x80)
                return null;
            double hoursOffset;
            if (offset < 0xD0)
                hoursOffset = (offset - 0x80) * 0.25;
            else
                hoursOffset = (offset - 0x100) * 0.25;
            var timeSpanOffset = TimeSpan.FromHours(hoursOffset);
            var existingZone = TimeZoneInfo.GetSystemTimeZones().FirstOrDefault(z => z.BaseUtcOffset == timeSpanOffset);
            if (existingZone != null)
                return existingZone;
            return TimeZoneInfo.CreateCustomTimeZone(hoursOffset.ToString(CultureInfo.InvariantCulture), timeSpanOffset, "", "");
        }

        /// <summary>
        /// Converts a <see cref="TimeZoneInfo"/> to time zone offset byte.
        /// </summary>
        /// <param name="timeZoneInfo">The time zone information.</param>
        /// <returns></returns>
        public static Byte ToTimeZoneOffset(this TimeZoneInfo timeZoneInfo)
        {
            if (timeZoneInfo == null)
                return 0;
            var quartersOffset = (int) timeZoneInfo.BaseUtcOffset.TotalHours * 4;
            if (quartersOffset < 0)
                return (byte) (0x100 + quartersOffset);
            return (byte) (0x80 + quartersOffset);
        }
    }
}