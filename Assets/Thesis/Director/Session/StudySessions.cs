using System;
using System.Globalization;
using Thesis.Sim;

namespace Thesis.Director
{
    // Which session a participant plays (D9, ARCHITECTURE.md §6; WP14). The setup screen asks for a
    // participant code and a session number and gets the condition and the seed from here; nothing
    // else decides them, so the four groups come out as the table says and a test can check it.
    //
    //   group  session 1           session 2
    //   1      static, seed X      adaptive, seed Y
    //   2      static, seed Y      adaptive, seed X
    //   3      adaptive, seed X    static, seed Y
    //   4      adaptive, seed Y    static, seed X
    //
    // The group follows from the code's number (the student, 2026-11-24): P01 is group 1, P02 group
    // 2, P05 group 1 again. Codes handed out in order therefore fill the four groups evenly.
    //
    // The seeds were picked by measurement (Results/2026-11-24_study-seeds): of seeds 101 to 130,
    // the two maps on which the scripted player lasted most alike against the static waves and both
    // directors. Fixed here before the pilot; changing them changes every later session's board.
    public static class StudySessions
    {
        public const ulong SeedX = 109;
        public const ulong SeedY = 116;
        public const ulong PracticeSeed = 103;

        // The practice run (GDD question 8): three waves of the static version on a board of its
        // own, recorded but marked as practice and never analysed.
        public const int PracticeWaves = 3;

        public const string SetX = "X";
        public const string SetY = "Y";
        public const string SetPractice = "practice";

        public const int MaxNumber = 999;

        // "P7", "p07" and " P07 " are the same participant, written "P07". Refuses anything else, so a
        // typo cannot silently become another participant's group.
        public static bool TryParse(string code, out int number)
        {
            number = 0;
            if (code == null) return false;
            string c = code.Trim();
            if (c.Length < 2 || (c[0] != 'P' && c[0] != 'p')) return false;
            string digits = c.Substring(1);
            foreach (char ch in digits) if (ch < '0' || ch > '9') return false;
            if (digits.Length > 3 || !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out number)) return false;
            return number >= 1 && number <= MaxNumber;
        }

        public static string Normalise(int number) { return "P" + number.ToString("00", CultureInfo.InvariantCulture); }

        public static int GroupOf(int number) { return (number - 1) % 4 + 1; }

        public static StudyInfo For(string code, int session)
        {
            if (!TryParse(code, out int number)) throw new ArgumentException("'" + code + "' is not a participant code (P followed by 1 to " + MaxNumber + ").");
            if (session != 1 && session != 2) throw new ArgumentException("A participant plays session 1 and session 2, not " + session + ".");
            int group = GroupOf(number);
            bool adaptiveFirst = group >= 3;
            bool xFirst = group == 1 || group == 3;
            bool first = session == 1;
            return new StudyInfo
            {
                Participant = Normalise(number),
                Group = group,
                SessionNumber = session,
                Condition = first == adaptiveFirst ? StudyInfo.Adaptive : StudyInfo.Static,
                SeedSet = first == xFirst ? SetX : SetY,
                Practice = false,
            };
        }

        public static StudyInfo Practice(string code)
        {
            if (!TryParse(code, out int number)) throw new ArgumentException("'" + code + "' is not a participant code (P followed by 1 to " + MaxNumber + ").");
            return new StudyInfo { Participant = Normalise(number), Group = 0, SessionNumber = 0, Condition = StudyInfo.Static, SeedSet = SetPractice, Practice = true };
        }

        public static ulong SeedOf(StudyInfo info)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));
            switch (info.SeedSet)
            {
                case SetX: return SeedX;
                case SetY: return SeedY;
                case SetPractice: return PracticeSeed;
                default: throw new ArgumentException("Unknown seed set '" + info.SeedSet + "'.");
            }
        }

        // The planner's name (PlannerFactory): the adaptive version is the full director, starting
        // cold each session (the student, 2026-11-24); the static one is the escalation spread over
        // as many entries as the director's waves use (SpreadStaticPlanner, the same day).
        public static string PlannerOf(StudyInfo info)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));
            return info.Condition == StudyInfo.Adaptive ? WaveDirector.FullId : SpreadStaticPlanner.Id;
        }
    }
}
