using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sora.Database;
using Sora.Database.Models;
using Sora.Enums;
using Sora.EventArgs.BanchoEventArgs;
using Sora.Framework.Allocation;
using Sora.Framework.Enums;
using Sora.Framework.Objects;
using Sora.Framework.Objects.Scores;
using Sora.Framework.Utilities;
using Sora.Services;
using Console = Colorful.Console;

namespace Sora.Controllers
{
    [ApiController]
    [Route("/web/")]
    public class WebController : Controller
    {
        private readonly Cache _cache;
        private readonly Config _config;
        private readonly Bot.Sora _sora;
        private readonly SoraDbContextFactory _factory;
        private readonly EventManager _ev;
        private readonly PresenceService _ps;
        private readonly Pisstaube _pisstaube;

        public WebController(
            SoraDbContextFactory factory,
            EventManager ev,
            PresenceService ps,
            Cache cache,
            Config config,
            Bot.Sora sora,
            Pisstaube pisstaube)
        {
            _factory = factory;
            _ev = ev;
            _ps = ps;
            _cache = cache;
            _config = config;
            _sora = sora;
            _pisstaube = pisstaube;
        }

        #region GET /web/

        [HttpGet]
        public IActionResult Index() => Ok("ERR: you sneaky little mouse :3");

        #endregion

        #region GET /web/osu-search.php

        [HttpGet("osu-search.php")]
        public async Task<IActionResult> GetSearchDIRECT(
            [FromQuery(Name = "m")] int playMode,
            [FromQuery(Name = "r")] int rankedStatus,
            [FromQuery(Name = "p")] int page,
            [FromQuery(Name = "q")] string query,
            [FromQuery(Name = "u")] string userName,
            [FromQuery(Name = "h")] string pass
        )
        {
            var user = await DBUser.GetDBUser(_factory, userName);
            if (user == null)
                return Ok("err: pass");

            if (!user.IsPassword(pass))
                return Ok("err: pass");

            var cache_hash = Hex.ToHex(Crypto.GetMd5($"m{playMode}r{rankedStatus}p{page}q{query}"));
            
            if (_cache.TryGet($"sora:DirectSearches:{cache_hash}", out string cachedData))
                return Ok(cachedData);

            Response.ContentType = "text/plain";

            var searchResult = await _pisstaube.SearchAsync(query, rankedStatus, playMode, page);

            _cache.Set($"sora:DirectSearches:{cache_hash}", cachedData = searchResult.ToDirect(),
                TimeSpan.FromMinutes(10));

            return Ok(cachedData);
        }

        #endregion

        #region GET /web/osu-search-set.php

        [HttpGet("osu-search-set.php")]
        public async Task<IActionResult> GetDirectNP(
            [FromQuery(Name = "s")] int setId,
            [FromQuery(Name = "b")] int beatmapId,
            [FromQuery(Name = "u")] string userName,
            [FromQuery(Name = "h")] string pass
        )
        {
            var user = await DBUser.GetDBUser(_factory, userName);
            if (user == null)
                return Ok("err: pass");

            if (!user.IsPassword(pass))
                return Ok("err: pass");

            var cache_hash = Hex.ToHex(Crypto.GetMd5($"s{setId}|b{beatmapId}"));
            
            if (_cache.TryGet($"sora:DirectNP:{cache_hash}", out string cachedData))
                return Ok(cachedData);

            if (!string.IsNullOrEmpty(cachedData))
                return Ok(cachedData);

            Response.ContentType = "text/plain";
            
            if (setId != 0)
            {
                var set = await _pisstaube.FetchBeatmapSetAsync(setId);
                _cache.Set($"sora:DirectNP:{cache_hash}", cachedData = BeatmapSet.ToNP(set),
                    TimeSpan.FromMinutes(10));
            }
            else
            {
                var bm = await _pisstaube.FetchBeatmapAsync(beatmapId);
                var set = await _pisstaube.FetchBeatmapSetAsync(bm.ParentSetID);
                _cache.Set($"sora:DirectNP:{cache_hash}", cachedData = BeatmapSet.ToNP(set),
                    TimeSpan.FromMinutes(10));
            }

            return Ok(cachedData);
        }

        #endregion

