using System;

namespace MoveAndHydrate
{
    [Flags]
    internal enum Reminder
    {
        None = 0,
        Movement = 1,
        Water = 2
    }

    internal sealed class ReminderSchedule
    {
        private const long Minute = 60000;
        private const long RecentInput = 15000;

        private Settings settings;
        private long movementEligibleAt;
        private long? movementStartedAt;
        private long? lastInputAt;
        private long waterDueAt;

        public ReminderSchedule(Settings settings, long now)
        {
            this.settings = settings;
            ResetMovement(now);
            ConfirmWater(now);
        }

        public void Apply(Settings next, long now)
        {
            if (settings.MovementMinutes != next.MovementMinutes || settings.BreakMinutes != next.BreakMinutes)
                ResetMovement(now);
            if (settings.WaterMinutes != next.WaterMinutes)
                waterDueAt = now + next.WaterMinutes * Minute;

            settings = next;
        }

        public void ResetMovement(long now)
        {
            movementEligibleAt = now;
            movementStartedAt = null;
            lastInputAt = null;
        }

        public void ConfirmWater(long now)
        {
            waterDueAt = now + settings.WaterMinutes * Minute;
        }

        public TimeSpan TimeUntilWater(long now)
        {
            return TimeSpan.FromMilliseconds(Math.Max(0, waterDueAt - now));
        }

        public Reminder Check(long now, uint? inputIdle)
        {
            Reminder due = now >= waterDueAt ? Reminder.Water : Reminder.None;
            if (!inputIdle.HasValue)
                return due;

            long breakLength = settings.BreakMinutes * Minute;
            if (inputIdle.Value >= breakLength)
            {
                ResetMovement(now);
                return due;
            }

            long inputAt = now - inputIdle.Value;
            if (inputAt < movementEligibleAt)
                return due;

            if (!lastInputAt.HasValue || inputAt - lastInputAt.Value >= breakLength)
                movementStartedAt = inputAt;
            lastInputAt = inputAt;

            if (inputIdle.Value <= RecentInput && now - movementStartedAt.Value >= settings.MovementMinutes * Minute)
                due |= Reminder.Movement;
            return due;
        }

        public void MarkReminded(Reminder reminders, long now)
        {
            if ((reminders & Reminder.Movement) != 0)
                movementStartedAt = now;
            if ((reminders & Reminder.Water) != 0)
                waterDueAt = now + settings.WaterMinutes * Minute;
        }
    }
}
