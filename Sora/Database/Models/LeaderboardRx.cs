﻿#region LICENSE

/*
    Sora - A Modular Bancho written in C#
    Copyright (C) 2019 Robin A. P.

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as
    published by the Free Software Foundation, either version 3 of the
    License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

#endregion

// ReSharper disable UnusedAutoPropertyAccessor.Global

using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using osu.Framework.Extensions.IEnumerableExtensions;
using Sora.Enums;

namespace Sora.Database.Models
{
    public class LeaderboardRx
    {
        [Key]
        [Required]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [DefaultValue(0)]
        public ulong RankedScoreOsu { get; set; }

        [Required]
        [DefaultValue(0)]
        public ulong RankedScoreTaiko { get; set; }

        [Required]
        [DefaultValue(0)]
        public ulong RankedScoreCtb { get; set; }

        [Required]
        [DefaultValue(0)]
        public ulong TotalScoreOsu { get; set; }

        [Required]
        [DefaultValue(0)]
        public ulong TotalScoreTaiko { get; set; }

        [Required]
        [DefaultValue(0)]
        public ulong TotalScoreCtb { get; set; }

        [Required]
        [DefaultValue(0)]
        public ulong PlayCountOsu { get; set; }

        [Required]
        [DefaultValue(0)]
        public ulong PlayCountTaiko { get; set; }

        [Required]
        [DefaultValue(0)]
        public ulong PlayCountCtb { get; set; }

        [Required]
        [DefaultValue(0)]
        public double PerformancePointsOsu { get; set; }

        [Required]
        [DefaultValue(0)]
        public double PerformancePointsTaiko { get; set; }

        [Required]
        [DefaultValue(0)]
        public double PerformancePointsCtb { get; set; }

        public static LeaderboardRx GetLeaderboard(SoraDbContextFactory factory, int userId)
        {
            using var db = factory.GetForWrite();

            var result = db.Context.LeaderboardRx.Where(t => t.Id == userId).Select(e => e).FirstOrDefault();
            if (result != null)
                return result;

            db.Context.LeaderboardRx.Add(new LeaderboardRx {Id = userId});

            return new LeaderboardRx {Id = userId};
        }

        public double GetAccuracy(SoraDbContextFactory factory, PlayMode mode)
        {
            var totalAcc = 0d;
            var divideTotal = 0d;
            var i = 0;

            factory.Get()
                   .Scores
                   .Where(s => s.PlayMode == mode)
                   .Where(s => (s.Mods & Mod.Relax) != 0 && (s.Mods & Mod.Relax2) == 0)
                   .Where(s => s.UserId == Id)
                   .Take(500)
                   .OrderByDescending(s => s.PerformancePoints)
                   .ForEach(
                       s =>
                       {
                           var divide = Math.Pow(.95d, i);

                           totalAcc += s.Accuracy * divide;
                           divideTotal += divide;

                           i++;
                       }
                   );

            return divideTotal > 0 ? totalAcc / divideTotal : 0;
        }
        public void IncreaseScore(SoraDbContextFactory factory, ulong score, bool ranked, PlayMode mode)
        {
            using var db = factory.GetForWrite();

            switch (mode)
            {
                case PlayMode.Osu:
                    if (ranked)
                        RankedScoreOsu += score;
                    else
                        TotalScoreOsu += score;
                    break;
                case PlayMode.Taiko:
                    if (ranked)
                        RankedScoreTaiko += score;
                    else
                        TotalScoreTaiko += score;
                    break;
                case PlayMode.Ctb:
                    if (ranked)
                        RankedScoreCtb += score;
                    else
                        TotalScoreCtb += score;
                    break;
            }

            db.Context.LeaderboardRx.Update(this);
        }

        public void IncreasePlaycount(SoraDbContextFactory factory, PlayMode mode)
        {
            using var db = factory.GetForWrite();

            switch (mode)
            {
                case PlayMode.Osu:
                    PlayCountOsu++;
                    break;
                case PlayMode.Taiko:
                    PlayCountTaiko++;
                    break;
                case PlayMode.Ctb:
                    PlayCountCtb++;
                    break;
            }

            db.Context.LeaderboardRx.Update(this);
        }

        public void UpdatePP(SoraDbContextFactory factory, PlayMode mode)
        {
            using var db = factory.GetForWrite();

            var TotalPP = db.Context.Scores
                            .Where(s => (s.Mods & Mod.Relax) == 0)
                            .Where(s => s.PlayMode == mode)
                            .Where(s => s.UserId == Id)
                            .OrderByDescending(s => s.PerformancePoints)
                            .Take(100).ToList()
                            .Select((t, i) => t.PerformancePoints * Math.Pow(0.95d, i))
                            .Sum();

            switch (mode)
            {
                case PlayMode.Osu:
                    PerformancePointsOsu = TotalPP;
                    break;
                case PlayMode.Taiko:
                    PerformancePointsTaiko = TotalPP;
                    break;
                case PlayMode.Ctb:
                    PerformancePointsCtb = TotalPP;
                    break;
                case PlayMode.Mania:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }

            db.Context.LeaderboardRx.Update(this);
        }

        public uint GetPosition(SoraDbContextFactory factory, PlayMode mode)
        {
            var pos = 0;

            switch (mode)
            {
                case PlayMode.Osu:
                    pos = factory.Get().LeaderboardRx.Count(x => x.PerformancePointsOsu > PerformancePointsOsu);
                    break;
                case PlayMode.Taiko:
                    pos = factory.Get().LeaderboardRx.Count(x => x.PerformancePointsTaiko > PerformancePointsTaiko);
                    break;
                case PlayMode.Ctb:
                    pos = factory.Get().LeaderboardRx.Count(x => x.PerformancePointsCtb > PerformancePointsCtb);
                    break;
            }

            return (uint) pos;
        }

        public static LeaderboardRx GetLeaderboard(SoraDbContextFactory factory, Users user)
            => GetLeaderboard(factory, user.Id);
    }
}