        #region GET /web/check-updates.php

        [HttpGet("check-updates.php")]
        public IActionResult CheckUpdates(
            [FromQuery] string action,
            [FromQuery(Name = "stream")] string qstream,
            [FromQuery] ulong time)
        {
            if (_cache.TryGet("sora:updater:" + action + qstream, out string answer))
                return Ok(answer);

            var request = (HttpWebRequest) WebRequest.Create(
                $"http://1.1.1.1/web/check-updates.php?action={action}&stream={qstream}&time={time}"
            );
            request.AutomaticDecompression = DecompressionMethods.GZip;
            request.Host = "osu.ppy.sh";

            using var response = (HttpWebResponse) request.GetResponse();
            using var stream = response.GetResponseStream();
            using var reader = new StreamReader(stream ?? throw new Exception("Request Failed!"));

            var result = reader.ReadToEnd();
            _cache.Set("sora:updater:" + action + qstream, result, TimeSpan.FromDays(1));
            return Ok(result);
        }

        #endregion

        #region POST /web/osu-screenshot.php

        [HttpPost("osu-screenshot.php")]
        public IActionResult UploadScreenshot()
        {
            if (!Directory.Exists("data/screenshots"))
                Directory.CreateDirectory("data/screenshots");

            var screenshot = Request.Form.Files.GetFile("ss");
            var Randi = Crypto.RandomString(16);
            using (var stream = screenshot.OpenReadStream())
            {
                using var fs = System.IO.File.OpenWrite($"data/screenshots/{Randi}");
                Image.FromStream(stream)
                     .Save(fs, ImageFormat.Jpeg);
            }

            return Ok($"http://{_config.Server.ScreenShotHostname}/ss/{Randi}");
        }

        #endregion

        #region GET /web/osu-getreplay.php

        [HttpGet("osu-getreplay.php")]
        public async Task<IActionResult> GetReplay(
            [FromQuery(Name = "c")] int replayId,
            [FromQuery(Name = "m")] PlayMode mode,
            [FromQuery(Name = "u")] string userName,
            [FromQuery(Name = "h")] string pass
        )
        {
            var user = await DBUser.GetDBUser(_factory, userName);
            if (user == null)
                return Ok("err: pass");

            if (!user.IsPassword(pass))
                return Ok("err: pass");

            var s = await DBScore.GetScore(_factory, replayId);
            if (s == null)
                return NotFound();

            return File(System.IO.File.OpenRead("data/replays/" + s.ReplayMd5), "binary/octet-stream", s.ReplayMd5);
        }

        #endregion
        
        #region POST /web/osu-submit-modular-selector.php

