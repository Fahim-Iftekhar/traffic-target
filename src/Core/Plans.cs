using System;

namespace TrafficTarget
{
    // The "Day" sheet of the original Excel workbook.
    //   B5 = B3 - B4          remaining GB
    //   C5 = minutes left until midnight (Windows clock)
    //   F3 = B5 / C5          GB per minute
    //   G3 = F3 * 1024 / 60   MB per second
    class DayPlan
    {
        public double TargetGB, DoneGB, RemainingGB;
        public int MinutesLeft;
        public double GBPerMinute, GBPerHour, MBPerSecond, Fraction;
        public bool Done;

        public static DayPlan Calc(double targetGB, double doneGB, DateTime now)
        {
            var p = new DayPlan();
            p.TargetGB = targetGB;
            p.DoneGB = doneGB;
            p.RemainingGB = targetGB - doneGB;
            TimeSpan left = now.Date.AddDays(1) - now;
            p.MinutesLeft = Math.Max(0, (int)Math.Floor(left.TotalMinutes));
            double denom = p.MinutesLeft > 0 ? p.MinutesLeft : Math.Max(left.TotalMinutes, 1.0 / 60.0);
            p.Done = p.RemainingGB <= 0;
            p.GBPerMinute = p.Done ? 0 : p.RemainingGB / denom;
            p.GBPerHour = p.GBPerMinute * 60;
            p.MBPerSecond = p.GBPerMinute * 1024 / 60;
            p.Fraction = targetGB > 0 ? Math.Max(0, Math.Min(1, doneGB / targetGB)) : 1;
            return p;
        }

        public string TimeLeftText
        {
            get { return (MinutesLeft / 60) + "h " + (MinutesLeft % 60).ToString("00") + "m"; }
        }
    }

    // The "Year" sheet of the original Excel workbook.
    //   G3 = E3 - F3          required TB
    //   H3 = G3 * 1024        required GB
    //   I3 = H3 / days left   GB per day   (days left = today .. 31 Dec, today included, Windows date)
    //   J3 = I3 * 7           GB per week
    class YearPlan
    {
        public double TargetTB, DoneTB, RequiredTB, RequiredGB, PerDayGB, PerWeekGB, Fraction;
        public int DaysLeft;
        public bool Done;

        public static int DaysLeftInYear(DateTime today)
        {
            return (new DateTime(today.Year, 12, 31) - today.Date).Days + 1;
        }

        public static YearPlan Calc(double targetTB, double doneTB, DateTime today)
        {
            var p = new YearPlan();
            p.TargetTB = targetTB;
            p.DoneTB = doneTB;
            p.RequiredTB = targetTB - doneTB;
            p.RequiredGB = p.RequiredTB * 1024;
            p.DaysLeft = DaysLeftInYear(today);
            p.Done = p.RequiredTB <= 0;
            p.PerDayGB = p.Done ? 0 : p.RequiredGB / p.DaysLeft;
            p.PerWeekGB = p.PerDayGB * 7;
            p.Fraction = targetTB > 0 ? Math.Max(0, Math.Min(1, doneTB / targetTB)) : 1;
            return p;
        }
    }
}