        [HttpPost("osu-submit-modular-selector.php")]
        public async Task<IActionResult> PostSubmitModular()
        {
            if (!Directory.Exists("data/replays"))
                Directory.CreateDirectory("data/replays");
            
            string encScore = Request.Form["score"];
            string iv = Request.Form["iv"];
            string osuver = Request.Form["osuver"];
            string passwd = Request.Form["pass"];

            var (pass, score) = ScoreSubmissionParser.ParseScore(encScore, iv, osuver);
            var dbUser = await DBUser.GetDBUser(_factory, score.UserName);

            if (dbUser == null)
                return Ok("error: pass");
            
            if (!dbUser.IsPassword(passwd))
                return Ok("error: pass");

            if (!_ps.TryGet(dbUser.Id, out var pr))
                return Ok("error: pass"); // User not logged in in Bancho!

            if (!pass || !RankedMods.IsRanked(score.Mods))
            {
                var lb = await DBLeaderboard.GetLeaderboardAsync(_factory, dbUser);

                lb.IncreasePlaycount(score.PlayMode);
                lb.IncreaseScore((ulong) score.TotalScore, false, score.PlayMode);

                lb.SaveChanges(_factory);
                
                // Send to other People
                await _ev.RunEvent(
                    EventType.BanchoUserStatsRequest,
                    new BanchoUserStatsRequestArgs {userIds = new List<int> {score.Id}, pr = pr}
                );

                // Send to self
                await _ev.RunEvent(
                    EventType.BanchoSendUserStatus,
                    new BanchoSendUserStatusArgs {status = pr.Status, pr = pr}
                );

                return Ok("Thanks for your hard work! onii-chyan~"); // even though, we're Sora, we can still be cute!
            }

            var ReplayFile = Request.Form.Files.GetFile("score");

            var dbScore = new DBScore
            {
                Accuracy = score.ComputeAccuracy(),
                Count100 = score.Count100,
                Count50 = score.Count50,
                Count300 = score.Count300,
                Date = score.Date,
                Mods = score.Mods,
                CountGeki = score.CountGeki,
                CountKatu = score.CountKatu,
                CountMiss = score.CountMiss,
                FileMd5 = score.FileMd5,
                MaxCombo = score.MaxCombo,
                PlayMode = score.PlayMode,
                ScoreOwner = dbUser,
                TotalScore = score.TotalScore,
                UserId = dbUser.Id
            };

            await _pisstaube.DownloadBeatmapAsync(dbScore.FileMd5);

            await using (var m = new MemoryStream())
            {
                ReplayFile.CopyTo(m);
                m.Position = 0;
                dbScore.ReplayMd5 = Hex.ToHex(Crypto.GetMd5(m)) ?? string.Empty;
                if (!string.IsNullOrEmpty(dbScore.ReplayMd5))
                {
                    await using var replayFile = System.IO.File.Create($"data/replays/{dbScore.ReplayMd5}");
                    m.Position = 0;
                    m.WriteTo(replayFile);
                    m.Close();
                    replayFile.Close();
                }
            }

            dbScore.PerformancePoints = dbScore.ComputePerformancePoints();

            var oldScore = await DBScore.GetLatestScore(_factory, dbScore);

            var oldLb = await DBLeaderboard.GetLeaderboardAsync(_factory, dbScore.ScoreOwner);
            var oldStdPos = oldLb.GetPosition(_factory, dbScore.PlayMode);
            
            var oldAcc = oldLb.GetAccuracy(_factory, dbScore.PlayMode);
            double newAcc;

            if (oldScore != null && oldScore.TotalScore <= dbScore.TotalScore)
            {
                using var db = _factory.GetForWrite();
                db.Context.Scores.Remove(oldScore);
                System.IO.File.Delete($"data/replays/{oldScore.ReplayMd5}");

                DBScore.InsertScore(_factory, dbScore);
            }
            else if (oldScore == null)
            {
                DBScore.InsertScore(_factory, dbScore);
            }
            else
            {
                System.IO.File.Delete($"data/replays/{oldScore.ReplayMd5}");
            }

            var newlb = await DBLeaderboard.GetLeaderboardAsync(_factory, dbScore.ScoreOwner);

            newlb.IncreasePlaycount(dbScore.PlayMode);
            newlb.IncreaseScore((ulong) dbScore.TotalScore, true, dbScore.PlayMode);
            newlb.IncreaseScore((ulong) dbScore.TotalScore, false, dbScore.PlayMode);

            newlb.UpdatePP(_factory, dbScore.PlayMode);

            newlb.SaveChanges(_factory);

            var newStdPos = newlb.GetPosition(_factory, dbScore.PlayMode);
            newAcc = newlb.GetAccuracy(_factory, dbScore.PlayMode);

            var newScore = await DBScore.GetLatestScore(_factory, dbScore);

            var set = await _pisstaube.FetchBeatmapSetAsync(dbScore.FileMd5);

            var bm = set?.ChildrenBeatmaps.First(x => x.FileMD5 == dbScore.FileMd5) ?? new Beatmap();

            ulong oldRankedScore;
            ulong newRankedScore;

            double oldPP;
            double newPP;

            switch (dbScore.PlayMode)
            {
                case PlayMode.Osu:
                    oldRankedScore = oldLb.RankedScoreOsu;
                    newRankedScore = newlb.RankedScoreOsu;

                    oldPP = oldLb.PerformancePointsOsu;
                    newPP = newlb.PerformancePointsOsu;
                    break;
                case PlayMode.Taiko:
                    oldRankedScore = oldLb.RankedScoreTaiko;
                    newRankedScore = newlb.RankedScoreTaiko;

                    oldPP = oldLb.PerformancePointsTaiko;
                    newPP = newlb.PerformancePointsTaiko;
                    break;
                case PlayMode.Ctb:
                    oldRankedScore = oldLb.RankedScoreCtb;
                    newRankedScore = newlb.RankedScoreCtb;

                    oldPP = oldLb.PerformancePointsCtb;
                    newPP = newlb.PerformancePointsCtb;
                    break;
                case PlayMode.Mania:
                    oldRankedScore = oldLb.RankedScoreMania;
                    newRankedScore = newlb.RankedScoreMania;

                    oldPP = oldLb.PerformancePointsMania;
                    newPP = newlb.PerformancePointsMania;
                    break;
                default:
                    return Ok("");
            }

            var newScorePosition = newScore != null ? await newScore.Position(_factory) : 0;
            var oldScorePosition = oldScore != null ? await oldScore.Position(_factory) : 0;
            
            if (newScorePosition == 1)
                _sora.SendMessage(
                    $"[http://{_config.Server.ScreenShotHostname}/{dbScore.ScoreOwner.Id} {dbScore.ScoreOwner.UserName}] " +
                    $"has reached #1 on [https://osu.ppy.sh/b/{bm.BeatmapID} {set?.Title} [{bm.DiffName}]] " +
                    $"using {ModUtil.ToString(newScore.Mods)} " +
                    $"Good job! +{newScore.PerformancePoints:F}PP",
                    "#announce",
                    false
                );

            Logger.Info(
                $"{L_COL.RED}{dbScore.ScoreOwner.UserName}",
                $"{L_COL.PURPLE}( {dbScore.ScoreOwner.Id} ){L_COL.WHITE}",
                $"has just submitted a Score! he earned {L_COL.BLUE}{newScore?.PerformancePoints:F}PP",
                $"{L_COL.WHITE}with an Accuracy of {L_COL.RED}{newScore?.Accuracy * 100:F}",
                $"{L_COL.WHITE}on {L_COL.YELLOW}{set?.Title} [{bm.DiffName}]",
                $"{L_COL.WHITE}using {L_COL.BLUE}{ModUtil.ToString(newScore?.Mods ?? Mod.None)}"
            );

            var bmChart = new Chart(
                "beatmap",
                "Beatmap Ranking",
                $"https://osu.ppy.sh/b/{bm.BeatmapID}",
                oldScorePosition,
                newScorePosition,
                oldScore?.MaxCombo ?? 0,
                newScore?.MaxCombo ?? 0,
                oldScore?.Accuracy * 100 ?? 0,
                newScore?.Accuracy * 100 ?? 0,
                (ulong) (oldScore?.TotalScore ?? 0),
                (ulong) (newScore?.TotalScore ?? 0),
                oldScore?.PerformancePoints ?? 0,
                newScore?.PerformancePoints ?? 0,
                newScore?.Id ?? 0
            );

            var overallChart = new Chart(
                "overall",
                "Global Ranking",
                $"https://osu.ppy.sh/u/{dbUser.Id}",
                (int) oldStdPos,
                (int) newStdPos,
                0,
                0,
                oldAcc * 100,
                newAcc * 100,
                oldRankedScore,
                newRankedScore,
                oldPP,
                newPP,
                newScore?.Id ?? 0,
                AchievementProcessor.ProcessAchievements(
                    _factory, dbScore.ScoreOwner, score, bm, set, oldLb, newlb
                )
            );

            pr["LB"] = newlb;

            pr.Stats.TotalScore = newlb.TotalScoreOsu;
            pr.Stats.RankedScore = newlb.RankedScoreOsu;
            pr.Stats.PerformancePoints = (ushort) newlb.PerformancePointsOsu;
            pr.Stats.PlayCount = (uint) newlb.PlayCountOsu;
            pr.Stats.Accuracy = (float) newlb.GetAccuracy(_factory, PlayMode.Osu);
            pr.Stats.Position = newlb.GetPosition(_factory, PlayMode.Osu);
            
            // Send to other People
            await _ev.RunEvent(
                EventType.BanchoUserStatsRequest,
                new BanchoUserStatsRequestArgs {userIds = new List<int> {score.Id}, pr = pr}
            );

            // Send to self
            await _ev.RunEvent(
                EventType.BanchoSendUserStatus,
                new BanchoSendUserStatusArgs {status = pr.Status, pr = pr}
            );

            return Ok(
                $"beatmapId:{bm.BeatmapID}|beatmapSetId:{bm.ParentSetID}|beatmapPlaycount:0|beatmapPasscount:0|approvedDate:\n\n" +
                bmChart.ToOsuString() + "\n" + overallChart.ToOsuString()
            );
        }

        #endregion

        #region GET /web/osu-osz2-getscores.php

        [HttpGet("osu-osz2-getscores.php")]
        public async Task<IActionResult> GetScoreResult(
            [FromQuery(Name = "v")] ScoreboardType type,
            [FromQuery(Name = "c")] string fileMD5,
            [FromQuery(Name = "f")] string f,
            [FromQuery(Name = "m")] PlayMode playMode,
            [FromQuery(Name = "i")] int i,
            [FromQuery(Name = "mods")] Mod mods,
            [FromQuery(Name = "us")] string us,
            [FromQuery(Name = "ha")] string pa)
        {
            try
            {
                var dbUser = await DBUser.GetDBUser(_factory, us);
                var user = dbUser?.ToUser();
                if (dbUser?.IsPassword(pa) != true)
                    return Ok("error: pass");

                var cache_hash =
                    Hex.ToHex(
                        Crypto.GetMd5(
                            $"{fileMD5}{playMode}{mods}{type}{user.Id}{user.UserName}"
                        )
                    );

                if (_cache.TryGet($"sora:Scoreboards:{cache_hash}", out string cachedData))
                    return Ok(cachedData);

                var scores = await DBScore.GetScores(_factory, fileMD5, dbUser, playMode,
                    type == ScoreboardType.Friends,
                    type == ScoreboardType.Country,
                    type == ScoreboardType.Mods,
                    mods);

                var set = await _pisstaube.FetchBeatmapSetAsync(fileMD5);

                var ownScore = await DBScore.GetLatestScore(_factory, new DBScore
                {
                    FileMd5 = fileMD5,
                    UserId = user.Id,
                    PlayMode = playMode,
                    TotalScore = 0
                });

                var sScores = scores.Select(s => new Score
                {
                    Count100 = s.Count100,
                    Count50 = s.Count50,
                    Count300 = s.Count300,
                    Date = s.Date,
                    Mods = s.Mods,
                    CountGeki = s.CountGeki,
                    CountKatu = s.CountKatu,
                    CountMiss = s.CountMiss,
                    FileMd5 = s.FileMd5,
                    MaxCombo = s.MaxCombo,
                    PlayMode = s.PlayMode,
                    TotalScore = s.TotalScore,
                    UserId = dbUser.Id,
                    UserName = dbUser.UserName,
                }).ToList();

                // Fetch the correct position for sScore
                for (var j = 0; j < scores.Count; j++)
                {
                    sScores[j].Position = await scores[j].Position(_factory);
                }

                Score ownsScore = null;
                if (ownScore != null)
                    ownsScore = new Score
                    {
                        Count100 = ownScore.Count100,
                        Count50 = ownScore.Count50,
                        Count300 = ownScore.Count300,
                        Date = ownScore.Date,
                        Mods = ownScore.Mods,
                        CountGeki = ownScore.CountGeki,
                        CountKatu = ownScore.CountKatu,
                        CountMiss = ownScore.CountMiss,
                        FileMd5 = ownScore.FileMd5,
                        MaxCombo = ownScore.MaxCombo,
                        PlayMode = ownScore.PlayMode,
                        TotalScore = ownScore.TotalScore,
                        UserId = dbUser.Id,
                        UserName = dbUser.UserName,
                        Position = await ownScore.Position(_factory)
                    };
                    
                var sboard = new Scoreboard(set.ChildrenBeatmaps.FirstOrDefault(bm => bm.FileMD5 == fileMD5), set, sScores, ownsScore);

                _cache.Set($"sora:Scoreboards:{cache_hash}", cachedData = sboard.ToOsuString(), TimeSpan.FromSeconds(30));
                return Ok(cachedData);
            }
            catch (Exception ex)
            {
                Logger.Err(ex);
                return Ok("Failed");
            }
        }

        #endregion
    }
}
